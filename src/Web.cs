// Panel web del server (dentro de MajinheimServer.exe). Fase 1: estado del server, sin botones de administración.
// La web solo escucha en este PC (127.0.0.1). Desde internet se llega a través de Caddy, que pone el HTTPS.
// Compilado con el csc de .NET Framework 4.8 (C# 5): sin $"", sin ?., sin nameof.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Majinheim
{
    public static class WebPanel
    {
        public const string Version = "0.4.0";
        static MiniHttp http;
        public static string Error { get; private set; }
        public static bool Running { get { return http != null; } }
        public static int Port { get { return Settings.Current.WebPort > 0 ? Settings.Current.WebPort : 8456; } }
        public static string LocalUrl { get { return "http://127.0.0.1:" + Port + "/"; } }
        public static string PublicUrl { get { return "https://" + PublicHost + "/"; } }
        public static string PublicHost
        {
            get
            {
                string h = (Settings.Current.PublicHost ?? "").Trim();
                h = Regex.Replace(h, @"^https?://", "", RegexOptions.IgnoreCase).TrimEnd('/');
                int c = h.IndexOf(':'); if (c > 0) h = h.Substring(0, c);
                return h != "" ? h : AppInfo.DefaultHost;
            }
        }

        // Arranca o para la web y Caddy según los ajustes. Se puede llamar las veces que haga falta.
        public static void Apply()
        {
            var s = Settings.Current;
            ServerWatch.Start();
            if (s.WebEnabled && http == null)
            {
                try { http = new MiniHttp(Port, Route); http.Start(); Error = null; Log.W("Panel web en marcha en {0}", LocalUrl); }
                catch (Exception e) { http = null; Error = "No pude abrir el puerto " + Port + ": " + e.Message; Log.W("ERROR: panel web: {0}", Error); }
            }
            else if (!s.WebEnabled && http != null) { http.Stop(); http = null; Log.W("Panel web apagado."); }
            if (s.WebEnabled && s.WebPublic && http != null) { if (Caddy.Installed) Caddy.Start(); else Caddy.InstallInBackground(); }
            else Caddy.Stop();
        }

        public static void Shutdown() { Caddy.Stop(); if (http != null) { http.Stop(); http = null; } }

        // ------------------------------------------------------------ rutas

        static readonly Dictionary<string, string[]> Files = new Dictionary<string, string[]>
        {
            { "/", new[] { "index.html", "text/html; charset=utf-8" } },
            { "/app.js", new[] { "app.js", "application/javascript; charset=utf-8" } },
            { "/estilo.css", new[] { "estilo.css", "text/css; charset=utf-8" } },
            { "/icono.svg", new[] { "icono.svg", "image/svg+xml" } },
            { "/admin.html", new[] { "admin.html", "text/html; charset=utf-8" } },
            { "/admin.js", new[] { "admin.js", "application/javascript; charset=utf-8" } },
            { "/estadisticas.html", new[] { "estadisticas.html", "text/html; charset=utf-8" } },
            { "/estadisticas.js", new[] { "estadisticas.js", "application/javascript; charset=utf-8" } },
        };

        static MiniHttp.Response Route(MiniHttp.Request req)
        {
            string path = req.Path;
            if (path.StartsWith("/api/admin/") || path == "/api/entrar" || path == "/api/salir" || path == "/api/sesion") return WebAdmin.Handle(req);
            if (req.Method == "POST") return MiniHttp.Response.Text(405, "Solo lectura");
            if (path == "/api/estado") return MiniHttp.Response.JsonOf(ServerWatch.Snapshot(), true);
            if (path == "/salud") return MiniHttp.Response.Text(200, "ok");
            if (path == "/robots.txt") return MiniHttp.Response.Text(200, "User-agent: *\nDisallow: /\n");   // que los buscadores no guarden la página (salen nombres de jugadores)
            if (path == "/api/estadisticas") return MiniHttp.Response.JsonOf(ServerWatch.Stats(), true);
            if (path == "/admin") path = "/admin.html";
            if (path == "/estadisticas") path = "/estadisticas.html";
            string[] f;
            if (Files.TryGetValue(path, out f))
            {
                byte[] body = Resource(f[0]);
                if (body != null) return new MiniHttp.Response(200, f[1], body, false);
            }
            return MiniHttp.Response.Text(404, "No existe");
        }

        // Las páginas van dentro del .exe (build.bat las mete con /resource). Si hay copia en web\ junto al .exe, manda esa (para retocarlas sin compilar).
        static byte[] Resource(string name)
        {
            string local = AppInfo.P("web", name);
            if (File.Exists(local)) return File.ReadAllBytes(local);
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (st == null) return null;
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // Comprueba si la web responde por internet (HTTPS). Devuelve true si va bien; msg explica el resultado.
        // Desde casa muchos routers no dejan abrir la dirección pública (no hacen "NAT loopback"): si eso falla, se comprueba
        // por partes: el HTTPS de este PC con su certificado, que la DDNS apunte a la IP de casa y las visitas que han
        // llegado de internet según el registro de Caddy.
        public static bool CheckPublic(out string msg)
        {
            try
            {
                string r = Http.GetString(PublicUrl + "salud", 15000);
                msg = r.Trim() == "ok" ? "Funciona: " + PublicUrl + " responde por HTTPS." : "Desde internet responde algo raro.";
                return r.Trim() == "ok";
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.TrustFailure || e.Status == WebExceptionStatus.SecureChannelFailure)
                { msg = "El certificado HTTPS aún no está listo (Caddy lo pide la primera vez; espera un minuto)."; return false; }
            }
            catch (Exception) { }

            string local = LocalHttps();
            if (local != null) { msg = "El HTTPS de este PC no funciona: " + local + "."; return false; }
            if (!Net.SamePublicIp(PublicHost)) { msg = PublicHost + " no apunta a la IP de casa: revisa la DDNS en el router."; return false; }
            DateTime last;
            int n = ExternalVisits(TimeSpan.FromHours(24), out last);
            const string inside = "\nDesde este PC no se puede abrir la dirección pública porque el router no lo permite dentro de casa (es normal).";
            if (n > 0)
            {
                msg = "Funciona: el HTTPS y la DDNS están bien, y en las últimas 24 h han llegado " + n + (n == 1 ? " visita" : " visitas") +
                    " desde internet (la última, a las " + last.ToString("HH:mm") + ")." + inside;
                return true;
            }
            msg = "El HTTPS y la DDNS están bien, pero hoy no ha llegado ninguna visita de internet. Ábrela desde el móvil con datos (sin wifi): " +
                PublicUrl + ". Si no carga, revisa que el router abra TCP 443 y 80 hacia este PC." + inside;
            return false;
        }

        // Pide /salud al Caddy de este PC con el nombre público, comprobando su certificado. null si va bien, o el problema.
        static string LocalHttps()
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(IPAddress.Loopback, 443, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000)) return "no responde en el puerto 443";
                    c.EndConnect(ar);
                    c.ReceiveTimeout = 10000; c.SendTimeout = 10000;
                    using (var ssl = new System.Net.Security.SslStream(c.GetStream(), false))
                    {
                        ssl.AuthenticateAsClient(PublicHost, null, System.Security.Authentication.SslProtocols.Tls12, true);   // falla si el certificado no vale para ese nombre
                        var req = Encoding.ASCII.GetBytes("GET /salud HTTP/1.1\r\nHost: " + PublicHost + "\r\nConnection: close\r\n\r\n");
                        ssl.Write(req, 0, req.Length);
                        string resp = new StreamReader(ssl, Encoding.UTF8).ReadToEnd();
                        return resp.StartsWith("HTTP/1.1 200") && resp.TrimEnd().EndsWith("ok") ? null : "responde algo raro";
                    }
                }
            }
            catch (System.Security.Authentication.AuthenticationException) { return "el certificado aún no está listo (espera un minuto)"; }
            catch (Exception e) { return e.GetBaseException().Message; }
        }

        // Visitas desde internet (no desde la red de casa) que han recibido una página, según caddy-visitas.log.
        static int ExternalVisits(TimeSpan window, out DateTime last)
        {
            last = DateTime.MinValue;
            string f = Path.Combine(AppInfo.LogDir, "caddy-visitas.log");
            if (!File.Exists(f)) return 0;
            int n = 0;
            var from = DateTime.UtcNow - window;
            string[] lines;
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) lines = sr.ReadToEnd().Split('\n');
            foreach (var line in lines)
            {
                var ip = Regex.Match(line, "\"remote_ip\":\"([^\"]+)\"");
                var ts = Regex.Match(line, "\"ts\":([0-9.]+)");
                if (!ip.Success || !ts.Success || !line.Contains("\"status\":200")) continue;
                string a = ip.Groups[1].Value.ToLowerInvariant();
                if (Net.IsPrivate(a) || a == "::1" || a.StartsWith("fe80") || a.StartsWith("fc") || a.StartsWith("fd")) continue;   // red de casa o este PC
                var t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(double.Parse(ts.Groups[1].Value, CultureInfo.InvariantCulture));
                if (t < from) continue;
                n++;
                if (t.ToLocalTime() > last) last = t.ToLocalTime();
            }
            return n;
        }
    }

    // ---------------------------------------------------------------- servidor HTTP mínimo (solo GET, solo 127.0.0.1)

    public class MiniHttp
    {
        public class Request
        {
            public string Method, Path, Query = "";
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public byte[] Body = new byte[0];
            public string Header(string name) { string v; return Headers.TryGetValue(name, out v) ? v : ""; }
            public string BodyText { get { return Encoding.UTF8.GetString(Body); } }
            // Detrás de Caddy, la IP real llega en X-Forwarded-For (Caddy la pone él; aquí solo entra lo que viene de este PC).
            public string ClientIp { get { string f = Header("X-Forwarded-For"); return f != "" ? f.Split(',')[0].Trim() : "este PC"; } }
            public bool Https { get { return Header("X-Forwarded-Proto") == "https"; } }
            public string Cookie(string name)
            {
                foreach (var part in Header("Cookie").Split(';'))
                {
                    int i = part.IndexOf('=');
                    if (i > 0 && part.Substring(0, i).Trim() == name) return part.Substring(i + 1).Trim();
                }
                return null;
            }
        }

        public class Response
        {
            public int Code; public string Type; public byte[] Body; public bool NoCache;
            public readonly List<string> Extra = new List<string>();
            public Response(int code, string type, byte[] body, bool noCache) { Code = code; Type = type; Body = body; NoCache = noCache; }
            public static Response JsonOf(object o, bool noCache) { return JsonOf(200, o); }
            public static Response JsonOf(int code, object o)
            {
                return new Response(code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Json.Str(o)), true);
            }
            public static Response Text(int code, string text) { return new Response(code, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), true); }
        }

        const int MaxBody = 4096;
        readonly int port;
        readonly Func<Request, Response> route;
        TcpListener listener;
        volatile bool running;

        public MiniHttp(int port, Func<Request, Response> route) { this.port = port; this.route = route; }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            running = true;
            var t = new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        var c = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(_ => Handle(c));
                    }
                    catch (Exception e) { if (running) Log.W("Web: error aceptando conexión: {0}", e.Message); }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void Stop() { running = false; try { listener.Stop(); } catch { } }

        static readonly string[] Codes = { "200 OK", "400 Bad Request", "401 Unauthorized", "403 Forbidden", "404 Not Found", "405 Method Not Allowed", "409 Conflict", "413 Payload Too Large", "429 Too Many Requests", "500 Internal Server Error", "502 Bad Gateway", "503 Service Unavailable" };

        void Handle(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
                    var ns = c.GetStream();
                    var req = Read(ns);
                    if (req == null) { Send(ns, Response.Text(400, "Petición no válida"), false); return; }
                    bool isHead = req.Method == "HEAD";
                    if (req.Method != "GET" && !isHead && req.Method != "POST") { Send(ns, Response.Text(405, "No permitido"), false); return; }
                    string full = req.Path + (req.Query != "" ? "?" + req.Query : "");
                    if (req.Path == MapProxy.Prefix) { Redirect(ns, MapProxy.Prefix + "/"); return; }
                    if (req.Path.StartsWith(MapProxy.Prefix + "/"))
                    {
                        if (req.Method == "POST") { Send(ns, Response.Text(405, "El mapa es solo para mirar"), false); return; }
                        MapProxy.Forward(ns, full.Substring(MapProxy.Prefix.Length), isHead);
                        return;
                    }
                    Response r;
                    try { r = route(req); }
                    catch (Exception e) { Log.W("Web: error en {0}: {1}", req.Path, e.Message); r = Response.Text(500, "Error interno"); }
                    Send(ns, r, isHead);
                }
                catch { }
            }
        }

        // Cabeceras hasta la línea en blanco (máx. 8 KB) y cuerpo con Content-Length (máx. 4 KB).
        static Request Read(NetworkStream ns)
        {
            var buf = new byte[8192 + MaxBody];
            int n = 0, end = -1;
            while (end < 0)
            {
                if (n >= 8192) return null;
                int r = ns.Read(buf, n, 8192 - n);
                if (r <= 0) return null;
                n += r;
                for (int i = 3; i < n; i++) if (buf[i - 3] == 13 && buf[i - 2] == 10 && buf[i - 1] == 13 && buf[i] == 10) { end = i + 1; break; }
            }
            var lines = Encoding.ASCII.GetString(buf, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[1].StartsWith("/")) return null;
            var req = new Request { Method = parts[0] };
            int q = parts[1].IndexOf('?');
            req.Path = q >= 0 ? parts[1].Substring(0, q) : parts[1];
            if (q >= 0) req.Query = parts[1].Substring(q + 1);
            for (int i = 1; i < lines.Length; i++)
            {
                int k = lines[i].IndexOf(':');
                if (k > 0) req.Headers[lines[i].Substring(0, k).Trim()] = lines[i].Substring(k + 1).Trim();
            }
            int len;
            if (int.TryParse(req.Header("Content-Length"), out len) && len > 0)
            {
                if (len > MaxBody) return null;
                int have = n - end;
                while (have < len)
                {
                    int r = ns.Read(buf, end + have, len - have);
                    if (r <= 0) return null;
                    have += r;
                }
                req.Body = new byte[len];
                Array.Copy(buf, end, req.Body, 0, len);
            }
            return req;
        }

        static void Redirect(NetworkStream ns, string to)
        {
            var hb = Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: " + to + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            ns.Write(hb, 0, hb.Length);
        }

        public static void Send(NetworkStream ns, Response r, bool headOnly)
        {
            string status = Codes.FirstOrDefault(x => x.StartsWith(r.Code.ToString())) ?? (r.Code + " Error");
            var h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            h.Append("Content-Type: ").Append(r.Type).Append("\r\n");
            h.Append("Content-Length: ").Append(r.Body.Length).Append("\r\n");
            h.Append("Cache-Control: ").Append(r.NoCache ? "no-store" : "no-cache").Append("\r\n");
            h.Append("X-Content-Type-Options: nosniff\r\n");
            h.Append("Referrer-Policy: no-referrer\r\n");
            h.Append("X-Frame-Options: DENY\r\n");
            h.Append("X-Robots-Tag: noindex, nofollow\r\n");
            h.Append("Content-Security-Policy: default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n");
            foreach (var x in r.Extra) h.Append(x).Append("\r\n");
            h.Append("Connection: close\r\n\r\n");
            var hb = Encoding.UTF8.GetBytes(h.ToString());
            ns.Write(hb, 0, hb.Length);
            if (!headOnly) ns.Write(r.Body, 0, r.Body.Length);
            ns.Flush();
        }
    }

    // ---------------------------------------------------------------- mapa (mod WebMap del server)

    // El mod WebMap (Mubs/WebMap, solo en el server) sirve su mapa en 127.0.0.1:3000. Lo enseñamos bajo /mapa/
    // de nuestra web, solo lectura: aquí no pasa ningún POST, así que nadie puede cambiar nada del mapa desde fuera.
    static class MapProxy
    {
        public const string Prefix = "/mapa";
        static int Port { get { return Settings.Current.MapPort > 0 ? Settings.Current.MapPort : 3000; } }

        // Las páginas del mod piden sus datos a la raíz ("/state", "/map.jpg"...). Con esto los piden a /mapa/...
        // "base" va relativo a "api" (el mod pide api + base), así que es "/map.jpg", no "/mapa/map.jpg".
        const string SiteConfig = "window.XNV = { api: \"/mapa\", base: \"/map.jpg\", brand: \"Majinheim\", links: [{ label: \"Estado del server\", href: \"/\" }] };\n";

        static readonly string[] PassHeaders = { "Content-Type", "Cache-Control", "Last-Modified", "ETag", "Expires" };

        public static bool Up()
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(IPAddress.Loopback, Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(300)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        public static void Forward(NetworkStream ns, string target, bool head)
        {
            string probe = Uri.UnescapeDataString(target);
            if (probe.Contains("..") || probe.Contains("\\") || probe.Contains("\0")) { MiniHttp.Send(ns, MiniHttp.Response.Text(400, "Petición no válida"), head); return; }
            if (target == "/site-config.js" || target.StartsWith("/site-config.js?"))
            {
                MiniHttp.Send(ns, new MiniHttp.Response(200, "application/javascript; charset=utf-8", Encoding.UTF8.GetBytes(SiteConfig), true), head);
                return;
            }
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + target);
            req.Method = head ? "HEAD" : "GET";
            req.Proxy = null;
            req.AllowAutoRedirect = false;
            req.Timeout = 15000; req.ReadWriteTimeout = 60000;
            req.UserAgent = "MajinheimWeb";
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException e)
            {
                resp = e.Response as HttpWebResponse;
                if (resp == null)
                {
                    MiniHttp.Send(ns, new MiniHttp.Response(502, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Offline), true), head);
                    return;
                }
            }
            using (resp)
            {
                var h = new StringBuilder();
                h.Append("HTTP/1.1 ").Append((int)resp.StatusCode).Append(' ').Append(Regex.Replace(resp.StatusDescription ?? "OK", @"[\r\n]", "")).Append("\r\n");
                foreach (var name in PassHeaders)
                {
                    string v = resp.Headers[name];
                    if (!string.IsNullOrEmpty(v)) h.Append(name).Append(": ").Append(Regex.Replace(v, @"[\r\n]", "")).Append("\r\n");
                }
                string loc = resp.Headers["Location"];
                if (!string.IsNullOrEmpty(loc) && loc.StartsWith("/") && !loc.StartsWith("//")) h.Append("Location: ").Append(Prefix).Append(Regex.Replace(loc, @"[\r\n]", "")).Append("\r\n");
                if (resp.ContentLength >= 0) h.Append("Content-Length: ").Append(resp.ContentLength).Append("\r\n");
                h.Append("X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: SAMEORIGIN\r\nX-Robots-Tag: noindex, nofollow\r\nConnection: close\r\n\r\n");
                var hb = Encoding.ASCII.GetBytes(h.ToString());
                ns.Write(hb, 0, hb.Length);
                if (!head) using (var body = resp.GetResponseStream()) body.CopyTo(ns);
                ns.Flush();
            }
        }

        const string Offline = "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>" +
            "<title>Mapa no disponible</title><body style='background:#14110f;color:#efe6da;font:16px system-ui;display:grid;place-items:center;min-height:90vh;text-align:center'>" +
            "<div><h1 style='color:#e0a54a'>El mapa no está disponible</h1><p>El mapa solo funciona mientras el server está encendido.</p><p><a style='color:#e0a54a' href='/'>Volver al estado del server</a></p></div>";
    }

    // ---------------------------------------------------------------- Caddy (HTTPS hacia internet)

    // Caddy es un programa gratuito que pide y renueva solo el certificado HTTPS (Let's Encrypt) y pasa las visitas al panel.
    // El launcher lo descarga de sus Releases oficiales de GitHub, comprueba la huella (SHA-512) y lo vigila.
    public static class Caddy
    {
        static Process proc;
        static Thread watcher;
        static volatile bool wanted;
        static readonly object lk = new object();

        public static string Dir { get { return AppInfo.P("web"); } }
        public static string Exe { get { return Path.Combine(Dir, "caddy.exe"); } }
        public static string Conf { get { return Path.Combine(Dir, "Caddyfile"); } }
        public static bool Installed { get { return File.Exists(Exe); } }
        public static bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }
        public static string LastError { get; private set; }

        // IP de este PC en la red de casa (la de Ajustes si sigue siendo suya; si no, la primera privada). null si no hay.
        public static string LanAddress()
        {
            var ips = Net.LocalIPv4();
            string want = (Settings.Current.LanHost ?? "").Trim();
            if (want != "" && ips.Contains(want) && Net.IsPrivate(want)) return want;
            return ips.FirstOrDefault(ip => Net.IsPrivate(ip) && !ip.StartsWith("127."));
        }

        static readonly object installLk = new object();
        static volatile bool installing;
        public static bool Installing { get { return installing; } }

        // "Acceso desde internet" marcado pero sin Caddy (una descarga que falló o se cortó): se prepara solo, sin volver a marcar la casilla.
        public static void InstallInBackground()
        {
            if (installing) return;
            installing = true;
            var t = new Thread(() =>
            {
                try { Install(m => Log.W("{0}", m)); Start(); }
                catch (Exception e) { LastError = e.Message; Log.W("ERROR: no pude preparar el HTTPS: {0}", e.Message); }
                finally { installing = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void Install(Action<string> status)
        {
            lock (installLk) InstallLocked(status);
        }

        static void InstallLocked(Action<string> status)
        {
            if (Installed) return;
            Directory.CreateDirectory(Dir);
            status("Buscando la última versión de Caddy...");
            var rel = Json.ParseObj(Http.GetString("https://api.github.com/repos/caddyserver/caddy/releases/latest"));
            string zipUrl = null, zipName = null, sumUrl = null;
            foreach (Dictionary<string, object> a in (System.Collections.IEnumerable)rel["assets"])
            {
                string n = (string)a["name"], u = (string)a["browser_download_url"];
                if (Regex.IsMatch(n, @"^caddy_[\d.]+_windows_amd64\.zip$")) { zipUrl = u; zipName = n; }
                if (Regex.IsMatch(n, @"^caddy_[\d.]+_checksums\.txt$")) sumUrl = u;
            }
            if (zipUrl == null || sumUrl == null) throw new Exception("No encuentro Caddy para Windows en GitHub.");
            string sums = Http.GetString(sumUrl);
            var m = Regex.Match(sums, @"^([0-9a-fA-F]{128})\s+\*?" + Regex.Escape(zipName) + @"\s*$", RegexOptions.Multiline);
            if (!m.Success) throw new Exception("No encuentro la huella de " + zipName + ".");
            string tmp = Path.Combine(Dir, zipName + ".part");
            status("Descargando " + zipName + "...");
            Http.Download(zipUrl, tmp, null);
            string got;
            using (var sha = SHA512.Create()) using (var f = File.OpenRead(tmp)) got = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
            if (!string.Equals(got, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new Exception("La descarga de Caddy no coincide con su huella oficial: se ha borrado."); }
            using (var z = ZipFile.OpenRead(tmp))
            {
                var e = z.Entries.FirstOrDefault(x => string.Equals(x.Name, "caddy.exe", StringComparison.OrdinalIgnoreCase));
                if (e == null) throw new Exception("El zip de Caddy no trae caddy.exe.");
                e.ExtractToFile(Exe, true);
            }
            File.Delete(tmp);
            Log.W("Caddy instalado ({0}).", zipName);
        }

        // El Caddyfile lo escribe el launcher: dirección pública (DDNS) -> panel en este PC.
        static void WriteConfig()
        {
            string logs = AppInfo.LogDir.Replace('\\', '/');
            var sb = new StringBuilder();
            sb.Append("# Lo escribe el launcher de Majinheim al activar el acceso desde internet. Si lo cambias a mano, se sobrescribe.\n");
            sb.Append("{\n\tadmin off\n}\n\n");
            sb.Append(WebPanel.PublicHost).Append(" {\n");
            sb.Append("\tencode gzip\n");
            sb.Append("\theader {\n\t\tStrict-Transport-Security \"max-age=31536000\"\n\t\t-Server\n\t}\n");
            sb.Append("\treverse_proxy 127.0.0.1:").Append(WebPanel.Port).Append("\n");
            sb.Append("\tlog {\n\t\toutput file \"").Append(logs).Append("/caddy-visitas.log\" {\n\t\t\troll_size 5MiB\n\t\t\troll_keep 3\n\t\t}\n\t}\n");
            sb.Append("}\n");
            // En casa: el router no deja usar la dirección pública desde dentro (no hace "NAT loopback"), así que en la red
            // de casa la parte pública de la web (estado, estadísticas, mapa) se abre en http://<IP de este PC>:<puerto>.
            // Solo escucha en esa IP y el router no reenvía ese puerto: desde internet no se ve. Sin cifrar no se entra:
            // las páginas de cuentas van a la dirección HTTPS y sus llamadas se rechazan.
            string lan = LanAddress();
            if (lan != null)
            {
                sb.Append("\nhttp://").Append(lan).Append(":").Append(WebPanel.Port).Append(" {\n");
                sb.Append("\tbind ").Append(lan).Append("\n");
                sb.Append("\theader -Server\n");
                sb.Append("\t@cuentas path /admin /admin.html /admin.js\n");
                sb.Append("\tredir @cuentas ").Append(WebPanel.PublicUrl).Append("admin 302\n");
                sb.Append("\t@cuentasApi path /api/entrar /api/salir /api/admin/*\n");
                sb.Append("\trespond @cuentasApi \"Para entrar usa la dirección segura (HTTPS).\" 403\n");
                sb.Append("\treverse_proxy 127.0.0.1:").Append(WebPanel.Port).Append("\n");
                sb.Append("}\n");
            }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Conf, sb.ToString(), new UTF8Encoding(false));
        }

        public static void Start()
        {
            lock (lk)
            {
                if (!Installed) { LastError = "Caddy no está instalado."; return; }
                WriteConfig();
                if (wanted && Running) return;
                wanted = true;
                if (watcher != null && watcher.IsAlive) return;
                KillOrphans();
                watcher = new Thread(() =>
                {
                    while (wanted)
                    {
                        try { Run(); }
                        catch (Exception e) { LastError = e.Message; Log.W("ERROR: no pude arrancar Caddy: {0}", e.Message); }
                        for (int i = 0; i < 30 && wanted; i++) Thread.Sleep(1000);   // si Caddy se cae, reintenta a los 30 s
                    }
                });
                watcher.IsBackground = true;
                watcher.Start();
            }
        }

        static void Run()
        {
            var psi = new ProcessStartInfo(Exe, "run --config \"" + Conf + "\" --adapter caddyfile");
            psi.WorkingDirectory = Dir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Directory.CreateDirectory(AppInfo.LogDir);
            using (var log = new StreamWriter(Path.Combine(AppInfo.LogDir, "caddy.log"), true, new UTF8Encoding(false)))
            {
                log.AutoFlush = true;
                var p = new Process { StartInfo = psi };
                DataReceivedEventHandler h = (o, e) =>
                {
                    try
                    {
                        if (e.Data == null) return;
                        lock (log) log.WriteLine(e.Data);
                        if (e.Data.Contains("\"level\":\"error\"")) LastError = e.Data.Length > 300 ? e.Data.Substring(0, 300) : e.Data;
                    }
                    catch { }
                };
                p.OutputDataReceived += h;
                p.ErrorDataReceived += h;
                p.Start();
                proc = p;
                LastError = null;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                Log.W("Acceso desde internet activo: {0}", WebPanel.PublicUrl);
                p.WaitForExit();
                if (wanted) Log.W("AVISO: Caddy se ha cerrado (código {0}). Mira logs\\caddy.log.", p.ExitCode);
            }
        }

        // Un Caddy que se quedó abierto de una vez anterior tendría los puertos ocupados.
        static void KillOrphans()
        {
            foreach (var p in Process.GetProcessesByName("caddy"))
                try { if (string.Equals(p.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); } }
                catch { }
        }

        public static void Stop()
        {
            lock (lk)
            {
                if (!wanted && !Running) return;
                wanted = false;
                try { if (Running) { proc.Kill(); proc.WaitForExit(5000); Log.W("Acceso desde internet apagado."); } } catch { }
            }
        }
    }

    // ---------------------------------------------------------------- vigilancia del server (lee el proceso y el registro)

    public class SessionRecord
    {
        public string Name { get; set; }
        public string Start { get; set; }   // ISO con zona horaria
        public string End { get; set; }
        public int Deaths { get; set; }
        public string Key { get; set; }     // para no repetir la misma sesión si se vuelve a leer el registro
    }

    static class ServerWatch
    {
        class Live { public string Steam, Name; public DateTime Since; public int Deaths; public bool Announced; }

        static readonly object lk = new object();
        static readonly Dictionary<string, Live> online = new Dictionary<string, Live>();
        static List<SessionRecord> history = new List<SessionRecord>();
        static Process proc;
        static DateTime procStart;
        static bool ready;
        static string version = "";
        static DateTime? lastSave;
        public static DateTime? LastSave { get { lock (lk) return lastSave; } }
        static volatile bool mapUp;
        public static bool MapUp { get { return mapUp; } }
        static double cpu;
        static long ramMb;
        static long logPos;
        static string partial = "";
        static TimeSpan lastCpu;
        static DateTime lastCpuAt;

        static string HistoryFile { get { return Path.Combine(AppInfo.LogDir, "sesiones.json"); } }
        static string GameLogFile { get { return Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log"); } }

        // Líneas de Valheim 1.0.21 (comprobadas en LogOutput.log del server):
        //   11/01/2026 00:45:38: Got connection SteamID 7656...
        //   11/01/2026 00:46:16: Got character ZDOID from Majin : 3513420997:1   (0:0 = ha muerto)
        //   11/01/2026 00:50:19: Closing socket 7656...
        static readonly Regex rxTime = new Regex(@"(\d\d/\d\d/\d{4} \d\d:\d\d:\d\d): ");
        static readonly Regex rxConn = new Regex(@"Got connection SteamID (\d+)");
        static readonly Regex rxChar = new Regex(@"Got character ZDOID from (.+?) : (-?\d+):(\d+)");
        static readonly Regex rxClose = new Regex(@"Closing socket (\d+)");
        static readonly Regex rxVersion = new Regex(@"Valheim version: ?([^\s(]+)");
        static readonly Regex rxSaved = new Regex(@"World saved");

        static bool started;
        static DateTime watchStart = DateTime.MaxValue;

        // Avisos para el launcher (Discord): ("entra" | "sale", nombre, minutos de la sesión). Solo de lo que pasa
        // desde que se abrió el launcher, nunca de líneas viejas del registro. Se llama con el cerrojo cogido: no bloquear.
        public static event Action<string, string, int> PlayerEvent;
        public static int OnlineCount { get { lock (lk) return online.Values.Count(l => l.Name != null); } }
        // SteamID de quien está jugando con ese nombre (para expulsar o banear sin equivocarse de persona). Nunca sale a la web.
        public static string SteamIdOf(string name) { lock (lk) { var l = online.Values.FirstOrDefault(x => x.Name != null && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); return l == null ? null : l.Steam; } }
        // Última vez que hubo alguien jugando (ahora mismo, si hay alguien dentro).
        public static DateTime LastActivity
        {
            get
            {
                lock (lk)
                {
                    if (online.Values.Any(l => l.Name != null)) return DateTime.Now;
                    var h = history.LastOrDefault();
                    return h == null ? DateTime.MinValue : ParseIso(h.End);
                }
            }
        }

        static void Fire(string what, string name, int minutes)
        {
            var h = PlayerEvent;
            if (h == null) return;
            try { h(what, name, minutes); } catch (Exception e) { Log.W("Web: aviso de jugador falló: {0}", e.Message); }
        }

        public static void Start()
        {
            if (started) return;
            started = true;
            watchStart = DateTime.Now.AddMinutes(-1);
            try { history = Json.Load<List<SessionRecord>>(HistoryFile); } catch (Exception e) { Log.W("Web: no pude leer sesiones.json: {0}", e.Message); }
            var t = new Thread(() =>
            {
                while (true)
                {
                    try { Tick(); } catch (Exception e) { Log.W("Web: error vigilando el server: {0}", e.Message); }
                    Thread.Sleep(2000);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

        static void Tick()
        {
            if (!Running) FindServer();
            if (Running)
            {
                try
                {
                    proc.Refresh();
                    ramMb = proc.WorkingSet64 / (1024 * 1024);
                    var now = DateTime.Now; var tot = proc.TotalProcessorTime;
                    if (lastCpuAt != DateTime.MinValue && now > lastCpuAt)
                        cpu = Math.Round(100.0 * (tot - lastCpu).TotalMilliseconds / (now - lastCpuAt).TotalMilliseconds / Environment.ProcessorCount, 1);
                    lastCpu = tot; lastCpuAt = now;
                }
                catch { }
            }
            ReadLog();
            mapUp = Running && MapProxy.Up();
            if (!Running)
                lock (lk)
                {
                    // Server apagado o caído: nadie puede seguir dentro. La sesión acaba cuando el registro dejó de escribirse.
                    DateTime end = DateTime.Now;
                    try { if (File.Exists(GameLogFile)) end = File.GetLastWriteTime(GameLogFile); } catch { }
                    if (online.Count > 0) foreach (var l in online.Values.ToList()) Close(l, end < l.Since ? l.Since : end);
                    ready = false; cpu = 0; ramMb = 0;
                }
        }

        // El server de Majinheim es el valheim_server.exe que usa nuestra carpeta saves (igual que hace el launcher).
        static void FindServer()
        {
            proc = null; lastCpuAt = DateTime.MinValue;
            foreach (var p in Process.GetProcessesByName("valheim_server"))
            {
                string cmd = null;
                try { cmd = ServerRunner.CommandLine(p.Id); } catch { }
                if (cmd == null || cmd.IndexOf(AppInfo.SavesDir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                proc = p;
                try { procStart = p.StartTime; } catch { procStart = DateTime.Now; }
                return;
            }
        }

        static void ReadLog()
        {
            string file = GameLogFile;
            if (!File.Exists(file)) return;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < logPos) { lock (lk) { recent.Clear(); foreach (var l in online.Values.ToList()) Close(l, DateTime.Now); ready = false; version = ""; lastSave = null; } logPos = 0; partial = ""; }   // registro nuevo: el server se ha reiniciado
                if (fs.Length == logPos) return;
                fs.Seek(logPos, SeekOrigin.Begin);
                var buf = new byte[Math.Min(fs.Length - logPos, 4 * 1024 * 1024)];
                int n = fs.Read(buf, 0, buf.Length);
                logPos += n;
                string txt = partial + Encoding.UTF8.GetString(buf, 0, n);
                var lines = txt.Split('\n');
                partial = lines[lines.Length - 1];
                lock (lk) for (int i = 0; i < lines.Length - 1; i++) Parse(lines[i].TrimEnd('\r'));
            }
        }

        static readonly Queue<string> recent = new Queue<string>();
        public static List<string> RecentLines(int n) { lock (lk) return recent.Skip(Math.Max(0, recent.Count - n)).ToList(); }

        static void Parse(string line)
        {
            if (line.Length == 0) return;
            recent.Enqueue(line.Length > 400 ? line.Substring(0, 400) : line);
            while (recent.Count > 300) recent.Dequeue();
            DateTime ts = DateTime.Now;
            var mt = rxTime.Match(line);
            DateTime parsed;
            if (mt.Success && DateTime.TryParseExact(mt.Groups[1].Value, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)) ts = parsed;

            Match m;
            if (line.Contains("Game server connected")) { ready = true; return; }
            if ((m = rxVersion.Match(line)).Success) { version = m.Groups[1].Value; return; }
            if (rxSaved.IsMatch(line)) { lastSave = ts; return; }
            if ((m = rxConn.Match(line)).Success)
            {
                string id = m.Groups[1].Value;
                Live old;
                if (online.TryGetValue(id, out old)) Close(old, ts);
                online[id] = new Live { Steam = id, Since = ts };
                return;
            }
            if ((m = rxChar.Match(line)).Success)
            {
                string name = m.Groups[1].Value.Trim();
                bool dead = m.Groups[2].Value == "0" && m.Groups[3].Value == "0";
                var same = online.Values.FirstOrDefault(l => l.Name == name);
                if (same != null) { if (dead) same.Deaths++; return; }
                // El personaje llega justo después de su conexión: se lo damos al último que entró sin nombre.
                var noName = online.Values.Where(l => l.Name == null).OrderByDescending(l => l.Since).FirstOrDefault();
                if (noName != null)
                {
                    noName.Name = name;
                    if (ts >= watchStart) { noName.Announced = true; Fire("entra", name, 0); }
                }
                return;
            }
            if ((m = rxClose.Match(line)).Success)
            {
                Live l;
                if (online.TryGetValue(m.Groups[1].Value, out l)) Close(l, ts);
            }
        }

        static void Close(Live l, DateTime end)
        {
            online.Remove(l.Steam);
            if (l.Name == null) return;   // se conectó pero no llegó a entrar (contraseña mal, versión distinta...)
            if (l.Announced) Fire("sale", l.Name, Minutes(l.Since, end));
            string key = l.Steam + "@" + l.Since.ToString("s");
            if (history.Any(h => h.Key == key)) return;
            history.Add(new SessionRecord { Name = l.Name, Start = Iso(l.Since), End = Iso(end), Deaths = l.Deaths, Key = key });
            if (history.Count > 2000) history = history.Skip(history.Count - 2000).ToList();
            try { Json.Save(HistoryFile, history); } catch (Exception e) { Log.W("Web: no pude guardar sesiones.json: {0}", e.Message); }
        }

        static string Iso(DateTime d) { return d.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture); }
        static int Minutes(DateTime from, DateTime to) { return Math.Max(0, (int)(to - from).TotalMinutes); }

        // Día del mundo según el último guardado: el archivo principal del mundo empieza por int versión + double
        // tiempo del mundo en segundos (igual en el formato 1.0 "_main.N.db2" y en el antiguo ".db"); un día de Valheim dura 1800 s.
        static string dayFile; static DateTime dayStamp; static int? day;
        static int? WorldDay()
        {
            try
            {
                string dir = Path.Combine(AppInfo.SavesDir, "worlds_local"), w = Settings.Current.WorldName;
                FileInfo f = null;
                if (Directory.Exists(Path.Combine(dir, w))) f = new DirectoryInfo(Path.Combine(dir, w)).GetFiles("_main.*.db2").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f == null) { var old = new FileInfo(Path.Combine(dir, w + ".db")); if (old.Exists) f = old; }
                if (f == null) return null;
                if (f.FullName == dayFile && f.LastWriteTimeUtc == dayStamp) return day;
                var buf = new byte[12];
                using (var st = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    if (st.Read(buf, 0, 12) < 12) return null;
                double t = BitConverter.ToDouble(buf, 4);
                day = t >= 0 && t < 1e10 ? (int?)(int)(t / 1800) : null;
                dayFile = f.FullName; dayStamp = f.LastWriteTimeUtc;
                return day;
            }
            catch { return null; }
        }

        // Lo que ve cualquiera en la página. Sin SteamID, sin IPs, sin contraseñas.
        public static Dictionary<string, object> Snapshot()
        {
            var now = DateTime.Now;
            var s = Settings.Current;
            var o = new Dictionary<string, object>();
            int? worldDay = WorldDay();
            lock (lk)
            {
                bool run = Running;
                o["servidor"] = new Dictionary<string, object>
                {
                    { "nombre", s.ServerName },
                    { "mundo", s.WorldName },
                    { "estado", !run ? "apagado" : (ready ? "en marcha" : "arrancando") },
                    { "desde", run ? Iso(procStart) : null },
                    { "version", version },
                    { "actualizacionPendiente", Steam.Server() != null && Steam.Server().UpdatePending },
                    { "ultimoGuardado", lastSave.HasValue ? Iso(lastSave.Value) : null },
                    { "dia", worldDay },
                    { "cpu", cpu },
                    { "ramMb", ramMb },
                };
                o["jugadores"] = online.Values.Where(l => l.Name != null).OrderBy(l => l.Since).Select(l => new Dictionary<string, object>
                {
                    { "nombre", l.Name }, { "desde", Iso(l.Since) }, { "minutos", Minutes(l.Since, now) }, { "muertes", l.Deaths },
                }).ToList();
                o["ultimasSesiones"] = history.AsEnumerable().Reverse().Take(10).Select(h => new Dictionary<string, object>
                {
                    { "nombre", h.Name }, { "inicio", h.Start }, { "fin", h.End }, { "muertes", h.Deaths },
                }).ToList();
                var weekAgo = now.AddDays(-7);
                o["semana"] = history.Select(h => new { h.Name, a = ParseIso(h.Start), b = ParseIso(h.End) })
                    .Where(h => h.b > weekAgo)
                    .GroupBy(h => h.Name)
                    .Select(g => new Dictionary<string, object> { { "nombre", g.Key }, { "minutos", g.Sum(h => Minutes(h.a < weekAgo ? weekAgo : h.a, h.b)) } })
                    .OrderByDescending(d => (int)d["minutos"]).ToList();
            }
            var b = Backup.LastWorld();
            o["ultimoBackup"] = b == null ? null : new Dictionary<string, object> { { "fecha", Iso(b.Date) }, { "tipo", b.Kind }, { "nombre", b.Label } };
            try
            {
                var pk = Modpack.Load();
                o["mods"] = new Dictionary<string, object>
                {
                    { "revision", pk.Revision },
                    { "lista", pk.Mods.Where(m => m.Enabled && m.ForServer()).Select(m => new Dictionary<string, object> { { "nombre", m.PkgName().Replace('_', ' ') }, { "version", m.Version }, { "web", m.Local ? null : m.WebUrl() } }).ToList() },
                };
            }
            catch { o["mods"] = null; }
            o["mapa"] = mapUp ? MapProxy.Prefix + "/" : null;
            o["jefes"] = Bosses.Public();
            o["generado"] = Iso(now);
            o["panel"] = WebPanel.Version;
            return o;
        }

        // Estadísticas de todas las partidas guardadas (lo mismo que ve cualquiera: nombres y tiempos, nada de red).
        public static Dictionary<string, object> Stats()
        {
            var now = DateTime.Now;
            List<SessionRecord> hist;
            List<Live> live;
            lock (lk) { hist = history.ToList(); live = online.Values.Where(l => l.Name != null).ToList(); }
            var all = hist.Select(h => new { h.Name, a = ParseIso(h.Start), b = ParseIso(h.End), h.Deaths, live = false })
                .Concat(live.Select(l => new { l.Name, a = l.Since, b = now, Deaths = l.Deaths, live = true }))
                .Where(x => x.a != DateTime.MinValue && x.b >= x.a).ToList();
            var weekAgo = now.AddDays(-7);
            var players = all.GroupBy(x => x.Name).Select(g => new Dictionary<string, object>
            {
                { "nombre", g.Key },
                { "minutos", g.Sum(x => Minutes(x.a, x.b)) },
                { "semana", g.Where(x => x.b > weekAgo).Sum(x => Minutes(x.a < weekAgo ? weekAgo : x.a, x.b)) },
                { "sesiones", g.Count() },
                { "masLarga", g.Max(x => Minutes(x.a, x.b)) },
                { "muertes", g.Sum(x => x.Deaths) },
                { "primera", Iso(g.Min(x => x.a)) },
                { "ultima", g.Any(x => x.live) ? null : Iso(g.Max(x => x.b)) },
                { "jugando", g.Any(x => x.live) },
            }).OrderByDescending(d => (int)d["minutos"]).ToList();

            // Últimos 30 días: cuánta gente distinta y cuánto se jugó (cada sesión repartida entre los días que toca).
            var days = new List<Dictionary<string, object>>();
            var hours = new int[24];
            for (int i = 29; i >= 0; i--)
            {
                var d0 = now.Date.AddDays(-i); var d1 = d0.AddDays(1);
                var on = all.Where(x => x.a < d1 && x.b > d0).ToList();
                days.Add(new Dictionary<string, object>
                {
                    { "fecha", d0.ToString("yyyy-MM-dd") },
                    { "jugadores", on.Select(x => x.Name).Distinct().Count() },
                    { "minutos", on.Sum(x => Minutes(x.a < d0 ? d0 : x.a, x.b > d1 ? d1 : x.b)) },
                });
            }
            // A qué horas se juega (últimos 60 días), en minutos por franja de una hora.
            var since = now.AddDays(-60);
            foreach (var x in all.Where(x => x.b > since))
                for (var t = x.a < since ? since : x.a; t < x.b; t = t.AddMinutes(10)) hours[t.Hour] += (int)Math.Min(10, (x.b - t).TotalMinutes);

            return new Dictionary<string, object>
            {
                { "jugadores", players },
                { "dias", days },
                { "horas", hours },
                { "total", new Dictionary<string, object>
                    {
                        { "minutos", all.Sum(x => Minutes(x.a, x.b)) },
                        { "sesiones", all.Count },
                        { "jugadores", players.Count },
                        { "desde", all.Count == 0 ? null : Iso(all.Min(x => x.a)) },
                        { "dia", WorldDay() },
                    }
                },
                { "generado", Iso(now) },
            };
        }

        public static DateTime ParseIsoPublic(string s) { return ParseIso(s); }

        static DateTime ParseIso(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }
    }

    // Jefes derrotados del mundo. El launcher los lee del server (orden globalKeys de ValheimRcon) y los guarda en logs\jefes.json
    // con la fecha en que los vio por primera vez. Aquí solo se leen para la web.
    public static class Bosses
    {
        public class Entry { public string Key { get; set; } public string Since { get; set; } public bool Before { get; set; } }
        public static readonly string[][] All =
        {
            new[] { "defeated_eikthyr", "Eikthyr", "Pradera" },
            new[] { "defeated_gdking", "El Anciano", "Selva Negra" },
            new[] { "defeated_bonemass", "Bonemass", "Pantano" },
            new[] { "defeated_dragon", "Moder", "Montañas" },
            new[] { "defeated_goblinking", "Yagluth", "Llanuras" },
            new[] { "defeated_queen", "La Reina", "Tierras Brumosas" },
            new[] { "defeated_fader", "Fader", "Tierras de Ceniza" },
        };
        public static string File { get { return Path.Combine(AppInfo.LogDir, "jefes.json"); } }
        static readonly object lk = new object();

        public static List<Entry> Load()
        {
            lock (lk)
            {
                try { if (System.IO.File.Exists(File)) return Json.Load<List<Entry>>(File) ?? new List<Entry>(); } catch { }
                return new List<Entry>();
            }
        }

        // Apunta los jefes que ve ahora en el mundo. Devuelve los nuevos (para avisar). La primera vez, los que ya estaban
        // se apuntan como "antes" y no se avisan.
        public static List<string> Update(IEnumerable<string> keysNow)
        {
            lock (lk)
            {
                var list = Load();
                bool first = !System.IO.File.Exists(File);
                var news = new List<string>();
                foreach (var k in keysNow.Select(x => x.ToLowerInvariant()).Distinct())
                {
                    if (!All.Any(b => b[0] == k) || list.Any(e => e.Key == k)) continue;
                    list.Add(new Entry { Key = k, Since = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), Before = first });
                    if (!first) news.Add(k);
                }
                Directory.CreateDirectory(AppInfo.LogDir);
                Json.Save(File, list);
                return news;
            }
        }

        public static string Name(string key) { var b = All.FirstOrDefault(x => x[0] == key); return b == null ? key : b[1]; }

        public static List<Dictionary<string, object>> Public()
        {
            if (!System.IO.File.Exists(File)) return new List<Dictionary<string, object>>();   // aún no se ha podido mirar el mundo
            var got = Load();
            return All.Select(b =>
            {
                var e = got.FirstOrDefault(x => x.Key == b[0]);
                return new Dictionary<string, object>
                {
                    { "nombre", b[1] }, { "bioma", b[2] }, { "derrotado", e != null },
                    { "fecha", e != null && !e.Before ? e.Since : null },
                };
            }).ToList();
        }
    }
}
