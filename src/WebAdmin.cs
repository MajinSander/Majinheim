// Administración desde la web (fase 3): entrar con contraseña y encender, apagar, reiniciar o hacer backup.
// Seguridad: contraseña guardada como hash PBKDF2 (nunca en claro), sesión en cookie HttpOnly + SameSite=Strict
// (+ Secure por HTTPS), cabecera propia obligatoria en cada POST, bloqueo tras fallos y registro de todo lo que se hace.
// Compilado con el csc de .NET Framework 4.8 (C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Majinheim
{
    public static class WebAdmin
    {
        const string CookieName = "mh_sesion";
        const int SessionHours = 12, Iterations = 150000, MinPassword = 10;
        const int MaxFailsPerIp = 5, MaxFailsTotal = 20;
        static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

        class Session { public DateTime Until; public string Ip; }
        class Fails { public int Count; public DateTime First, LockedUntil; }

        static readonly object lk = new object();
        static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        static readonly Dictionary<string, Fails> fails = new Dictionary<string, Fails>();
        static Fails total = new Fails();

        // Lo pone la ventana del launcher: ejecuta "iniciar", "parar", "reiniciar" o "backup" y devuelve qué ha hecho.
        public static Func<string, string> Action;

        public static bool HasPassword { get { return !string.IsNullOrEmpty(Settings.Current.WebAdminHash); } }
        static string AuditFile { get { return Path.Combine(AppInfo.LogDir, "web-admin.log"); } }

        // ------------------------------------------------------------ contraseña

        public static string SetPassword(string pw)
        {
            if (pw == null || pw.Length < MinPassword) return "La contraseña tiene que tener al menos " + MinPassword + " caracteres.";
            if (pw == Settings.Current.Password) return "Usa una contraseña distinta a la del server de Valheim.";
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            Settings.Current.WebAdminHash = "pbkdf2-sha256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(Derive(pw, salt, Iterations));
            Settings.Save();
            lock (lk) sessions.Clear();   // al cambiar la contraseña, todo el mundo vuelve a entrar
            Audit("este PC", "contraseña de administrador cambiada");
            return null;
        }

        public static void RemovePassword()
        {
            Settings.Current.WebAdminHash = "";
            Settings.Save();
            lock (lk) sessions.Clear();
            Audit("este PC", "administración web desactivada");
        }

        static byte[] Derive(string pw, byte[] salt, int iter)
        {
            using (var k = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pw), salt, iter, HashAlgorithmName.SHA256)) return k.GetBytes(32);
        }

        static bool Check(string pw)
        {
            var p = (Settings.Current.WebAdminHash ?? "").Split('$');
            if (p.Length != 4 || p[0] != "pbkdf2-sha256" || pw == null) return false;
            int iter;
            if (!int.TryParse(p[1], out iter)) return false;
            var want = Convert.FromBase64String(p[3]);
            var got = Derive(pw, Convert.FromBase64String(p[2]), iter);
            int diff = want.Length ^ got.Length;
            for (int i = 0; i < Math.Min(want.Length, got.Length); i++) diff |= want[i] ^ got[i];
            return diff == 0;
        }

        // ------------------------------------------------------------ peticiones

        public static MiniHttp.Response Handle(MiniHttp.Request req)
        {
            if (req.Method == "POST" && req.Header("X-Majinheim") != "1") return Err(403, "Petición no permitida.");
            if (req.Method != "POST" && req.Path != "/api/admin/estado") return Err(405, "No permitido.");

            if (req.Path == "/api/entrar") return Login(req);
            if (req.Path == "/api/salir")
            {
                string t = req.Cookie(CookieName);
                if (t != null) lock (lk) sessions.Remove(t);
                var r = Ok("Has salido.");
                r.Extra.Add(ClearCookie(req));
                return r;
            }

            string ip = req.ClientIp;
            if (!Authed(req))
            {
                if (req.Path == "/api/admin/estado") return MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "dentro", false }, { "configurado", HasPassword } });
                return Err(401, "Tienes que entrar otra vez.");
            }

            if (req.Path == "/api/admin/estado")
            {
                return MiniHttp.Response.JsonOf(200, new Dictionary<string, object>
                {
                    { "dentro", true },
                    { "configurado", true },
                    { "registro", ServerWatch.RecentLines(150) },
                    { "acciones", ReadAudit(30) },
                    { "estado", ServerWatch.Snapshot() },
                });
            }
            if (req.Path == "/api/admin/accion")
            {
                string what = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("accion", out v)) what = v as string; } catch { }
                if (what != "iniciar" && what != "parar" && what != "reiniciar" && what != "backup") return Err(400, "Acción desconocida.");
                var act = Action;
                if (act == null) return Err(503, "El launcher del server no está listo.");
                string result;
                try { result = act(what); }
                catch (Exception e) { result = "No se pudo: " + e.Message; }
                Audit(ip, what + ": " + result);
                return Ok(result);
            }
            return Err(404, "No existe.");
        }

        static MiniHttp.Response Login(MiniHttp.Request req)
        {
            string ip = req.ClientIp;
            if (!HasPassword) return Err(403, "La administración web está desactivada. Ponle una contraseña desde el launcher del server (pestaña Web).");
            lock (lk)
            {
                var now = DateTime.Now;
                if (total.LockedUntil > now) return Err(429, "Demasiados intentos fallidos. Prueba dentro de un rato.");
                Fails f;
                if (fails.TryGetValue(ip, out f) && f.LockedUntil > now) return Err(429, "Demasiados intentos fallidos. Prueba dentro de " + Math.Ceiling((f.LockedUntil - now).TotalMinutes) + " min.");
            }
            string pw = null;
            try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("clave", out v)) pw = v as string; } catch { }
            bool ok = Check(pw);
            lock (lk)
            {
                var now = DateTime.Now;
                if (!ok)
                {
                    Fails f;
                    if (!fails.TryGetValue(ip, out f) || now - f.First > LockFor) fails[ip] = f = new Fails { First = now };
                    f.Count++;
                    if (f.Count >= MaxFailsPerIp) f.LockedUntil = now + LockFor;
                    if (now - total.First > TimeSpan.FromHours(1)) total = new Fails { First = now };
                    total.Count++;
                    if (total.Count >= MaxFailsTotal) total.LockedUntil = now + LockFor;
                }
                else fails.Remove(ip);
            }
            if (!ok) { Audit(ip, "contraseña incorrecta"); return Err(401, "Contraseña incorrecta."); }

            var tb = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(tb);
            string token = BitConverter.ToString(tb).Replace("-", "").ToLowerInvariant();
            lock (lk)
            {
                foreach (var k in sessions.Where(x => x.Value.Until < DateTime.Now).Select(x => x.Key).ToList()) sessions.Remove(k);
                sessions[token] = new Session { Until = DateTime.Now.AddHours(SessionHours), Ip = ip };
            }
            Audit(ip, "ha entrado");
            var r = Ok("Dentro.");
            r.Extra.Add("Set-Cookie: " + CookieName + "=" + token + "; Path=/; HttpOnly; SameSite=Strict; Max-Age=" + SessionHours * 3600 + (req.Https ? "; Secure" : ""));
            return r;
        }

        static bool Authed(MiniHttp.Request req)
        {
            if (!HasPassword) return false;
            string t = req.Cookie(CookieName);
            if (string.IsNullOrEmpty(t)) return false;
            lock (lk)
            {
                Session s;
                if (!sessions.TryGetValue(t, out s)) return false;
                if (s.Until < DateTime.Now) { sessions.Remove(t); return false; }
                return true;
            }
        }

        static string ClearCookie(MiniHttp.Request req) { return "Set-Cookie: " + CookieName + "=; Path=/; HttpOnly; SameSite=Strict; Max-Age=0" + (req.Https ? "; Secure" : ""); }
        static MiniHttp.Response Ok(string msg) { return MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "mensaje", msg } }); }
        static MiniHttp.Response Err(int code, string msg) { return MiniHttp.Response.JsonOf(code, new Dictionary<string, object> { { "ok", false }, { "mensaje", msg } }); }

        // ------------------------------------------------------------ registro de acciones

        static readonly object auditLk = new object();

        public static void Audit(string ip, string what)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + ip.Replace("\t", " ") + "\t" + what.Replace("\t", " ").Replace("\n", " ");
            lock (auditLk)
                try { Directory.CreateDirectory(AppInfo.LogDir); File.AppendAllText(AuditFile, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
            Log.W("Web admin: {0} ({1})", what, ip);
        }

        static List<Dictionary<string, object>> ReadAudit(int n)
        {
            lock (auditLk)
            {
                if (!File.Exists(AuditFile)) return new List<Dictionary<string, object>>();
                return File.ReadAllLines(AuditFile).Reverse().Take(n).Select(l => l.Split('\t')).Where(p => p.Length >= 3)
                    .Select(p => new Dictionary<string, object> { { "fecha", p[0] }, { "ip", p[1] }, { "que", p[2] } }).ToList();
            }
        }
    }
}
