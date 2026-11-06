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
        const string CookieName = "mh_sesion", DeviceCookie = "mh_equipo";
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
        class PendingTotp { public string Secret; public DateTime Until; }
        static readonly Dictionary<string, PendingTotp> pendingTotp = new Dictionary<string, PendingTotp>(StringComparer.OrdinalIgnoreCase);

        static readonly object lk = new object();
        static Dictionary<string, Session> sessions;
        static readonly Dictionary<string, Fails> fails = new Dictionary<string, Fails>();
        static Fails total = new Fails();
        static string SessionsFile { get { return AppInfo.P("web", "sesiones.json"); } }
        static string AuditFile { get { return Path.Combine(AppInfo.LogDir, "web-admin.log"); } }

        // Lo pone la ventana del launcher: ejecuta "iniciar", "parar", "reiniciar" o "backup" y devuelve qué ha hecho.
        public static Func<string, string> Action;
        public static Func<string, string> Say;   // mensaje al chat del juego; devuelve null si ha llegado (lo pone el launcher)
        public static Func<Dictionary<string, object>> AdminExtras;
        public static Func<string, string> RconExec;   // orden al server por RCON; devuelve la respuesta o lanza excepción (lo pone el launcher)
        public static Func<string, string> Maintain;   // mantenimiento con prueba (mods y Valheim al día) pedido por alguien; devuelve qué va a hacer
        public static Func<string> Health;             // la última revisión de salud, en texto para Discord
        public static Func<string, string, WebResult> RestoreWorld;             // (archivo del backup, quién): restaura el mundo (lo pone el launcher)
        public static Func<List<ModSettings.Change>, string, WebResult> SaveModSettings;   // (cambios, quién): guarda ajustes de mods (lo pone el launcher)

        // Órdenes que se pueden mandar desde la consola web. Nada que borre cosas del mundo o toque inventarios.
        public static readonly string[] ConsoleAllowed = { "players", "banlist", "kick", "ban", "unban", "save", "time", "globalkeys", "serverstats", "logs", "say", "showmessage", "findobjects" };   // backups, mods con versión nueva y tareas (lo pone el launcher)

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
            WebDevices.Forget(u.Name);
            Audit("este PC", "cuenta borrada: " + u.Name);
            return true;
        }

        // Para cuando se pierde el móvil: solo desde el PC del server (launcher o consola).
        public static string RemoveTotp(string name)
        {
            var u = Find(name);
            if (u == null) return "No existe esa cuenta.";
            if (string.IsNullOrEmpty(u.Totp)) return u.Name + " no tiene la verificación en dos pasos activada.";
            u.Totp = "";
            Settings.Save();
            Audit("este PC", "verificación en dos pasos quitada a " + u.Name);
            Discord.Send("🔓 Se ha quitado la verificación en dos pasos de la cuenta **" + Esc(u.Name) + "** desde el PC del server.");
            return null;
        }

        public static bool HasTotp(WebUser u) { return u != null && !string.IsNullOrEmpty(u.Totp); }
        static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|").Replace("@", "@\u200b"); }

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
            bool get = req.Path == "/api/sesion" || req.Path == "/api/admin/estado" || req.Path == "/api/admin/problema" || req.Path == "/api/admin/ajustes" || req.Path == "/api/admin/mapa.png";
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
            else if (req.Path == "/api/admin/buscar")
            {
                // Buscar en el mundo (2.2): para cualquier cuenta, también las de jugador. Solo lee lo que apunta el mod Majinheim Mundo.
                string q = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("objeto", out v)) q = v as string; } catch { }
                q = System.Text.RegularExpressions.Regex.Replace(q ?? "", @"[\x00-\x1f]+", " ").Trim();
                if (q.Length == 0 || q.Length > 60) return Err(400, "Escribe qué buscas (como mucho 60 letras).");
                object found;
                try { found = WorldData.FindItem(q); }
                catch (Exception e) { Log.W("Web: buscar en el mundo: {0}", e.Message); found = new Dictionary<string, object> { { "error", "No he podido leer los datos del mundo." } }; }
                var dict = found as Dictionary<string, object>;
                if (dict != null && !dict.ContainsKey("error"))
                    try { var meta = WorldData.MapMeta(); if (meta != null) dict["mapa"] = meta; } catch (Exception e) { Log.W("Web: mapa de la búsqueda: {0}", e.Message); }
                res = MiniHttp.Response.JsonOf(200, found);
            }
            else if (req.Path == "/api/admin/mapa.png")
            {
                // El fondo del mapa de "Buscar en el mundo": solo lo explorado, para cualquier cuenta.
                WorldData.MapImage m = null;
                try { m = WorldData.GetMap(); } catch (Exception e) { Log.W("Web: mapa de la búsqueda: {0}", e.Message); }
                res = m == null ? Err(404, "Todavía no hay mapa.") : new MiniHttp.Response(200, "image/png", m.Png, false);
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
                var ex = AdminExtras;
                if (ex != null) try { foreach (var kv in ex()) o[kv.Key] = kv.Value; } catch (Exception e) { Log.W("Web: extras del panel: {0}", e.Message); }
                res = MiniHttp.Response.JsonOf(200, o);
            }
            else if (req.Path == "/api/admin/problema")
            {
                // Un problema mandado desde el launcher de jugador (2.4): solo lo ve el administrador.
                var r = SupportDesk.Get(QueryValue(req.Query, "id"));
                res = r == null ? Err(404, "Ese problema ya no está.") : MiniHttp.Response.JsonOf(200, new Dictionary<string, object>
                {
                    { "ok", true }, { "id", r.Id }, { "fecha", r.Fecha }, { "nombre", r.Nombre }, { "texto", r.Texto }, { "pistas", r.Pistas }, { "ia", r.Ia },
                    { "equipo", r.Launcher + " · " + r.Sistema + " · " + r.Equipo + (r.DiscoLibreGb >= 0 ? " · " + r.DiscoLibreGb + " GB libres" : "") },
                    { "juego", !r.Juego ? "no encuentra Valheim" : "Valheim " + (r.JuegoVersion != "" ? Support.CleanVersion(r.JuegoVersion) : "(versión desconocida)") + (r.JuegoUltimaVez != "" ? ", abierto por última vez " + r.JuegoUltimaVez : "") + (r.Cargador ? "" : " · sin cargador de mods") },
                    { "mods", "lista revisión " + r.Revision + ", instalada " + r.Instalada + (r.ModsAlDia ? ", al día" : ", sin terminar de instalar") + (r.ModsUltimaVez != "" ? " · cargados por última vez " + r.ModsUltimaVez : " · nunca se han cargado") },
                    { "registros", new Dictionary<string, object> { { "mods", r.RegistroMods }, { "juego", r.RegistroJuego }, { "launcher", r.RegistroLauncher } } },
                });
            }
            else if (req.Path == "/api/admin/problema-borrar")
            {
                string id = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("id", out v)) id = v as string; } catch { }
                bool done = SupportDesk.Delete(id);
                if (done) Audit(ip, user.Name + " borró el problema " + id);
                res = done ? Ok("Borrado.") : Err(404, "Ese problema ya no está.");
            }
            else if (req.Path == "/api/admin/2fa-preparar")
            {
                // Verificación en dos pasos (2.5): clave nueva para la app; no se guarda hasta que se confirma con un código.
                if (HasTotp(user)) return Err(409, "Ya la tienes activada.");
                string secret = Totp.NewSecret();
                lock (pendingTotp) pendingTotp[user.Name] = new PendingTotp { Secret = secret, Until = DateTime.UtcNow.AddMinutes(10) };
                res = MiniHttp.Response.JsonOf(200, new Dictionary<string, object>
                {
                    { "ok", true }, { "clave", Totp.Grouped(secret) }, { "qr", QrCode.SvgDataUri(Totp.Uri(user.Name, secret)) },
                });
            }
            else if (req.Path == "/api/admin/2fa-activar")
            {
                if (HasTotp(user)) return Err(409, "Ya la tienes activada.");
                PendingTotp p;
                lock (pendingTotp) { if (!pendingTotp.TryGetValue(user.Name, out p) || p.Until < DateTime.UtcNow) p = null; }
                if (p == null) return Err(409, "Han pasado más de 10 minutos. Empieza otra vez con «Activar».");
                if (!Totp.Check(p.Secret, BodyValue(req, "codigo"), user.Name)) return Err(400, "Ese código no vale. Escribe el que sale ahora en la app (y mira que la hora del móvil esté en automático).");
                string prot = Secret.Protect(p.Secret);
                if (prot == null) return Err(500, "No he podido guardar la clave cifrada en este PC.");
                user.Totp = prot;
                lock (pendingTotp) pendingTotp.Remove(user.Name);
                Settings.Save();
                Audit(ip, user.Name + " activó la verificación en dos pasos");
                Discord.Send("🔐 **" + Esc(user.Name) + "** ha activado la verificación en dos pasos en la web del server.");
                res = Ok("Activada. A partir de ahora, al entrar te pedirá también el código de la app.");
            }
            else if (req.Path == "/api/admin/2fa-quitar")
            {
                // Hace falta un código bueno: con una sesión robada no se puede quitar.
                if (!HasTotp(user)) return Err(409, "No la tienes activada.");
                string locked = LockedMessage(ip);
                if (locked != null) return Err(429, locked);
                string secret = Secret.Unprotect(user.Totp);
                if (secret == null) return Err(409, "No puedo leer tu clave en este PC. Quítala desde el launcher del server, pestaña Web.");
                if (!Totp.Check(secret, BodyValue(req, "codigo"), user.Name))
                {
                    Failed(ip);
                    Audit(ip, user.Name + ": código incorrecto al quitar la verificación en dos pasos");
                    return Err(400, "Código incorrecto. Escribe el que sale ahora en la app.");
                }
                user.Totp = "";
                Settings.Save();
                Audit(ip, user.Name + " quitó la verificación en dos pasos");
                Discord.Send("🔓 **" + Esc(user.Name) + "** ha quitado la verificación en dos pasos de su cuenta de la web. Si no has sido tú, cambia la contraseña en el launcher del server (pestaña Web) y cierra las sesiones.");
                res = Ok("Quitada. Ahora se entra solo con la contraseña.");
            }
            else if (req.Path == "/api/admin/restaurar")
            {
                // Volver el mundo a un backup (2.5). Doble confirmación en la web: el botón y escribir RESTAURAR.
                string file = BodyValue(req, "archivo"), confirm = BodyValue(req, "confirmar");
                if (confirm == null || confirm.Trim().ToUpperInvariant() != "RESTAURAR") return Err(400, "Para confirmar, escribe RESTAURAR.");
                if (string.IsNullOrEmpty(file) || file.Length > 120 || file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return Err(400, "Backup desconocido.");
                var f = RestoreWorld;
                if (f == null) return Err(503, "El launcher del server no está listo.");
                WebResult r;
                try { r = f(file, user.Name); }
                catch (Exception e) { r = WebResult.No("No se pudo: " + e.Message); }
                Audit(ip, user.Name + " · restaurar el mundo (" + file + "): " + r.Message);
                res = r.Ok ? Ok(r.Message) : Err(409, r.Message);
            }
            else if (req.Path == "/api/admin/ajustes")
            {
                // Ajustes de los mods (2.5): ?archivo=x.cfg (uno), ?buscar=texto (en todos) o nada (los más usados).
                string file = QueryValue(req.Query, "archivo"), q = QueryValue(req.Query, "buscar");
                if (q != null && q.Length > 60) q = q.Substring(0, 60);
                Dictionary<string, object> o;
                try { o = ModSettings.List(file, q); if (string.IsNullOrEmpty(file) && string.IsNullOrEmpty(q)) o["mods"] = ModSettings.Files(); }
                catch (Exception e) { Log.W("Web: ajustes de mods: {0}", e.Message); o = new Dictionary<string, object> { { "ok", false }, { "mensaje", "No he podido leer los ajustes de los mods." } }; }
                res = MiniHttp.Response.JsonOf(200, o);
            }
            else if (req.Path == "/api/admin/ajustes-guardar")
            {
                var changes = new List<ModSettings.Change>();
                try
                {
                    var o = Json.ParseObj(req.BodyText); object v;
                    if (o.TryGetValue("cambios", out v) && v is System.Collections.IEnumerable && !(v is string))
                        foreach (var x in (System.Collections.IEnumerable)v)
                        {
                            var d = x as Dictionary<string, object>;
                            if (d == null) continue;
                            Func<string, string> g = k => { object y; return d.TryGetValue(k, out y) && y != null ? Convert.ToString(y, CultureInfo.InvariantCulture) : null; };
                            changes.Add(new ModSettings.Change { File = g("archivo"), Section = g("seccion"), Key = g("clave"), Value = g("valor") });
                        }
                }
                catch { }
                if (changes.Count == 0) return Err(400, "No hay cambios.");
                var f = SaveModSettings;
                if (f == null) return Err(503, "El launcher del server no está listo.");
                WebResult r;
                try { r = f(changes, user.Name); }
                catch (Exception e) { r = WebResult.No("No se pudo: " + e.Message); }
                Audit(ip, user.Name + " · ajustes de mods: " + r.Message);
                res = r.Ok ? Ok(r.Message) : Err(409, r.Message);
            }
            else if (req.Path == "/api/admin/salir-todos")
            {
                ForgetAllSessions();
                Audit(ip, user.Name + " cerró la sesión en todos los dispositivos");
                res = Ok("Sesión cerrada en todos los dispositivos.");
                res.Extra.Add(ClearCookie(req));
                return res;
            }
            else if (req.Path == "/api/admin/aviso")
            {
                string text = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("mensaje", out v)) text = v as string; } catch { }
                if (string.IsNullOrWhiteSpace(text)) return Err(400, "Escribe el mensaje.");
                if (text.Length > 200) return Err(400, "Máximo 200 caracteres.");
                var say = Say;
                string err = say == null ? "El launcher del server no está listo." : say(text);
                Audit(ip, user.Name + " · aviso en el juego: " + (err == null ? text : "no llegó (" + err + ")"));
                res = err == null ? Ok("Mensaje enviado al chat del juego.") : Err(409, "No se pudo: " + err);
            }
            else if (req.Path == "/api/admin/consola")
            {
                string cmd = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("orden", out v)) cmd = v as string; } catch { }
                cmd = System.Text.RegularExpressions.Regex.Replace(cmd ?? "", @"[\x00-\x1f]+", " ").Trim();
                if (cmd.Length == 0) return Err(400, "Escribe una orden.");
                if (cmd.Length > 200) return Err(400, "Máximo 200 caracteres.");
                string verb = cmd.Split(' ')[0].ToLowerInvariant();
                if (Array.IndexOf(ConsoleAllowed, verb) < 0) return Err(400, "Esa orden no se puede usar desde la web. Permitidas: " + string.Join(", ", ConsoleAllowed) + ".");
                var exec = RconExec;
                if (exec == null) return Err(503, "La consola no está lista (falta ValheimRcon o el launcher del server).");
                string output, err = null;
                try { output = exec(cmd) ?? ""; }
                catch (Exception e) { output = null; err = e.Message; }
                Audit(ip, user.Name + " · consola: " + cmd + (err != null ? " (falló: " + err + ")" : ""));
                if (err != null) return Err(409, "No se pudo: " + err);
                if (output.Length > 8000) output = output.Substring(0, 8000) + "\n…";
                res = MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "salida", output } });
            }
            else if (req.Path == "/api/admin/jugador")
            {
                string name = null, what = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("nombre", out v)) name = v as string; if (o.TryGetValue("accion", out v)) what = v as string; } catch { }
                if (what != "expulsar" && what != "banear") return Err(400, "Acción desconocida.");
                if (string.IsNullOrWhiteSpace(name)) return Err(400, "Falta el jugador.");
                string id = ServerWatch.SteamIdOf(name);
                if (id == null) return Err(409, "No encuentro a " + name + " entre los conectados.");
                var exec = RconExec;
                if (exec == null) return Err(503, "La consola no está lista (falta ValheimRcon o el launcher del server).");
                string err = null;
                try { exec((what == "banear" ? "ban " : "kick ") + id); }
                catch (Exception e) { err = e.Message; }
                Audit(ip, user.Name + " · " + (what == "banear" ? "baneó" : "expulsó") + " a " + name + (err != null ? " (falló: " + err + ")" : ""));
                if (err != null) return Err(409, "No se pudo: " + err);
                res = Ok(what == "banear" ? name + " está baneado. Para quitarlo: unban " + name + " en la consola." : name + " ha sido expulsado.");
            }
            else if (req.Path == "/api/admin/accion")
            {
                string what = null;
                try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue("accion", out v)) what = v as string; } catch { }
                if (what != "iniciar" && what != "parar" && what != "reiniciar" && what != "backup" && what != "actualizar") return Err(400, "Acción desconocida.");
                var act = Action;
                var maint = Maintain;
                if (what == "actualizar" ? maint == null : act == null) return Err(503, "El launcher del server no está listo.");
                string result;
                try { result = what == "actualizar" ? maint(user.Name + " (web)") : act(what); }
                catch (Exception e) { result = "No se pudo: " + e.Message; }
                Audit(ip, user.Name + " · " + what + ": " + result);
                res = Ok(result);
            }
            else res = Err(404, "No existe.");

            if (renew && ses != null) res.Extra.Add(SessionCookie(req, req.Cookie(CookieName), true));
            return res;
        }

        static string BodyValue(MiniHttp.Request req, string key)
        {
            try { var o = Json.ParseObj(req.BodyText); object v; if (o.TryGetValue(key, out v)) return v as string; } catch { }
            return null;
        }

        internal static string QueryValue(string query, string key)
        {
            foreach (var part in (query ?? "").Split('&'))
            {
                int i = part.IndexOf('=');
                if (i > 0 && part.Substring(0, i) == key) { try { return Uri.UnescapeDataString(part.Substring(i + 1).Replace('+', ' ')); } catch { return null; } }
            }
            return null;
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
                { "dosPasos", u != null && u.Role == RoleAdmin && HasTotp(u) },
            };
        }

        static string LockedMessage(string ip)
        {
            lock (lk)
            {
                var now = DateTime.UtcNow;
                if (total.LockedUntil > now) return "Demasiados intentos fallidos. Prueba dentro de un rato.";
                Fails f;
                if (fails.TryGetValue(ip, out f) && f.LockedUntil > now) return "Demasiados intentos fallidos. Prueba dentro de " + Math.Ceiling((f.LockedUntil - now).TotalMinutes) + " min.";
            }
            return null;
        }

        // Un intento fallido (contraseña o código): 5 seguidos desde la misma IP la bloquean 15 minutos; 20 en una hora, a todas.
        static void Failed(string ip)
        {
            lock (lk)
            {
                var now = DateTime.UtcNow;
                Fails f;
                if (!fails.TryGetValue(ip, out f) || now - f.First > LockFor) fails[ip] = f = new Fails { First = now };
                f.Count++;
                if (f.Count >= MaxFailsPerIp) f.LockedUntil = now + LockFor;
                if (now - total.First > TimeSpan.FromHours(1)) total = new Fails { First = now };
                total.Count++;
                if (total.Count >= MaxFailsTotal) total.LockedUntil = now + LockFor;
            }
        }

        static MiniHttp.Response NeedCode(string msg)
        {
            return MiniHttp.Response.JsonOf(401, new Dictionary<string, object> { { "ok", false }, { "codigo", true }, { "mensaje", msg } });
        }

        static MiniHttp.Response Login(MiniHttp.Request req)
        {
            string ip = req.ClientIp;
            if (!Users.Any(u => !string.IsNullOrEmpty(u.Hash))) return Err(403, "Todavía no hay cuentas. Se crean desde el launcher del server (pestaña Web).");
            string locked = LockedMessage(ip);
            if (locked != null) return Err(429, locked);
            string pw = null, name = null, code = null; bool remember = false;
            try
            {
                var o = Json.ParseObj(req.BodyText); object v;
                if (o.TryGetValue("clave", out v)) pw = v as string;
                if (o.TryGetValue("usuario", out v)) name = v as string;
                if (o.TryGetValue("codigo", out v)) code = v as string;
                if (o.TryGetValue("recordar", out v) && v is bool) remember = (bool)v;
            }
            catch { }
            var user = Check(name, pw);
            if (user == null) { Failed(ip); Audit(ip, "usuario o contraseña incorrectos"); return Err(401, "Usuario o contraseña incorrectos."); }

            // Verificación en dos pasos (2.5): con la contraseña buena, falta el código de la app.
            if (HasTotp(user))
            {
                string secret = Secret.Unprotect(user.Totp);
                if (secret == null)
                {
                    Audit(ip, user.Name + ": no se puede leer su clave de verificación en dos pasos");
                    return Err(409, "No puedo leer la verificación en dos pasos de esta cuenta en este PC. Quítala en el launcher del server (pestaña Web) y vuelve a activarla.");
                }
                if (string.IsNullOrWhiteSpace(code)) return NeedCode("Escribe el código de 6 cifras que sale en tu app de verificación.");
                if (!Totp.Check(secret, code, user.Name))
                {
                    Failed(ip);
                    Audit(ip, user.Name + ": código de verificación incorrecto");
                    return NeedCode("Código incorrecto o caducado. Escribe el que sale ahora en la app.");
                }
            }
            lock (lk) fails.Remove(ip);

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
            Audit(ip, user.Name + " ha entrado (" + (user.Role == RoleAdmin ? "administrador" : "jugador") + (HasTotp(user) ? ", con código" : "") + (remember ? ", recordado en este dispositivo" : "") + ")");
            var r = MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "mensaje", "Dentro." }, { "usuario", user.Name }, { "rol", user.Role } });
            r.Extra.Add(SessionCookie(req, token, remember));
            if (user.Role == RoleAdmin) CheckDevice(req, r, user);
            return r;
        }

        // Aviso en Discord cuando un administrador entra desde un navegador desde el que no había entrado nunca (2.5).
        // El navegador se reconoce por una cookie propia que dura 400 días; en Discord no sale la IP.
        static void CheckDevice(MiniHttp.Request req, MiniHttp.Response r, WebUser user)
        {
            string dev = req.Cookie(DeviceCookie);
            if (dev == null || dev.Length != 64 || !Regex.IsMatch(dev, "^[0-9a-f]{64}$"))
            {
                var b = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
                dev = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            }
            r.Extra.Add("Set-Cookie: " + DeviceCookie + "=" + dev + "; Path=/; HttpOnly; SameSite=Strict; Max-Age=" + 400 * 86400 + (req.Https ? "; Secure" : ""));
            string device = DeviceName(req.Header("User-Agent"));
            if (WebDevices.Seen(user.Name, Sha(dev), device)) return;
            Audit(req.ClientIp, user.Name + " ha entrado desde un dispositivo nuevo (" + device + ")");
            Discord.Send("🔑 **" + Esc(user.Name) + "** ha entrado en el panel de control de la web desde un dispositivo nuevo (" + device + "). " +
                "Si has sido tú, no hace falta hacer nada. Si no: en el launcher del server, pestaña Web, cambia la contraseña de esa cuenta y pulsa «Cerrar sesiones».");
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
                try { Directory.CreateDirectory(AppInfo.LogDir); Log.Roll(AuditFile); File.AppendAllText(AuditFile, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
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
