// "Cero sustos" (2.1): mantenimiento con red de seguridad (mods y Valheim al día con backup, prueba de arranque y vuelta atrás),
// backups comprobados, aviso de mods que conviene vigilar y revisión de salud del server.
// Sin ventanas: lo usan el launcher del server y MajinheimCli. Compilado con el csc de .NET Framework 4.8 (C# 5).
#if SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Majinheim
{
    // ---------------------------------------------------------------- errores del registro de BepInEx

    // Para saber si un arranque ha ido bien se comparan sus errores con los de antes: lo que importa es lo NUEVO.
    public static class LogCheck
    {
        public static string LogFile { get { return Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log"); } }
        static string KnownFile { get { return AppInfo.P("logs", "errores-conocidos.json"); } }

        // Sin pantalla, Valheim siempre se queja de gráficos y vídeo: eso no cuenta.
        static readonly string[] Noise = { "shader", "video", "asyncresourceupload", "cinematic", "render path" };
        static readonly Regex rxLine = new Regex(@"^\[(Error|Fatal|Warning)\s*:\s*([^\]]*)\]\s*(.*)$");

        // Firma de una línea: nivel, quién la escribe y el mensaje sin números, para comparar arranques. null si no es un fallo que importe.
        public static string Signature(string line)
        {
            line = (line ?? "").Trim();
            var m = rxLine.Match(line);
            string lvl, src, msg;
            if (m.Success) { lvl = m.Groups[1].Value; src = m.Groups[2].Value.Trim(); msg = m.Groups[3].Value; }
            else if (line.Contains("HarmonyException") || line.Contains("Patching exception")) { lvl = "Error"; src = "Harmony"; msg = line; }
            else return null;
            if (lvl == "Warning")
            {
                // De los avisos solo cuentan los del cargador de mods cuando dice que un mod no se ha cargado.
                string w = msg.ToLowerInvariant();
                bool loader = src.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) &&
                    (w.Contains("skipping") || w.Contains("could not load") || w.Contains("incompatible") || w.Contains("missing dependenc"));
                if (!loader) return null;
            }
            string low = (src + " " + msg).ToLowerInvariant();
            foreach (var n in Noise) if (low.Contains(n)) return null;
            return lvl + " " + src + ": " + Norm(msg);
        }

        // ¿Lo escribe el cargador de mods (un mod que no se ha podido cargar)?
        public static bool FromLoader(string line)
        {
            string sig = Signature(line);
            return sig != null && Regex.IsMatch(sig, @"^(Error|Fatal|Warning) BepInEx:", RegexOptions.IgnoreCase);
        }

        static string Norm(string s)
        {
            s = Regex.Replace(s ?? "", @"\d+", "#");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }

        public static HashSet<string> FromLines(IEnumerable<string> lines)
        {
            var set = new HashSet<string>();
            foreach (var l in lines) { var sg = Signature(l); if (sg != null) set.Add(sg); }
            return set;
        }

        public static HashSet<string> FromFile(string file)
        {
            if (!File.Exists(file)) return new HashSet<string>();
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) return FromLines(sr.ReadToEnd().Split('\n'));
            }
            catch { return new HashSet<string>(); }
        }

        // Errores que ya salían cuando el server funcionaba bien (se guardan tras cada mantenimiento que sale bien).
        public static HashSet<string> Known()
        {
            try { var l = Json.Load<List<string>>(KnownFile); return new HashSet<string>(l ?? new List<string>()); }
            catch { return new HashSet<string>(); }
        }

        public static void SaveKnown(IEnumerable<string> sigs)
        {
            try { Json.Save(KnownFile, sigs.Distinct().OrderBy(x => x).Take(500).ToList()); } catch (Exception e) { Log.W("No pude guardar los errores conocidos: {0}", e.Message); }
        }

        static string Key(string s) { return Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", ""); }

        // ¿De qué mod de la lista es este error? Por el nombre del mod dentro de la línea. null si no se sabe.
        public static ModEntry Culprit(string line, IEnumerable<ModEntry> mods)
        {
            string k = Key(line);
            ModEntry best = null; int bestLen = 0;
            foreach (var m in mods)
            {
                string n = Key(m.PkgName());
                if (n.Length >= 4 && k.Contains(n) && n.Length > bestLen) { best = m; bestLen = n.Length; }
            }
            return best;
        }
    }

    // ---------------------------------------------------------------- prueba de arranque

    public static class ServerTest
    {
        public class Result
        {
            public bool Loaded;                                  // el mundo ha cargado y el server sigue encendido
            public bool Exited;                                  // el server se ha cerrado solo durante la prueba
            public List<string> NewErrors = new List<string>();  // líneas con errores que no salían antes
            public List<string> NewSigs = new List<string>();
            public TimeSpan Took;
        }

        // Arranca el server (sin el backup de cada arranque: el mantenimiento ya ha hecho uno) y espera a que cargue el mundo.
        // Se queda encendido: quien llama decide si lo apaga. Mientras dura, no se reinicia solo si se cae.
        public static Result Run(ServerRunner r, HashSet<string> baseline, int timeoutSec, Action<string> status)
        {
            var res = new Result();
            var lk = new object();
            bool loaded = false;
            Action<string> h = l =>
            {
                if (l.Contains("Game server connected")) loaded = true;
                string sig = LogCheck.Signature(l);
                if (sig == null || baseline.Contains(sig)) return;
                lock (lk)
                    if (!res.NewSigs.Contains(sig))
                    {
                        res.NewSigs.Add(sig);
                        string t = l.Trim();
                        res.NewErrors.Add(t.Length > 300 ? t.Substring(0, 300) : t);
                    }
            };
            r.NoAutoRestart = true;
            r.GameLog += h;
            var t0 = DateTime.Now;
            try
            {
                if (status != null) status("Probando el arranque del server (tarda un par de minutos)...");
                r.Start(false);
                while ((DateTime.Now - t0).TotalSeconds < timeoutSec)
                {
                    Thread.Sleep(1000);
                    if (!r.Running) { res.Exited = true; break; }
                    if (loaded)
                    {
                        if (status != null) status("El mundo ha cargado. Mirando que los mods no den errores...");
                        Thread.Sleep(30000);   // los mods terminan de engancharse justo después de cargar el mundo
                        break;
                    }
                }
                res.Loaded = loaded && r.Running;
                if (!r.Running) res.Exited = true;
            }
            finally
            {
                r.GameLog -= h;
                res.Took = DateTime.Now - t0;
            }
            Log.W("Prueba de arranque: {0} en {1:0} s ({2} errores nuevos).", res.Loaded ? "el mundo carga" : res.Exited ? "el server se ha cerrado" : "el mundo no ha cargado a tiempo", res.Took.TotalSeconds, res.NewErrors.Count);
            return res;
        }
    }

    // ---------------------------------------------------------------- mods: versiones nuevas y mods a vigilar

    public static class ModWatch
    {
        public class Upd
        {
            public ModEntry Mod;
            public string To;
            public string Text { get { return Nice(Mod) + " (" + Mod.Version + " → " + To + ")"; } }
        }

        public static string Nice(ModEntry m) { return m.PkgName().Replace('_', ' '); }

        // Pregunta a Thunderstore por la última versión de cada mod (de 6 en 6).
        public static void Refresh(Modpack pack, Dictionary<string, PkgInfo> latest)
        {
            var mods = pack.Mods.Where(m => !m.Local).ToList();
            Parallel.ForEach(mods, new ParallelOptions { MaxDegreeOfParallelism = 6 }, m =>
            {
                try { var p = Thunderstore.Latest(m.Id); lock (latest) latest[m.Id] = p; }
                catch (Exception e) { Log.W("No pude consultar {0}: {1}", m.Id, e.Message); }
            });
        }

        // Mods que se pueden actualizar solos: activos, de Thunderstore, sin arreglo propio, que carga el server
        // (si no, la prueba no los ve) y cuya versión nueva no haya fallado ya. En notes, los que esperan y por qué.
        public static List<Upd> Candidates(Modpack pack, Dictionary<string, PkgInfo> latest, bool retrySkipped, List<string> notes)
        {
            var skip = Settings.Current.SkippedUpdates ?? new List<string>();
            var list = new List<Upd>();
            lock (latest)
            {
                foreach (var m in pack.Mods.Where(x => x.Enabled && !x.Local))
                {
                    PkgInfo li;
                    if (!latest.TryGetValue(m.Id, out li) || Ver.Compare(li.Version, m.Version) <= 0) continue;
                    if (!string.IsNullOrEmpty(m.Patch)) { notes.Add(Nice(m) + " " + li.Version + " lleva un arreglo propio: se actualiza a mano."); continue; }
                    if (!m.ForServer()) { notes.Add(Nice(m) + " " + li.Version + " es solo de jugadores y el server no puede probarlo: se actualiza a mano."); continue; }
                    if (!retrySkipped && skip.Contains(m.Id + "@" + li.Version, StringComparer.OrdinalIgnoreCase)) { notes.Add(Nice(m) + " " + li.Version + " ya falló la prueba: se queda en la " + m.Version + "."); continue; }
                    list.Add(new Upd { Mod = m, To = li.Version });
                }
                // La versión nueva puede pedir otras más nuevas de sus dependencias: se suben también, o ese mod espera.
                for (int pass = 0; pass < 8; pass++)
                {
                    bool changed = false;
                    foreach (var u in list.ToList())
                    {
                        if (!list.Contains(u)) continue;
                        PkgInfo li = latest[u.Mod.Id];
                        foreach (var dep in li.Deps)
                        {
                            string did, dver;
                            try { Thunderstore.SplitDep(dep, out did, out dver); } catch { continue; }
                            var have = pack.Find(did);
                            if (have == null || !have.Enabled)
                            {
                                list.Remove(u); changed = true;
                                notes.Add(Nice(u.Mod) + " " + u.To + " necesita " + did + ", que no está en la lista: se actualiza a mano.");
                                break;
                            }
                            var up = list.FirstOrDefault(x => x.Mod == have);
                            if (Ver.Compare(up != null ? up.To : have.Version, dver) >= 0) continue;
                            PkgInfo dl;
                            bool canBump = up == null && !have.Local && string.IsNullOrEmpty(have.Patch) && latest.TryGetValue(have.Id, out dl) && Ver.Compare(dl.Version, dver) >= 0
                                && (retrySkipped || !skip.Contains(have.Id + "@" + dl.Version, StringComparer.OrdinalIgnoreCase));
                            if (canBump) { list.Add(new Upd { Mod = have, To = latest[have.Id].Version }); changed = true; continue; }
                            list.Remove(u); changed = true;
                            notes.Add(Nice(u.Mod) + " " + u.To + " pide " + Nice(have) + " " + dver + " o más nuevo: espera.");
                            break;
                        }
                    }
                    if (!changed) break;
                }
            }
            return list;
        }

        // Quita de la lista los culpables y los que dependen de sus versiones nuevas.
        public static List<Upd> Without(List<Upd> ups, List<Upd> bad, Dictionary<string, PkgInfo> latest)
        {
            var rest = ups.Where(u => !bad.Contains(u)).ToList();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var u in rest.ToList())
                {
                    PkgInfo li;
                    lock (latest) if (!latest.TryGetValue(u.Mod.Id, out li)) continue;
                    foreach (var dep in li.Deps)
                    {
                        string did, dver;
                        try { Thunderstore.SplitDep(dep, out did, out dver); } catch { continue; }
                        var b = bad.FirstOrDefault(x => x.Mod.Id.Equals(did, StringComparison.OrdinalIgnoreCase));
                        if (b != null && Ver.Compare(b.Mod.Version, dver) < 0) { rest.Remove(u); changed = true; break; }
                    }
                }
            }
            return rest;
        }

        // Por qué conviene vigilar un mod: obsoleto según su autor, o sin versión nueva desde hace mucho. null si está bien.
        public static string Concern(PkgInfo li)
        {
            if (li == null) return null;
            if (li.Deprecated) return "su autor lo ha marcado como obsoleto en Thunderstore";
            string before = Settings.Current.StaleBefore ?? "";
            if (!string.IsNullOrEmpty(li.Date) && before.Length > 0 && string.CompareOrdinal(li.Date, before) < 0) return "sin versión nueva desde el " + Fecha(li.Date);
            return null;
        }

        static string Fecha(string iso)
        {
            DateTime d;
            return DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-ES")) : iso;
        }

        // Mods a vigilar de los que todavía no se ha avisado (se recuerda en los ajustes para no repetirlo).
        public static List<string> NewNotices(Modpack pack, Dictionary<string, PkgInfo> latest)
        {
            var s = Settings.Current;
            if (s.ModNotices == null) s.ModNotices = new List<string>();
            var res = new List<string>();
            bool save = false;
            lock (latest)
                foreach (var m in pack.Mods.Where(x => x.Enabled && !x.Local))
                {
                    PkgInfo li;
                    if (!latest.TryGetValue(m.Id, out li)) continue;
                    string why = Concern(li);
                    if (why == null) continue;
                    string key = m.Id + "@" + (li.Deprecated ? "obsoleto" : "viejo") + "@" + li.Version;
                    if (s.ModNotices.Contains(key)) continue;
                    s.ModNotices.Add(key); save = true;
                    res.Add("• " + Nice(m) + ": " + why + ".");
                }
            if (save) Settings.Save();
            return res;
        }
    }

    // ---------------------------------------------------------------- backups comprobados

    public static class BackupCheck
    {
        public class Result { public bool Ok; public string Text; }

        public static bool Due { get { return Age(Settings.Current.BackupCheckAt).TotalDays >= 7; } }

        static TimeSpan Age(string iso)
        {
            DateTime d;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return TimeSpan.MaxValue;
            return DateTime.Now - d;
        }

        // Abre el último backup del mundo y lo lee entero (si algún trozo está dañado, falla), y lo mismo con la copia fuera del PC.
        // save = apuntar el resultado en los ajustes (la consola no lo hace con el launcher abierto, que los pisaría).
        public static Result Run(bool save = true)
        {
            var r = Check();
            if (save) Save(r); else Log.W("Comprobación de backups: {0}", r.Text);
            return r;
        }

        static Result Check()
        {
            var r = new Result();
            var last = Backup.LastWorld();
            if (last == null) { r.Text = "No hay ningún backup del mundo todavía."; return r; }
            string err = Verify(last.Path);
            string when = last.Date.ToString("dd/MM HH:mm");
            if (err != null) { r.Text = "El último backup del mundo (" + when + ") está dañado: " + err + "."; return r; }
            string text = "El último backup del mundo (" + when + ", " + Math.Round(last.Size / 1048576.0, 1).ToString(CultureInfo.GetCultureInfo("es-ES")) + " MB) se abre bien";
            string od = Backup.OffsiteFolder;
            if (od != null)
            {
                if (!Directory.Exists(od)) { r.Text = text + ", pero no encuentro la carpeta de la copia fuera del PC (" + od + ")."; return r; }
                var f = new DirectoryInfo(od).GetFiles("*.zip").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f == null) { r.Text = text + ", pero en la copia fuera del PC todavía no hay ninguno."; return r; }
                string e2 = Verify(f.FullName);
                if (e2 != null) { r.Text = text + ", pero la copia fuera del PC (" + f.LastWriteTime.ToString("dd/MM HH:mm") + ") está dañada: " + e2 + "."; return r; }
                if ((last.Date - f.LastWriteTime).TotalDays > 2) { r.Text = text + ", pero la copia fuera del PC se ha quedado atrás: la última es del " + f.LastWriteTime.ToString("dd/MM") + "."; return r; }
                text += ", y la copia fuera del PC (" + f.LastWriteTime.ToString("dd/MM HH:mm") + ") también";
            }
            r.Ok = true;
            r.Text = text + ".";
            return r;
        }

        static void Save(Result r)
        {
            var s = Settings.Current;
            s.BackupCheckAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            s.BackupCheckOk = r.Ok;
            s.BackupCheckText = r.Text;
            Settings.Save();
            Log.W("Comprobación de backups: {0}", r.Text);
        }

        // null si el zip se lee entero y lleva el mundo con datos; si no, el problema.
        public static string Verify(string zip)
        {
            try
            {
                long worldBytes = 0; int worldFiles = 0;
                var buf = new byte[81920];
                using (var z = ZipFile.OpenRead(zip))
                {
                    if (z.GetEntry("backup.json") == null) return "no lleva backup.json";
                    foreach (var e in z.Entries)
                    {
                        if (e.Name == "") continue;
                        long n = 0; int k;
                        using (var st = e.Open()) while ((k = st.Read(buf, 0, buf.Length)) > 0) n += k;
                        if (n != e.Length) return e.FullName + " no se lee entero";
                        if (e.FullName.StartsWith("saves_worlds_local/", StringComparison.OrdinalIgnoreCase)) { worldFiles++; worldBytes += n; }
                    }
                }
                if (worldFiles == 0) return "no lleva el mundo";
                if (worldBytes < 1024) return "el mundo está vacío";
                return null;
            }
            catch (Exception e) { return e.Message; }
        }
    }

    // ---------------------------------------------------------------- revisión de salud

    public static class Health
    {
        public class Item { public string Key, Name, State, Detail; }   // State: ok | aviso | mal | info

        public static List<Item> Last { get; private set; }   // la última revisión (la que enseñan la web y Discord)
        public static DateTime LastAt { get; private set; }

        static DateTime ddnsAt = DateTime.MinValue, certAt = DateTime.MinValue;
        static bool ddnsOk;
        static string certText, certState;

        static Item I(string key, string name, string state, string detail) { return new Item { Key = key, Name = name, State = state, Detail = detail }; }
        static string F(DateTime d) { return d.Date == DateTime.Today ? "hoy a las " + d.ToString("HH:mm") : d.Date == DateTime.Today.AddDays(-1) ? "ayer a las " + d.ToString("HH:mm") : "el " + d.ToString("dd/MM HH:mm"); }

        // r = el server que controla el launcher (null desde la consola). latest = últimas versiones de Thunderstore (puede estar vacío).
        public static List<Item> Check(ServerRunner r, Dictionary<string, PkgInfo> latest, Modpack pack)
        {
            var s = Settings.Current;
            var list = new List<Item>();
            Action<Func<Item>> add = f => { try { var it = f(); if (it != null) list.Add(it); } catch (Exception e) { Log.W("Salud: {0}", e.Message); } };

            add(() =>
            {
                bool on = r != null ? r.Running : System.Diagnostics.Process.GetProcessesByName("valheim_server").Length > 0;
                if (r != null && !on && r.Status == ServerRunner.GaveUpStatus) return I("server", "Server", "mal", "Se ha caído 3 veces nada más arrancar y está apagado: mira el registro del server.");
                if (!on) return I("server", "Server", "info", "Apagado.");
                return I("server", "Server", "ok", r != null ? "En marcha desde " + F(r.StartedAt) + "." : "En marcha.");
            });

            add(() =>
            {
                var last = Backup.LastWorld();
                if (last == null) return I("backup", "Último backup del mundo", "mal", "No hay ninguno.");
                string d = F(last.Date) + " (" + Math.Round(last.Size / 1048576.0, 1).ToString(CultureInfo.GetCultureInfo("es-ES")) + " MB).";
                return I("backup", "Último backup del mundo", (DateTime.Now - last.Date).TotalDays > 7 ? "aviso" : "ok", (DateTime.Now - last.Date).TotalDays > 7 ? d + " Hace más de una semana: se hacen con el server encendido." : d);
            });

            add(() =>
            {
                DateTime at;
                bool has = DateTime.TryParse(s.BackupCheckAt ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at);
                if (!has) return I("comprobacion", "Backups comprobados", "aviso", "Todavía no se han comprobado (se hace solo una vez por semana).");
                if (!s.BackupCheckOk) return I("comprobacion", "Backups comprobados", "mal", s.BackupCheckText ?? "La última comprobación falló.");
                return I("comprobacion", "Backups comprobados", (DateTime.Now - at).TotalDays > 8 ? "aviso" : "ok", "Comprobados " + F(at) + ": se abren bien.");
            });

            add(() =>
            {
                string od = Backup.OffsiteFolder;
                if (od == null) return I("externa", "Copia fuera del PC", "aviso", "Desactivada: si el disco del PC falla, el mundo se pierde. Elige una carpeta en Automático.");
                if (Backup.OffsiteError != null) return I("externa", "Copia fuera del PC", "mal", "La última copia falló: " + Backup.OffsiteError);
                if (!Directory.Exists(od)) return I("externa", "Copia fuera del PC", "mal", "No encuentro la carpeta " + od + ".");
                var f = new DirectoryInfo(od).GetFiles("*.zip").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f == null) return I("externa", "Copia fuera del PC", "aviso", "Activada; se copia con el próximo backup del mundo.");
                var last = Backup.LastWorld();
                if (last != null && (last.Date - f.LastWriteTime).TotalDays > 2) return I("externa", "Copia fuera del PC", "aviso", "Se ha quedado atrás: la última es de " + F(f.LastWriteTime) + ".");
                return I("externa", "Copia fuera del PC", "ok", "Última copia " + F(f.LastWriteTime) + ".");
            });

            add(() =>
            {
                var drive = new DriveInfo(Path.GetPathRoot(AppInfo.Root));
                double gb = drive.AvailableFreeSpace / 1073741824.0;
                string d = Math.Round(gb, gb < 10 ? 1 : 0).ToString(CultureInfo.GetCultureInfo("es-ES")) + " GB libres en " + drive.Name.TrimEnd('\\') + ".";
                return I("disco", "Espacio en disco", gb < 5 ? "mal" : gb < 15 ? "aviso" : "ok", gb < 5 ? d + " Con tan poco, los backups pueden fallar." : d);
            });

            add(() =>
            {
                if (pack == null) return null;
                var notes = new List<string>();
                int ups = latest == null ? 0 : ModWatch.Candidates(pack, latest, false, notes).Count;
                var watch = new List<string>();
                if (latest != null)
                    lock (latest)
                        foreach (var m in pack.Mods.Where(x => x.Enabled && !x.Local))
                        {
                            PkgInfo li;
                            if (latest.TryGetValue(m.Id, out li) && li.Deprecated) watch.Add(ModWatch.Nice(m));
                        }
                if (latest == null || latest.Count == 0) return I("mods", "Mods", "info", "Todavía no he mirado Thunderstore.");
                int stale = 0;
                lock (latest)
                    foreach (var m in pack.Mods.Where(x => x.Enabled && !x.Local))
                    {
                        PkgInfo li;
                        if (latest.TryGetValue(m.Id, out li) && !li.Deprecated && ModWatch.Concern(li) != null) stale++;
                    }
                string extra = (notes.Count > 0 ? " " + notes.Count + (notes.Count == 1 ? " espera" : " esperan") + " a mano (pestaña Mods)." : "") +
                    (stale > 0 ? " " + stale + (stale == 1 ? " lleva" : " llevan") + " mucho sin versión nueva (en amarillo en la pestaña Mods)." : "");
                if (watch.Count > 0) return I("mods", "Mods", "aviso", "Marcados como obsoletos por su autor: " + string.Join(", ", watch) + ". Conviene buscarles relevo." + extra);
                if (ups > 0) return I("mods", "Mods", "ok", ups + (ups == 1 ? " con versión nueva" : " con versiones nuevas") +
                    (s.AutoUpdateMods ? ": se prueban y se instalan en el mantenimiento diario." : ": se actualizan a mano (pestaña Mods o Actualizar ahora).") + extra);
                return I("mods", "Mods", "ok", "Al día." + extra);
            });

            add(() =>
            {
                var st = Steam.Server();
                if (st == null) return I("valheim", "Valheim", "info", "No encuentro la instalación de Steam del server.");
                if (st.UpdatePending) return I("valheim", "Valheim", "aviso", "Hay una actualización esperando: se instala y se prueba en el próximo mantenimiento.");
                if (!string.IsNullOrEmpty(s.VerifiedBuild) && s.VerifiedBuild == st.BuildId) return I("valheim", "Valheim", "ok", "Al día y probado con los mods de ahora.");
                return I("valheim", "Valheim", "ok", "Al día (versión " + st.BuildId + ").");
            });

            add(() =>
            {
                if (!(s.WebEnabled && s.WebPublic)) return I("https", "Web desde internet", "info", "Desactivada.");
                if ((DateTime.Now - certAt).TotalMinutes >= 60) { CheckCert(); certAt = DateTime.Now; }
                return I("https", "Web desde internet", certState, certText);
            });

            add(() =>
            {
                if (!(s.WebEnabled && s.WebPublic)) return null;
                if ((DateTime.Now - ddnsAt).TotalMinutes >= 60) { ddnsOk = Net.SamePublicIp(WebPanel.PublicHost); ddnsAt = DateTime.Now; }
                return I("ddns", "Dirección de internet (DDNS)", ddnsOk ? "ok" : "mal", ddnsOk ? WebPanel.PublicHost + " apunta a casa." : WebPanel.PublicHost + " no apunta a la IP de casa: revisa la DDNS en el router.");
            });

            add(() =>
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile"))
                {
                    object v = k == null ? null : k.GetValue("EnableFirewall");
                    if (!(v is int)) return I("cortafuegos", "Cortafuegos de Windows", "info", "No he podido mirarlo.");
                    return (int)v == 1 ? I("cortafuegos", "Cortafuegos de Windows", "ok", "Encendido en la red de casa.")
                        : I("cortafuegos", "Cortafuegos de Windows", "aviso", "Apagado en la red de casa (perfil Privado).");
                }
            });

            add(() =>
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"))
                {
                    object a = k == null ? null : k.GetValue("ActiveHoursStart"), b = k == null ? null : k.GetValue("ActiveHoursEnd");
                    if (!(a is int) || !(b is int)) return I("windows", "Reinicios de Windows Update", "aviso", "Sin horas activas: Windows puede reiniciar el PC a cualquier hora. Ponlas de 12:00 a 06:00 (Configuración, Windows Update, Opciones avanzadas).");
                    int from = (int)a, to = (int)b;
                    Func<int, bool> active = h => from < to ? h >= from && h < to : h >= from || h < to;
                    bool evenings = new[] { 18, 19, 20, 21, 22, 23, 0 }.All(active);
                    string d = "No reinicia el PC de " + from.ToString("00") + ":00 a " + to.ToString("00") + ":00.";
                    return evenings ? I("windows", "Reinicios de Windows Update", "ok", d)
                        : I("windows", "Reinicios de Windows Update", "aviso", d + " Puede reiniciarlo mientras jugáis: ponlas de 12:00 a 06:00.");
                }
            });

            add(() => s.AutoStartServer ? I("arranque", "Arrancar con Windows", "ok", "Si el PC se reinicia, el server vuelve solo.")
                : I("arranque", "Arrancar con Windows", "aviso", "Si el PC se reinicia, el server no vuelve solo: marca \"Arrancar con Windows\" en la pestaña Servidor."));

            add(() =>
            {
                if (!Discord.Configured) return I("discord", "Avisos en Discord", "info", "Sin configurar.");
                return Discord.LastError != null ? I("discord", "Avisos en Discord", "aviso", "El último aviso falló: " + Discord.LastError + ".")
                    : I("discord", "Avisos en Discord", "ok", "Funcionando" + (Discord.LastSent != DateTime.MinValue ? " (último aviso " + F(Discord.LastSent) + ")." : "."));
            });

            add(() =>
            {
                if (!s.AiEnabled) return I("ia", "Asistente IA", "info", "Apagado.");
                var c = AiChat.Status();
                return c.Up && c.HasModel ? I("ia", "Asistente IA", "ok", "Listo con " + AiChat.Model + ".") : I("ia", "Asistente IA", "mal", c.Text);
            });

            add(() =>
            {
                if (r != null) return WorldData.Check(r.Running, r.StartedAt);
                var ps = System.Diagnostics.Process.GetProcessesByName("valheim_server");
                return WorldData.Check(ps.Length > 0, ps.Length > 0 ? ps[0].StartTime : DateTime.MinValue);
            });

            add(() =>
            {
                var h = Maintenance.History().FirstOrDefault();
                string when = "a las " + s.NightlyRestartHour.ToString("00") + ":00";
                if (!s.AutoUpdateMods) return I("mantenimiento", "Mantenimiento diario", "info", "Desactivado: mods y Valheim se actualizan a mano.");
                if (h == null) return I("mantenimiento", "Mantenimiento diario", "ok", "Cada día " + when + ", si hay algo que actualizar y nadie jugando.");
                DateTime d; DateTime.TryParse(h.Fecha, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d);
                return I("mantenimiento", "Mantenimiento diario", h.Nivel == "mal" ? "mal" : h.Nivel == "aviso" ? "aviso" : "ok", "Último " + F(d) + ": " + h.Resumen);
            });

            Last = list;
            LastAt = DateTime.Now;
            return list;
        }

        // Caducidad del certificado HTTPS que da Caddy en este PC (lo renueva solo unos 30 días antes).
        static void CheckCert()
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(IPAddress.Loopback, 443, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000)) { certState = "mal"; certText = "El HTTPS de este PC no responde (¿está Caddy en marcha?)."; return; }
                    c.EndConnect(ar);
                    SslPolicyErrors errs = SslPolicyErrors.None;
                    using (var ssl = new SslStream(c.GetStream(), false, (o, cert, ch, e) => { errs = e; return true; }))
                    {
                        ssl.AuthenticateAsClient(WebPanel.PublicHost, null, System.Security.Authentication.SslProtocols.Tls12, false);
                        var x = new X509Certificate2(ssl.RemoteCertificate);
                        int days = (int)Math.Floor((x.NotAfter - DateTime.Now).TotalDays);
                        if (errs != SslPolicyErrors.None) { certState = "mal"; certText = "El certificado no es válido para " + WebPanel.PublicHost + "."; return; }
                        certState = days < 7 ? "mal" : days < 20 ? "aviso" : "ok";
                        certText = "Certificado válido hasta el " + x.NotAfter.ToString("dd/MM/yyyy") + (days < 20 ? ": debería haberse renovado ya, revisa la pestaña Web." : ".");
                    }
                }
            }
            catch (Exception e) { certState = "mal"; certText = "No he podido comprobar el HTTPS: " + e.GetBaseException().Message; }
        }

        public static string Dot(string st) { return st == "ok" ? "🟢" : st == "aviso" ? "🟡" : st == "mal" ? "🔴" : "⚪"; }

        public static string Summary(List<Item> items)
        {
            int bad = items.Count(i => i.State == "mal"), warn = items.Count(i => i.State == "aviso");
            if (bad == 0 && warn == 0) return "Todo en verde.";
            var p = new List<string>();
            if (bad > 0) p.Add(bad + (bad == 1 ? " problema" : " problemas"));
            if (warn > 0) p.Add(warn + (warn == 1 ? " aviso" : " avisos"));
            return string.Join(" y ", p) + ".";
        }

        // Para /salud en Discord: la última revisión (se hace sola cada 10 minutos).
        public static string ForDiscord()
        {
            var items = Last;
            if (items == null) return "Todavía estoy haciendo la primera revisión: prueba en un minuto.";
            var sb = new StringBuilder("🩺 **Salud del server**: " + Summary(items) + "\n");
            foreach (var i in items) sb.Append(Dot(i.State)).Append(" **").Append(i.Name).Append("**: ").Append(i.Detail).Append('\n');
            int min = (int)(DateTime.Now - LastAt).TotalMinutes;
            sb.Append(min < 1 ? "(revisado ahora mismo)" : "(revisado hace " + min + " min)");
            return sb.ToString();
        }

        // Avisos para Discord cuando algo importante se estropea (dos revisiones seguidas, para no avisar de un parpadeo) o se arregla.
        static readonly string[] AlertKeys = { "disco", "backup", "externa", "https", "ddns", "ia" };
        static readonly Dictionary<string, int> badRuns = new Dictionary<string, int>();

        public static List<string> Alerts(List<Item> items)
        {
            var s = Settings.Current;
            if (s.HealthAlerts == null) s.HealthAlerts = new List<string>();
            var msgs = new List<string>();
            bool save = false;
            foreach (var it in items.Where(i => AlertKeys.Contains(i.Key)))
            {
                int n;
                badRuns.TryGetValue(it.Key, out n);
                n = it.State == "mal" ? n + 1 : 0;
                badRuns[it.Key] = n;
                bool alerted = s.HealthAlerts.Contains(it.Key);
                if (n >= 2 && !alerted) { s.HealthAlerts.Add(it.Key); save = true; msgs.Add("🔴 **" + it.Name + "**: " + it.Detail); }
                else if (it.State == "ok" && alerted) { s.HealthAlerts.Remove(it.Key); save = true; msgs.Add("🟢 **" + it.Name + "** vuelve a estar bien: " + it.Detail); }
            }
            if (save) Settings.Save();
            return msgs;
        }
    }

    // ---------------------------------------------------------------- mantenimiento con red de seguridad

    // Apaga (solo si no hay nadie), hace backup, deja que Steam actualice Valheim, actualiza los mods y prueba que el server arranca.
    // Si un mod nuevo da problemas, vuelve a la versión de antes (y al mundo de antes de la prueba) y lo apunta para no reintentarlo solo.
    public static class Maintenance
    {
        public class Options
        {
            public bool Mods = true;             // actualizar los mods con versión nueva
            public bool RetrySkipped;            // también las versiones que ya fallaron (cuando alguien lo pide a mano)
            public bool Verify;                  // probar el arranque aunque no haya nada que actualizar
            public string Who = "automático";    // quién lo ha pedido (para el registro)
            public bool Manual;                  // pedido por alguien: se avisa en Discord aunque no haya nada que hacer
        }

        public class Context
        {
            public ServerRunner Runner;
            public Action<string> Status = s => { };
            public Func<bool> Playing = () => false;
            public Dictionary<string, PkgInfo> Latest;
        }

        public class Report
        {
            public string Level = "ok";      // ok | aviso | mal
            public string Text = "";
            public bool DidSomething;
            public List<string> Details = new List<string>();
        }

        public class Entry
        {
            public string Fecha { get; set; }
            public string Nivel { get; set; }
            public string Resumen { get; set; }
            public string Quien { get; set; }
            public Entry() { Fecha = ""; Nivel = "ok"; Resumen = ""; Quien = ""; }
        }

        const int TestTimeout = 600;
        public static string HistoryFile { get { return AppInfo.P("logs", "mantenimiento.json"); } }
        static readonly object histLk = new object();

        public static List<Entry> History()
        {
            lock (histLk)
                try { return Json.Load<List<Entry>>(HistoryFile) ?? new List<Entry>(); }
                catch { return new List<Entry>(); }
        }

        static void AddHistory(Report rep, string who)
        {
            lock (histLk)
                try
                {
                    var l = History();
                    l.Insert(0, new Entry { Fecha = DateTime.Now.ToString("o", CultureInfo.InvariantCulture), Nivel = rep.Level, Resumen = rep.Text, Quien = who });
                    Json.Save(HistoryFile, l.Take(30).ToList());
                }
                catch (Exception e) { Log.W("No pude guardar el historial de mantenimiento: {0}", e.Message); }
        }

        class State { public string Zip; public DateTime BackupAt; public bool Touched; public bool WorldRestored; }

        public static Report Run(Options o, Context c)
        {
            var rep = new Report();
            var r = c.Runner;
            var s = Settings.Current;
            if (c.Playing())
            {
                rep.Level = "aviso"; rep.Text = "Hay gente jugando: el mantenimiento espera a que no haya nadie.";
                if (o.Manual) Discord.Send("🛠️ **Mantenimiento**: " + rep.Text);
                return rep;
            }
            bool wasRunning = r.Running;
            var latest = c.Latest ?? new Dictionary<string, PkgInfo>(StringComparer.OrdinalIgnoreCase);
            string origJson = File.Exists(AppInfo.ModpackFile) ? File.ReadAllText(AppInfo.ModpackFile, Encoding.UTF8) : null;
            if (origJson == null) { rep.Level = "mal"; rep.Text = "No encuentro modpack.json."; return rep; }
            var oldPack = Json.Parse<Modpack>(origJson);
            var notes = new List<string>();
            var ups = new List<ModWatch.Upd>();
            if (o.Mods)
            {
                c.Status("Mantenimiento: mirando versiones nuevas de los mods...");
                ModWatch.Refresh(oldPack, latest);
                ups = ModWatch.Candidates(oldPack, latest, o.RetrySkipped, notes);
            }
            var before = Steam.ServerNow();
            bool valheim = before != null && before.UpdatePending;
            bool verify = o.Verify || (!wasRunning && before != null && !string.IsNullOrEmpty(s.VerifiedBuild) && !string.IsNullOrEmpty(before.BuildId) && before.BuildId != s.VerifiedBuild);
            rep.Details.AddRange(notes);
            if (ups.Count == 0 && !valheim && !verify)
            {
                rep.Text = "Nada que actualizar: los mods y Valheim están al día." + (notes.Count > 0 ? " Esperan a mano: " + notes.Count + "." : "");
                if (o.Manual) { Discord.Send("🛠️ **Mantenimiento**: " + rep.Text); AddHistory(rep, o.Who); }
                return rep;
            }

            if (c.Playing())   // mirar Thunderstore lleva un rato: alguien ha podido entrar mientras
            {
                rep.Level = "aviso"; rep.Text = "Alguien ha entrado a jugar: el mantenimiento espera a que no haya nadie.";
                if (o.Manual) Discord.Send("🛠️ **Mantenimiento**: " + rep.Text);
                return rep;
            }
            rep.DidSomething = true;
            var plan = new List<string>();
            if (ups.Count > 0) plan.Add("actualizar " + Join(ups.Select(u => u.Text)));
            if (valheim) plan.Add("instalar la actualización de Valheim");
            if (plan.Count == 0) plan.Add("probar que el server arranca bien con la versión nueva de Valheim");
            string what = string.Join(" y ", plan);
            Log.W("Mantenimiento ({0}): {1}", o.Who, what);
            Discord.Send("🛠️ **Mantenimiento**: voy a " + what + ". Antes hago backup y después pruebo que todo arranca; si algo falla, vuelvo atrás. Tardo unos minutos.");

            var baseline = LogCheck.FromFile(LogCheck.LogFile);
            baseline.UnionWith(LogCheck.Known());
            var st = new State();
            var kept = new List<ModWatch.Upd>();
            var failed = new List<ModWatch.Upd>();
            ServerTest.Result t = null;
            bool publishedOk = true;
            string publishErr = null;
            r.NoAutoRestart = true;
            try
            {
                Stop(c, "Mantenimiento: guardando el mundo y apagando el server...");
                c.Status("Mantenimiento: backup del mundo, los ajustes y la lista de mods...");
                st.BackupAt = DateTime.Now;
                st.Zip = Backup.Create("antes-de-actualizar", SafetyItems(), true);
                if (valheim)
                {
                    WaitSteam(c, 20);
                    // El arreglo del mod de cajones se hace contra el Valheim instalado: con uno nuevo, se vuelve a aplicar.
                    if (ups.Count == 0 && oldPack.Mods.Any(m => m.Enabled && !string.IsNullOrEmpty(m.Patch)))
                    {
                        c.Status("Mantenimiento: volviendo a preparar los mods para la versión nueva de Valheim...");
                        Installer.Install(oldPack, AppInfo.Profile, false, null, c.Status);
                    }
                }

                if (ups.Count > 0)
                {
                    t = TryMods(origJson, ups, baseline, c, st);
                    if (Passed(t, ups)) kept = ups;
                    else
                    {
                        var blamed = Blame(t, ups);
                        var rest = blamed.Count > 0 ? ModWatch.Without(ups, blamed, latest) : new List<ModWatch.Upd>();
                        if (rest.Count > 0)
                        {
                            Log.W("Mantenimiento: falla {0}; pruebo sin {1}.", Join(blamed.Select(u => u.Text)), blamed.Count == 1 ? "él" : "ellos");
                            Stop(c, "Mantenimiento: algo ha fallado; pruebo sin el mod que da problemas...");
                            RestoreWorldIfChanged(st);
                            var t2 = TryMods(origJson, rest, baseline, c, st);
                            if (Passed(t2, rest)) { kept = rest; t = t2; }
                        }
                        failed = ups.Where(u => !kept.Contains(u)).ToList();
                    }
                    if (kept.Count == 0)
                    {
                        Stop(c, "Mantenimiento: volviendo a las versiones de antes...");
                        RestoreWorldIfChanged(st);
                        File.WriteAllText(AppInfo.ModpackFile, origJson, new UTF8Encoding(false));
                        Installer.Install(Json.Parse<Modpack>(origJson), AppInfo.Profile, false, null, c.Status);
                        c.Status("Mantenimiento: comprobando que con las versiones de antes arranca bien...");
                        t = ServerTest.Run(r, baseline, TestTimeout, c.Status);
                    }
                    Remember(kept, failed);
                }
                else t = ServerTest.Run(r, baseline, TestTimeout, c.Status);

                bool up = t.Loaded && r.Running;
                if (!up)
                {
                    if (r.Running) Stop(c, "Mantenimiento: el mundo no carga; apago el server para no estropear nada...");
                    RestoreWorldIfChanged(st);
                }
                else
                {
                    var now = Steam.ServerNow();
                    if (now != null && !string.IsNullOrEmpty(now.BuildId)) s.VerifiedBuild = now.BuildId;
                    Settings.Save();
                    var known = new HashSet<string>(baseline);
                    known.UnionWith(t.NewSigs.Where(sig => !t.NewErrors.Any(l => LogCheck.Signature(l) == sig && (LogCheck.FromLoader(l) || LogCheck.Culprit(l, kept.Select(u => u.Mod)) != null))));
                    LogCheck.SaveKnown(known);
                }

                if (kept.Count > 0)
                {
                    c.Status("Mantenimiento: publicando la lista de mods para tus amigos...");
                    try { Publish.Pack(true); }
                    catch (Exception e) { publishedOk = false; publishErr = e.Message; Log.W("ERROR al publicar la lista de mods: {0}", e.Message); }
                }

                // Al terminar, el server queda como estaba: encendido si lo estaba y apagado si no.
                if (up && !wasRunning) Stop(c, "Mantenimiento: apagando el server (estaba apagado)...");

                var after = Steam.ServerNow();
                bool valheimDone = valheim && after != null && !after.UpdatePending;
                // Con un Valheim nuevo, un mod que no se ha tocado puede empezar a fallar aunque el server arranque.
                var broken = !up || !valheimDone ? new List<ModEntry>() :
                    t.NewErrors.Select(l => LogCheck.Culprit(l, oldPack.Mods.Where(m => m.Enabled && m.ForServer()))).Where(m => m != null && !kept.Any(u => u.Mod.Id == m.Id)).Distinct().ToList();
                rep.Level = !up ? "mal" : failed.Count > 0 || !publishedOk || (valheim && !valheimDone) || broken.Count > 0 ? "aviso" : "ok";
                var sb = new StringBuilder();
                if (kept.Count > 0) sb.Append("Actualizados " + Join(kept.Select(u => u.Text)) + ". ");
                if (failed.Count > 0) sb.Append(Join(failed.Select(u => ModWatch.Nice(u.Mod) + " " + u.To)) + (failed.Count == 1 ? " no arranca bien con vuestro server: se queda en la versión de antes. " : " no arrancan bien con vuestro server: se quedan en la versión de antes. "));
                if (valheimDone) sb.Append("Valheim actualizado. ");
                else if (valheim) sb.Append("Steam no ha terminado de actualizar Valheim: se reintenta en el próximo mantenimiento. ");
                if (broken.Count > 0) sb.Append("Ojo: con la versión nueva de Valheim, " + Join(broken.Select(ModWatch.Nice)) + (broken.Count == 1 ? " da" : " dan") + " errores nuevos; puede que algo no funcione hasta que saquen versión nueva. ");
                if (up) sb.Append(wasRunning ? "El server arranca bien y ya está encendido." : "El server arranca bien; sigue apagado, como estaba.");
                else sb.Append("El server no llega a cargar el mundo y lo dejo apagado para no estropear nada. Hay que mirarlo (lo vuelvo a intentar en el próximo mantenimiento).");
                if (st.WorldRestored) sb.Append(" El mundo ha vuelto a como estaba antes de la prueba, sin perder nada.");
                if (kept.Count > 0) sb.Append(publishedOk ? " Vuestros launchers bajan los mods nuevos solos al abrirlos." : " No he podido publicar la lista de mods en GitHub (" + publishErr + "): tus amigos no los verán hasta que se publique.");
                rep.Text = sb.ToString().Trim();
                var shown = t.NewErrors.Where(l => up ? (LogCheck.FromLoader(l) || LogCheck.Culprit(l, ups.Select(u => u.Mod)) != null || LogCheck.Culprit(l, broken) != null) : true).Take(3).ToList();
                if (shown.Count > 0 && (failed.Count > 0 || !up || broken.Count > 0)) rep.Details.AddRange(shown);
                else if (up && t.NewErrors.Count > 0) rep.Details.Add(t.NewErrors.Count + " errores nuevos en el registro que no parecen de ningún mod actualizado (el server funciona).");
            }
            catch (Exception e)
            {
                Log.W("ERROR: mantenimiento: {0}", e);
                rep.Level = "mal";
                try
                {
                    if (r.Running) r.Stop(true);
                    if (st.Touched)
                    {
                        File.WriteAllText(AppInfo.ModpackFile, origJson, new UTF8Encoding(false));
                        Installer.Install(Json.Parse<Modpack>(origJson), AppInfo.Profile, false, null, c.Status);
                    }
                    RestoreWorldIfChanged(st);
                    if (wasRunning) r.Start(false);
                    rep.Text = "El mantenimiento ha fallado (" + e.GetBaseException().Message + ") y he dejado todo como estaba.";
                }
                catch (Exception e2)
                {
                    Log.W("ERROR: no pude dejarlo como estaba: {0}", e2);
                    rep.Text = "El mantenimiento ha fallado (" + e.GetBaseException().Message + ") y no he podido dejarlo como estaba (" + e2.GetBaseException().Message + "). Hay que mirarlo: el backup de antes es " + (st.Zip != null ? Path.GetFileName(st.Zip) : "el último") + ".";
                }
            }
            finally { r.NoAutoRestart = false; }

            AddHistory(rep, o.Who);
            Log.W("Mantenimiento terminado: {0}", rep.Text);
            string icon = rep.Level == "ok" ? "✅" : rep.Level == "aviso" ? "⚠️" : "🔴";
            string msg = icon + " **Mantenimiento terminado**: " + rep.Text;
            var errs = rep.Details.Where(d => d.StartsWith("[")).Take(3).ToList();
            if (errs.Count > 0) msg += "\n```\n" + string.Join("\n", errs.Select(l => l.Length > 180 ? l.Substring(0, 180) + "…" : l)).Replace("```", "'''") + "\n```";
            Discord.Send(msg);
            return rep;
        }

        static string Join(IEnumerable<string> items)
        {
            var l = items.ToList();
            if (l.Count <= 1) return l.FirstOrDefault() ?? "";
            return string.Join(", ", l.Take(l.Count - 1)) + " y " + l.Last();
        }

        static void Stop(Context c, string text)
        {
            if (!c.Runner.Running) return;
            c.Status(text);
            c.Runner.Stop(true);
            Thread.Sleep(3000);
        }

        // Instala el modpack de antes con estas versiones nuevas y prueba el arranque.
        static ServerTest.Result TryMods(string origJson, List<ModWatch.Upd> ups, HashSet<string> baseline, Context c, State st)
        {
            Stop(c, "Mantenimiento: apagando el server...");
            var p = Json.Parse<Modpack>(origJson);
            foreach (var u in ups) { var m = p.Find(u.Mod.Id); if (m != null) m.Version = u.To; }
            p.Revision++;
            p.LauncherVersion = AppInfo.Version;
            st.Touched = true;
            p.Save();
            c.Status("Mantenimiento: instalando " + Join(ups.Select(u => u.Text)) + "...");
            Installer.Install(p, AppInfo.Profile, false, null, c.Status);
            return ServerTest.Run(c.Runner, baseline, TestTimeout, c.Status);
        }

        // Sale bien si el mundo carga y ningún error nuevo es del cargador de mods o de uno de los mods actualizados.
        static bool Passed(ServerTest.Result t, List<ModWatch.Upd> ups)
        {
            if (!t.Loaded) return false;
            var mods = ups.Select(u => u.Mod).ToList();
            return !t.NewErrors.Any(l => LogCheck.FromLoader(l) || LogCheck.Culprit(l, mods) != null);
        }

        static List<ModWatch.Upd> Blame(ServerTest.Result t, List<ModWatch.Upd> ups)
        {
            var mods = ups.Select(u => u.Mod).ToList();
            var bad = t.NewErrors.Select(l => LogCheck.Culprit(l, mods)).Where(m => m != null).Distinct().ToList();
            return ups.Where(u => bad.Contains(u.Mod)).ToList();
        }

        // Las versiones que fallan no se reintentan solas; las que van bien se quitan de esa lista.
        static void Remember(List<ModWatch.Upd> kept, List<ModWatch.Upd> failed)
        {
            var s = Settings.Current;
            if (s.SkippedUpdates == null) s.SkippedUpdates = new List<string>();
            foreach (var u in kept) s.SkippedUpdates.RemoveAll(x => x.StartsWith(u.Mod.Id + "@", StringComparison.OrdinalIgnoreCase));
            foreach (var u in failed) { string k = u.Mod.Id + "@" + u.To; if (!s.SkippedUpdates.Contains(k, StringComparer.OrdinalIgnoreCase)) s.SkippedUpdates.Add(k); }
            Settings.Save();
        }

        static IEnumerable<Backup.Item> SafetyItems()
        {
            var list = Backup.WorldItems().ToList();
            string cfg = Path.Combine(AppInfo.Profile, @"BepInEx\config");
            if (Directory.Exists(cfg)) list.Add(new Backup.Item(cfg, "config"));
            if (File.Exists(AppInfo.ModpackFile)) list.Add(new Backup.Item(AppInfo.ModpackFile, "modpack"));
            return list;
        }

        // Steam actualiza el server de Valheim solo, en cuanto está apagado. Se espera a que termine (como mucho maxMin minutos).
        static void WaitSteam(Context c, int maxMin)
        {
            if (System.Diagnostics.Process.GetProcessesByName("steam").Length == 0) { Log.W("Steam no está abierto: no puede actualizar Valheim."); return; }
            var t0 = DateTime.Now;
            while ((DateTime.Now - t0).TotalMinutes < maxMin)
            {
                var st = Steam.ServerNow();
                if (st == null || !st.UpdatePending) { Log.W("Valheim está al día (versión {0}).", st != null ? st.BuildId : "?"); return; }
                c.Status("Mantenimiento: esperando a que Steam actualice Valheim (" + (int)(DateTime.Now - t0).TotalMinutes + " min)...");
                Thread.Sleep(20000);
            }
            Log.W("Steam no ha terminado de actualizar Valheim en {0} minutos.", maxMin);
        }

        static bool WorldFile(string rel, string world)
        {
            return rel.StartsWith(world, StringComparison.OrdinalIgnoreCase) && !rel.Contains("_backup") && !rel.EndsWith(".old");
        }

        static bool WorldChangedSince(DateTime t)
        {
            string wl = Path.Combine(AppInfo.SavesDir, "worlds_local"), world = Settings.Current.WorldName;
            if (!Directory.Exists(wl)) return false;
            foreach (var f in Directory.GetFiles(wl, "*", SearchOption.AllDirectories))
                if (WorldFile(f.Substring(wl.Length + 1), world) && File.GetLastWriteTime(f) > t) return true;
            return false;
        }

        // Si la prueba ha guardado el mundo, vuelve al del backup. Lo de ahora no se borra: se aparta a saves\descartados\<fecha>.
        static void RestoreWorldIfChanged(State st)
        {
            if (st.Zip == null || !WorldChangedSince(st.BackupAt)) return;
            string wl = Path.Combine(AppInfo.SavesDir, "worlds_local"), world = Settings.Current.WorldName;
            string aside = Path.Combine(AppInfo.SavesDir, "descartados", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(aside);
            foreach (var p in Directory.GetFileSystemEntries(wl))
            {
                string name = Path.GetFileName(p);
                if (!WorldFile(name, world)) continue;
                if (Directory.Exists(p)) Directory.Move(p, Path.Combine(aside, name)); else File.Move(p, Path.Combine(aside, name));
            }
            string root = Path.GetFullPath(wl).TrimEnd('\\') + "\\";
            using (var z = ZipFile.OpenRead(st.Zip))
                foreach (var e in z.Entries)
                {
                    const string pfx = "saves_worlds_local/";
                    if (e.Name == "" || !e.FullName.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) continue;
                    string dest = Path.GetFullPath(Path.Combine(wl, e.FullName.Substring(pfx.Length).Replace('/', '\\')));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            st.WorldRestored = true;
            Log.W("Mundo devuelto al backup {0}; el de la prueba queda en {1}.", Path.GetFileName(st.Zip), aside);
        }
    }
}
#endif
