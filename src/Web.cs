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
        public const string Version = "0.1.0";
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
            if (s.WebEnabled && s.WebPublic && http != null) Caddy.Start();
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
        };

        static MiniHttp.Response Route(string path)
        {
            if (path == "/api/estado") return MiniHttp.Response.JsonOf(ServerWatch.Snapshot(), true);
            if (path == "/salud") return new MiniHttp.Response(200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("ok"), true);
            string[] f;
            if (Files.TryGetValue(path, out f))
            {
                byte[] body = Resource(f[0]);
                if (body != null) return new MiniHttp.Response(200, f[1], body, false);
            }
            return new MiniHttp.Response(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("No existe"), true);
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

        // Comprueba desde este PC si la web responde por internet (HTTPS). Devuelve null si va bien, o el motivo.
        public static string CheckPublic()
        {
            try
            {
                string r = Http.GetString(PublicUrl + "salud", 15000);
                return r.Trim() == "ok" ? null : "responde algo raro";
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.TrustFailure || e.Status == WebExceptionStatus.SecureChannelFailure) return "el certificado HTTPS aún no está listo (Caddy lo pide la primera vez; espera un minuto)";
                if (e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.Timeout) return "no llega: revisa que el router abra TCP 443 y 80 hacia este PC";
                return e.Message;
            }
            catch (Exception e) { return e.Message; }
        }
    }

    // ---------------------------------------------------------------- servidor HTTP mínimo (solo GET, solo 127.0.0.1)

    class MiniHttp
    {
        public class Response
        {
            public int Code; public string Type; public byte[] Body; public bool NoCache;
            public Response(int code, string type, byte[] body, bool noCache) { Code = code; Type = type; Body = body; NoCache = noCache; }
            public static Response JsonOf(object o, bool noCache)
            {
                return new Response(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Json.Str(o)), noCache);
            }
        }

        readonly int port;
        readonly Func<string, Response> route;
        TcpListener listener;
        volatile bool running;

        public MiniHttp(int port, Func<string, Response> route) { this.port = port; this.route = route; }

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

        static readonly string[] Codes = { "200 OK", "400 Bad Request", "404 Not Found", "405 Method Not Allowed", "500 Internal Server Error" };

        void Handle(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
                    var ns = c.GetStream();
                    string head = ReadHead(ns);
                    if (head == null) { Send(ns, new Response(400, "text/plain", Encoding.UTF8.GetBytes("Petición no válida"), true), false); return; }
                    string first = head.Split('\n')[0].Trim();
                    var parts = first.Split(' ');
                    if (parts.Length != 3) { Send(ns, new Response(400, "text/plain", Encoding.UTF8.GetBytes("Petición no válida"), true), false); return; }
                    bool isHead = parts[0] == "HEAD";
                    if (parts[0] != "GET" && !isHead) { Send(ns, new Response(405, "text/plain", Encoding.UTF8.GetBytes("Solo lectura"), true), false); return; }
                    string path = parts[1];
                    int q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);
                    Response r;
                    try { r = route(path); }
                    catch (Exception e) { Log.W("Web: error en {0}: {1}", path, e.Message); r = new Response(500, "text/plain", Encoding.UTF8.GetBytes("Error interno"), true); }
                    Send(ns, r, isHead);
                }
                catch { }
            }
        }

        // Lee hasta la línea en blanco. Máximo 8 KB: lo que sea más grande no es una petición nuestra.
        static string ReadHead(NetworkStream ns)
        {
            var buf = new byte[8192];
            int n = 0;
            while (n < buf.Length)
            {
                int r = ns.Read(buf, n, buf.Length - n);
                if (r <= 0) return null;
                n += r;
                string s = Encoding.ASCII.GetString(buf, 0, n);
                if (s.Contains("\r\n\r\n") || s.Contains("\n\n")) return s;
            }
            return null;
        }

        static void Send(NetworkStream ns, Response r, bool headOnly)
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
            h.Append("Content-Security-Policy: default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n");
            h.Append("Connection: close\r\n\r\n");
            var hb = Encoding.ASCII.GetBytes(h.ToString());
            ns.Write(hb, 0, hb.Length);
            if (!headOnly) ns.Write(r.Body, 0, r.Body.Length);
            ns.Flush();
        }
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

        public static void Install(Action<string> status)
        {
            if (Installed) return;
            Directory.CreateDirectory(Dir);
            status("Buscando la última versión de Caddy...");
            var rel = Json.ParseObj(Http.GetString("https://api.github.com/repos/caddyserver/caddy/releases/latest"));
            string zipUrl = null, zipName = null, sumUrl = null;
            foreach (Dictionary<string, object> a in (System.Collections.ArrayList)rel["assets"])
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
        class Live { public string Steam, Name; public DateTime Since; public int Deaths; }

        static readonly object lk = new object();
        static readonly Dictionary<string, Live> online = new Dictionary<string, Live>();
        static List<SessionRecord> history = new List<SessionRecord>();
        static Process proc;
        static DateTime procStart;
        static bool ready;
        static string version = "";
        static DateTime? lastSave;
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

        public static void Start()
        {
            if (started) return;
            started = true;
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
                if (fs.Length < logPos) { lock (lk) { foreach (var l in online.Values.ToList()) Close(l, DateTime.Now); ready = false; version = ""; lastSave = null; } logPos = 0; partial = ""; }   // registro nuevo: el server se ha reiniciado
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

        static void Parse(string line)
        {
            if (line.Length == 0) return;
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
                if (noName != null) noName.Name = name;
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
            string key = l.Steam + "@" + l.Since.ToString("s");
            if (history.Any(h => h.Key == key)) return;
            history.Add(new SessionRecord { Name = l.Name, Start = Iso(l.Since), End = Iso(end), Deaths = l.Deaths, Key = key });
            if (history.Count > 2000) history = history.Skip(history.Count - 2000).ToList();
            try { Json.Save(HistoryFile, history); } catch (Exception e) { Log.W("Web: no pude guardar sesiones.json: {0}", e.Message); }
        }

        static string Iso(DateTime d) { return d.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture); }
        static int Minutes(DateTime from, DateTime to) { return Math.Max(0, (int)(to - from).TotalMinutes); }

        // Lo que ve cualquiera en la página. Sin SteamID, sin IPs, sin contraseñas.
        public static Dictionary<string, object> Snapshot()
        {
            var now = DateTime.Now;
            var s = Settings.Current;
            var o = new Dictionary<string, object>();
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
                    { "ultimoGuardado", lastSave.HasValue ? Iso(lastSave.Value) : null },
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
            var b = Backup.List().FirstOrDefault();
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
            o["generado"] = Iso(now);
            o["panel"] = WebPanel.Version;
            return o;
        }

        static DateTime ParseIso(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }
    }
}
