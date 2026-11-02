// Cuentas de la web: entrar con usuario y contraseña. Solo los administradores ven los controles del server
// (encender, apagar, reiniciar, backup), el registro, las IPs y los dispositivos; los jugadores ven lo mismo que un invitado.
// Seguridad: contraseñas solo como hash PBKDF2 (nunca en claro), sesión en cookie HttpOnly + SameSite=Strict
// (+ Secure por HTTPS) guardada en disco solo como huella SHA-256, cabecera propia obligatoria en cada POST,
// bloqueo tras fallos y registro de todo lo que se hace.
// Compilado con el csc de .NET Framework 4.8 (C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Majinheim
{
    public static class WebAdmin
    {
        public const string RoleAdmin = "admin", RolePlayer = "jugador";
        public const int MinAdminPassword = 8;
        const string CookieName = "mh_sesion";
        const int SessionHours = 12, RememberDays = 30, Iterations = 150000, MaxSessions = 100;
        const int MaxFailsPerIp = 5, MaxFailsTotal = 20;
        static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

        // Sesiones guardadas en disco (solo la huella SHA-256 del token, nunca el token): "recordarme" sobrevive a reinicios.
        // Fechas en UTC (el serializador JSON las devuelve en UTC).
        public class Session
        {
            public string Hash { get; set; }
            public string User { get; set; }
            public bool Remember { get; set; }
            public DateTime Created { get; set; }
            public DateTime Until { get; set; }
            public DateTime Renewed { get; set; }
            public string Ip { get; set; }
            public string Device { get; set; }
        }
        class Fails { public int Count; public DateTime First, LockedUntil; }

        static readonly object lk = new object();
        static Dictionary<string, Session> sessions;
        static readonly Dictionary<string, Fails> fails = new Dictionary<string, Fails>();
        static Fails total = new Fails();
        static string SessionsFile { get { return AppInfo.P("web", "sesiones.json"); } }
        static string AuditFile { get { return Path.Combine(AppInfo.LogDir, "web-admin.log"); } }

        // Lo pone la ventana del launcher: ejecuta "iniciar", "parar", "reiniciar" o "backup" y devuelve qué ha hecho.
        public static Func<string, string> Action;

        static List<WebUser> Users { get { var s = Settings.Current; if (s.WebUsers == null) s.WebUsers = new List<WebUser>(); return s.WebUsers; } }
        static WebUser Find(string name)
        {
            name = (name ?? "").Trim();
            return Users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // Hay algún administrador con contraseña: los controles de la web están activados.
        public static bool HasAdmin { get { return Users.Any(u => u.Role == RoleAdmin && !string.IsNullOrEmpty(u.Hash)); } }
        public static List<WebUser> List() { return Users.OrderBy(u => u.Role == RoleAdmin ? 0 : 1).ThenBy(u => u.Name).ToList(); }

        // ------------------------------------------------------------ cuentas

        // Crea o cambia una cuenta. pw == null: deja la contraseña como estaba (solo cambia el papel).
        // El mínimo de longitud solo se exige al poner la contraseña de un administrador.
        public static string SetUser(string name, string role, string pw)
        {
            name = (name ?? "").Trim();
            if (!Regex.IsMatch(name, @"^[\p{L}\p{N}_.\- ]{2,32}$")) return "El usuario tiene que tener entre 2 y 32 letras o números (sin símbolos raros).";
            if (role != RoleAdmin && role != RolePlayer) return "Papel desconocido: " + role;
            var u = Find(name);
            if (pw == null && (u == null || string.IsNullOrEmpty(u.Hash))) return "Falta la contraseña.";
            if (pw != null)
            {
                if (pw.Length == 0) return "Falta la contraseña.";
                if (role == RoleAdmin && pw.Length < MinAdminPassword) return "La contraseña de un administrador tiene que tener al menos " + MinAdminPassword + " caracteres.";
                if (role == RoleAdmin && pw == Settings.Current.Password) return "Usa una contraseña distinta a la del server de Valheim.";
            }
            bool isNew = u == null;
            if (isNew) { u = new WebUser { Name = name }; Users.Add(u); }
            bool changed = u.Role != role || pw != null;
            u.Role = role;
            if (pw != null) u.Hash = MakeHash(pw);
            Settings.Save();
            if (changed && !isNew) ForgetSessions(u.Name);   // con contraseña o papel nuevos, sus dispositivos vuelven a entrar
            Audit("este PC", (isNew ? "cuenta creada: " : "cuenta cambiada: ") + u.Name + " (" + role + (pw != null ? ", contraseña nueva" : "") + ")");
            return null;
        }

        public static bool RemoveUser(string name)
        {
            var u = Find(name);
            if (u == null) return false;
            Users.Remove(u);
            Settings.Save();
            ForgetSessions(u.Name);
            Audit("este PC", "cuenta borrada: " + u.Name);
            return true;
        }

        static string MakeHash(string pw)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            return "pbkdf2-sha256$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(Derive(pw, salt, Iterations));
        }

        static byte[] Derive(string pw, byte[] salt, int iter)
        {
            using (var k = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pw), salt, iter, HashAlgorithmName.SHA256)) return k.GetBytes(32);
        }

        static readonly string Dummy = MakeHash("no-es-una-cuenta");

        // Siempre calcula un hash (aunque el usuario no exista) para que el tiempo de respuesta no delate qué usuarios hay.
        static WebUser Check(string name, string pw)
        {
            var u = Find(name);
            string stored = u != null && !string.IsNullOrEmpty(u.Hash) ? u.Hash : Dummy;
            var p = stored.Split('$');
            int iter;
            if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out iter)) return null;
            var want = Convert.FromBase64String(p[3]);
            var got = Derive(pw ?? "", Convert.FromBase64String(p[2]), iter);
            int diff = want.Length ^ got.Length;
            for (int i = 0; i < Math.Min(want.Length, got.Length); i++) diff |= want[i] ^ got[i];
            return diff == 0 && pw != null && u != null && stored == u.Hash ? u : null;
        }

        // ------------------------------------------------------------ sesiones

        static void LoadSessions()
        {
            if (sessions != null) return;
            sessions = new Dictionary<string, Session>();
            try
            {
                foreach (var x in Json.Load<List<Session>>(SessionsFile))
                    if (x != null && !string.IsNullOrEmpty(x.Hash) && x.Until > DateTime.UtcNow) sessions[x.Hash] = x;
            }
            catch (Exception e) { Log.W("Web: no pude leer las sesiones guardadas: {0}", e.Message); }
        }

        static void SaveSessions()
        {
            try { Json.Save(SessionsFile, sessions.Values.Where(x => x.Until > DateTime.UtcNow).ToList()); }
            catch (Exception e) { Log.W("Web: no pude guardar las sesiones: {0}", e.Message); }
        }

        static string Sha(string t)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(t ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        public static void ForgetAllSessions() { lock (lk) { LoadSessions(); sessions.Clear(); SaveSessions(); } }

        static void ForgetSessions(string user)
        {
            lock (lk)
            {
                LoadSessions();
                foreach (var k in sessions.Where(x => string.Equals(x.Value.User, user, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList()) sessions.Remove(k);
                SaveSessions();
            }
        }

        public static int SessionCount() { lock (lk) { LoadSessions(); return sessions.Values.Count(x => x.Until > DateTime.UtcNow); } }

        // Sesión válida de la petición (o null). Las recordadas se alargan solas con el uso; renew = hay que reenviar la cookie.
        static Session Current(MiniHttp.Request req, out WebUser user, out bool renew)
        {
            user = null; renew = false;
            string t = req.Cookie(CookieName);
            if (string.IsNullOrEmpty(t) || t.Length != 64) return null;
            lock (lk)
            {
                LoadSessions();
                Session s;
                string h = Sha(t);
                if (!sessions.TryGetValue(h, out s)) return null;
                var now = DateTime.UtcNow;
                user = Find(s.User);
                if (s.Until < now || user == null || string.IsNullOrEmpty(user.Hash)) { sessions.Remove(h); SaveSessions(); user = null; return null; }
                if (s.Remember && now - s.Renewed > TimeSpan.FromHours(12))
                {
                    s.Until = now.AddDays(RememberDays); s.Renewed = now; s.Ip = req.ClientIp;
                    SaveSessions();
                    renew = true;
                }
                return s;
            }
        }

        static string SessionCookie(MiniHttp.Request req, string token, bool remember)
        {
            return "Set-Cookie: " + CookieName + "=" + token + "; Path=/; HttpOnly; SameSite=Strict" + (remember ? "; Max-Age=" + RememberDays * 86400 : "") + (req.Https ? "; Secure" : "");
        }
        static string ClearCookie(MiniHttp.Request req) { return "Set-Cookie: " + CookieName + "=; Path=/; HttpOnly; SameSite=Strict; Max-Age=0" + (req.Https ? "; Secure" : ""); }

        // ------------------------------------------------------------ peticiones

        public static MiniHttp.Response Handle(MiniHttp.Request req)
        {
            bool get = req.Path == "/api/sesion" || req.Path == "/api/admin/estado";
            if (req.Method == "POST" && req.Header("X-Majinheim") != "1") return Err(403, "Petición no permitida.");
            if (get ? req.Method != "GET" && req.Method != "HEAD" : req.Method != "POST") return Err(405, "No permitido.");

            if (req.Path == "/api/entrar") return Login(req);

            WebUser user; bool renew;
            var ses = Current(req, out user, out renew);
            string ip = req.ClientIp;

            if (req.Path == "/api/salir")
            {
                string t = req.Cookie(CookieName);
                if (t != null) lock (lk) { LoadSessions(); if (sessions.Remove(Sha(t))) SaveSessions(); }
                if (user != null) Audit(ip, user.Name + " ha salido");
                var r = Ok("Has salido.");
                r.Extra.Add(ClearCookie(req));
                return r;
            }

            MiniHttp.Response res;
            if (req.Path == "/api/sesion") res = MiniHttp.Response.JsonOf(200, Who(user, ses));
            else if (user == null)
            {
                if (req.Path == "/api/admin/estado") res = MiniHttp.Response.JsonOf(200, Who(null, null));
                else res = Err(401, "Tienes que entrar otra vez.");
            }
            else if (user.Role != RoleAdmin)
            {
                // Un jugador no ve nada sensible: ni registro, ni IPs, ni dispositivos.
                if (req.Path == "/api/admin/estado") res = MiniHttp.Response.JsonOf(200, Who(user, ses));
                else res = Err(403, "Tu cuenta no tiene permiso para esto.");
            }
            else if (req.Path == "/api/admin/estado")
            {
                var o = Who(user, ses);
                o["registro"] = ServerWatch.RecentLines(150);
                o["acciones"] = ReadAudit(30);
                o["dispositivos"] = Devices(req);
                o["estado"] = ServerWatch.Snapshot();
                res = MiniHttp.Response.JsonOf(200, o);
            }
            else if (req.Path == "/api/admin/salir-todos")
            {
                ForgetAllSessions();
                Audit(ip, user.Name + " cerró la sesión en todos los dispositivos");
                res = Ok("Sesión cerrada en todos los dispositivos.");
                res.Extra.Add(ClearCookie(req));
                return res;
            }
            else if (req.Path == "/api/admin/accion")
            {
                string what = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("accion", out v)) what = v as string; } catch { }
                if (what != "iniciar" && what != "parar" && what != "reiniciar" && what != "backup") return Err(400, "Acción desconocida.");
                var act = Action;
                if (act == null) return Err(503, "El launcher del server no está listo.");
                string result;
                try { result = act(what); }
                catch (Exception e) { result = "No se pudo: " + e.Message; }
                Audit(ip, user.Name + " · " + what + ": " + result);
                res = Ok(result);
            }
            else res = Err(404, "No existe.");

            if (renew && ses != null) res.Extra.Add(SessionCookie(req, req.Cookie(CookieName), true));
            return res;
        }

        static Dictionary<string, object> Who(WebUser u, Session s)
        {
            return new Dictionary<string, object>
            {
                { "configurado", HasAdmin },
                { "cuentas", Users.Any(x => !string.IsNullOrEmpty(x.Hash)) },
                { "dentro", u != null },
                { "usuario", u != null ? u.Name : null },
                { "rol", u != null ? u.Role : null },
                { "recordado", s != null && s.Remember },
            };
        }

        static MiniHttp.Response Login(MiniHttp.Request req)
        {
            string ip = req.ClientIp;
            if (!Users.Any(u => !string.IsNullOrEmpty(u.Hash))) return Err(403, "Todavía no hay cuentas. Se crean desde el launcher del server (pestaña Web).");
            lock (lk)
            {
                var now = DateTime.UtcNow;
                if (total.LockedUntil > now) return Err(429, "Demasiados intentos fallidos. Prueba dentro de un rato.");
                Fails f;
                if (fails.TryGetValue(ip, out f) && f.LockedUntil > now) return Err(429, "Demasiados intentos fallidos. Prueba dentro de " + Math.Ceiling((f.LockedUntil - now).TotalMinutes) + " min.");
            }
            string pw = null, name = null; bool remember = false;
            try
            {
                var o = Json.ParseObj(req.BodyText); object v;
                if (o.TryGetValue("clave", out v)) pw = v as string;
                if (o.TryGetValue("usuario", out v)) name = v as string;
                if (o.TryGetValue("recordar", out v) && v is bool) remember = (bool)v;
            }
            catch { }
            var user = Check(name, pw);
            lock (lk)
            {
                var now = DateTime.UtcNow;
                if (user == null)
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
            if (user == null) { Audit(ip, "usuario o contraseña incorrectos"); return Err(401, "Usuario o contraseña incorrectos."); }

            var tb = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(tb);
            string token = BitConverter.ToString(tb).Replace("-", "").ToLowerInvariant();
            lock (lk)
            {
                LoadSessions();
                var now = DateTime.UtcNow;
                foreach (var k in sessions.Where(x => x.Value.Until < now).Select(x => x.Key).ToList()) sessions.Remove(k);
                while (sessions.Count >= MaxSessions) sessions.Remove(sessions.OrderBy(x => x.Value.Renewed).First().Key);
                sessions[Sha(token)] = new Session
                {
                    Hash = Sha(token), User = user.Name, Remember = remember, Created = now, Renewed = now, Ip = ip,
                    Until = remember ? now.AddDays(RememberDays) : now.AddHours(SessionHours), Device = DeviceName(req.Header("User-Agent")),
                };
                SaveSessions();
            }
            Audit(ip, user.Name + " ha entrado (" + (user.Role == RoleAdmin ? "administrador" : "jugador") + (remember ? ", recordado en este dispositivo" : "") + ")");
            var r = MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "mensaje", "Dentro." }, { "usuario", user.Name }, { "rol", user.Role } });
            r.Extra.Add(SessionCookie(req, token, remember));
            return r;
        }

        static string DeviceName(string ua)
        {
            ua = ua ?? "";
            string os = ua.Contains("Android") ? "Android" : ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "Mac" : ua.Contains("Linux") ? "Linux" : "otro sistema";
            string br = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "Navegador";
            return br + " en " + os;
        }

        static List<Dictionary<string, object>> Devices(MiniHttp.Request req)
        {
            string mine = Sha(req.Cookie(CookieName));
            lock (lk)
            {
                LoadSessions();
                return sessions.Values.Where(x => x.Until > DateTime.UtcNow).OrderByDescending(x => x.Renewed).Select(x => new Dictionary<string, object>
                {
                    { "usuario", x.User ?? "" }, { "dispositivo", x.Device ?? "" }, { "ip", x.Ip ?? "" },
                    { "desde", x.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
                    { "recordado", x.Remember }, { "este", x.Hash == mine },
                }).ToList();
            }
        }

        static MiniHttp.Response Ok(string msg) { return MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "mensaje", msg } }); }
        static MiniHttp.Response Err(int code, string msg) { return MiniHttp.Response.JsonOf(code, new Dictionary<string, object> { { "ok", false }, { "mensaje", msg } }); }

        // ------------------------------------------------------------ registro de acciones

        static readonly object auditLk = new object();

        public static void Audit(string ip, string what)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + ip.Replace("\t", " ") + "\t" + what.Replace("\t", " ").Replace("\n", " ");
            lock (auditLk)
                try { Directory.CreateDirectory(AppInfo.LogDir); File.AppendAllText(AuditFile, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
            Log.W("Web: {0} ({1})", what, ip);
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
