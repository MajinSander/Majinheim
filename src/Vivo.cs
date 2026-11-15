// Mapa en vivo, máquina del tiempo y mundo en 3D (2.7).
// - LiveMap: lee cada 2 s posiciones.json (lo apunta el mod Majinheim Mundo) y guarda la ruta de cada vikingo en logs\rutas.
// - Chronicle: guarda cada día cómo estaba el mundo (lo explorado, portales, tumbas, piezas construidas) en logs\historia,
//   y con las partidas y los jefes cuenta qué pasó cada día.
// - Terrain: el relieve de lo explorado, en una rejilla, para dibujarlo en 3D en el navegador.
// Todo es de solo lectura sobre el mundo: aquí no se manda nada al server.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Majinheim
{
    public static class LiveMap
    {
        public class Pos { public string Name; public int X, Y, Z; public bool Visible; }
        public class Pt { public long T; public string Name; public int X, Z; public bool Visible; }

        const int LogEvery = 10, MinMove = 6, StaleAfter = 20, KeepHours = 36;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly object lk = new object();
        static System.Threading.Thread th;
        static List<Pos> current = new List<Pos>();
        static long currentAt;                       // hora (unix) de la última lectura buena
        static DateTime fileAt;
        static readonly List<Pt> recent = new List<Pt>();
        static readonly Dictionary<string, Pt> lastLogged = new Dictionary<string, Pt>(StringComparer.OrdinalIgnoreCase);

        public static string PosFile { get { return Path.Combine(WorldMod.OutDir, "posiciones.json"); } }
        public static string RouteDir { get { return Path.Combine(AppInfo.LogDir, "rutas"); } }
        public static string RouteFile(DateTime day) { return Path.Combine(RouteDir, day.ToString("yyyy-MM-dd", Inv) + ".jsonl"); }

        public static long Unix(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        public static DateTime FromUnix(long t) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t).ToLocalTime(); }

        public static void Start()
        {
            if (th != null) return;
            try { lock (lk) { recent.AddRange(ReadDay(DateTime.Today.AddDays(-1))); recent.AddRange(ReadDay(DateTime.Today)); Trim(); } }
            catch (Exception e) { Log.W("Mapa en vivo: {0}", e.Message); }
            th = new System.Threading.Thread(() =>
            {
                int n = 0, bad = 0;
                while (true)
                {
                    try { Poll(DateTime.Now); } catch (Exception e) { if (bad++ % 150 == 0) Log.W("Mapa en vivo: {0}", e.Message); }
                    try { if (n++ % 30 == 0) Chronicle.Snapshot(DateTime.Now); } catch (Exception e) { Log.W("Máquina del tiempo: {0}", e.Message); }
                    System.Threading.Thread.Sleep(2000);
                }
            }) { IsBackground = true, Name = "Mapa en vivo" };
            th.Start();
        }

        // Lee posiciones.json si ha cambiado y apunta la ruta. Público para poder probarlo sin hilo.
        public static void Poll(DateTime now)
        {
            if (!File.Exists(PosFile)) { lock (lk) current = new List<Pos>(); return; }
            var t = File.GetLastWriteTimeUtc(PosFile);
            if (t == fileAt)
            {
                // El mod deja de escribir cuando no hay nadie o el server se apaga: lo viejo no vale.
                if (currentAt > 0 && Unix(now) - currentAt > StaleAfter) lock (lk) current = new List<Pos>();
                return;
            }
            // Un archivo que quedó de antes (el server se apagó, o el launcher acaba de abrirse): ahí ya no hay nadie.
            if ((now.ToUniversalTime() - t).TotalSeconds > StaleAfter) { fileAt = t; lock (lk) current = new List<Pos>(); return; }
            string text;
            try { text = File.ReadAllText(PosFile, Encoding.UTF8); } catch (IOException) { return; }   // el mod lo está escribiendo
            var list = Parse(text);
            if (list == null) return;
            fileAt = t;
            lock (lk)
            {
                current = list; currentAt = Unix(now);
                foreach (var p in list)
                {
                    Pt last;
                    bool had = lastLogged.TryGetValue(p.Name, out last);
                    if (had && currentAt - last.T < LogEvery) continue;
                    if (had && Math.Abs(p.X - last.X) < MinMove && Math.Abs(p.Z - last.Z) < MinMove && last.Visible == p.Visible && currentAt - last.T < 600) continue;
                    var pt = new Pt { T = currentAt, Name = p.Name, X = p.X, Z = p.Z, Visible = p.Visible };
                    lastLogged[p.Name] = pt; recent.Add(pt);
                    try
                    {
                        Directory.CreateDirectory(RouteDir);
                        File.AppendAllText(RouteFile(now), "{\"t\":" + pt.T + ",\"n\":" + Json.Compact(pt.Name) + ",\"x\":" + pt.X + ",\"z\":" + pt.Z + ",\"v\":" + (pt.Visible ? 1 : 0) + "}\n", new UTF8Encoding(false));
                    }
                    catch (Exception e) { Log.W("Mapa en vivo: no pude apuntar la ruta: {0}", e.Message); }
                }
                foreach (var gone in lastLogged.Keys.Where(k => !list.Any(p => string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase))).ToList()) lastLogged.Remove(gone);
                Trim();
            }
        }

        static void Trim()
        {
            long min = Unix(DateTime.Now) - KeepHours * 3600;
            recent.RemoveAll(p => p.T < min);
        }

        static int I(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o, Inv) : 0; }

        public static List<Pos> Parse(string json)
        {
            Dictionary<string, object> d;
            try { d = Json.ParseObj(json); } catch { return null; }
            object o;
            if (d == null || !d.TryGetValue("jugadores", out o) || !(o is object[])) return null;
            var res = new List<Pos>();
            foreach (var p in ((object[])o).OfType<Dictionary<string, object>>())
            {
                object n, v;
                string name = p.TryGetValue("n", out n) ? Convert.ToString(n, Inv) : "";
                if (name.Length == 0 || name.Length > 40) continue;
                res.Add(new Pos { Name = name, X = I(p, "x"), Y = I(p, "y"), Z = I(p, "z"), Visible = p.TryGetValue("v", out v) && v is bool && (bool)v });
            }
            return res;
        }

        public static List<Pt> ReadDay(DateTime day)
        {
            var res = new List<Pt>();
            string f = RouteFile(day);
            if (!File.Exists(f)) return res;
            foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
            {
                if (line.Length < 10) continue;
                try
                {
                    var d = Json.ParseObj(line);
                    object n;
                    res.Add(new Pt { T = Convert.ToInt64(d["t"], Inv), Name = d.TryGetValue("n", out n) ? Convert.ToString(n, Inv) : "", X = I(d, "x"), Z = I(d, "z"), Visible = I(d, "v") != 0 });
                }
                catch { }   // una línea a medias (un apagón): se salta
            }
            return res;
        }

        public static bool HasDay(DateTime day) { return File.Exists(RouteFile(day)); }

        // Las rutas como las quiere la web: { "Fran": [[t, x, z], ...] }. Un salto grande (un portal, una muerte) va con un 1 al final
        // del punto de llegada, para no pintar una raya que cruce el mapa.
        public static Dictionary<string, object> Routes(IEnumerable<Pt> pts, bool all)
        {
            var res = new Dictionary<string, object>();
            foreach (var g in pts.Where(p => all || p.Visible).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var list = new List<object>(); Pt prev = null;
                foreach (var p in g.OrderBy(x => x.T))
                {
                    bool jump = prev != null && (Math.Abs(p.X - prev.X) + Math.Abs(p.Z - prev.Z) > 400 || p.T - prev.T > 300);
                    list.Add(jump ? new object[] { p.T, p.X, p.Z, 1 } : new object[] { p.T, p.X, p.Z });
                    prev = p;
                }
                res[g.First().Name] = list;
            }
            return res;
        }

        // Lo que pide la web cada 2 segundos. since = hora del último punto que ya tiene (0 = la primera vez: las últimas 6 horas).
        public static Dictionary<string, object> Api(long since, bool all)
        {
            long now = Unix(DateTime.Now);
            if (since <= 0 || since < now - 6 * 3600) since = now - 6 * 3600;
            List<Pos> cur; List<Pt> pts;
            lock (lk) { cur = current.ToList(); pts = recent.Where(p => p.T > since).ToList(); }
            var o = new Dictionary<string, object>
            {
                { "t", now },
                { "jugadores", cur.Where(p => all || p.Visible).Select(p => (object)new Dictionary<string, object> { { "n", p.Name }, { "x", p.X }, { "z", p.Z }, { "y", p.Y }, { "oculto", !p.Visible } }).ToList() },
                { "ocultos", all ? 0 : cur.Count(p => !p.Visible) },
                { "rutas", Routes(pts, all) },
            };
            return o;
        }

        public static List<Pos> Current(bool all) { lock (lk) return current.Where(p => all || p.Visible).ToList(); }

        // Para la IA: dónde está cada vikingo ahora, en palabras ("a 200 m al norte del portal «Mina»", bioma). Solo los que
        // se han marcado como visibles (o todos, si así está puesto en la pestaña Web): el bot contesta delante de todos.
        public static object Tool(string player)
        {
            if (th == null) try { Poll(DateTime.Now); } catch { }   // desde la consola no hay hilo
            bool all = Settings.Current.LiveShowAll;
            List<Pos> cur; lock (lk) cur = current.ToList();
            var w = WorldData.GetWorld();
            var terr = Terrain.Load();
            string f = AiChat.Fold(player ?? "");
            var shown = cur.Where(p => (all || p.Visible) && (f == "" || AiChat.Fold(p.Name).Contains(f))).ToList();
            var o = new Dictionary<string, object>
            {
                { "ahora", shown.Select(p =>
                {
                    var d = new Dictionary<string, object> { { "nombre", p.Name }, { "donde", w != null ? WorldData.Where(w, p.X, p.Z) : "x " + p.X + ", z " + p.Z } };
                    string b = terr != null ? terr.BiomeAt(p.X, p.Z) : null;
                    if (b != null) d["bioma"] = b;
                    return (object)d;
                }).ToList() },
            };
            int hidden = cur.Count(p => !all && !p.Visible && (f == "" || AiChat.Fold(p.Name).Contains(f)));
            if (hidden > 0) o["jugandoSinMarcarseVisibles"] = hidden;
            if (cur.Count == 0) o["nota"] = "No hay nadie en el mapa ahora mismo (o el server está apagado).";
            else if (shown.Count == 0) o["nota"] = f != "" && hidden == 0 ? "Ese vikingo no está jugando ahora." : "Está jugando, pero no se ha marcado como visible en el juego (casilla «Visible para los demás» del mapa), así que no se dice dónde está.";
            if (Settings.Current.WebEnabled && Settings.Current.WebPublic) o["mapaEnVivo"] = WebPanel.PublicUrl + "vivo";
            return o;
        }
    }

    // El terreno de lo explorado (mapa.bin del mod), ya leído.
    public class Terrain
    {
        public int Step, Per, Count;
        public int[] Zx, Zy; public byte[][] Cells;
        public const int MaxZone = 180;

        public static string File { get { return Path.Combine(WorldMod.OutDir, "mapa.bin"); } }
        public static int Key(int x, int y) { return (x + 512) * 1024 + (y + 512); }

        public static Terrain Parse(byte[] data)
        {
            if (data == null || data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "MHM1") return null;
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadBytes(4);
            int n = r.ReadInt32(), step = r.ReadInt16(), per = r.ReadInt16();
            if (n <= 0 || per <= 0 || per > 16 || step <= 0 || data.Length < 12 + (long)n * (4 + per * per * 2)) return null;
            var t = new Terrain { Step = step, Per = per, Zx = new int[n], Zy = new int[n], Cells = new byte[n][] };
            for (int k = 0; k < n; k++)
            {
                int x = r.ReadInt16(), y = r.ReadInt16();
                var c = r.ReadBytes(per * per * 2);
                if (Math.Abs(x) > MaxZone || Math.Abs(y) > MaxZone) continue;
                t.Zx[t.Count] = x; t.Zy[t.Count] = y; t.Cells[t.Count] = c; t.Count++;
            }
            return t.Count == 0 ? null : t;
        }

        static readonly string[] BiomeNames = { null, "Prados", "Pantano", "Montaña", "Selva Negra", "Llanuras", "Tierras de Ceniza", "Norte Profundo", null, "Océano", "Tierras Brumosas" };
        Dictionary<int, int> index;
        // El bioma en ese punto, si está explorado. null = no se sabe.
        public string BiomeAt(int x, int z)
        {
            int zone = Step * Per;
            int zx = (int)Math.Floor((x + zone / 2.0) / zone), zy = (int)Math.Floor((z + zone / 2.0) / zone);
            if (index == null) { var ix = new Dictionary<int, int>(); for (int k = 0; k < Count; k++) ix[Key(Zx[k], Zy[k])] = k; index = ix; }
            int at;
            if (!index.TryGetValue(Key(zx, zy), out at)) return null;
            int i = Math.Max(0, Math.Min(Per - 1, (int)((x - (zx * zone - zone / 2.0)) / Step))), j = Math.Max(0, Math.Min(Per - 1, (int)((z - (zy * zone - zone / 2.0)) / Step)));
            int b = Cells[at][(j * Per + i) * 2], h = Cells[at][(j * Per + i) * 2 + 1];
            if (h < 30) return "en el agua";
            return b > 0 && b < BiomeNames.Length ? BiomeNames[b] : null;
        }

        static Terrain cached; static DateTime cachedAt;
        public static Terrain Load()
        {
            lock (typeof(Terrain))
            {
                if (!System.IO.File.Exists(File)) return null;
                var at = System.IO.File.GetLastWriteTimeUtc(File);
                if (cached == null || at != cachedAt) { cached = Parse(System.IO.File.ReadAllBytes(File)); cachedAt = at; }
                return cached;
            }
        }
        public static long Stamp { get { return System.IO.File.Exists(File) ? System.IO.File.GetLastWriteTimeUtc(File).Ticks / TimeSpan.TicksPerSecond : 0; } }

        // Rejilla para el 3D: "MHT1", x0, z0 (centro de la primera muestra, la de más al suroeste), paso en metros, ancho, alto;
        // luego ancho*alto alturas (0-255, el agua está a 30) y ancho*alto biomas (255 = sin explorar). Fila 0 = la de más al sur.
        // only: si no es null, solo esas zonas (el mundo como estaba otro día). Si sale muy grande, se da a media resolución.
        public byte[] Grid(HashSet<int> only, int maxSamples = 1400000)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, used = 0;
            for (int k = 0; k < Count; k++)
            {
                if (only != null && !only.Contains(Key(Zx[k], Zy[k]))) continue;
                used++;
                minX = Math.Min(minX, Zx[k]); maxX = Math.Max(maxX, Zx[k]); minY = Math.Min(minY, Zy[k]); maxY = Math.Max(maxY, Zy[k]);
            }
            if (used == 0) return null;
            int skip = 1;
            while ((long)((maxX - minX + 1) * Per / skip) * ((maxY - minY + 1) * Per / skip) > maxSamples && skip < Per) skip *= 2;
            int per = Per / skip, w = (maxX - minX + 1) * per, h = (maxY - minY + 1) * per;
            var hs = new byte[w * h]; var bs = new byte[w * h];
            for (int i = 0; i < bs.Length; i++) bs[i] = 255;
            for (int k = 0; k < Count; k++)
            {
                if (only != null && !only.Contains(Key(Zx[k], Zy[k]))) continue;
                for (int j = 0; j < per; j++)
                    for (int i = 0; i < per; i++)
                    {
                        int o = ((Zy[k] - minY) * per + j) * w + (Zx[k] - minX) * per + i, c = (j * skip * Per + i * skip) * 2;
                        bs[o] = Cells[k][c]; hs[o] = Cells[k][c + 1];
                    }
            }
            int zone = Step * Per, step = Step * skip;
            var ms = new MemoryStream(20 + w * h * 2);
            var bw = new BinaryWriter(ms);
            bw.Write(Encoding.ASCII.GetBytes("MHT1"));
            bw.Write(minX * zone - zone / 2 + step / 2); bw.Write(minY * zone - zone / 2 + step / 2);
            bw.Write(step); bw.Write(w); bw.Write(h);
            bw.Write(hs); bw.Write(bs);
            return ms.ToArray();
        }
    }

    // La máquina del tiempo: cómo estaba el mundo cada día y qué pasó.
    public static class Chronicle
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly object lk = new object();
        static DateTime worldSeen, mapSeen;

        public static string Dir { get { return Path.Combine(AppInfo.LogDir, "historia"); } }
        static string DayName(DateTime d) { return d.ToString("yyyy-MM-dd", Inv); }
        static string SnapFile(DateTime d) { return Path.Combine(Dir, DayName(d) + ".json"); }
        static string ZoneFile(DateTime d) { return Path.Combine(Dir, DayName(d) + ".zonas"); }

        public class Mark { public string nombre { get; set; } public int x { get; set; } public int z { get; set; } public string fecha { get; set; } }
        public class Snap
        {
            public string fecha { get; set; }
            public int dia { get; set; }
            public int piezas { get; set; }
            public int cofres { get; set; }
            public int zonas { get; set; }
            public List<Mark> portales { get; set; }
            public List<Mark> tumbas { get; set; }
            public Snap() { portales = new List<Mark>(); tumbas = new List<Mark>(); piezas = -1; }
        }

        // Guarda la foto de hoy si el mod ha escrito algo nuevo. La de cada día se va pisando hasta medianoche: queda la última.
        public static void Snapshot(DateTime now)
        {
            lock (lk)
            {
                var wt = WorldData.WorldFileTime;
                var mt = File.Exists(Terrain.File) ? File.GetLastWriteTimeUtc(Terrain.File) : DateTime.MinValue;
                if (wt == DateTime.MinValue && mt == DateTime.MinValue) return;
                bool worldNew = wt != worldSeen && wt != DateTime.MinValue, mapNew = mt != mapSeen && mt != DateTime.MinValue;
                if (!worldNew && !mapNew) return;
                // Lo que quedó escrito otro día (el server lleva apagado desde entonces) es la foto de aquel día, no de hoy.
                DateTime wDay = wt.Date == now.Date || wt == DateTime.MinValue ? now.Date : wt.Date, mDay = mt == DateTime.MinValue || mt.ToLocalTime().Date == now.Date ? now.Date : mt.ToLocalTime().Date;
                Directory.CreateDirectory(Dir);
                try
                {
                    // Las rutas de hace más de un año se borran: ocupan poco, pero no hacen falta para siempre.
                    if (Directory.Exists(LiveMap.RouteDir))
                        foreach (string f in Directory.GetFiles(LiveMap.RouteDir, "*.jsonl"))
                            if (File.GetLastWriteTime(f) < now.AddDays(-400)) File.Delete(f);
                }
                catch { }
                var terr = Terrain.Load();
                if (mapNew)
                {
                    if (terr != null && !(mDay != now.Date && File.Exists(ZoneFile(mDay))))
                    {
                        var ms = new MemoryStream(terr.Count * 4);
                        var bw = new BinaryWriter(ms);
                        for (int k = 0; k < terr.Count; k++) { bw.Write((short)terr.Zx[k]); bw.Write((short)terr.Zy[k]); }
                        WriteSafe(ZoneFile(mDay), ms.ToArray());
                    }
                    mapSeen = mt;
                }
                if (worldNew)
                {
                    var w = WorldData.GetWorld();
                    if (w != null && !(wDay != now.Date && File.Exists(SnapFile(wDay))))
                    {
                        var s = new Snap
                        {
                            fecha = (w.Date != DateTime.MinValue ? w.Date : now).ToString("yyyy-MM-ddTHH:mm:ss", Inv), dia = w.Day, piezas = w.Pieces,
                            cofres = w.Chests.Count + w.Drawers.Count, zonas = terr != null ? terr.Count : 0,
                            portales = w.Portals.Where(p => p.Value != "").Select(p => new Mark { nombre = p.Value, x = p.X, z = p.Z }).ToList(),
                            tumbas = w.Tombs.Select(t => new Mark { nombre = t.Player, x = t.X, z = t.Z, fecha = t.Death == DateTime.MinValue ? null : t.Death.ToString("yyyy-MM-ddTHH:mm:ss", Inv) }).ToList(),
                        };
                        WriteSafe(SnapFile(wDay), Encoding.UTF8.GetBytes(Json.Str(s)));
                    }
                    worldSeen = wt;
                }
            }
        }

        static void WriteSafe(string path, byte[] data)
        {
            File.WriteAllBytes(path + ".tmp", data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        static List<DateTime> Days(string ext)
        {
            var res = new List<DateTime>();
            if (!Directory.Exists(Dir)) return res;
            foreach (string f in Directory.GetFiles(Dir, "*" + ext))
            {
                DateTime d;
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) res.Add(d);
            }
            res.Sort();
            return res;
        }

        static Snap LoadSnap(DateTime d) { try { return Json.Load<Snap>(SnapFile(d)); } catch { return null; } }

        // Las zonas exploradas a final de ese día (la foto de ese día o la anterior más cercana). null = no hay foto tan antigua.
        public static HashSet<int> ZonesAt(DateTime day)
        {
            var days = Days(".zonas").Where(d => d <= day.Date).ToList();
            if (days.Count == 0) return null;
            var data = File.ReadAllBytes(ZoneFile(days[days.Count - 1]));
            var set = new HashSet<int>();
            for (int i = 0; i + 3 < data.Length; i += 4) set.Add(Terrain.Key(BitConverter.ToInt16(data, i), BitConverter.ToInt16(data, i + 2)));
            return set;
        }

        public static DateTime? FirstPhoto { get { var d = Days(".zonas"); return d.Count == 0 ? (DateTime?)null : d[0]; } }

        static string Dur(int min) { return min < 60 ? min + " min" : (min / 60) + " h" + (min % 60 > 0 ? " " + (min % 60) + " min" : ""); }

        // Todos los días en que pasó algo, del más nuevo al más viejo, con lo que pasó en palabras.
        public static Dictionary<string, object> Days(bool all)
        {
            var sessions = ServerWatch.Sessions(DateTime.MinValue);
            var snaps = Days(".json");
            var bosses = Bosses.Load().Where(b => !b.Before).ToList();
            var days = new SortedDictionary<DateTime, Dictionary<string, object>>();
            Func<DateTime, Dictionary<string, object>> get = d =>
            {
                Dictionary<string, object> o;
                if (!days.TryGetValue(d.Date, out o))
                    days[d.Date] = o = new Dictionary<string, object> { { "dia", DayName(d) }, { "jugadores", new List<object>() }, { "muertes", 0 }, { "minutos", 0 }, { "hechos", new List<object>() } };
                return o;
            };
            foreach (var g in sessions.GroupBy(s => s.Start.Date))
            {
                var o = get(g.Key);
                var per = g.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new { Name = p.First().Name, Min = p.Sum(s => Math.Max(0, (int)Math.Round((s.End - s.Start).TotalMinutes))), Deaths = p.Sum(s => s.Deaths) })
                    .OrderByDescending(p => p.Min).ToList();
                o["jugadores"] = per.Select(p => (object)new Dictionary<string, object> { { "nombre", p.Name }, { "minutos", p.Min }, { "muertes", p.Deaths } }).ToList();
                o["muertes"] = per.Sum(p => p.Deaths); o["minutos"] = per.Sum(p => p.Min);
                var facts = (List<object>)o["hechos"];
                foreach (var p in per.Where(p => p.Deaths > 0)) facts.Add(Fact("muerte", p.Name + " murió " + (p.Deaths == 1 ? "una vez" : p.Deaths + " veces")));
            }
            foreach (var b in bosses)
            {
                DateTime when;
                if (DateTime.TryParse(b.Since, Inv, DateTimeStyles.None, out when)) ((List<object>)get(when)["hechos"]).Insert(0, Fact("jefe", "Cayó " + Bosses.Name(b.Key)));
            }
            Snap prev = null;
            foreach (var d in snaps)
            {
                var s = LoadSnap(d);
                if (s == null) continue;
                if (prev != null)
                {
                    var facts = new List<object>();
                    int zones = s.zonas - prev.zonas;
                    if (zones > 0) facts.Add(Fact("mapa", "Se exploraron " + (zones * 64 * 64 / 10000.0).ToString(zones < 25 ? "0.#" : "0", new CultureInfo("es-ES")) + " hectáreas nuevas de mapa"));
                    if (s.piezas >= 0 && prev.piezas >= 0 && s.piezas != prev.piezas)
                        facts.Add(Fact("obra", s.piezas > prev.piezas ? "Se construyeron " + (s.piezas - prev.piezas) + " piezas" : "Se quitaron " + (prev.piezas - s.piezas) + " piezas"));
                    var newPortals = s.portales.Where(p => !prev.portales.Any(q => string.Equals(q.nombre, p.nombre, StringComparison.OrdinalIgnoreCase))).Select(p => p.nombre).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (newPortals.Count > 0) facts.Add(Fact("portal", (newPortals.Count == 1 ? "Portal nuevo: " : "Portales nuevos: ") + string.Join(", ", newPortals.Take(6).Select(n => "«" + n + "»"))));
                    if (facts.Count > 0 || days.ContainsKey(d)) ((List<object>)get(d)["hechos"]).AddRange(facts);
                }
                if (days.ContainsKey(d)) { days[d]["foto"] = true; days[d]["diaMundo"] = s.dia; }
                prev = s;
            }
            foreach (var kv in days) if (LiveMap.HasDay(kv.Key)) kv.Value["rutas"] = true;
            var first = FirstPhoto;
            return new Dictionary<string, object>
            {
                { "dias", days.Values.Reverse().Cast<object>().ToList() },
                { "primeraFoto", first.HasValue ? DayName(first.Value) : null },
                { "hoy", DayName(DateTime.Today) },
            };
        }

        // Para la IA: qué pasó los últimos días, con la fecha en palabras y los minutos ya en horas.
        public static object Tool(int days)
        {
            var es = new CultureInfo("es-ES");
            var list = ((List<object>)Days(Settings.Current.LiveShowAll)["dias"]).Cast<Dictionary<string, object>>()
                .Where(d => DateTime.ParseExact((string)d["dia"], "yyyy-MM-dd", Inv) >= DateTime.Today.AddDays(-days)).ToList();
            var res = list.Select(d =>
            {
                var day = DateTime.ParseExact((string)d["dia"], "yyyy-MM-dd", Inv);
                int ago = (int)(DateTime.Today - day).TotalDays;
                var o = new Dictionary<string, object>
                {
                    { "dia", (ago == 0 ? "hoy, " : ago == 1 ? "ayer, " : "hace " + ago + " días, ") + day.ToString("dddd d 'de' MMMM", es) },
                    { "jugaron", ((List<object>)d["jugadores"]).Cast<Dictionary<string, object>>().Select(p => (object)((string)p["nombre"] + " (" + Dur((int)p["minutos"]) + ")")).ToList() },
                    { "hechos", ((List<object>)d["hechos"]).Cast<Dictionary<string, object>>().Select(h => h["texto"]).ToList() },
                };
                return (object)o;
            }).ToList();
            var r = new Dictionary<string, object> { { "dias", res } };
            if (res.Count == 0) r["nota"] = "No hay nada apuntado en esos días: nadie jugó.";
            if (Settings.Current.WebEnabled && Settings.Current.WebPublic) r["maquinaDelTiempo"] = WebPanel.PublicUrl + "historia";
            return r;
        }

        static object Fact(string kind, string text) { return new Dictionary<string, object> { { "tipo", kind }, { "texto", text } }; }

        // Un día al detalle: las partidas con sus horas, las rutas y lo que había en el mapa.
        public static Dictionary<string, object> Day(DateTime day, bool all)
        {
            day = day.Date;
            var ses = ServerWatch.Sessions(day).Where(s => s.Start.Date == day).ToList();   // cada partida cuenta en el día en que empezó
            var snaps = Days(".json").Where(d => d <= day).ToList();
            var snap = snaps.Count > 0 ? LoadSnap(snaps[snaps.Count - 1]) : null;
            bool old = snap == null;
            if (old) { var w = WorldData.GetWorld(); if (w != null) snap = new Snap { portales = w.Portals.Where(p => p.Value != "").Select(p => new Mark { nombre = p.Value, x = p.X, z = p.Z }).ToList() }; }
            var o = new Dictionary<string, object>
            {
                { "dia", DayName(day) },
                { "partidas", ses.Select(s => (object)new Dictionary<string, object> { { "nombre", s.Name }, { "inicio", LiveMap.Unix(s.Start) }, { "fin", LiveMap.Unix(s.End) }, { "muertes", s.Deaths }, { "jugando", s.Live } }).ToList() },
                { "rutas", LiveMap.Routes(LiveMap.ReadDay(day), all) },
                { "mapaDeHoy", ZonesAt(day) == null },   // no hay foto tan antigua: se enseña el mapa como está ahora
                { "portales", snap == null ? new List<object>() : snap.portales.GroupBy(p => p.nombre, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(60).Select(p => (object)new Dictionary<string, object> { { "nombre", p.nombre }, { "x", p.x }, { "z", p.z } }).ToList() },
                { "tumbas", snap == null || old ? new List<object>() : snap.tumbas.Where(t => t.fecha != null && t.fecha.StartsWith(DayName(day))).Select(t => (object)new Dictionary<string, object> { { "nombre", t.nombre }, { "x", t.x }, { "z", t.z } }).ToList() },
            };
            if (snap != null && !old) { o["diaMundo"] = snap.dia; if (snap.piezas >= 0) o["piezas"] = snap.piezas; o["cofres"] = snap.cofres; o["zonas"] = snap.zonas; }
            return o;
        }
    }
}
