// Majinheim Launcher - nucleo compartido por la edicion Server y la edicion Cliente.
// Compilado con el csc de .NET Framework 4.8 (C# 5): sin $"", sin ?., sin nameof.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Majinheim
{
    public static class AppInfo
    {
        public const string Version = "1.3.0";
#if SERVER
        public const bool IsServer = true;
        public const string EditionName = "Server";
#else
        public const bool IsServer = false;
        public const string EditionName = "Cliente";
#endif
        public const string DefaultHost = "majinrouter.asuscomm.com";
        public const int DefaultGamePort = 2456;
        public const string GitHubRepo = "MajinSander/Majinheim";   // donde se publica el modpack y el launcher de jugador
        public const string Author = "Majin";
        public const string GameAppId = "892970";
        public const string BepInExId = "denikson-BepInExPack_Valheim";
        public const string ClientExeName = "Majinheim.exe";

        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string P(params string[] parts) { return Path.Combine(new[] { Root }.Concat(parts).ToArray()); }
        public static string SettingsFile { get { return P("launcher.json"); } }
        public static string ModpackFile { get { return P("modpack.json"); } }
        public static string CacheDir { get { return P("cache"); } }
        public static string BackupDir { get { return P("backups"); } }
        public static string LogDir { get { return P("logs"); } }
        public static string SavesDir { get { return P("saves"); } }
        public static string Profile { get { return P("profiles", IsServer ? "server" : "client"); } }
        public static string DistDir { get { return P("distribucion"); } }
        public static string OriginalSaves { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\IronGate\Valheim"); } }
    }

    public static class Log
    {
        public static event Action<string> Line;
        static readonly object lk = new object();
        public static void W(string fmt, params object[] a)
        {
            string s = a.Length > 0 ? string.Format(fmt, a) : fmt;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            lock (lk)
            {
                try
                {
                    Directory.CreateDirectory(AppInfo.LogDir);
                    File.AppendAllText(Path.Combine(AppInfo.LogDir, "launcher.log"), DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine);
                }
                catch { }
            }
            var h = Line;
            if (h != null) h(line);
        }
    }

    public static class Json
    {
        static JavaScriptSerializer S() { var s = new JavaScriptSerializer(); s.MaxJsonLength = int.MaxValue; return s; }
        public static T Load<T>(string path) where T : new()
        {
            if (!File.Exists(path)) return new T();
            return S().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF'));
        }
        public static T Parse<T>(string s) { return S().Deserialize<T>(s.TrimStart('\uFEFF')); }
        public static Dictionary<string, object> ParseObj(string s) { return (Dictionary<string, object>)S().DeserializeObject(s.TrimStart('\uFEFF')); }
        public static string Str(object o) { return Pretty(S().Serialize(o)); }
        public static void Save(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Str(o), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(c).Append(json[i + 1]); i++; break; }
                        ind++; sb.Append(c).Append("\r\n").Append(new string(' ', ind * 2)); break;
                    case '}':
                    case ']':
                        ind--; sb.Append("\r\n").Append(new string(' ', ind * 2)).Append(c); break;
                    case ',': sb.Append(c).Append("\r\n").Append(new string(' ', ind * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- modelos

    public class ModEntry
    {
        public string Id { get; set; }        // Autor-Nombre, como en Thunderstore
        public string Version { get; set; }
        public bool Enabled { get; set; }
        public string Side { get; set; }      // both | server | client
        public string Note { get; set; }
        public bool Local { get; set; }       // paquete propio: no esta en Thunderstore (solo en la cache de este PC)
        public string Patch { get; set; }     // arreglo que aplica el launcher al instalarlo (p.ej. itemdrawers-1.0)
        public ModEntry() { Enabled = true; Side = "both"; Note = ""; Patch = ""; }
        public string Ns() { return Id.Substring(0, Id.IndexOf('-')); }
        public string PkgName() { return Id.Substring(Id.IndexOf('-') + 1); }
        public string FullName() { return Id + "-" + Version; }
        public bool ForClient() { return Side != "server"; }
        public bool ForServer() { return Side != "client"; }
        public string WebUrl() { return "https://thunderstore.io/c/valheim/p/" + Ns() + "/" + PkgName() + "/"; }
    }

    public class Modpack
    {
        public string Name { get; set; }
        public int Revision { get; set; }
        public string GameAddress { get; set; }
        public string LanHost { get; set; }
        public string LauncherVersion { get; set; }
        public string Message { get; set; }
        public string WebUrl { get; set; }       // web del server (panel de estado); vacío si no hay acceso desde internet
        public List<ModEntry> Mods { get; set; }
        public Modpack() { Name = "Majinheim"; Mods = new List<ModEntry>(); Message = ""; WebUrl = ""; GameAddress = ""; LanHost = ""; LauncherVersion = AppInfo.Version; }
        public ModEntry Find(string id) { return Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)); }
        public static Modpack Load() { return Json.Load<Modpack>(AppInfo.ModpackFile); }
        public void Save() { Json.Save(AppInfo.ModpackFile, this); }
    }

    public class InstalledInfo
    {
        public int Revision { get; set; }
        public List<string> Mods { get; set; }
        public string Date { get; set; }
        public InstalledInfo() { Mods = new List<string>(); Date = ""; }
        public static InstalledInfo Load(string profile) { return Json.Load<InstalledInfo>(Path.Combine(profile, "installed.json")); }
    }

    public class Settings
    {
        // comunes
        public string GameDir { get; set; }
        // edicion server
        public string ServerDir { get; set; }
        public string ServerName { get; set; }
        public string WorldName { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
        public bool Public { get; set; }
        public bool Crossplay { get; set; }
        public string ExtraArgs { get; set; }
        public bool AutoRestart { get; set; }
        public int KeepAutoBackups { get; set; }
        public string PublicHost { get; set; }
        public string LanHost { get; set; }      // IP del server en casa: solo en este PC, nunca se publica
        public string GitHubToken { get; set; }
        public string StaleBefore { get; set; }
        public bool AutoStartServer { get; set; }
        public bool WebEnabled { get; set; }     // panel web dentro del launcher del server (solo escucha en 127.0.0.1)
        public bool WebPublic { get; set; }      // acceso desde internet por HTTPS (Caddy + DDNS)
        public int WebPort { get; set; }
        public int MapPort { get; set; }         // puerto del mod WebMap en este PC (server_port de su .cfg)
        // comunes: de dónde sale el modpack (GitHub)
        public string GitHubRepo { get; set; }
        // edicion cliente
        public bool AutoConnect { get; set; }

        public Settings()
        {
            GameDir = ""; ServerDir = ""; ServerName = "Majinheim"; WorldName = "Majinheim"; Password = "";
            Port = AppInfo.DefaultGamePort; Public = false; Crossplay = false; ExtraArgs = ""; AutoRestart = true;
            KeepAutoBackups = 40; PublicHost = AppInfo.DefaultHost; LanHost = ""; GitHubToken = "";
            StaleBefore = "2026-06-01";
            GitHubRepo = AppInfo.GitHubRepo; AutoConnect = true; WebEnabled = true; WebPublic = false; WebPort = 8456; MapPort = 3000;
        }
        public static Settings Current = new Settings();
        public static void Load() { Current = Json.Load<Settings>(AppInfo.SettingsFile); }
        public static void Save() { Json.Save(AppInfo.SettingsFile, Current); }
    }

    public static class Ver
    {
        public static int Compare(string a, string b)
        {
            var x = (a ?? "").Split('.'); var y = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = 0, q = 0;
                if (i < x.Length) int.TryParse(x[i], out p);
                if (i < y.Length) int.TryParse(y[i], out q);
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }
    }

    // ---------------------------------------------------------------- red

    public static class Http
    {
        static Http()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 8;
        }
        static HttpWebRequest Req(string url, int timeout)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "MajinheimLauncher/" + AppInfo.Version;
            r.Timeout = timeout; r.ReadWriteTimeout = Math.Max(timeout, 60000); r.AllowAutoRedirect = true;
            return r;
        }
        public static string GetString(string url, int timeout = 30000)
        {
            using (var resp = Req(url, timeout).GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        public static void Download(string url, string dest, Action<long, long> progress)
        {
            using (var resp = (HttpWebResponse)Req(url, 30000).GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(dest))
            {
                long total = resp.ContentLength, done = 0, last = 0;
                var buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    f.Write(buf, 0, n); done += n;
                    if (progress != null && done - last > (1 << 20)) { last = done; progress(done, total); }
                }
                if (progress != null) progress(done, total);
            }
        }
    }

    public class PkgInfo
    {
        public string Id, Version, Date, Description;
        public List<string> Deps = new List<string>();
    }

    public static class Thunderstore
    {
        const string Api = "https://thunderstore.io/api/experimental/package/";

        public static PkgInfo Latest(string id)
        {
            int i = id.IndexOf('-');
            if (i <= 0) throw new Exception("Id de mod no válido: " + id);
            var d = Json.ParseObj(Http.GetString(Api + id.Substring(0, i) + "/" + id.Substring(i + 1) + "/"));
            var l = (Dictionary<string, object>)d["latest"];
            var p = new PkgInfo();
            p.Id = d["namespace"] + "-" + d["name"];
            p.Version = (string)l["version_number"];
            p.Date = ((string)l["date_created"]).Substring(0, 10);
            p.Description = l.ContainsKey("description") ? (string)l["description"] : "";
            var deps = l["dependencies"] as IEnumerable;
            if (deps != null) foreach (var o in deps) p.Deps.Add((string)o);
            return p;
        }

        public static string DownloadUrl(ModEntry m)
        {
            return "https://thunderstore.io/package/download/" + m.Ns() + "/" + m.PkgName() + "/" + m.Version + "/";
        }

        // Acepta "Autor-Nombre", "Autor-Nombre-1.2.3" o una URL de thunderstore.io
        public static string ParseId(string s)
        {
            s = (s ?? "").Trim();
            var m = Regex.Match(s, @"thunderstore\.io/(?:c/valheim/)?(?:p|package)/([^/]+)/([^/]+)");
            if (m.Success) return m.Groups[1].Value + "-" + m.Groups[2].Value;
            m = Regex.Match(s, @"^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)(?:-\d+(?:\.\d+)*)?$");
            if (m.Success) return m.Groups[1].Value + "-" + m.Groups[2].Value;
            return null;
        }

        public static void SplitDep(string dep, out string id, out string ver)
        {
            int i = dep.LastIndexOf('-');
            id = dep.Substring(0, i); ver = dep.Substring(i + 1);
        }
    }

    public static class PackageCache
    {
        public static string PathFor(ModEntry m) { return Path.Combine(AppInfo.CacheDir, m.FullName() + ".zip"); }

        public static bool Valid(string path)
        {
            try { using (var z = ZipFile.OpenRead(path)) return z.Entries.Count > 0; }
            catch { return false; }
        }

        public static string Ensure(ModEntry m, string fallbackBase, Action<string> status)
        {
            string p = PathFor(m);
            if (File.Exists(p) && Valid(p)) return p;
            Directory.CreateDirectory(AppInfo.CacheDir);
            var urls = new List<string>();
            if (!m.Local) urls.Add(Thunderstore.DownloadUrl(m));
            if (!string.IsNullOrEmpty(fallbackBase)) urls.Add(fallbackBase.TrimEnd('/') + "/pkg/" + m.FullName() + ".zip");
            Exception last = null;
            foreach (var u in urls)
            {
                string tmp = p + ".part";
                try
                {
                    Http.Download(u, tmp, (done, total) =>
                    {
                        if (status != null) status(string.Format("Descargando {0}  {1:0.0}/{2:0.0} MB", m.FullName(), done / 1048576.0, total > 0 ? total / 1048576.0 : 0));
                    });
                    if (!Valid(tmp)) throw new Exception("el archivo descargado esta dañado");
                    if (File.Exists(p)) File.Delete(p);
                    File.Move(tmp, p);
                    return p;
                }
                catch (Exception e)
                {
                    last = e;
                    Log.W("Fallo descargando {0} desde {1}: {2}", m.FullName(), u, e.Message);
                    try { File.Delete(tmp); } catch { }
                }
            }
            throw new Exception("No pude descargar " + m.FullName() + ": " + (last != null ? last.Message : "?"));
        }

        public static Dictionary<string, object> Manifest(string zip)
        {
            using (var z = ZipFile.OpenRead(zip))
            {
                var e = z.Entries.FirstOrDefault(x => x.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
                if (e == null) return new Dictionary<string, object>();
                using (var sr = new StreamReader(e.Open(), Encoding.UTF8)) return Json.ParseObj(sr.ReadToEnd());
            }
        }
    }

    // ---------------------------------------------------------------- instalación

    public static class Installer
    {
        static readonly string[] PackRootFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "start_game_bepinex.sh", "start_server_bepinex.sh" };

        public static List<ModEntry> ModsFor(Modpack pack, bool client)
        {
            return pack.Mods.Where(m => m.Enabled && (client ? m.ForClient() : m.ForServer())).ToList();
        }

        public static bool UpToDate(Modpack pack, string profile, bool client)
        {
            if (!File.Exists(Path.Combine(profile, @"BepInEx\core\BepInEx.Preloader.dll"))) return false;
            var inst = InstalledInfo.Load(profile);
            var want = ModsFor(pack, client).Select(m => m.FullName()).OrderBy(x => x).ToList();
            return want.SequenceEqual(inst.Mods.OrderBy(x => x));
        }

        // Instala el modpack en un perfil. Descarga todo ANTES de tocar nada, y hace backup de la config.
        public static List<string> Install(Modpack pack, string profile, bool client, string fallbackBase, Action<string> status)
        {
            var warnings = new List<string>();
            var mods = ModsFor(pack, client);
            if (!mods.Any(m => m.Id.Equals(AppInfo.BepInExId, StringComparison.OrdinalIgnoreCase)))
                throw new Exception("El modpack necesita " + AppInfo.BepInExId);

            var zips = new Dictionary<ModEntry, string>();
            int k = 0;
            foreach (var m in mods)
            {
                k++;
                if (status != null) status(string.Format("[{0}/{1}] {2}", k, mods.Count, m.FullName()));
                zips[m] = PackageCache.Ensure(m, fallbackBase, status);
            }

            string bep = Path.Combine(profile, "BepInEx");
            string cfg = Path.Combine(bep, "config");
            if (Directory.Exists(cfg) && Directory.EnumerateFileSystemEntries(cfg).Any())
                Backup.Create("config-antes-de-instalar", new[] { new Backup.Item(cfg, "config") }, true);

            if (status != null) status("Instalando...");
            foreach (var d in new[] { "plugins", "patchers", "core" }) DeleteDir(Path.Combine(bep, d));
            try { DeleteDir(Path.Combine(bep, "cache")); } catch (IOException) { }   // caché de BepInEx: se regenera sola
            DeleteDir(Path.Combine(profile, "doorstop_libs"));
            foreach (var f in PackRootFiles) { var fp = Path.Combine(profile, f); if (File.Exists(fp)) File.Delete(fp); }
            Directory.CreateDirectory(cfg);
            Directory.CreateDirectory(Path.Combine(bep, "plugins"));

            try
            {
                foreach (var m in mods.OrderBy(m => m.Id.Equals(AppInfo.BepInExId, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                    Extract(m, zips[m], profile);
            }
            catch (PathTooLongException)
            {
                throw new Exception("La carpeta del launcher está demasiado escondida y Windows no admite rutas tan largas:\n" + AppInfo.Root +
                    "\n\nMueve Majinheim.exe a una carpeta más corta, por ejemplo C:\\Majinheim o Documentos\\Majinheim, y vuelve a abrirlo.");
            }

            foreach (var m in mods.Where(x => !string.IsNullOrEmpty(x.Patch)))
            {
                if (status != null) status("Aplicando arreglo " + m.Patch + " a " + m.Id + "...");
                ApplyPatch(m, profile, client);
            }

            var byId = mods.ToDictionary(m => m.Id.ToLowerInvariant());
            foreach (var m in mods)
            {
                try
                {
                    var man = PackageCache.Manifest(zips[m]);
                    object depsO;
                    if (!man.TryGetValue("dependencies", out depsO) || !(depsO is IEnumerable)) continue;
                    foreach (var o in (IEnumerable)depsO)
                    {
                        string did, dver;
                        Thunderstore.SplitDep((string)o, out did, out dver);
                        ModEntry have;
                        if (!byId.TryGetValue(did.ToLowerInvariant(), out have))
                            warnings.Add(m.Id + " necesita " + did + " (no está en el modpack)");
                        else if (Ver.Compare(have.Version, dver) < 0)
                            warnings.Add(m.Id + " pide " + did + " " + dver + " o mas nuevo (tienes " + have.Version + ")");
                    }
                }
                catch (Exception e) { warnings.Add("No pude leer manifest de " + m.Id + ": " + e.Message); }
            }

            var inst = new InstalledInfo { Revision = pack.Revision, Date = DateTime.Now.ToString("s") };
            inst.Mods = mods.Select(m => m.FullName()).ToList();
            Json.Save(Path.Combine(profile, "installed.json"), inst);
            foreach (var w in warnings) Log.W("AVISO: {0}", w);
            Log.W("Instalados {0} mods en {1}", mods.Count, profile);
            return warnings;
        }

        // Carpeta Managed del juego (para que el arreglo encuentre assembly_valheim.dll)
        public static string GameManaged(bool client)
        {
            if (client)
            {
                string g = Player.GameDir();
                if (g == null) throw new Exception("No encuentro Valheim instalado: hace falta para preparar el mod de cajones. Indica la carpeta del juego en Ajustes.");
                return Path.Combine(g, @"valheim_Data\Managed");
            }
            return Path.Combine(Settings.Current.ServerDir ?? "", @"valheim_server_Data\Managed");
        }

        static void ApplyPatch(ModEntry m, string profile, bool client)
        {
            if (m.Patch != DrawerPatch.Name) throw new Exception("Arreglo desconocido: " + m.Patch);
            string dll = Path.Combine(profile, @"BepInEx\plugins", m.Id, @"plugins\kg_ItemDrawers.dll");
            if (!File.Exists(dll)) dll = Directory.GetFiles(Path.Combine(profile, @"BepInEx\plugins", m.Id), "kg_ItemDrawers.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dll == null) throw new Exception("No encuentro kg_ItemDrawers.dll en " + m.Id);
            CecilLoader.Hook(profile);
            RunDrawerPatch(dll, GameManaged(client));
        }

        // separado para que Mono.Cecil solo se cargue cuando hace falta (después de Hook)
        static void RunDrawerPatch(string dll, string managed)
        {
            var log = DrawerPatch.Apply(dll, managed);
            Log.W("Cajones reparados para la 1.0 ({0} cambios)", log.Count);
        }

        static void Extract(ModEntry m, string zip, string profile)
        {
            string full = Path.GetFullPath(profile).TrimEnd('\\') + "\\";
            string cfgDir = full + "BepInEx\\config\\";
            bool isPack = m.Id.Equals(AppInfo.BepInExId, StringComparison.OrdinalIgnoreCase);
            using (var z = ZipFile.OpenRead(zip))
            {
                foreach (var e in z.Entries)
                {
                    string rel = e.FullName.Replace('\\', '/');
                    if (rel.EndsWith("/") || e.Name == "") continue;
                    string target = Map(m, rel, isPack);
                    if (target == null) continue;
                    string dest = Path.GetFullPath(Path.Combine(profile, target.Replace('/', '\\')));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    // nunca pisamos una config existente: es la del usuario
                    if (dest.StartsWith(cfgDir, StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
        }

        // Mismas reglas que usan r2modman/Thunderstore para BepInEx
        static string Map(ModEntry m, string rel, bool isPack)
        {
            if (isPack)
            {
                const string pfx = "BepInExPack_Valheim/";
                return rel.StartsWith(pfx, StringComparison.OrdinalIgnoreCase) ? rel.Substring(pfx.Length) : null;
            }
            var parts = rel.Split('/').ToList();
            if (parts.Count > 1 && parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(0);
            if (parts.Count > 1)
            {
                string top = parts[0].ToLowerInvariant();
                string rest = string.Join("/", parts.Skip(1));
                if (top == "plugins") return "BepInEx/plugins/" + m.Id + "/" + rest;
                if (top == "patchers") return "BepInEx/patchers/" + m.Id + "/" + rest;
                if (top == "config") return "BepInEx/config/" + rest;
                if (top == "core") return "BepInEx/core/" + rest;
            }
            return "BepInEx/plugins/" + m.Id + "/" + string.Join("/", parts);
        }

        public static void DeleteDir(string d)
        {
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }

        public static void CopyDir(string src, string dst, bool overwrite)
        {
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string t = Path.Combine(dst, f.Substring(src.TrimEnd('\\').Length + 1));
                if (!overwrite && File.Exists(t)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                File.Copy(f, t, overwrite);
            }
        }
    }

    // ---------------------------------------------------------------- backups

    public static class Backup
    {
        public class Item
        {
            public string Source, Name;
            public Func<string, bool> Filter;
            public Item(string src, string name) { Source = src; Name = name; }
        }

        public class Info
        {
            public string Path, Label, Kind;
            public DateTime Date;
            public long Size;
        }

        // Cada zip lleva backup.json con a donde va cada carpeta, para poder restaurarlo.
        public static string Create(string label, IEnumerable<Item> items, bool auto)
        {
            Directory.CreateDirectory(AppInfo.BackupDir);
            string name = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Regex.Replace(label, @"[^A-Za-z0-9_\-]", "_") + (auto ? ".auto" : "") + ".zip";
            string path = Path.Combine(AppInfo.BackupDir, name), tmp = path + ".part";
            var map = new Dictionary<string, string>();
            using (var fs = File.Create(tmp))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var it in items)
                {
                    map[it.Name] = it.Source;
                    if (File.Exists(it.Source)) AddFile(z, it.Source, it.Name + "/" + System.IO.Path.GetFileName(it.Source));
                    else if (Directory.Exists(it.Source))
                        foreach (var f in Directory.GetFiles(it.Source, "*", SearchOption.AllDirectories))
                        {
                            string rel = f.Substring(it.Source.TrimEnd('\\').Length + 1);
                            if (it.Filter != null && !it.Filter(rel)) continue;
                            AddFile(z, f, it.Name + "/" + rel.Replace('\\', '/'));
                        }
                }
                var je = z.CreateEntry("backup.json");
                using (var w = new StreamWriter(je.Open(), new UTF8Encoding(false))) w.Write(Json.Str(map));
            }
            File.Move(tmp, path);
            Log.W("Backup creado: {0}", name);
            if (auto) Prune();
            return path;
        }

        static void AddFile(ZipArchive z, string f, string name)
        {
            var e = z.CreateEntry(name, CompressionLevel.Fastest);
            e.LastWriteTime = File.GetLastWriteTime(f);
            using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = e.Open()) src.CopyTo(dst);
        }

        public static List<Info> List()
        {
            if (!Directory.Exists(AppInfo.BackupDir)) return new List<Info>();
            return new DirectoryInfo(AppInfo.BackupDir).GetFiles("*.zip").OrderByDescending(f => f.Name).Select(f =>
            {
                var i = new Info { Path = f.FullName, Size = f.Length, Date = f.LastWriteTime, Kind = f.Name.EndsWith(".auto.zip") ? "auto" : "manual" };
                string n = f.Name.Replace(".auto.zip", "").Replace(".zip", "");
                i.Label = n.Length > 20 ? n.Substring(20) : n;
                return i;
            }).ToList();
        }

        static void Prune()
        {
            int keep = Math.Max(5, Settings.Current.KeepAutoBackups);
            foreach (var group in List().Where(b => b.Kind == "auto").GroupBy(b => b.Label))
                foreach (var old in group.Skip(keep))
                    try { File.Delete(old.Path); Log.W("Backup antiguo borrado: {0}", System.IO.Path.GetFileName(old.Path)); } catch { }
        }

        // Restaura sobre los destinos originales. Antes guarda un backup de lo que va a pisar.
        public static void Restore(string zipPath)
        {
            Dictionary<string, string> map;
            using (var z = ZipFile.OpenRead(zipPath))
            {
                var je = z.GetEntry("backup.json");
                if (je == null) throw new Exception("Este backup no tiene backup.json, no se puede restaurar automaticamente.");
                using (var sr = new StreamReader(je.Open())) map = Json.Parse<Dictionary<string, string>>(sr.ReadToEnd());
            }
            Create("antes-de-restaurar", map.Select(kv => new Item(kv.Value, kv.Key)), false);
            using (var z = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in z.Entries)
                {
                    if (e.Name == "" || e.FullName == "backup.json") continue;
                    int s = e.FullName.IndexOf('/');
                    string key = e.FullName.Substring(0, s), rel = e.FullName.Substring(s + 1);
                    string root;
                    if (!map.TryGetValue(key, out root)) continue;
                    bool isFile = File.Exists(root) || (!Directory.Exists(root) && System.IO.Path.HasExtension(root));
                    string baseDir = isFile ? System.IO.Path.GetDirectoryName(root) : root;
                    string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, rel.Replace('/', '\\')));
                    if (!dest.StartsWith(System.IO.Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
            Log.W("Restaurado: {0}", System.IO.Path.GetFileName(zipPath));
        }

        // Mundo + personajes guardados en el server, sin los backups internos de Valheim.
        public static IEnumerable<Item> WorldItems()
        {
            Func<string, bool> noInternal = rel => !rel.Contains("_backup") && !rel.EndsWith(".old") && !rel.StartsWith("backups\\");
            // Valheim 1.0 guarda el mundo en worlds_local\<Mundo>\ (chunks); antes en <Mundo>.db/.fwl
            string world = Settings.Current.WorldName;
            var w = new Item(Path.Combine(AppInfo.SavesDir, "worlds_local"), "saves_worlds_local");
            w.Filter = rel => noInternal(rel) && rel.StartsWith(world, StringComparison.OrdinalIgnoreCase);
            var c = new Item(Path.Combine(AppInfo.SavesDir, "characters_local"), "saves_characters_local");
            c.Filter = noInternal;
            var list = new List<Item> { w, c };
            foreach (var f in new[] { "adminlist.txt", "bannedlist.txt", "permittedlist.txt" })
                if (File.Exists(Path.Combine(AppInfo.SavesDir, f))) list.Add(new Item(Path.Combine(AppInfo.SavesDir, f), "saves_" + f.Replace(".txt", "")));
            return list;
        }

        public static IEnumerable<Item> FullItems()
        {
            var list = WorldItems().ToList();
            string cfg = Path.Combine(AppInfo.Profile, @"BepInEx\config");
            if (Directory.Exists(cfg)) list.Add(new Item(cfg, "config"));
            if (File.Exists(AppInfo.ModpackFile)) list.Add(new Item(AppInfo.ModpackFile, "modpack"));
            if (File.Exists(AppInfo.SettingsFile)) list.Add(new Item(AppInfo.SettingsFile, "settings"));
            return list;
        }
    }

    // ---------------------------------------------------------------- steam / doorstop

    public static class Steam
    {
        public static string SteamPath()
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var key in new[] { @"Software\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" })
                {
                    using (var k = hive.OpenSubKey(key))
                    {
                        if (k == null) continue;
                        var v = (k.GetValue("SteamPath") ?? k.GetValue("InstallPath")) as string;
                        if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v.Replace('/', '\\');
                    }
                }
            }
            return @"C:\Program Files (x86)\Steam";
        }

        public static string SteamExe() { return Path.Combine(SteamPath(), "steam.exe"); }

        public static List<string> Libraries()
        {
            var libs = new List<string> { SteamPath() };
            string vdf = Path.Combine(SteamPath(), @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
            return libs;
        }

        public static string FindApp(string appId, string exe)
        {
            foreach (var lib in Libraries())
            {
                string acf = Path.Combine(lib, @"steamapps\appmanifest_" + appId + ".acf");
                if (!File.Exists(acf)) continue;
                var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"(.+?)\"");
                if (!m.Success) continue;
                string dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                if (File.Exists(Path.Combine(dir, exe))) return dir;
            }
            return null;
        }
    }

    public static class Doorstop
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

        public static string ShortPath(string p)
        {
            var sb = new StringBuilder(1024);
            return GetShortPathName(p, sb, 1024) > 0 ? sb.ToString() : p;
        }

        public static string Preloader(string profile) { return Path.Combine(profile, @"BepInEx\core\BepInEx.Preloader.dll"); }

        // Doorstop 4: los argumentos de linea de comandos mandan sobre doorstop_config.ini
        public static string Args(string profile, bool enabled)
        {
            if (!enabled) return "--doorstop-enabled false";
            string pre = Preloader(profile);
            if (pre.Contains(" ")) pre = ShortPath(pre);
            return "--doorstop-enabled true --doorstop-target-assembly \"" + pre + "\"";
        }

        static string Hash(string f) { using (var s = File.OpenRead(f)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }

        // Server: solo anade winhttp.dll si falta. No modifica nada que ya exista en la carpeta de Steam.
        public static void EnsureServerProxy(string serverDir, string profile)
        {
            string dst = Path.Combine(serverDir, "winhttp.dll");
            if (File.Exists(dst)) return;
            File.Copy(Path.Combine(profile, "winhttp.dll"), dst);
            string ini = Path.Combine(serverDir, "doorstop_config.ini");
            if (!File.Exists(ini)) File.WriteAllText(ini, DisabledIni());
            Log.W("Añadido winhttp.dll (doorstop) a la carpeta del server");
        }

        // Cliente: deja el juego vanilla al abrirlo desde Steam; solo carga mods cuando lo lanza el launcher.
        public static void EnsureClientProxy(string gameDir, string profile)
        {
            string src = Path.Combine(profile, "winhttp.dll");
            string dst = Path.Combine(gameDir, "winhttp.dll");
            string ini = Path.Combine(gameDir, "doorstop_config.ini");
            if (File.Exists(dst) && Hash(dst) == Hash(src)) return;
            if (File.Exists(dst) || File.Exists(ini))
                Backup.Create("doorstop-original-del-juego", new[] { new Backup.Item(dst, "game_winhttp"), new Backup.Item(ini, "game_doorstop_ini") }.Where(i => File.Exists(i.Source)), false);
            File.Copy(src, dst, true);
            File.Copy(Path.Combine(profile, ".doorstop_version"), Path.Combine(gameDir, ".doorstop_version"), true);
            if (!File.Exists(ini)) File.WriteAllText(ini, DisabledIni());
            Log.W("Instalado doorstop en la carpeta del juego");
        }

        static string DisabledIni()
        {
            return "# Majinheim Launcher: desactivado por defecto. El launcher lo activa al jugar con mods.\r\n[General]\r\nenabled = false\r\ntarget_assembly=BepInEx\\core\\BepInEx.Preloader.dll\r\nredirect_output_log = false\r\nboot_config_override =\r\nignore_disable_switch = false\r\n\r\n[UnityMono]\r\ndll_search_path_override =\r\ndebug_enabled = false\r\ndebug_address = 127.0.0.1:10000\r\ndebug_suspend = false\r\n";
        }
    }

    // ---------------------------------------------------------------- servidor de juego

    public class ServerRunner
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr h, bool add);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint ev, uint group);
        static readonly object consoleLock = new object();

        Process proc;
        volatile bool stopRequested;
        volatile int tailGen;
        public event Action<string> GameLog;
        public event Action StateChanged;
        public int Players { get; private set; }
        public DateTime StartedAt { get; private set; }
        public string Status { get; private set; }
        public ServerRunner() { Status = "Detenido"; }

        public bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

        void SetStatus(string s) { Status = s; var h = StateChanged; if (h != null) h(); }

        public static string ServerExe() { return Path.Combine(Settings.Current.ServerDir, "valheim_server.exe"); }

        public string BuildArgs()
        {
            var s = Settings.Current;
            var a = new StringBuilder();
            a.AppendFormat("-nographics -batchmode -name \"{0}\" -port {1} -world \"{2}\" -password \"{3}\" -public {4} -savedir \"{5}\"",
                s.ServerName, s.Port, s.WorldName, s.Password, s.Public ? 1 : 0, AppInfo.SavesDir);
            if (s.Crossplay) a.Append(" -crossplay");
            if (!string.IsNullOrWhiteSpace(s.ExtraArgs)) a.Append(" ").Append(s.ExtraArgs.Trim());
            a.Append(" ").Append(Doorstop.Args(AppInfo.Profile, true));
            return a.ToString();
        }

        public void Start()
        {
            var s = Settings.Current;
            if (Running) throw new Exception("El server ya está en marcha.");
            if (!File.Exists(ServerExe())) throw new Exception("No encuentro valheim_server.exe en: " + s.ServerDir);
            if (!File.Exists(Doorstop.Preloader(AppInfo.Profile))) throw new Exception("Los mods no están instalados. Ve a la pestaña Mods y pulsa 'Aplicar al servidor'.");
            if (Process.GetProcessesByName("valheim_server").Length > 0) throw new Exception("Ya hay otro valheim_server.exe abierto. Ciérralo antes.");
            if (s.Password.Length > 0 && s.Password.Length < 5) Log.W("Ojo: Valheim recomienda contraseñas de 5 o mas caracteres.");

            stopRequested = false;
            SetStatus("Haciendo backup...");
            if (Directory.Exists(Path.Combine(AppInfo.SavesDir, "worlds_local")))
                Backup.Create("mundo-al-arrancar", Backup.WorldItems(), true);
            Doorstop.EnsureServerProxy(s.ServerDir, AppInfo.Profile);

            var psi = new ProcessStartInfo(ServerExe(), BuildArgs());
            psi.WorkingDirectory = s.ServerDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.EnvironmentVariables["SteamAppId"] = AppInfo.GameAppId;

            Directory.CreateDirectory(AppInfo.LogDir);
            var stdout = new StreamWriter(Path.Combine(AppInfo.LogDir, "server-stdout.log"), false, new UTF8Encoding(false));
            stdout.AutoFlush = true;
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler h = (o, e) => { if (e.Data != null) lock (stdout) stdout.WriteLine(e.Data); };
            proc.OutputDataReceived += h;
            proc.ErrorDataReceived += h;
            proc.Exited += (o, e) => { lock (stdout) stdout.Dispose(); OnExit(); };
            Log.W("Arrancando server: {0} {1}", ServerExe(), BuildArgs().Replace("-password \"" + s.Password + "\"", "-password \"****\""));
            StartedAt = DateTime.Now;
            proc.Start();
            try { proc.PriorityClass = ProcessPriorityClass.High; } catch { }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            Players = 0;
            SetStatus("Arrancando...");
            int gen = ++tailGen;
            var t = new Thread(() => Tail(Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log"), StartedAt, gen));
            t.IsBackground = true;
            t.Start();
        }

        void OnExit()
        {
            int code = -1;
            try { code = proc.ExitCode; } catch { }
            Log.W("El server se ha cerrado (codigo {0}).", code);
            if (!stopRequested && Settings.Current.AutoRestart)
            {
                SetStatus("Crasheo: reiniciando en 10 s");
                Thread.Sleep(10000);
                if (stopRequested) { SetStatus("Detenido"); return; }
                try { Start(); }
                catch (Exception e) { Log.W("No pude reiniciar: {0}", e.Message); SetStatus("Detenido"); }
                return;
            }
            SetStatus("Detenido");
        }

        // Si cerraste el launcher con el server en marcha, al abrirlo lo vuelve a controlar.
        public bool TryAdopt()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("valheim_server"))
                {
                    string cmd = CommandLine(p.Id);
                    if (cmd == null || cmd.IndexOf(AppInfo.SavesDir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    proc = p;
                    stopRequested = false;
                    p.EnableRaisingEvents = true;
                    p.Exited += (o, e) => OnExit();
                    StartedAt = p.StartTime;
                    SetStatus("En marcha");
                    int gen = ++tailGen;
                    var t = new Thread(() => Tail(Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log"), DateTime.MinValue, gen));
                    t.IsBackground = true;
                    t.Start();
                    Log.W("Server ya en marcha detectado (PID {0}); el launcher vuelve a controlarlo.", p.Id);
                    return true;
                }
            }
            catch (Exception e) { Log.W("No pude comprobar servers abiertos: {0}", e.Message); }
            return false;
        }

        public static string CommandLine(int pid)
        {
            using (var s = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                foreach (System.Management.ManagementObject o in s.Get()) return o["CommandLine"] as string;
            return null;
        }

        public readonly List<string> RecentJoins = new List<string>();
        static readonly Regex rxJoin = new Regex(@"Got character ZDOID from (.+?) :");
        static readonly Regex rxConn = new Regex(@"Connections (\d+) ZDOS");

        void Tail(string file, DateTime since, int gen)
        {
            long pos = 0;
            string partial = "";
            bool fresh = false;
            while (gen == tailGen)
            {
                try
                {
                    if (!fresh && File.Exists(file) && (since == DateTime.MinValue || File.GetLastWriteTime(file) >= since.AddSeconds(-2))) fresh = true;
                    if (fresh && File.Exists(file))
                    {
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            if (fs.Length < pos) pos = 0;
                            if (fs.Length > pos)
                            {
                                fs.Seek(pos, SeekOrigin.Begin);
                                var buf = new byte[fs.Length - pos];
                                int n = fs.Read(buf, 0, buf.Length);
                                pos += n;
                                string txt = partial + Encoding.UTF8.GetString(buf, 0, n);
                                var lines = txt.Split('\n');
                                partial = lines[lines.Length - 1];
                                for (int i = 0; i < lines.Length - 1; i++) Emit(lines[i].TrimEnd('\r'));
                            }
                        }
                    }
                }
                catch { }
                if (!Running && gen == tailGen && (DateTime.Now - since).TotalSeconds > 5) { Thread.Sleep(1500); if (!Running) break; }
                Thread.Sleep(700);
            }
        }

        void Emit(string line)
        {
            if (line.Length == 0) return;
            var m = rxConn.Match(line);
            if (m.Success) { Players = int.Parse(m.Groups[1].Value); SetStatus("En marcha"); }
            else if (line.Contains("Game server connected")) SetStatus("En marcha");
            else if (line.Contains("Got connection SteamID")) { Players++; SetStatus(Status); }
            var j = rxJoin.Match(line);
            if (j.Success)
                lock (RecentJoins)
                {
                    RecentJoins.Insert(0, DateTime.Now.ToString("dd/MM HH:mm") + "  " + j.Groups[1].Value);
                    if (RecentJoins.Count > 12) RecentJoins.RemoveAt(RecentJoins.Count - 1);
                }
            var h = GameLog;
            if (h != null) h(line);
        }

        // Cierre limpio: Ctrl+C a la consola del server, que guarda el mundo antes de salir.
        public void Stop(bool wait)
        {
            if (!Running) return;
            stopRequested = true;
            SetStatus("Guardando y cerrando...");
            var p = proc;
            Action body = () =>
            {
                bool sent = SendCtrlC(p.Id);
                Log.W(sent ? "Enviado cierre limpio al server, esperando a que guarde..." : "No pude enviar cierre limpio.");
                if (!p.WaitForExit(sent ? 120000 : 1000))
                {
                    Log.W("El server no respondió: forzando cierre (el último autoguardado se conserva).");
                    try { p.Kill(); } catch { }
                }
            };
            if (wait) body();
            else { var t = new Thread(() => body()); t.IsBackground = true; t.Start(); }
        }

        static bool SendCtrlC(int pid)
        {
            lock (consoleLock)
            {
                FreeConsole();
                if (!AttachConsole((uint)pid)) { AttachConsole(uint.MaxValue); return false; }
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                bool ok = GenerateConsoleCtrlEvent(0, 0);
                Thread.Sleep(1500);
                FreeConsole();
                SetConsoleCtrlHandler(IntPtr.Zero, false);
                AttachConsole(uint.MaxValue);
                return ok;
            }
        }
    }

    // ---------------------------------------------------------------- reparto a amigos (HTTP minimo)

    // GitHub: ahí se publica el modpack (modpack.json) y el launcher de jugador (Releases). Nunca los mods.
    public static class GitHub
    {
        static GitHub() { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }   // GitHub exige TLS 1.2

        public static string Repo
        {
            get
            {
                string r = (Settings.Current.GitHubRepo ?? "").Trim().Trim('/');
                r = Regex.Replace(r, @"^https?://github\.com/", "", RegexOptions.IgnoreCase);
                return r != "" ? r : AppInfo.GitHubRepo;
            }
        }

        static string Api(string path) { return "https://api.github.com/repos/" + Repo + path; }

        // Clave para publicar: la de Ajustes si la hay; si no, la sesión de GitHub CLI de este PC (gh auth login).
        public static string Token(bool required = false)
        {
            string t = (Settings.Current.GitHubToken ?? "").Trim();
            if (t == "")
            {
                foreach (var gh in new[] { "gh", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GitHub CLI\gh.exe") })
                {
                    try
                    {
                        var psi = new ProcessStartInfo(gh, "auth token --hostname github.com") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                        using (var p = Process.Start(psi))
                        {
                            string o = p.StandardOutput.ReadToEnd().Trim();
                            p.WaitForExit(10000);
                            if (p.ExitCode == 0 && o != "") { t = o; break; }
                        }
                    }
                    catch { }
                }
            }
            if (t == "" && required) throw new Exception("GitHub no está conectado en este PC. Conéctalo una vez con GitHub CLI (gh auth login) o pon una clave en Amigos.");
            return t == "" ? null : t;
        }

        public static string Call(string method, string url, string token, byte[] body, string contentType, string accept)
        {
            if (string.IsNullOrEmpty(Repo)) throw new Exception("Falta el repositorio de GitHub (usuario/nombre) en los ajustes.");
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = method;
            r.UserAgent = "MajinheimLauncher/" + AppInfo.Version;
            r.Accept = accept ?? "application/vnd.github+json";
            r.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            r.Timeout = 60000; r.ReadWriteTimeout = 600000;
            if (!string.IsNullOrEmpty(token)) r.Headers["Authorization"] = "Bearer " + token.Trim();
            if (body != null)
            {
                r.ContentType = contentType ?? "application/json";
                r.ContentLength = body.Length;
                using (var s = r.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            try
            {
                using (var resp = r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException e)
            {
                var hr = e.Response as HttpWebResponse;
                string detail = "";
                if (e.Response != null) using (var sr = new StreamReader(e.Response.GetResponseStream())) detail = sr.ReadToEnd();
                var ex = new Exception("GitHub " + (hr != null ? ((int)hr.StatusCode).ToString() : "") + ": " + e.Message + (detail != "" ? " " + detail : ""));
                ex.Data["status"] = hr != null ? (int)hr.StatusCode : 0;
                throw ex;
            }
        }

        static int Status(Exception e) { return e.Data.Contains("status") ? (int)e.Data["status"] : 0; }
        static Dictionary<string, object> Obj(string json) { return Json.Parse<Dictionary<string, object>>(json); }
        static byte[] Body(object o) { return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(o)); }

        // Lee un archivo del repositorio (rama principal), sin necesidad de clave.
        public static string ReadFile(string path)
        {
            return Call("GET", Api("/contents/" + path), null, null, null, "application/vnd.github.raw");
        }

        // Crea o actualiza un archivo del repositorio.
        public static void PutFile(string path, byte[] content, string message, string token)
        {
            string sha = null;
            try { sha = (string)Obj(Call("GET", Api("/contents/" + path), token, null, null, null))["sha"]; }
            catch (Exception e) { if (Status(e) != 404) throw; }
            var req = new Dictionary<string, object> { { "message", message }, { "content", Convert.ToBase64String(content) } };
            if (sha != null) req["sha"] = sha;
            Call("PUT", Api("/contents/" + path), token, Body(req), "application/json", null);
        }

        public class Release { public string Version, Url, Notes; }

        // Última versión del launcher de jugador publicada en Releases (null si no hay ninguna).
        public static Release Latest()
        {
            Dictionary<string, object> o;
            try { o = Obj(Call("GET", Api("/releases/latest"), null, null, null, null)); }
            catch (Exception e) { if (Status(e) == 404) return null; throw; }
            var rel = new Release { Version = ((string)o["tag_name"]).TrimStart('v', 'V'), Notes = o.ContainsKey("body") ? o["body"] as string : "" };
            foreach (var a in (IEnumerable)o["assets"])
            {
                var ad = (Dictionary<string, object>)a;
                if (string.Equals((string)ad["name"], AppInfo.ClientExeName, StringComparison.OrdinalIgnoreCase)) rel.Url = (string)ad["browser_download_url"];
            }
            return rel;
        }

        // Publica (o reemplaza) el launcher de jugador como Release "v<version>".
        public static void PublishRelease(string version, string exe, string notes, string token)
        {
            string tag = "v" + version;
            Dictionary<string, object> rel;
            try { rel = Obj(Call("GET", Api("/releases/tags/" + tag), token, null, null, null)); }
            catch (Exception e)
            {
                if (Status(e) != 404) throw;
                rel = Obj(Call("POST", Api("/releases"), token, Body(new Dictionary<string, object> { { "tag_name", tag }, { "name", "Majinheim " + version }, { "body", notes ?? "" } }), "application/json", null));
            }
            foreach (var a in (IEnumerable)rel["assets"])
            {
                var ad = (Dictionary<string, object>)a;
                if (string.Equals((string)ad["name"], AppInfo.ClientExeName, StringComparison.OrdinalIgnoreCase))
                    Call("DELETE", Api("/releases/assets/" + ad["id"]), token, null, null, null);
            }
            string up = Regex.Replace((string)rel["upload_url"], @"\{.*\}$", "") + "?name=" + AppInfo.ClientExeName;
            Call("POST", up, token, File.ReadAllBytes(exe), "application/octet-stream", null);
        }
    }

#if SERVER
    // Lo que se publica para los jugadores: la lista de mods (sin los mods) y el launcher.
    public static class Publish
    {
        public static Modpack ClientPack()
        {
            var src = Modpack.Load();
            var s = Settings.Current;
            var p = new Modpack { Name = src.Name, Revision = src.Revision, Message = src.Message, LauncherVersion = AppInfo.Version };
            p.GameAddress = (string.IsNullOrWhiteSpace(s.PublicHost) ? AppInfo.DefaultHost : s.PublicHost.Trim()) + ":" + s.Port;
            p.WebUrl = s.WebEnabled && s.WebPublic ? WebPanel.PublicUrl : "";
            p.Mods = Installer.ModsFor(src, true);
            if (p.Mods.Any(m => m.Local)) throw new Exception("El modpack tiene mods propios (" + string.Join(", ", p.Mods.Where(m => m.Local).Select(m => m.Id)) + ") que no están en Thunderstore: los jugadores no podrían bajarlos.");
            return p;
        }

        // Siempre deja una copia en distribucion\modpack.json, y la sube a GitHub si hay clave.
        public static string Pack(bool upload)
        {
            var p = ClientPack();
            Directory.CreateDirectory(AppInfo.DistDir);
            string f = Path.Combine(AppInfo.DistDir, "modpack.json");
            Json.Save(f, p);
            if (!upload) return "Guardado en " + f;
            string token = GitHub.Token();
            if (token == null) return "Guardado en " + f + " (GitHub no está conectado en este PC: súbelo a mano)";
            GitHub.PutFile("modpack.json", File.ReadAllBytes(f), "Modpack revisión " + p.Revision, token);
            Log.W("Modpack revisión {0} publicado en GitHub ({1})", p.Revision, GitHub.Repo);
            return "Publicado en GitHub (revisión " + p.Revision + ")";
        }

        // Las notas de la versión salen de CHANGELOG.md (sección "## <versión>").
        public static string ChangelogFor(string version)
        {
            string f = AppInfo.P("CHANGELOG.md");
            if (!File.Exists(f)) return "";
            var m = Regex.Match(File.ReadAllText(f), @"^## " + Regex.Escape(version) + @"\s*\r?\n(.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public static void Launcher(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) notes = ChangelogFor(AppInfo.Version);
            string exe = Path.Combine(AppInfo.DistDir, AppInfo.ClientExeName);
            if (!File.Exists(exe)) throw new Exception("No encuentro " + exe);
            GitHub.PublishRelease(AppInfo.Version, exe, notes, GitHub.Token(true));
            Log.W("Launcher de jugador {0} publicado en GitHub", AppInfo.Version);
        }

        // Sube el código fuente (sin contraseñas, sin mods, sin mundo).
        public static int Source()
        {
            string src = AppInfo.P("src");
            var files = new List<string>();
            foreach (var pat in new[] { "*.cs", "*.bat", "*.ico", "*.md" }) files.AddRange(Directory.GetFiles(src, pat));
            files.AddRange(Directory.GetFiles(Path.Combine(src, "patches"), "*.ps1"));
            files.AddRange(Directory.GetFiles(Path.Combine(src, "plugins"), "*.cs"));
            if (Directory.Exists(Path.Combine(src, "web"))) files.AddRange(Directory.GetFiles(Path.Combine(src, "web")));
            string token = GitHub.Token(true);
            foreach (var f in files)
            {
                string rel = "src/" + f.Substring(src.Length + 1).Replace('\\', '/');
                GitHub.PutFile(rel, File.ReadAllBytes(f), "Código del launcher " + AppInfo.Version, token);
            }
            foreach (var doc in new[] { "README.md", "CHANGELOG.md" })
                if (File.Exists(AppInfo.P(doc))) GitHub.PutFile(doc, File.ReadAllBytes(AppInfo.P(doc)), doc, token);
            return files.Count;
        }
    }
#endif

    // ---------------------------------------------------------------- edicion cliente

    // Utilidades de red: IPs de este PC, red de casa y búsqueda rápida del server.
    public static class Net
    {
        public static List<string> LocalIPv4()
        {
            var withGw = new List<string>();
            var rest = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    bool gw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    foreach (var a in props.UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) (gw ? withGw : rest).Add(a.Address.ToString());
                }
            }
            catch { }
            return withGw.Concat(rest).Distinct().ToList();
        }

        // La IP de casa de este PC (la que tiene router); null si no hay.
        public static string LanIp() { return LocalIPv4().FirstOrDefault(IsPrivate); }

        public static bool IsPrivate(string host)
        {
            IPAddress ip;
            if (host == "localhost") return true;
            if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        public static bool SameSubnet(string a, string b)
        {
            int i = a.LastIndexOf('.'), j = b.LastIndexOf('.');
            return i > 0 && j > 0 && a.Substring(0, i) == b.Substring(0, j);
        }

        // ¿Salimos a Internet por la misma IP pública a la que apunta la DDNS? Entonces estamos en casa del server.
        public static bool SamePublicIp(string ddns)
        {
            try
            {
                var mine = Http.GetString("https://api.ipify.org", 5000).Trim();
                return Dns.GetHostAddresses(ddns).Any(a => a.AddressFamily == AddressFamily.InterNetwork && a.ToString() == mine);
            }
            catch (Exception e) { Log.W("No pude comprobar la IP pública: {0}", e.Message); return false; }
        }

        // ¿Hay un server de Valheim en esa IP? Pregunta por el puerto de consultas de Steam (juego + 1, UDP).
        public static bool ValheimAnswers(string host, int gamePort, int ms)
        {
            try
            {
                using (var u = new UdpClient())
                {
                    u.Client.ReceiveTimeout = ms;
                    var q = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
                    q.AddRange(Encoding.ASCII.GetBytes("Source Engine Query"));
                    q.Add(0);
                    u.Send(q.ToArray(), q.Count, host, gamePort + 1);
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var r = u.Receive(ref from);
                    return r.Length > 4;
                }
            }
            catch { return false; }
        }
    }

    public static class Player
    {
        public static bool ViaLan;          // true si el server está en la misma red de casa que este PC
        public static GitHub.Release NewLauncher;   // versión nueva del launcher disponible (o null)

        // Baja la lista de mods de GitHub.
        public static Modpack FetchRemote(Action<string> status)
        {
            if (status != null) status("Consultando la lista de mods en GitHub (" + GitHub.Repo + ")...");
            return Json.Parse<Modpack>(GitHub.ReadFile("modpack.json"));
        }

        // A qué dirección entra el juego: si el server responde en la red de casa, por la IP de casa; si no, por la DDNS.
        public static string GameAddressFor(Modpack pack)
        {
            string ga = pack.GameAddress ?? "";
            int port;
            if (!ga.Contains(":") || !int.TryParse(ga.Substring(ga.LastIndexOf(':') + 1), out port)) port = AppInfo.DefaultGamePort;
            ViaLan = false;
            string lan = (Settings.Current.LanHost ?? "").Trim();   // solo la tiene quien juega en casa del server (Ajustes)
            if (lan == "") return ga;
            string host = ga.Contains(":") ? ga.Substring(0, ga.LastIndexOf(':')) : ga;
            if (Net.LocalIPv4().Any(ip => Net.SameSubnet(ip, lan)) && (Net.SamePublicIp(host) || Net.ValheimAnswers(lan, port, 1500)))
            {
                ViaLan = true;
                Log.W("El server responde en la red de casa ({0})", lan);
                return lan + ":" + port;
            }
            return ga;
        }

        // Baja el modpack de GitHub. Si no hay conexión usa el último que tengamos.
        public static Modpack Sync(Action<string> status, out bool online, out List<string> warnings)
        {
            warnings = new List<string>();
            Modpack pack;
            online = false;
            try
            {
                pack = FetchRemote(status);
                online = true;
                var local = Modpack.Load();
                if (File.Exists(AppInfo.ModpackFile) && local.Revision != pack.Revision) Log.W("Modpack actualizado: revisión {0} -> {1}", local.Revision, pack.Revision);
                pack.Save();
            }
            catch (Exception e)
            {
                Log.W("No pude bajar la lista de mods de GitHub: {0}", e.Message);
                if (!File.Exists(AppInfo.ModpackFile)) throw new Exception("No puedo bajar la lista de mods de GitHub y aún no tengo ninguna guardada. ¿Hay Internet?\n\n" + e.Message);
                pack = Modpack.Load();
            }
            try { var rel = GitHub.Latest(); NewLauncher = rel != null && rel.Url != null && Ver.Compare(rel.Version, AppInfo.Version) > 0 ? rel : null; }
            catch (Exception e) { Log.W("No pude mirar si hay launcher nuevo: {0}", e.Message); }
            if (!Installer.UpToDate(pack, AppInfo.Profile, true))
                warnings = Installer.Install(pack, AppInfo.Profile, true, null, status);
            return pack;
        }
        public static string GameDir()
        {
            var s = Settings.Current;
            if (!string.IsNullOrEmpty(s.GameDir) && File.Exists(Path.Combine(s.GameDir, "valheim.exe"))) return s.GameDir;
            var d = Steam.FindApp(AppInfo.GameAppId, "valheim.exe");
            if (d != null) { s.GameDir = d; Settings.Save(); }
            return d;
        }

        public static void Launch(Modpack pack, bool modded, bool connect)
        {
            string game = GameDir();
            if (game == null) throw new Exception("No encuentro Valheim instalado en Steam. Indica la carpeta del juego en Ajustes.");
            if (modded) Doorstop.EnsureClientProxy(game, AppInfo.Profile);
            string args = "-applaunch " + AppInfo.GameAppId + " -console " + Doorstop.Args(AppInfo.Profile, modded);   // -console: activa la consola (F5)
            string addr = pack != null ? GameAddressFor(pack) : "";
            if (modded && connect && addr != "") args += " +connect " + addr;
            Log.W("Lanzando Valheim {0}", modded ? "con mods" : "vanilla");
            Process.Start(new ProcessStartInfo(Steam.SteamExe(), args) { UseShellExecute = false });
        }

        // Auto-actualizacion desde GitHub Releases: renombra el exe en uso (Windows lo permite) y pone el nuevo en su lugar.
        public static bool SelfUpdate(Action<string> status)
        {
            var rel = NewLauncher;
            if (rel == null) return false;
            string me = Process.GetCurrentProcess().MainModule.FileName;
            string tmp = me + ".new", old = me + ".old";
            if (status != null) status("Actualizando el launcher a la versión " + rel.Version + "...");
            Http.Download(rel.Url, tmp, null);
            if (new FileInfo(tmp).Length < 20000) { File.Delete(tmp); throw new Exception("La descarga del launcher nuevo es inválida"); }
            if (File.Exists(old)) File.Delete(old);
            File.Move(me, old);
            File.Move(tmp, me);
            Log.W("Launcher actualizado a {0}", rel.Version);
            return true;
        }
        public static void CleanupOldExe()
        {
            try { string old = Process.GetCurrentProcess().MainModule.FileName + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        }
    }

    // ---------------------------------------------------------------- edicion server: utilidades

    public static class Admin
    {
        // Primera vez: copia (nunca mueve) el mundo y la config de la instalación original.
        public static List<string> ImportOriginal(string originalServerDir)
        {
            var done = new List<string>();
            if (!Directory.Exists(Path.Combine(AppInfo.SavesDir, "worlds_local")) && Directory.Exists(AppInfo.OriginalSaves))
            {
                Installer.CopyDir(AppInfo.OriginalSaves, AppInfo.SavesDir, false);
                done.Add("Mundo y personajes copiados de " + AppInfo.OriginalSaves);
            }
            string ocfg = Path.Combine(originalServerDir, @"BepInEx\config");
            string ncfg = Path.Combine(AppInfo.Profile, @"BepInEx\config");
            if (Directory.Exists(ocfg) && !Directory.Exists(ncfg))
            {
                Installer.CopyDir(ocfg, ncfg, false);
                done.Add("Configuración de mods copiada de " + ocfg);
            }
            foreach (var d in done) Log.W(d);
            return done;
        }

        public static void PublishClientExe()
        {
            // la edicion cliente compilada se deja en distribucion\ para que el server la reparta
            string f = Path.Combine(AppInfo.DistDir, AppInfo.ClientExeName);
            if (!File.Exists(f)) Log.W("Aviso: no existe {0}; tus amigos no podran descargar el launcher desde la web.", f);
        }
    }
}
