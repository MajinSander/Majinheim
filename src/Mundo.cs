// Lo que la IA sabe del mundo (2.2). El mod propio "Majinheim Mundo" (src\mod\MajinheimMundo.cs) va SOLO en el server y solo lee:
// cada pocos minutos apunta qué hay en cofres, cajones y tumbas, y al arrancar las recetas del juego con los mods, en español.
// El launcher lo compila aquí, en el PC del server, contra el Valheim instalado (si Valheim se actualiza, se vuelve a compilar solo
// al arrancar). Luego el asistente lo consulta con tres herramientas: buscar en cofres, recetas y tumbas. Edición Server.
#if SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Majinheim
{
    // ---------------------------------------------------------------- el mod: se prepara al arrancar el server

    public static class WorldMod
    {
        public const string Version = "1.3.1";
        const string Folder = "Majinheim-Mundo", DllName = "MajinheimMundo.dll", SourceName = "MajinheimMundo.cs";
        static readonly object lk = new object();

        public static string LastError { get; private set; }   // por qué no se pudo preparar (null = bien)
        public static string OutDir { get { return Path.Combine(AppInfo.Profile, @"BepInEx\majinheim"); } }   // donde el mod deja mundo.json y recetas.json
        static string BuildDir { get { return Path.Combine(AppInfo.CacheDir, "majinheim-mundo"); } }
        static string PluginDir { get { return Path.Combine(AppInfo.Profile, @"BepInEx\plugins", Folder); } }
        public static string ErrorLog { get { return Path.Combine(AppInfo.LogDir, "mundo-mod.log"); } }
        public static bool Installed { get { return File.Exists(Path.Combine(PluginDir, DllName)); } }

        // Justo antes de arrancar el server: deja el mod listo, o lo quita si está desactivado. Nunca lanza:
        // si algo falla, el server arranca igual, sin el mod (y la IA dice que no sabe qué hay en los cofres).
        public static void EnsureFresh()
        {
            lock (lk)
            {
                try
                {
                    if (!Settings.Current.WorldExport) { Remove(); LastError = null; return; }
                    string managed = Installer.GameManaged(false);
                    string bep = Path.Combine(AppInfo.Profile, @"BepInEx\core\BepInEx.dll");
                    if (!File.Exists(Path.Combine(managed, "assembly_valheim.dll"))) throw new Exception("no encuentro assembly_valheim.dll en " + managed);
                    if (!File.Exists(bep)) throw new Exception("no encuentro BepInEx.dll: instala los mods primero");
                    string src = Source();
                    string stamp = Version + "|" + Sha(Encoding.UTF8.GetBytes(src)) + "|" + managed.ToLowerInvariant() + "|" + Signature(Directory.GetFiles(managed, "*.dll")) + "|" + Signature(new[] { bep });
                    string built = Path.Combine(BuildDir, DllName), stampFile = Path.Combine(BuildDir, "version.txt");
                    if (!File.Exists(built) || !File.Exists(stampFile) || File.ReadAllText(stampFile) != stamp)
                    {
                        Directory.CreateDirectory(BuildDir);
                        if (File.Exists(stampFile)) File.Delete(stampFile);
                        Compile(src, managed, bep, built);
                        File.WriteAllText(stampFile, stamp);
                        if (File.Exists(ErrorLog)) File.Delete(ErrorLog);
                        Log.W("Majinheim Mundo: mod preparado para la versión de Valheim de este PC.");
                    }
                    string dest = Path.Combine(PluginDir, DllName);
                    if (!File.Exists(dest) || Sha(File.ReadAllBytes(dest)) != Sha(File.ReadAllBytes(built)))
                    {
                        Directory.CreateDirectory(PluginDir);
                        File.Copy(built, dest, true);
                    }
                    LastError = null;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    Log.W("Majinheim Mundo: no he podido preparar el mod ({0}). El server arranca sin él: la IA no sabrá qué hay en los cofres.", e.Message);
                    try { Remove(); } catch { }
                }
            }
        }

        public static void Remove()
        {
            if (Directory.Exists(PluginDir)) Directory.Delete(PluginDir, true);
        }

        static string Source()
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(SourceName))
            {
                if (st == null) throw new Exception("este launcher no lleva el código del mod");
                using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static void Compile(string src, string managed, string bep, string output)
        {
            string csc = Csc();
            string cs = Path.Combine(BuildDir, SourceName);
            File.WriteAllText(cs, src, new UTF8Encoding(false));
            if (File.Exists(output)) File.Delete(output);
            // Primero contra todas las DLL de Valheim; si alguna choca con otra, solo con las imprescindibles.
            var all = Directory.GetFiles(managed, "*.dll").Where(IsManaged).ToList();
            string err1 = RunCsc(csc, cs, output, all, bep);
            if (err1 == null) return;
            var few = new[] { "mscorlib", "System", "System.Core", "netstandard", "UnityEngine", "UnityEngine.CoreModule", "assembly_valheim", "assembly_utils", "assembly_guiutils" }
                .Select(n => Path.Combine(managed, n + ".dll")).Where(File.Exists).ToList();
            string err2 = RunCsc(csc, cs, output, few, bep);
            if (err2 == null) return;
            Directory.CreateDirectory(AppInfo.LogDir);
            File.WriteAllText(ErrorLog, "Majinheim Mundo " + Version + ": el compilador ha dado errores (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")\r\n\r\n" +
                "Con todas las DLL de Valheim:\r\n" + err1 + "\r\n\r\nSolo con las imprescindibles:\r\n" + err2 + "\r\n", new UTF8Encoding(false));
            throw new Exception("el compilador ha dado errores (detalles en logs\\mundo-mod.log)");
        }

        // null = compilado bien; si no, lo que ha dicho el compilador.
        static string RunCsc(string csc, string cs, string output, List<string> refs, string bep)
        {
            var rsp = new StringBuilder();
            rsp.AppendLine("/nologo /nostdlib+ /target:library /optimize+ /utf8output /codepage:65001 /warn:1 /nowarn:1701,1702,1685");
            rsp.AppendLine("/out:\"" + output + "\"");
            foreach (var r in refs) rsp.AppendLine("/r:\"" + r + "\"");
            rsp.AppendLine("/r:\"" + bep + "\"");
            rsp.AppendLine("/r:\"" + Path.Combine(Path.GetDirectoryName(bep), "0Harmony.dll") + "\"");   // viene con BepInEx: para escuchar el chat
            rsp.AppendLine("\"" + cs + "\"");
            string rspFile = Path.Combine(BuildDir, "compilar.rsp");
            File.WriteAllText(rspFile, rsp.ToString(), new UTF8Encoding(false));
            var psi = new ProcessStartInfo(csc, "/noconfig @\"" + rspFile + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = BuildDir,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string outp = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return "el compilador no ha terminado en 2 minutos"; }
                string text = (outp + "\n" + err.Result).Trim();
                if (p.ExitCode == 0 && File.Exists(output)) return null;
                var lines = text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
                return lines.Count == 0 ? "código de salida " + p.ExitCode : string.Join("\r\n", lines.Take(40));
            }
        }

        // El compilador de C# que trae Windows con .NET Framework 4 (el mismo que usa build.bat).
        static string Csc()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var d in new[] { RuntimeEnvironment.GetRuntimeDirectory(), Path.Combine(win, @"Microsoft.NET\Framework64\v4.0.30319"), Path.Combine(win, @"Microsoft.NET\Framework\v4.0.30319") })
            {
                string p = Path.Combine(d ?? "", "csc.exe");
                if (File.Exists(p)) return p;
            }
            throw new Exception("no encuentro el compilador de C# de Windows (csc.exe de .NET Framework 4)");
        }

        static bool IsManaged(string dll)
        {
            try { AssemblyName.GetAssemblyName(dll); return true; } catch { return false; }
        }

        static string Signature(IEnumerable<string> files)
        {
            var sb = new StringBuilder();
            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name.ToLowerInvariant()).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            return Sha(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        static string Sha(byte[] b)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").Substring(0, 16);
        }
    }

    // ---------------------------------------------------------------- los datos que apunta el mod

    public static class WorldData
    {
        public class Spot { public int X, Y, Z; }
        public class Box : Spot
        {
            public string Type, Name, Player;
            public DateTime Death;
            public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>();
        }
        public class Text : Spot { public string Value; }
        public class World
        {
            public DateTime Date;
            public int Day;
            public List<Box> Chests = new List<Box>(), Drawers = new List<Box>(), Tombs = new List<Box>();
            public List<Text> Signs = new List<Text>(), Portals = new List<Text>();
            public Dictionary<string, string[]> Names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
        public class Mat { public string Id, Es; public int N, PerLevel; }
        public class Recipe
        {
            public string Id, Es, En, Station, Tool;
            public int Amount = 1, Level, MaxQuality = 1;
            public bool OnlyOne, Piece;
            public List<Mat> Mats = new List<Mat>();
        }
        public class Book
        {
            public DateTime Date;
            public List<Recipe> Recipes = new List<Recipe>();
            public Dictionary<string, string[]> Names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        static World world; static DateTime worldAt;
        static Book book; static DateTime bookAt;
        static readonly object lk = new object();

        static string WorldFile { get { return Path.Combine(WorldMod.OutDir, "mundo.json"); } }
        static string BookFile { get { return Path.Combine(WorldMod.OutDir, "recetas.json"); } }

        // Se vuelve a leer solo si el mod lo ha reescrito.
        public static World GetWorld()
        {
            lock (lk)
            {
                if (!File.Exists(WorldFile)) return null;
                var t = File.GetLastWriteTimeUtc(WorldFile);
                if (world == null || t != worldAt) { world = ParseWorld(Json.ParseObj(File.ReadAllText(WorldFile, Encoding.UTF8))); worldAt = t; }
                return world;
            }
        }

        public static Book GetBook()
        {
            lock (lk)
            {
                if (!File.Exists(BookFile)) return null;
                var t = File.GetLastWriteTimeUtc(BookFile);
                if (book == null || t != bookAt) { book = ParseBook(Json.ParseObj(File.ReadAllText(BookFile, Encoding.UTF8))); bookAt = t; }
                return book;
            }
        }

        public static DateTime WorldFileTime { get { return File.Exists(WorldFile) ? File.GetLastWriteTime(WorldFile) : DateTime.MinValue; } }

        static IEnumerable<Dictionary<string, object>> Objs(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o is object[] ? ((object[])o).OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();
        }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : ""; }
        static int N(Dictionary<string, object> d, string k, int def) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o, CultureInfo.InvariantCulture) : def; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static DateTime D(Dictionary<string, object> d, string k)
        {
            DateTime t;
            return DateTime.TryParseExact(S(d, k), "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t) ? t : DateTime.MinValue;
        }
        static T At<T>(T s, Dictionary<string, object> d) where T : Spot { s.X = N(d, "x", 0); s.Y = N(d, "y", 0); s.Z = N(d, "z", 0); return s; }

        static void ReadNames(Dictionary<string, object> d, Dictionary<string, string[]> into)
        {
            object o;
            if (!d.TryGetValue("nombres", out o) || !(o is Dictionary<string, object>)) return;
            foreach (var kv in (Dictionary<string, object>)o)
            {
                var a = kv.Value as object[];
                if (a == null || a.Length < 2) continue;
                into[kv.Key] = new[] { Convert.ToString(a[0], CultureInfo.InvariantCulture), Convert.ToString(a[1], CultureInfo.InvariantCulture) };
            }
        }

        static World ParseWorld(Dictionary<string, object> d)
        {
            var w = new World { Date = D(d, "fecha"), Day = N(d, "dia", 0) };
            Func<Dictionary<string, object>, Box> box = o =>
            {
                var b = At(new Box { Type = S(o, "tipo"), Name = S(o, "nombre"), Player = S(o, "jugador"), Death = D(o, "muerte") }, o);
                foreach (var it in Objs(o, "objetos")) { string id = S(it, "id"); int n = N(it, "n", 0); if (id != "" && n > 0) b.Items.Add(new KeyValuePair<string, int>(id, n)); }
                return b;
            };
            foreach (var o in Objs(d, "cofres")) w.Chests.Add(box(o));
            foreach (var o in Objs(d, "tumbas")) w.Tombs.Add(box(o));
            foreach (var o in Objs(d, "cajones"))
            {
                var b = At(new Box { Type = "cajon", Name = "Cajón" }, o);
                string id = S(o, "id"); int n = N(o, "n", 0);
                if (id != "" && n > 0) { b.Items.Add(new KeyValuePair<string, int>(id, n)); w.Drawers.Add(b); }
            }
            foreach (var o in Objs(d, "carteles")) w.Signs.Add(At(new Text { Value = CleanSign(S(o, "texto")) }, o));
            foreach (var o in Objs(d, "portales")) w.Portals.Add(At(new Text { Value = S(o, "nombre").Trim() }, o));
            w.Signs.RemoveAll(x => x.Value.Length == 0);
            ReadNames(d, w.Names);
            return w;
        }

        static Book ParseBook(Dictionary<string, object> d)
        {
            var b = new Book { Date = D(d, "fecha") };
            Func<Dictionary<string, object>, bool, Recipe> rec = (o, piece) =>
            {
                var r = new Recipe
                {
                    Id = S(o, "id"), Es = S(o, "es"), En = S(o, "en"), Station = S(o, "estacion"), Tool = S(o, "herramienta"), Piece = piece,
                    Amount = N(o, "cantidad", 1), Level = N(o, "nivel", 0), MaxQuality = N(o, "mejorasHasta", 1), OnlyOne = B(o, "soloUno"),
                };
                foreach (var m in Objs(o, "materiales")) r.Mats.Add(new Mat { Id = S(m, "id"), Es = S(m, "es"), N = N(m, "n", 0), PerLevel = N(m, "porMejora", 0) });
                return r;
            };
            foreach (var o in Objs(d, "recetas")) b.Recipes.Add(rec(o, false));
            foreach (var o in Objs(d, "construcciones")) b.Recipes.Add(rec(o, true));
            ReadNames(d, b.Names);
            return b;
        }

        static readonly Regex rxTags = new Regex(@"<[^>]*>");
        static string CleanSign(string s) { return Regex.Replace(rxTags.Replace(s ?? "", ""), @"\s+", " ").Trim(); }

        // ------------------------------------------------------------ buscar nombres ("hierro" -> Iron, "lingotes de cobre" -> Copper)

        static readonly HashSet<string> Stop = new HashSet<string> { "de", "del", "la", "el", "los", "las", "lo", "un", "una", "unos", "unas", "y", "con", "en", "para", "al", "mi", "mis", "tu", "tus", "su", "sus", "the", "of", "a", "an" };

        static List<string> Words(string s)
        {
            return Regex.Split(AiChat.Fold(s), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0 && !Stop.Contains(w)).Select(Sing).ToList();
        }

        // Singular sencillo: "lingotes" -> "lingote", "pieles" -> "piel", "flores" -> "flor", "irons" -> "iron".
        static string Sing(string w)
        {
            if (w.Length > 4 && w.EndsWith("es") && "lrndzj".IndexOf(w[w.Length - 3]) >= 0) return w.Substring(0, w.Length - 2);
            if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss")) return w.Substring(0, w.Length - 1);
            return w;
        }

        // 0 = nada que ver; 100 = se llama exactamente así; menos cuantas más palabras de sobra tiene el nombre.
        static int Score(List<string> q, string id, string es, string en)
        {
            if (q.Count == 0) return 0;
            int best = 0;
            if (AiChat.Fold(id) == string.Join("", q)) return 100;
            foreach (var name in new[] { es, en })
            {
                var nw = Words(name);
                if (nw.Count == 0) continue;
                int hit = 0, prefix = 0;   // "made" encuentra "madera", pero no cuenta como nombre exacto
                foreach (var w in q)
                {
                    if (nw.Contains(w)) hit++;
                    else if (w.Length >= 4 && nw.Any(x => x.StartsWith(w))) { hit++; prefix++; }
                }
                if (hit == 0) continue;
                int sc = hit == q.Count ? 100 - 6 * Math.Max(0, nw.Count - hit) - 3 * prefix : 40 * hit / q.Count;
                best = Math.Max(best, Math.Max(1, Math.Min(100, sc)));
            }
            return best;
        }

        // Los objetos que mejor encajan con lo que se busca. exact = alguno se llama exactamente así.
        static List<string> Match(string query, Dictionary<string, string[]> names, out bool exact)
        {
            var q = Words(query);
            var scored = names.Select(kv => new { Id = kv.Key, Sc = Score(q, kv.Key, kv.Value[0], kv.Value[1]) }).Where(x => x.Sc > 0).ToList();
            exact = scored.Any(x => x.Sc == 100);
            int top = scored.Count == 0 ? 0 : scored.Max(x => x.Sc);
            int floor = top >= 60 ? 60 : top;   // si hay alguno que contiene todas las palabras, solo esos
            return scored.Where(x => x.Sc >= floor).OrderByDescending(x => x.Sc).ThenBy(x => x.Id.Length).Select(x => x.Id).ToList();
        }

        static string Es(Dictionary<string, string[]> names, string id) { string[] n; return names.TryGetValue(id, out n) && n[0] != "" ? n[0] : id; }

        // ------------------------------------------------------------ dónde está cada cosa, en palabras

        static readonly string[] Dirs = { "norte", "noreste", "este", "sureste", "sur", "suroeste", "oeste", "noroeste" };

        static double Dist(double x1, double z1, double x2, double z2) { return Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2)); }

        // En Valheim el norte es +z y el este +x.
        static string Dir(double dx, double dz)
        {
            double a = Math.Atan2(dx, dz) * 180 / Math.PI;
            int i = (int)Math.Round(((a % 360) + 360) % 360 / 45.0) % 8;
            return Dirs[i];
        }

        static string Meters(double d)
        {
            if (d >= 1000) return (Math.Round(d / 100) / 10).ToString("0.#", CultureInfo.GetCultureInfo("es-ES")) + " km";
            return ((int)(Math.Round(d / 10) * 10)) + " m";
        }

        public static string Where(World w, double x, double z)
        {
            // El portal con nombre más cercano (los amigos se orientan por ellos); si no hay ninguno cerca, uno sin nombre.
            Text best = null; double bd = double.MaxValue;
            foreach (var named in new[] { true, false })
            {
                foreach (var p in w.Portals.Where(t => (t.Value != "") == named))
                {
                    double d = Dist(x, z, p.X, p.Z);
                    if (d < bd) { bd = d; best = p; }
                }
                if (best != null && bd <= 400) break;
            }
            if (best != null && bd <= 400)
            {
                bool named = best.Value != "";
                if (bd < 20) return named ? "junto al portal «" + best.Value + "»" : "junto a un portal sin nombre";
                return "a " + Meters(bd) + " al " + Dir(x - best.X, z - best.Z) + (named ? " del portal «" + best.Value + "»" : " de un portal sin nombre");
            }
            double c = Dist(x, z, 0, 0);
            if (c < 50) return "en el centro del mapa, donde se empieza";
            return "a " + Meters(c) + " al " + Dir(x, z) + " del centro del mapa (donde se empieza)";
        }

        class Place
        {
            public double X, Z; public int Amount, Chests, Drawers;
            public List<Box> Boxes = new List<Box>();
        }

        // Junta los cofres que están a menos de 25 m (una misma base o almacén).
        static List<Place> Group(IEnumerable<KeyValuePair<Box, int>> found)
        {
            var places = new List<Place>();
            foreach (var kv in found)
            {
                var b = kv.Key;
                var p = places.FirstOrDefault(x => Dist(x.X, x.Z, b.X, b.Z) <= 25);
                if (p == null) { p = new Place { X = b.X, Z = b.Z }; places.Add(p); }
                p.Boxes.Add(b);
                p.X = p.Boxes.Average(x => (double)x.X); p.Z = p.Boxes.Average(x => (double)x.Z);
                p.Amount += kv.Value;
                if (b.Type == "cajon") p.Drawers++; else p.Chests++;
            }
            return places.OrderByDescending(x => x.Amount).ToList();
        }

        static List<string> SignsNear(World w, Place p)
        {
            return w.Signs.Where(s => p.Boxes.Any(b => Dist(b.X, b.Z, s.X, s.Z) <= 4 && Math.Abs(b.Y - s.Y) <= 4))
                .Select(s => s.Value.Length > 40 ? s.Value.Substring(0, 40) + "…" : s.Value).Distinct().Take(3).ToList();
        }

        static string Containers(int chests, int drawers)
        {
            var p = new List<string>();
            if (chests > 0) p.Add(chests == 1 ? "1 cofre" : chests + " cofres");
            if (drawers > 0) p.Add(drawers == 1 ? "1 cajón" : drawers + " cajones");
            return string.Join(" y ", p);
        }

        static string Fresh(World w)
        {
            var at = w.Date != DateTime.MinValue ? w.Date : WorldFileTime;
            bool on = System.Diagnostics.Process.GetProcessesByName("valheim_server").Length > 0;
            return (on ? "datos del mundo de " : "datos de cuando el server estuvo encendido por última vez, ") + AiChat.Ago(at) + (w.Day > 0 ? " (día " + w.Day + " del mundo)" : "");
        }

        static Dictionary<string, object> Fail(string msg) { return new Dictionary<string, object> { { "error", msg } }; }

        static string NotReady()
        {
            if (!Settings.Current.WorldExport) return "Esta función está apagada en el launcher del server (pestaña Asistente IA).";
            if (WorldMod.LastError != null) return "El mod que lee el mundo no se ha podido preparar en el PC del server; un administrador lo puede mirar en la pestaña Salud.";
            return "Todavía no tengo esos datos: se apuntan solos unos minutos después de encender el server.";
        }

        // ------------------------------------------------------------ mapa de "Buscar en el mundo" (2.6)
        // El mod apunta en mapa.bin el bioma y la altura de lo explorado (cada 16 m). Aquí se pinta en un PNG, con lo no
        // explorado en oscuro (no se adelanta nada del mundo que nadie ha visto). Norte arriba: fila 0 = la z más alta.

        public class MapImage { public int X0, Z0, X1, Z1; public byte[] Png; public long Stamp; }
        static MapImage mapImg; static DateTime mapImgAt;
        static string MapFile { get { return Path.Combine(WorldMod.OutDir, "mapa.bin"); } }

        public static MapImage GetMap()
        {
            lock (lk)
            {
                if (!File.Exists(MapFile)) return null;
                var t = File.GetLastWriteTimeUtc(MapFile);
                if (mapImg == null || t != mapImgAt)
                {
                    mapImg = RenderMap(File.ReadAllBytes(MapFile));
                    if (mapImg != null) mapImg.Stamp = t.Ticks / TimeSpan.TicksPerSecond;
                    mapImgAt = t;
                }
                return mapImg;
            }
        }

        // 1 Pradera, 2 Pantano, 3 Montaña, 4 Selva Negra, 5 Llanuras, 6 Tierras de ceniza, 7 Norte profundo, 9 Océano, 10 Tierras de niebla
        static readonly int[] BiomeColor = { 0x6d7a5a, 0x7ea04c, 0x5e5636, 0xd6dbe2, 0x2f4d2c, 0xc8b25a, 0x7c3b2c, 0xe7eef6, 0x2d5f86, 0x2d5f86, 0x6c6a86 };
        const int Unexplored = 0x14110f;   // el fondo de la web

        public static MapImage RenderMap(byte[] data)
        {
            if (data == null || data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "MHM1") return null;
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadBytes(4);
            int n = r.ReadInt32(), step = r.ReadInt16(), per = r.ReadInt16();
            if (n <= 0 || per <= 0 || per > 16 || step <= 0 || data.Length < 12 + (long)n * (4 + per * per * 2)) return null;
            var zx = new int[n]; var zy = new int[n]; var cells = new byte[n][];
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int k = 0; k < n; k++)
            {
                zx[k] = r.ReadInt16(); zy[k] = r.ReadInt16(); cells[k] = r.ReadBytes(per * per * 2);
                minX = Math.Min(minX, zx[k]); maxX = Math.Max(maxX, zx[k]); minY = Math.Min(minY, zy[k]); maxY = Math.Max(maxY, zy[k]);
            }
            minX--; minY--; maxX++; maxY++;   // un poco de borde
            // Que no quede muy alargado: se completa hasta una proporción razonable.
            while ((maxX - minX + 1) * 2 < (maxY - minY + 1)) { minX--; maxX++; }
            while ((maxY - minY + 1) * 2 < (maxX - minX + 1)) { minY--; maxY++; }
            int w = (maxX - minX + 1) * per, h = (maxY - minY + 1) * per;
            if ((long)w * h > 9000000) return null;   // más de 3000 x 3000: no debería pasar nunca
            var biome = new byte[w * h]; var height = new byte[w * h]; var known = new bool[w * h];
            for (int k = 0; k < n; k++)
                for (int j = 0; j < per; j++)
                    for (int i = 0; i < per; i++)
                    {
                        int px = (zx[k] - minX) * per + i, py = h - 1 - ((zy[k] - minY) * per + j);
                        int o = py * w + px, c = (j * per + i) * 2;
                        biome[o] = cells[k][c]; height[o] = cells[k][c + 1]; known[o] = true;
                    }
            var px32 = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * w + x;
                    if (!known[o]) { px32[o] = unchecked((int)0xFF000000) | Unexplored; continue; }
                    int hh = height[o], col;
                    if (hh < 30)
                    {
                        // Agua: más oscura cuanto más honda.
                        double d = Math.Min(1.0, (30 - hh) / 30.0);
                        col = Mix(0x4f86ad, 0x1c3d5a, d);
                    }
                    else
                    {
                        int b = biome[o] < BiomeColor.Length ? biome[o] : 0;
                        col = BiomeColor[b];
                        if (b == 9) col = 0x7ea04c;   // tierra que el juego cuenta como océano: orilla
                        // Relieve: luz desde el noroeste.
                        int nw = (x > 0 && y > 0 && known[o - w - 1]) ? height[o - w - 1] : hh;
                        double shade = Math.Max(0.7, Math.Min(1.3, 1 + (nw - hh) * 0.035));
                        if (hh < 32) col = Mix(col, 0xc9b98a, 0.5);   // playa
                        col = Scale(col, shade);
                    }
                    px32[o] = unchecked((int)0xFF000000) | col;
                }
            byte[] png;
            using (var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(px32, y * w, bd.Scan0 + y * bd.Stride, w);
                bmp.UnlockBits(bd);
                var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                png = ms.ToArray();
            }
            int zone = step * per;
            return new MapImage { X0 = minX * zone - zone / 2, X1 = (maxX + 1) * zone - zone / 2, Z0 = minY * zone - zone / 2, Z1 = (maxY + 1) * zone - zone / 2, Png = png };
        }

        static int Mix(int a, int b, double t)
        {
            Func<int, int> ch = sh => (int)Math.Round(((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t);
            return (ch(16) << 16) | (ch(8) << 8) | ch(0);
        }
        static int Scale(int a, double f)
        {
            Func<int, int> ch = sh => Math.Max(0, Math.Min(255, (int)Math.Round(((a >> sh) & 255) * f)));
            return (ch(16) << 16) | (ch(8) << 8) | ch(0);
        }

        // Lo que la web necesita para dibujar encima: los límites del dibujo y los portales con nombre (para orientarse).
        public static Dictionary<string, object> MapMeta()
        {
            MapImage m;
            try { m = GetMap(); } catch (Exception e) { Log.W("Mapa de búsqueda: {0}", e.Message); return null; }
            if (m == null) return null;
            var w = GetWorld();
            var portals = w == null ? new List<object>() : w.Portals
                .Where(p => p.Value != "" && p.X >= m.X0 && p.X <= m.X1 && p.Z >= m.Z0 && p.Z <= m.Z1)
                .GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(60)
                .Select(p => (object)new Dictionary<string, object> { { "x", p.X }, { "z", p.Z }, { "nombre", p.Value.Length > 30 ? p.Value.Substring(0, 30) : p.Value } }).ToList();
            return new Dictionary<string, object>
            {
                { "x0", m.X0 }, { "x1", m.X1 }, { "z0", m.Z0 }, { "z1", m.Z1 }, { "v", m.Stamp }, { "portales", portals },
            };
        }

        // ------------------------------------------------------------ herramientas de la IA

        public static object FindItem(string query)
        {
            if ((query ?? "").Trim().Length == 0) return Fail("Falta decir qué objeto se busca.");
            var w = GetWorld();
            if (w == null) return Fail(NotReady());
            var bk = GetBook();
            var names = new Dictionary<string, string[]>(w.Names, StringComparer.OrdinalIgnoreCase);
            if (bk != null) foreach (var kv in bk.Names) if (!names.ContainsKey(kv.Key)) names[kv.Key] = kv.Value;

            // Lo que hay de cada objeto en cofres y cajones (los cofres personales no se miran).
            var where = new Dictionary<string, List<KeyValuePair<Box, int>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in w.Chests.Concat(w.Drawers))
                foreach (var g in b.Items.GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase))
                {
                    List<KeyValuePair<Box, int>> l;
                    if (!where.TryGetValue(g.Key, out l)) where[g.Key] = l = new List<KeyValuePair<Box, int>>();
                    l.Add(new KeyValuePair<Box, int>(b, g.Sum(i => i.Value)));
                }

            bool exact;
            var ids = Match(query, names, out exact);
            if (ids.Count == 0)
                return Fail("No conozco ningún objeto que se llame «" + query + "». Dilo con el nombre que sale en el juego (en español o en inglés).");
            var present = ids.Where(where.ContainsKey).ToList();
            var main = exact ? ids.Where(id => Score(Words(query), id, Es(names, id), names[id][1]) == 100).ToList() : present.Take(3).ToList();
            if (main.Count == 0) main = ids.Take(1).ToList();

            var res = new Dictionary<string, object> { { "datos", Fresh(w) }, { "busqueda", query } };
            var found = new List<object>();
            foreach (var id in main.Take(3))
            {
                List<KeyValuePair<Box, int>> l;
                var o = new Dictionary<string, object> { { "objeto", Es(names, id) } };
                if (!where.TryGetValue(id, out l) || l.Count == 0)
                {
                    o["total"] = 0;
                    o["nota"] = "No hay " + Es(names, id) + " en ningún cofre ni cajón.";
                    found.Add(o);
                    continue;
                }
                int inChests = l.Where(x => x.Key.Type != "cajon").Sum(x => x.Value), inDrawers = l.Where(x => x.Key.Type == "cajon").Sum(x => x.Value);
                o["total"] = inChests + inDrawers;
                o["reparto"] = (inChests > 0 ? inChests + " en " + Containers(l.Count(x => x.Key.Type != "cajon"), 0) : "") +
                    (inChests > 0 && inDrawers > 0 ? " y " : "") + (inDrawers > 0 ? inDrawers + " en " + Containers(0, l.Count(x => x.Key.Type == "cajon")) : "");
                var places = Group(l);
                o["sitios"] = places.Take(main.Count == 1 ? 5 : 3).Select(p =>
                {
                    var s = new Dictionary<string, object>
                    {
                        { "donde", Where(w, p.X, p.Z) },
                        { "cantidad", p.Amount },
                        { "en", Containers(p.Chests, p.Drawers) },
                        { "coordenadas", "x " + (int)Math.Round(p.X) + ", z " + (int)Math.Round(p.Z) },
                    };
                    var signs = SignsNear(w, p);
                    if (signs.Count > 0) s["carteles"] = signs;
                    return (object)s;
                }).ToList();
                if (places.Count > (main.Count == 1 ? 5 : 3)) o["otrosSitios"] = places.Count - (main.Count == 1 ? 5 : 3);
                found.Add(o);
            }
            res["encontrado"] = found;

            // En tumbas no cuenta como "guardado", pero se dice (es de alguien que murió y aún no lo ha recogido).
            var tombs = w.Tombs.Select(t => new { t, n = t.Items.Where(i => main.Contains(i.Key, StringComparer.OrdinalIgnoreCase)).Sum(i => i.Value) }).Where(x => x.n > 0).ToList();
            if (tombs.Count > 0) res["enTumbas"] = tombs.Select(x => x.n + " en la tumba de " + (x.t.Player == "" ? "alguien" : x.t.Player)).ToList();

            var similar = present.Where(id => !main.Contains(id)).Take(5).Select(id => Es(names, id) + ": " + where[id].Sum(x => x.Value)).ToList();
            if (similar.Count > 0) res["parecidos"] = similar;
            res["nota"] = "Los cofres personales no se miran. Los barcos y carros cuentan como cofres.";
            return res;
        }

        public static object Craft(string query)
        {
            if ((query ?? "").Trim().Length == 0) return Fail("Falta decir qué se quiere fabricar o construir.");
            var bk = GetBook();
            if (bk == null) return Fail(NotReady());
            var names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in bk.Recipes) if (!names.ContainsKey(r.Id)) names[r.Id] = new[] { r.Es, r.En };
            bool exact;
            var ids = Match(query, names, out exact);
            var q = Words(query);
            var res = new Dictionary<string, object> { { "busqueda", query } };
            var list = new List<object>();
            // Si lo buscado es un material ("piel de lobo"): para qué sirve.
            bool matExact;
            var mats = Match(query, bk.Names, out matExact);
            var mat = matExact ? mats.FirstOrDefault(id => Score(q, id, Es(bk.Names, id), bk.Names[id][1]) == 100) : null;

            // Recetas que se llaman así; si ninguna lleva todas las palabras, solo se sugieren nombres (no una receta de otra cosa).
            var pick = exact ? ids.Where(id => Score(q, id, names[id][0], names[id][1]) == 100).ToList()
                : ids.Where(id => Score(q, id, names[id][0], names[id][1]) >= 60).Take(3).ToList();
            foreach (var r in bk.Recipes.Where(x => pick.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).Take(4))
                list.Add(Describe(r));
            if (list.Count > 0) res["recetas"] = list;
            else if (mat == null && ids.Count > 0) res["quizaQuieresDecir"] = ids.Take(5).Select(id => names[id][0]).ToList();
            if (mat != null)
            {
                var uses = bk.Recipes.Where(r => r.Mats.Any(m => m.Id.Equals(mat, StringComparison.OrdinalIgnoreCase))).Select(r => r.Es).Distinct().ToList();
                if (uses.Count > 0)
                {
                    res["seUsaPara"] = uses.Take(15).ToList();
                    if (uses.Count > 15) res["yOtros"] = uses.Count - 15;
                }
            }
            if (list.Count == 0 && !res.ContainsKey("seUsaPara") && !res.ContainsKey("quizaQuieresDecir"))
                return Fail("No hay ninguna receta ni construcción que se llame «" + query + "» en este server (con sus mods). Puede que no se fabrique: se encuentra o lo sueltan los enemigos.");
            res["nota"] = "Recetas reales de este server, con los mods instalados.";
            return res;
        }

        static string Mats(IEnumerable<Mat> mats, Func<Mat, int> n)
        {
            return string.Join(", ", mats.Where(m => n(m) > 0).Select(m => m.Es + " x" + n(m)));
        }

        static Dictionary<string, object> Describe(Recipe r)
        {
            var o = new Dictionary<string, object> { { r.Piece ? "construccion" : "objeto", r.Es } };
            if (r.Piece)
            {
                o["seConstruyeCon"] = r.Tool == "" ? "el martillo" : r.Tool;
                if (r.Station != "") o["necesitaCerca"] = r.Station;
            }
            else
            {
                o["seFabricaEn"] = r.Station == "" ? "a mano, desde el inventario (sin mesa)" : r.Station + (r.Level > 1 ? " de nivel " + r.Level : "");
                if (r.Amount > 1) o["salen"] = r.Amount;
            }
            o["materiales"] = Mats(r.Mats, m => m.N);
            if (r.OnlyOne) o["ojo"] = "Basta con uno cualquiera de esos materiales.";
            if (!r.Piece && r.MaxQuality > 1 && r.Mats.Any(m => m.PerLevel > 0))
            {
                int baseLevel = Math.Max(1, r.Level);
                o["mejoras"] = Enumerable.Range(2, Math.Min(r.MaxQuality, 5) - 1).Select(q =>
                    "a calidad " + q + ": " + Mats(r.Mats, m => m.PerLevel * (q - 1)) + (r.Station != "" ? " (" + r.Station + " de nivel " + (baseLevel + q - 1) + ")" : "")).ToList();
            }
            return o;
        }

        public static object Tombs(string player)
        {
            var w = GetWorld();
            if (w == null) return Fail(NotReady());
            var names = w.Names;
            var list = w.Tombs.Where(t => player == "" || AiChat.Fold(t.Player).Contains(AiChat.Fold(player))).OrderByDescending(t => t.Death).ToList();
            var res = new Dictionary<string, object> { { "datos", Fresh(w) } };
            if (list.Count == 0)
            {
                res["tumbas"] = new object[0];
                res["nota"] = player == "" ? "No hay ninguna tumba en el mundo: todo el mundo ha recogido sus cosas." : "No hay ninguna tumba de «" + player + "»: o no ha muerto, o ya la ha recogido.";
                return res;
            }
            res["tumbas"] = list.Take(6).Select(t =>
            {
                var o = new Dictionary<string, object>
                {
                    { "jugador", t.Player == "" ? "desconocido" : t.Player },
                    { "donde", Where(w, t.X, t.Z) },
                    { "coordenadas", "x " + t.X + ", z " + t.Z },
                    { "objetos", t.Items.Count },
                    { "lleva", string.Join(", ", t.Items.OrderByDescending(i => i.Value).Take(6).Select(i => Es(names, i.Key) + (i.Value > 1 ? " x" + i.Value : ""))) },
                };
                if (t.Death != DateTime.MinValue) o["murio"] = AiChat.When(t.Death);
                return (object)o;
            }).ToList();
            if (list.Count > 6) res["otras"] = list.Count - 6;
            res["nota"] = "La tumba desaparece cuando su dueño la recoge.";
            return res;
        }

        // ------------------------------------------------------------ para la pestaña Salud y la consola

        public static Health.Item Check(bool serverOn, DateTime startedAt)
        {
            const string key = "mundo", name = "La IA conoce el mundo";
            if (!Settings.Current.WorldExport) return new Health.Item { Key = key, Name = name, State = "info", Detail = "Apagado: la IA no sabe qué hay en los cofres." };
            if (WorldMod.LastError != null)
                return new Health.Item { Key = key, Name = name, State = "aviso", Detail = "No he podido preparar el mod Majinheim Mundo: " + WorldMod.LastError + ". El server funciona igual, sin él." };
            var at = WorldFileTime;
            World w = null;
            try { w = GetWorld(); } catch (Exception e) { return new Health.Item { Key = key, Name = name, State = "aviso", Detail = "No puedo leer los datos del mundo: " + e.Message }; }
            if (serverOn && (DateTime.Now - startedAt).TotalMinutes > 15 && (w == null || (DateTime.Now - at).TotalMinutes > 15))
                return new Health.Item { Key = key, Name = name, State = "aviso", Detail = "El mod no está apuntando el mundo (" + (w == null ? "todavía no hay datos" : "los últimos son de " + AiChat.Ago(at)) + "). Mira el registro del server." };
            if (w == null) return new Health.Item { Key = key, Name = name, State = "info", Detail = "Sin datos todavía: se apuntan solos unos minutos después de encender el server." };
            string what = w.Chests.Count + " cofres, " + w.Drawers.Count + " cajones y " + w.Tombs.Count + (w.Tombs.Count == 1 ? " tumba" : " tumbas");
            return new Health.Item { Key = key, Name = name, State = "ok", Detail = "Sabe qué hay en " + what + " (" + AiChat.Ago(at) + ")." };
        }
    }

    // Aviso de lag (2.6). El mod Majinheim Mundo apunta cada 15 s a cuántos fotogramas por segundo va el server
    // (rendimiento.json). Si con gente jugando se queda por debajo de 20 durante 2 minutos, avisa en Discord con la causa
    // más probable de las que se pueden ver desde aquí; y otra vez cuando se pasa. Como mucho un aviso cada 30 minutos.
    public static class LagWatch
    {
        public const double Bad = 20, Good = 28;   // por debajo de Bad, lag; vuelve a estar bien por encima de Good
        public class Reading { public long T; public double Fps; public int WorstMs, Objects; }
        static string FileName { get { return Path.Combine(WorldMod.OutDir, "rendimiento.json"); } }
        static readonly List<Reading> recent = new List<Reading>();
        static readonly object lk = new object();
        static System.Threading.Thread th;
        static bool alerted;
        static DateTime lastAlert = DateTime.MinValue;
        static TimeSpan lastCpu; static DateTime lastCpuAt; static double cores = -1;
        public static Func<bool> Running = () => System.Diagnostics.Process.GetProcessesByName("valheim_server").Length > 0;

        public static Reading Read()
        {
            try
            {
                if (!File.Exists(FileName)) return null;
                var o = Json.ParseObj(File.ReadAllText(FileName, Encoding.UTF8));
                var r = new Reading
                {
                    T = Convert.ToInt64(o["t"], CultureInfo.InvariantCulture),
                    Fps = Convert.ToDouble(o["fps"], CultureInfo.InvariantCulture),
                    WorstMs = Convert.ToInt32(o["peor"], CultureInfo.InvariantCulture),
                    Objects = o.ContainsKey("objetos") ? Convert.ToInt32(o["objetos"], CultureInfo.InvariantCulture) : -1,
                };
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - r.T <= 60 ? r : null;   // de hace más de un minuto: el server no va
            }
            catch { return null; }
        }

        public static void Start()
        {
            if (th != null) return;
            th = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    try { Poll(); } catch (Exception e) { Log.W("Aviso de lag: {0}", e.Message); }
                    System.Threading.Thread.Sleep(30000);
                }
            }) { IsBackground = true, Name = "Aviso de lag" };
            th.Start();
        }

        static void Poll()
        {
            SampleCpu();
            var r = Read();
            bool on = Running();
            string msg = null;
            lock (lk)
            {
                if (r == null || !on) { recent.Clear(); return; }
                if (recent.Count == 0 || recent[recent.Count - 1].T != r.T) recent.Add(r);
                recent.RemoveAll(x => r.T - x.T > 125);
                msg = Decide(recent, ServerWatch.OnlineCount, DateTime.Now);
            }
            if (msg != null && Settings.Current.LagAlerts) Discord.Send(msg);
        }

        // Separado para probarlo sin server: devuelve el aviso que toca mandar (o null).
        public static string Decide(List<Reading> rs, int players, DateTime now)
        {
            if (rs.Count == 0) return null;
            var last = rs[rs.Count - 1];
            if (!alerted && players > 0 && rs.Count >= 4 && rs.All(x => x.Fps < Bad) && (now - lastAlert).TotalMinutes >= 30)
            {
                alerted = true; lastAlert = now;
                double avg = rs.Average(x => x.Fps);
                Log.W("Aviso de lag: {0:0} fps de media en 2 minutos con {1} jugando.", avg, players);
                return "🐢 **El server va con lag**: " + avg.ToString("0", CultureInfo.InvariantCulture) + " fotogramas por segundo en los últimos 2 minutos (cuando va bien pasa de 30), con " +
                    players + (players == 1 ? " persona" : " personas") + " jugando. " + Cause(last);
            }
            if (alerted && rs.Count >= 2 && rs.Skip(rs.Count - 2).All(x => x.Fps >= Good))
            {
                alerted = false;
                return "✅ **El lag se ha pasado**: el server vuelve a ir a " + last.Fps.ToString("0", CultureInfo.InvariantCulture) + " fotogramas por segundo.";
            }
            if (alerted && players == 0) alerted = false;   // se fueron todos: el siguiente lag es otro aviso
            return null;
        }

        // Lo que se puede ver desde el launcher. No es seguro, por eso "lo más probable".
        public static string Cause(Reading r)
        {
            var c = new List<string>();
            if (AiChat.Busy || (DateTime.Now - AiChat.LastBusy).TotalMinutes < 2)
                c.Add("la IA estaba contestando a la vez y usa mucho el PC (si pasa a menudo, se puede elegir un modelo más pequeño en la pestaña Asistente IA)");
            if (cores >= 0.9) c.Add("Valheim tiene un núcleo del procesador al límite (pasa con bases muy grandes o mucha gente en sitios distintos a la vez)");
            int load = MemoryLoad();
            if (load >= 90) c.Add("la memoria del PC está casi llena (" + load + " %): cierra programas que no hagan falta");
            if (r != null && r.WorstMs >= 2000) c.Add("hubo un parón de " + (r.WorstMs / 1000.0).ToString("0.#", CultureInfo.GetCultureInfo("es-ES")) + " s (suele ser al guardar el mundo o al hacer un backup)");
            if (c.Count == 0) return "No veo una causa clara desde el PC del server. Si dura, reiniciar el server cuando no moleste suele arreglarlo.";
            return "Lo más probable: " + string.Join("; ", c) + ".";
        }

        static void SampleCpu()
        {
            try
            {
                var p = System.Diagnostics.Process.GetProcessesByName("valheim_server").FirstOrDefault();
                if (p == null) { cores = -1; lastCpuAt = DateTime.MinValue; return; }
                var t = p.TotalProcessorTime; var now = DateTime.Now;
                if (lastCpuAt != DateTime.MinValue && now > lastCpuAt) cores = (t - lastCpu).TotalSeconds / (now - lastCpuAt).TotalSeconds;
                lastCpu = t; lastCpuAt = now;
            }
            catch { cores = -1; }
        }

        [StructLayout(LayoutKind.Sequential)]
        class MemStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemStatus));
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemStatus m);
        static int MemoryLoad()
        {
            try { var m = new MemStatus(); return GlobalMemoryStatusEx(m) ? (int)m.Load : 0; }
            catch { return 0; }
        }

        public static Health.Item Check(bool serverOn)
        {
            const string key = "rendimiento", name = "Rendimiento del server";
            if (!serverOn) return new Health.Item { Key = key, Name = name, State = "info", Detail = "El server está apagado." };
            var r = Read();
            if (r == null) return new Health.Item { Key = key, Name = name, State = "info", Detail = "Sin datos todavía (los apunta el mod Majinheim Mundo cada 15 segundos)." };
            string fps = r.Fps.ToString("0", CultureInfo.InvariantCulture) + " fotogramas por segundo";
            string objs = r.Objects > 0 ? "; " + r.Objects.ToString("N0", CultureInfo.GetCultureInfo("es-ES")) + " objetos en el mundo" : "";
            if (r.Fps < Bad) return new Health.Item { Key = key, Name = name, State = "aviso", Detail = "Va con lag: " + fps + objs + ". " + Cause(r) };
            return new Health.Item { Key = key, Name = name, State = "ok", Detail = "Va bien: " + fps + objs + "." };
        }
    }

    // MajinBot en el chat de la partida (2.6): quien escribe "!majin <pregunta>" recibe la respuesta de la IA en el chat del juego.
    // El mod Majinheim Mundo apunta solo esos mensajes en chat.jsonl; aquí se leen, se pregunta a la IA y se contesta por RCON.
    public static class GameChat
    {
        public const int Cooldown = 10, MaxParts = 3, PartLen = 180;
        public static string FileName { get { return Path.Combine(WorldMod.OutDir, "chat.jsonl"); } }
        public static Func<string, string> Say = t => "los mensajes en el juego no están listos";   // null = ha llegado
        public static Func<string, string, List<string[]>, string> Ask = (q, who, past) => AiChat.Ask(q, who, false, past);
        static System.Threading.Thread th;
        static long pos = -1;
        static readonly Dictionary<string, DateTime> lastAsk = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static List<string[]> past = new List<string[]>();
        static DateTime pastAt;

        public static void Start()
        {
            if (th != null) return;
            th = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    try { Poll(); } catch (Exception e) { Log.W("Chat del juego: {0}", e.Message); }
                    System.Threading.Thread.Sleep(2000);
                }
            }) { IsBackground = true, Name = "Chat del juego" };
            th.Start();
        }

        // Lee lo nuevo de chat.jsonl. Al empezar se salta lo que ya había (no se contestan preguntas viejas).
        public static void Poll()
        {
            var f = new FileInfo(FileName);
            if (!f.Exists) { pos = 0; return; }
            if (pos < 0) { pos = f.Length; return; }
            if (f.Length < pos) pos = 0;   // el mod lo ha empezado de nuevo
            if (f.Length == pos) return;
            byte[] buf;
            using (var fs = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(pos, SeekOrigin.Begin);
                buf = new byte[f.Length - pos];
                int n = 0, r;
                while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                if (n < buf.Length) Array.Resize(ref buf, n);
            }
            int end = Array.LastIndexOf(buf, (byte)'\n');
            if (end < 0) return;   // línea a medio escribir
            pos += end + 1;
            foreach (var line in Encoding.UTF8.GetString(buf, 0, end).Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                try
                {
                    var o = Json.ParseObj(line);
                    long t = Convert.ToInt64(o["t"], CultureInfo.InvariantCulture);
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - t > 120) continue;   // muy vieja (el launcher estaba cerrado)
                    Handle(o["nombre"] as string ?? "?", o["texto"] as string ?? "");
                }
                catch (Exception e) { Log.W("Chat del juego: línea rara ({0})", e.Message); }
            }
        }

        public static void Handle(string who, string text)
        {
            var s = Settings.Current;
            if (!s.GameChat) return;
            who = Regex.Replace(who ?? "?", @"[\x00-\x1f]+", " ").Trim();
            if (who.Length > 40) who = who.Substring(0, 40);
            string q = Regex.Replace(text ?? "", @"^!majin\S*", "", RegexOptions.IgnoreCase).Trim(' ', ':', ',', '.');
            if (!s.AiEnabled) { Send("MajinBot: la IA está apagada ahora mismo."); return; }
            if (q.Length == 0) { Send("MajinBot: ¡dime! Escribe !majin y tu pregunta, por ejemplo: !majin ¿dónde está el hierro?"); return; }
            lock (lastAsk)
            {
                DateTime prev;
                if (lastAsk.TryGetValue(who, out prev) && (DateTime.Now - prev).TotalSeconds < Cooldown) { Send("MajinBot: dame unos segundos, " + who + "."); return; }
                lastAsk[who] = DateTime.Now;
            }
            List<string[]> ctx;
            lock (lastAsk) { if ((DateTime.Now - pastAt).TotalMinutes > 10) past = new List<string[]>(); ctx = past.ToList(); }
            var t0 = DateTime.Now;
            string answer = Ask(q + "\n\n(Pregunta hecha desde el chat de la partida: contesta en una o dos frases cortas, sin listas ni formato.)", who, ctx);
            Log.W("Chat del juego: pregunta de {0} respondida en {1:0.0} s.", who, (DateTime.Now - t0).TotalSeconds);
            lock (lastAsk)
            {
                ctx.Add(new[] { who + ": " + q, answer });
                past = ctx.Skip(Math.Max(0, ctx.Count - 3)).ToList(); pastAt = DateTime.Now;
            }
            foreach (var part in Parts(who + ", " + Plain(answer))) Send(part);
        }

        static void Send(string text)
        {
            string err = Say(text);
            if (err != null) Log.W("Chat del juego: no he podido contestar ({0})", err);
        }

        // Sin formato de Discord: el chat del juego no lo entiende.
        public static string Plain(string s)
        {
            s = Regex.Replace(s ?? "", @"\[([^\]]+)\]\([^)]+\)", "$1");
            s = Regex.Replace(s, @"(\*\*|__|`|~~|^#+\s*)", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-*•]\s+", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\s*\n+\s*", " ");
            return Regex.Replace(s, @"\s{2,}", " ").Trim();
        }

        // Trozos que caben en un mensaje del chat (como mucho 3), cortando por espacios.
        public static List<string> Parts(string s)
        {
            var r = new List<string>();
            s = "MajinBot: " + s;
            while (s.Length > 0 && r.Count < MaxParts)
            {
                if (s.Length <= PartLen) { r.Add(s); break; }
                int cut = s.LastIndexOf(' ', PartLen);
                if (cut < PartLen / 2) cut = PartLen;
                if (r.Count == MaxParts - 1) { r.Add(s.Substring(0, cut).TrimEnd() + "…"); break; }
                r.Add(s.Substring(0, cut).TrimEnd());
                s = s.Substring(cut).TrimStart();
            }
            return r;
        }
    }
}
#endif
