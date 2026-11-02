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
        const int SessionHours = 12, RememberDays = 30, Iterations = 150000, MinPassword = 8;
        const int MaxFailsPerIp = 5, MaxFailsTotal = 20;
        static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

        // Sesiones guardadas en disco (solo la huella SHA-256 del token, nunca el token): "recordarme" sobrevive a reinicios.
        public class Session { public string Hash { get; set; } public DateTime Until { get; set; } public string Ip { get; set; } public string Device { get; set; } public DateTime Created { get; set; } }
        class Fails { public int Count; public DateTime First, LockedUntil; }

        static readonly object lk = new object();
        static Dictionary<string, Session> sessions;
        static string SessionsFile { get { return AppInfo.P("web", "sesiones-admin.json"); } }

        static void LoadSessions()
        {
            if (sessions != null) return;
            sessions = new Dictionary<string, Session>();
            try { foreach (var x in Json.Load<List<Session>>(SessionsFile)) if (x.Until > DateTime.Now && !string.IsNullOrEmpty(x.Hash)) sessions[x.Hash] = x; }
            catch (Exception e) { Log.W("Web admin: no pude leer las sesiones guardadas: {0}", e.Message); }
        }

        static void SaveSessions()
        {
            try { Json.Save(SessionsFile, sessions.Values.Where(x => x.Until > DateTime.Now).ToList()); }
            catch (Exception e) { Log.W("Web admin: no pude guardar las sesiones: {0}", e.Message); }
        }

        static string Sha(string t)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(t ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        public static void ForgetAllSessions() { lock (lk) { LoadSessions(); sessions.Clear(); SaveSessions(); } }
        static readonly Dictionary<string, Fails> fails = new Dictionary<string, Fails>();
        static Fails total = new Fails();

        // Lo pone la ventana del launcher: ejecuta "iniciar", "parar", "reiniciar" o "backup" y devuelve qué ha hecho.
        public static Func<string, string> Action;

        public static bool HasPassword { get { return !string.IsNullOrEmpty(Settings.Current.WebAdminHash) && !string.IsNullOrEmpty(Settings.Current.WebAdminUser); } }
        public static string User { get { return Settings.Current.WebAdminUser ?? ""; } }
        static string AuditFile { get { return Path.Combine(AppInfo.LogDir, "web-admin.log"); } }

        // ------------------------------------------------------------ contraseña

        public static string SetPassword(string user, string pw)
        {
            user = (user ?? "").Trim();
            if (user.Length < 3 || user.Length > 40) return "El usuario tiene que tener entre 3 y 40 caracteres.";
            if (pw == null || pw.Length < MinPassword) return "La contraseña tiene que tener al menos " + MinPassword + " caracteres.";
            if (pw == Settings.Current.Password) return "Usa una contraseña distinta a la del server de Valheim.";
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            Settings.Current.WebAdminUser = user;
            Settings.Current.WebAdminHash = "pbkdf2-sha256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(Derive(pw, salt, Iterations));
            Settings.Save();
            ForgetAllSessions();   // al cambiar la contraseña, todos los dispositivos vuelven a entrar
            Audit("este PC", "usuario y contraseña de administrador cambiados (" + user + ")");
            return null;
        }

        public static void RemovePassword()
        {
            Settings.Current.WebAdminHash = "";
            Settings.Save();
            ForgetAllSessions();
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
                if (t != null) lock (lk) { LoadSessions(); if (sessions.Remove(Sha(t))) SaveSessions(); }
                var r = Ok("Has salido.");
                r.Extra.Add(ClearCookie(req));
                return r;
            }

            string ip = req.ClientIp;
            if (!Authed(req))
            {
                if (req.Path == "/api/admin/estado") return MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "dentro", false }, { "configurado", HasPassword } });
                if (req.Path == "/api/admin/salir-todos") return Err(401, "Tienes que entrar otra vez.");
                return Err(401, "Tienes que entrar otra vez.");
            }

            if (req.Path == "/api/admin/estado")
            {
                return MiniHttp.Response.JsonOf(200, new Dictionary<string, object>
                {
                    { "dentro", true },
                    { "usuario", User },
                    { "dispositivos", Devices() },
                    { "configurado", true },
                    { "registro", ServerWatch.RecentLines(150) },
                    { "acciones", ReadAudit(30) },
                    { "estado", ServerWatch.Snapshot() },
                });
            }
            if (req.Path == "/api/admin/salir-todos")
            {
                ForgetAllSessions();
                Audit(ip, "cerró la sesión en todos los dispositivos");
                var r = Ok("Sesión cerrada en todos los dispositivos.");
                r.Extra.Add(ClearCookie(req));
                return r;
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
            string pw = null, user = null; bool remember = false;
            try
            {
                var o = Json.ParseObj(req.BodyText); object v;
                if (o.TryGetValue("clave", out v)) pw = v as string;
                if (o.TryGetValue("usuario", out v)) user = v as string;
                if (o.TryGetValue("recordar", out v) && v is bool) remember = (bool)v;
            }
            catch { }
            bool ok = Check(pw) & string.Equals((user ?? "").Trim(), User, StringComparison.OrdinalIgnoreCase);
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
            if (!ok) { Audit(ip, "usuario o contraseña incorrectos"); return Err(401, "Usuario o contraseña incorrectos."); }

            var tb = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(tb);
            string token = BitConverter.ToString(tb).Replace("-", "").ToLowerInvariant();
            var life = remember ? TimeSpan.FromDays(RememberDays) : TimeSpan.FromHours(SessionHours);
            lock (lk)
            {
                LoadSessions();
                foreach (var k in sessions.Where(x => x.Value.Until < DateTime.Now).Select(x => x.Key).ToList()) sessions.Remove(k);
                sessions[Sha(token)] = new Session { Hash = Sha(token), Until = DateTime.Now + life, Ip = ip, Device = DeviceName(req.Header("User-Agent")), Created = DateTime.Now };
                SaveSessions();
            }
            Audit(ip, "ha entrado" + (remember ? " (recordado " + RememberDays + " días)" : ""));
            var r = Ok("Dentro.");
            r.Extra.Add("Set-Cookie: " + CookieName + "=" + token + "; Path=/; HttpOnly; SameSite=Strict; Max-Age=" + (int)life.TotalSeconds + (req.Https ? "; Secure" : ""));
            return r;
        }

        static bool Authed(MiniHttp.Request req)
        {
            if (!HasPassword) return false;
            string t = req.Cookie(CookieName);
            if (string.IsNullOrEmpty(t)) return false;
            lock (lk)
            {
                LoadSessions();
                Session s;
                string h = Sha(t);
                if (!sessions.TryGetValue(h, out s)) return false;
                if (s.Until < DateTime.Now) { sessions.Remove(h); SaveSessions(); return false; }
                return true;
            }
        }

        static string DeviceName(string ua)
        {
            ua = ua ?? "";
            string os = ua.Contains("Android") ? "Android" : ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "Mac" : ua.Contains("Linux") ? "Linux" : "otro";
            string br = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "navegador";
            return br + " en " + os;
        }

        static List<Dictionary<string, object>> Devices()
        {
            lock (lk)
            {
                LoadSessions();
                return sessions.Values.Where(x => x.Until > DateTime.Now).OrderByDescending(x => x.Created).Select(x => new Dictionary<string, object>
                {
                    { "dispositivo", x.Device ?? "" }, { "ip", x.Ip ?? "" }, { "desde", x.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) }, { "hasta", x.Until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                }).ToList();
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
