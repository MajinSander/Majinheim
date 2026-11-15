// Soporte para amigos (2.4): el botón "Tengo un problema" del launcher de jugador, el estado del server antes de jugar
// y lo que hace el server con los problemas que llegan (guardarlos, un primer diagnóstico y avisar a Majin).
// Lo que manda el launcher no lleva datos personales: se quitan el usuario de Windows, el nombre del PC, las IPs,
// los IDs de Steam y los correos. Llega con límite de tamaño y solo lo ve el administrador.
// Compilado con el csc de .NET Framework 4.8 (C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
#if SERVER
using System.Security.Cryptography;
using System.Threading;
#else
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
#endif

namespace Majinheim
{
    // Informe de "Tengo un problema". Lo arma el launcher de jugador; el server le pone Id, Fecha, Pistas e Ia y lo guarda.
    public class SupportReport
    {
        public string Id { get; set; }
        public string Fecha { get; set; }
        public string Nombre { get; set; }           // cómo quiere que le llame Majin (lo escribe él)
        public string Texto { get; set; }            // qué le pasa, con sus palabras
        public string Launcher { get; set; }         // versión del launcher de jugador
        public string Sistema { get; set; }          // versión de Windows
        public string Equipo { get; set; }           // RAM y tarjeta gráfica
        public int RamGb { get; set; }
        public int DiscoLibreGb { get; set; }        // en el disco del launcher (-1: no se sabe)
        public bool Juego { get; set; }              // encuentra Valheim
        public bool Cargador { get; set; }           // winhttp.dll (el cargador de mods) en la carpeta del juego
        public string JuegoVersion { get; set; }     // la de la última vez que se abrió Valheim (Player.log)
        public string JuegoUltimaVez { get; set; }   // cuándo se escribió Player.log por última vez
        public string ModsUltimaVez { get; set; }    // cuándo se escribió el registro de BepInEx por última vez
        public string AnteriorFin { get; set; }      // cuándo acabó la partida anterior (Player-prev.log)
        public int Revision { get; set; }            // lista de mods descargada
        public int Instalada { get; set; }           // lista de mods instalada
        public bool ModsAlDia { get; set; }
        public bool EntrarDirecto { get; set; }
        public string RegistroMods { get; set; }     // BepInEx: cabecera, mods cargados, errores y avisos
        public string RegistroJuego { get; set; }    // Player.log: lo importante y el final
        public string RegistroLauncher { get; set; } // el final de launcher.log
        public List<string> Pistas { get; set; }     // diagnóstico por reglas
        public string Ia { get; set; }               // primer diagnóstico de la IA (si está)
        public SupportReport()
        {
            Id = ""; Fecha = ""; Nombre = ""; Texto = ""; Launcher = ""; Sistema = ""; Equipo = ""; JuegoVersion = ""; JuegoUltimaVez = ""; ModsUltimaVez = ""; AnteriorFin = "";
            RegistroMods = ""; RegistroJuego = ""; RegistroLauncher = ""; Pistas = new List<string>(); DiscoLibreGb = -1;
        }
    }

    // Lo que se sabe del server en este momento (en el launcher de jugador, de la web; en el server, de primera mano).
    public class ServerFacts
    {
        public bool Known;                      // se ha podido mirar
        public string State = "";               // "en marcha", "arrancando" o "apagado"
        public string Version = "";             // versión de Valheim del server
        public int Revision;                    // lista de mods del server
        public bool OnDemand;                   // cualquiera lo enciende con /encender
        public List<string> Players = new List<string>();
        public DateTime NextMeetup = DateTime.MinValue;
        public int MeetupPeople;

        // Del JSON de /api/estado (vale tanto el que llega por la web como el de ServerWatch.Snapshot).
        public static ServerFacts FromSnapshot(Dictionary<string, object> o)
        {
            var f = new ServerFacts();
            if (o == null) return f;
            var srv = Get(o, "servidor") as Dictionary<string, object>;
            if (srv == null) return f;
            f.Known = true;
            f.State = Convert.ToString(Get(srv, "estado") ?? "", CultureInfo.InvariantCulture);
            f.Version = Convert.ToString(Get(srv, "version") ?? "", CultureInfo.InvariantCulture);
            var mods = Get(o, "mods") as Dictionary<string, object>;
            if (mods != null) f.Revision = Int(Get(mods, "revision"));
            var od = Get(o, "bajoDemanda");
            f.OnDemand = od is bool && (bool)od;
            var js = Get(o, "jugadores") as IEnumerable;
            if (js != null)
                foreach (var j in js)
                {
                    var d = j as Dictionary<string, object>;
                    string n = d != null ? Get(d, "nombre") as string : null;
                    if (!string.IsNullOrEmpty(n)) f.Players.Add(n);
                }
            var q = Get(o, "proximaQuedada") as Dictionary<string, object>;
            DateTime at;
            if (q != null && DateTime.TryParse(Get(q, "cuando") as string ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at))
            {
                f.NextMeetup = at;
                f.MeetupPeople = Int(Get(q, "van"));
            }
            return f;
        }

        static object Get(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        static int Int(object v) { try { return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; } }
    }

    public static class Support
    {
        public const int MaxName = 40, MaxText = 600, MaxLog = 20000, MaxLauncherLog = 6000;
        public const int MaxBody = 64 * 1024;          // lo que acepta el server en /api/problema
        public const string ApiPath = "/api/problema";

        // ------------------------------------------------------------ diagnóstico por reglas (lo usan el launcher de jugador y el server)

        static readonly Regex rxBepLine = new Regex(@"^\[(Error|Fatal|Warning)\s*:\s*([^\]]*)\]\s*(.*)$", RegexOptions.Multiline);
        internal static readonly Regex rxCouldNotLoad = new Regex(@"(?:Could not load|Error loading|Skipping) \[([^\]]+)\]");
        static readonly Regex rxModMismatch = new Regex(@"(?i)version mismatch|incompatible (?:mod|version|with the server)|not compatible with the server|must have the same version|missing on the (?:server|client)|not installed on the server|compatibility check failed");
        static readonly Regex rxMemory = new Regex(@"(?i)out of memory|could not allocate memory|failed to allocate");

        public static string CleanVersion(string v)
        {
            var m = Regex.Match(v ?? "", @"\d+(?:\.\d+)+");
            return m.Success ? m.Value : "";
        }

        static bool Has(string text, string what) { return (text ?? "").IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0; }

        static DateTime When(string iso)
        {
            DateTime d;
            return DateTime.TryParse(iso ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d) ? d : DateTime.MinValue;
        }

        // Frases sencillas, de lo más importante a lo menos. f puede ser null (no se sabe nada del server).
        public static List<string> Diagnose(SupportReport r, ServerFacts f)
        {
            var p = new List<string>();
            string mods = r.RegistroMods ?? "", game = r.RegistroJuego ?? "";
            bool known = f != null && f.Known;
            bool on = known && f.State == "en marcha";

            if (!r.Juego) p.Add("No encuentro Valheim en este PC. Si lo tienes en otra carpeta, indícala en Ajustes → «Carpeta de Valheim».");
            if (known && f.State == "apagado")
                p.Add("El server está apagado ahora mismo. " + (f.OnDemand ? "Cualquiera puede encenderlo escribiendo /encender en el Discord (tarda un par de minutos)." : "Avisa a Majin para que lo encienda."));
            else if (known && f.State == "arrancando")
                p.Add("El server está arrancando: espera un par de minutos y vuelve a probar.");

            string mine = CleanVersion(r.JuegoVersion), theirs = known ? CleanVersion(f.Version) : "";
            if (mine != "" && theirs != "" && mine != theirs)
                p.Add(Ver.Compare(mine, theirs) < 0
                    ? "Tu Valheim es la versión " + mine + " y el del server la " + theirs + ": abre Steam y deja que actualice Valheim (si no lo hace, cierra Steam del todo y vuelve a abrirlo)."
                    : "Tu Valheim (" + mine + ") es más nuevo que el del server (" + theirs + "): hay que actualizar el server. Majin lo verá en este informe.");
            else if (Has(game, "ErrorVersion"))
                p.Add("Valheim dice que tu versión no es la del server: actualiza Valheim en Steam. Si ya estaba al día, el que va por detrás es el server y Majin lo verá en este informe.");

            if (Has(game, "ErrorPassword")) p.Add("El server dijo que la contraseña no es correcta. Escríbela tal cual te la dio Majin (las mayúsculas y los espacios cuentan); si no la tienes, pídesela.");
            if (Has(game, "ErrorBanned")) p.Add("El server no te deja entrar porque tu cuenta está bloqueada: habla con Majin.");
            if (Has(game, "ErrorFull")) p.Add("El server estaba lleno cuando intentaste entrar.");
            if (Has(game, "ErrorConnectFailed") || Has(game, "ErrorDisconnected"))
                p.Add("Tu juego no llegó a conectar con el server o se cortó la conexión. " + (on
                    ? "El server está encendido: cierra Valheim y Steam, y vuelve a entrar con JUGAR. Si sigue, reinicia el router."
                    : "Mira primero que el server esté encendido (sale arriba en el launcher)."));

            if (known && f.Revision > 0 && r.Revision > 0 && r.Revision < f.Revision)
                p.Add("Tu lista de mods es la revisión " + r.Revision + " y el server ya va por la " + f.Revision + ": pulsa «Buscar actualizaciones». Si sigue igual, Majin tiene que publicar la lista nueva.");
            if (r.Juego && !r.ModsAlDia)
                p.Add("Te faltan mods por instalar o actualizar: pulsa «Buscar actualizaciones» y luego JUGAR. Si no se arregla, en Ajustes → «Reinstalar mods» (tus personajes no se tocan).");
            if (rxModMismatch.IsMatch(mods) || rxModMismatch.IsMatch(game))
                p.Add("Algún mod no tiene la misma versión que en el server: pulsa «Buscar actualizaciones». Si sigue, en Ajustes → «Reinstalar mods».");

            var broken = rxCouldNotLoad.Matches(mods).Cast<Match>().Select(m => Regex.Replace(m.Groups[1].Value, @"\s+[\d.]+$", "")).Distinct().Take(3).ToList();
            if (broken.Count > 0)
                p.Add((broken.Count == 1 ? "El mod «" + broken[0] + "» no se ha podido cargar" : "Hay mods que no se han podido cargar (" + string.Join(", ", broken) + ")") +
                    ": en Ajustes → «Reinstalar mods». Si sigue, Majin tiene que revisarlo.");

            if (r.Juego)
            {
                // Con mods, BepInEx reescribe su registro al arrancar el juego: si es de antes de que acabara la partida anterior,
                // la última vez se abrió sin mods. (Se compara con el final de la anterior porque la hora de inicio no es fiable.)
                DateTime g = When(r.JuegoUltimaVez), m = When(r.ModsUltimaVez), prevEnd = When(r.AnteriorFin);
                if (!r.Cargador)
                    p.Add("Todavía no está el cargador de mods en la carpeta de Valheim: abre el juego con el botón JUGAR de este launcher (desde Steam se abre sin mods).");
                else if ((g != DateTime.MinValue && m == DateTime.MinValue) || (prevEnd != DateTime.MinValue && m != DateTime.MinValue && m < prevEnd))
                    p.Add("La última vez abriste Valheim sin mods (desde Steam o con «Jugar sin mods»). Para el server, ábrelo siempre con el botón JUGAR de este launcher.");
            }

            // Errores de algún mod en concreto (los de Unity y BepInEx son genéricos y no dicen de quién es la culpa).
            foreach (var src in NoisyMods(mods))
                p.Add("El mod «" + src + "» ha dado errores. Si algo no funciona en el juego, Majin lo verá en este informe.");

            if (Has(game, "Crash!!!"))
            {
                string mem = rxMemory.IsMatch(game) ? " Parece que se quedó sin memoria: cierra otros programas (el navegador, sobre todo) antes de jugar." : "";
                p.Add("Valheim se cerró de golpe." + (mem != "" ? mem : " Si te pasa siempre en el mismo sitio, díselo a Majin: ya tiene los registros."));
            }
            else if (rxMemory.IsMatch(game) || rxMemory.IsMatch(mods))
                p.Add("Valheim se quedó sin memoria: cierra otros programas (el navegador, sobre todo) antes de jugar.");
            if (r.RamGb > 0 && r.RamGb < 8)
                p.Add("Tu PC tiene " + r.RamGb + " GB de RAM: con mods Valheim va justo. Cierra todo lo demás mientras juegas.");
            if (r.DiscoLibreGb >= 0 && r.DiscoLibreGb < 3)
                p.Add("Te queda muy poco espacio en el disco (" + r.DiscoLibreGb + " GB): libera un poco, que Valheim y los mods lo necesitan.");

            if (p.Count == 0) p.Add("No veo nada raro en los registros.");
            return p;
        }

        // Los mods con 2 errores o más (contando los repetidos, que en el informe salen una vez con "(xN)").
        static readonly Regex rxRepeat = new Regex(@"\(x(\d+)\)\s*$");
        static List<string> NoisyMods(string log)
        {
            var count = new Dictionary<string, int>();
            string src = null; int n = 0;
            Action flush = () => { if (src != null) { int c; count.TryGetValue(src, out c); count[src] = c + Math.Max(1, n); } src = null; n = 0; };
            foreach (var line in (log ?? "").Split('\n'))
            {
                if (line.StartsWith("["))
                {
                    flush();
                    var m = rxBepLine.Match(line);
                    if (m.Success && m.Groups[1].Value != "Warning")
                    {
                        string s = m.Groups[2].Value.Trim();
                        if (s != "" && s != "Unity Log" && s != "BepInEx" && !s.StartsWith("Preloader")) src = s;
                    }
                }
                if (src != null) { var r = rxRepeat.Match(line); if (r.Success) n = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture); }
            }
            flush();
            return count.Where(kv => kv.Value >= 2).OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToList();
        }

        // ------------------------------------------------------------ quitar datos personales

        static readonly Regex rxUserPath = new Regex(@"(?i)([a-z]:[\\/]+(?:users|usuarios|documents and settings)[\\/]+)[^\\/\r\n""'<>|:*?]+");
        static readonly Regex rxSteamId = new Regex(@"\b7656119\d{10}\b");
        static readonly Regex rxIp4 = new Regex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b");
        static readonly Regex rxIp6Full = new Regex(@"(?i)(?<![\w.:])(?:[0-9a-f]{1,4}:){3,7}[0-9a-f]{1,4}(?![\w.:])");
        static readonly Regex rxIp6Short = new Regex(@"(?i)(?<![\w.:])(?:[0-9a-f]{1,4}:)+:(?:[0-9a-f]{1,4}(?::[0-9a-f]{1,4})*)?(?![\w.:])");
        static readonly Regex rxMail = new Regex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+");
        static readonly Regex rxAccount = new Regex(@"(?i)\b(persona(?:\s*name)?|steam\s*name|user\s*name|username|account\s*name)(\s*[:=]?\s+)\S+");
        static readonly Regex rxLongHex = new Regex(@"\b[0-9A-Fa-f]{16,}\b");
        static readonly Regex rxControl = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]");

        // local = en el PC del jugador: también quita su usuario de Windows y el nombre del PC donde salgan.
        public static string Redact(string s, bool local)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = rxControl.Replace(s, "");
            s = rxUserPath.Replace(s, "$1<usuario>");
            s = rxMail.Replace(s, "<correo>");
            s = rxAccount.Replace(s, "$1$2<oculto>");
            if (local)
            {
                foreach (var name in new[] { Environment.UserName, Environment.MachineName })
                    if (!string.IsNullOrEmpty(name) && name.Length >= 3)
                        s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", "<usuario>", RegexOptions.IgnoreCase);
            }
            s = rxSteamId.Replace(s, "<steamid>");
            // Las versiones de 4 números ("[BepInEx.Preloader 5.4.22.0]", "v1.2.3.4") se parecen a una IP: esas se dejan.
            s = rxIp4.Replace(s, m => m.Value == "127.0.0.1" || m.Value == "0.0.0.0" || Version4(s, m) ? m.Value : "<ip>");
            s = rxIp6Full.Replace(s, "<ip>");
            s = rxIp6Short.Replace(s, "<ip>");
            s = rxLongHex.Replace(s, "<id>");
            return s;
        }

        static bool Version4(string s, Match m)
        {
            int end = m.Index + m.Length;
            return (end < s.Length && s[end] == ']') || (m.Index > 0 && (s[m.Index - 1] == 'v' || s[m.Index - 1] == 'V'));
        }

        public static string OneLine(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"[\x00-\x1f\x7f]+", " ").Trim();
            return s.Length > max ? s.Substring(0, max).TrimEnd() + "…" : s;
        }

        public static string Text(string s, int max)
        {
            s = rxControl.Replace((s ?? "").Replace("\r\n", "\n").Replace('\r', '\n'), "").Trim();
            return s.Length > max ? s.Substring(0, max).TrimEnd() + "…" : s;
        }

        // Recorta un registro por el principio (lo último suele ser lo que importa).
        public static string ClipLog(string s, int max)
        {
            s = s ?? "";
            if (s.Length <= max) return s;
            string cut = s.Substring(s.Length - max);
            int nl = cut.IndexOf('\n');
            if (nl >= 0 && nl < 400) cut = cut.Substring(nl + 1);
            return "[…]\n" + cut;
        }

        // ------------------------------------------------------------ el informe en texto (para "Ver lo que se manda", el escritorio y el server)

        public static string Plain(SupportReport r)
        {
            var sb = new StringBuilder();
            sb.Append("Majinheim · Tengo un problema\r\n");
            if (r.Fecha != "") sb.Append("Fecha: ").Append(r.Fecha).Append("\r\n");
            sb.Append("Nombre: ").Append(r.Nombre == "" ? "(sin nombre)" : r.Nombre).Append("\r\n");
            sb.Append("Qué ha pasado: ").Append((r.Texto ?? "").Replace("\n", "\r\n")).Append("\r\n\r\n");
            sb.Append("Launcher: ").Append(r.Launcher).Append(" · ").Append(r.Sistema).Append("\r\n");
            sb.Append("Equipo: ").Append(r.Equipo).Append(r.DiscoLibreGb >= 0 ? " · " + r.DiscoLibreGb + " GB libres en el disco" : "").Append("\r\n");
            sb.Append("Valheim: ").Append(!r.Juego ? "no lo encuentro" : (r.JuegoVersion != "" ? "versión " + CleanVersion(r.JuegoVersion) : "versión desconocida") +
                (r.JuegoUltimaVez != "" ? " · abierto por última vez " + r.JuegoUltimaVez : "") + " · cargador de mods " + (r.Cargador ? "puesto" : "sin poner")).Append("\r\n");
            sb.Append("Mods: lista revisión ").Append(r.Revision).Append(", instalada ").Append(r.Instalada).Append(r.ModsAlDia ? ", al día" : ", sin terminar de instalar")
              .Append(r.ModsUltimaVez != "" ? " · cargados por última vez " + r.ModsUltimaVez : " · nunca se han cargado").Append(" · entrar directo: ").Append(r.EntrarDirecto ? "sí" : "no").Append("\r\n");
            if (r.Pistas != null && r.Pistas.Count > 0)
            {
                sb.Append("\r\nLo que he visto:\r\n");
                foreach (var x in r.Pistas) sb.Append("• ").Append(x).Append("\r\n");
            }
            if (!string.IsNullOrEmpty(r.Ia)) sb.Append("\r\nEl asistente dice:\r\n").Append(r.Ia.Replace("\n", "\r\n")).Append("\r\n");
            sb.Append("\r\n== Registro de los mods (BepInEx: lo importante) ==\r\n").Append(Nl(r.RegistroMods, "(no hay)"));
            sb.Append("\r\n\r\n== Registro de Valheim (Player.log: lo importante y el final) ==\r\n").Append(Nl(r.RegistroJuego, "(no hay)"));
            sb.Append("\r\n\r\n== Registro del launcher (el final) ==\r\n").Append(Nl(r.RegistroLauncher, "(no hay)")).Append("\r\n");
            return sb.ToString();
        }

        static string Nl(string s, string empty) { return string.IsNullOrEmpty(s) ? empty : s.Replace("\r\n", "\n").Replace("\n", "\r\n"); }
    }

#if !SERVER
    // ================================================================ launcher de jugador

    public static class SupportClient
    {
        public static string LastGoodBase;   // la dirección de la web que respondió la última vez (la pública o la de casa)

        // Por dónde llegar a la web del server: la dirección pública (HTTPS) y, si juega en casa del server, la de la red de casa.
        public static List<string> Bases(Modpack pack)
        {
            var l = new List<string>();
            Uri u;
            string w = pack != null ? (pack.WebUrl ?? "").Trim() : "";
            if (w != "" && Uri.TryCreate(w, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps) l.Add(u.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
            string lan = (Settings.Current.LanHost ?? "").Trim();
            if (lan != "" && Regex.IsMatch(lan, @"^[\w.\-]+$")) l.Add("http://" + lan + ":8456/");
            if (LastGoodBase != null && l.Remove(LastGoodBase)) l.Insert(0, LastGoodBase);
            return l;
        }

        // Estado del server por la web (como mucho unos segundos). Known = false si no responde.
        public static ServerFacts FetchStatus(Modpack pack)
        {
            foreach (var b in Bases(pack))
            {
                try
                {
                    var f = ServerFacts.FromSnapshot(Json.ParseObj(Http.GetString(b + "api/estado", 8000)));
                    if (f.Known) { LastGoodBase = b; return f; }
                }
                catch { }
            }
            return new ServerFacts();
        }

        // ------------------------------------------------------------ armar el informe

        public static SupportReport Build(string name, string text, Modpack pack, ServerFacts facts)
        {
            var r = new SupportReport();
            r.Nombre = Support.OneLine(name, Support.MaxName);
            r.Texto = Support.Text(text, Support.MaxText);
            r.Launcher = AppInfo.Version;
            r.Sistema = OsName();
            r.RamGb = RamGb();
            string gpu = Gpu();
            r.Equipo = (r.RamGb > 0 ? r.RamGb + " GB de RAM" : "RAM desconocida") + (gpu != "" ? " · " + gpu : "");
            r.DiscoLibreGb = FreeGb(AppInfo.Root);
            string game = null;
            try { game = Player.GameDir(); } catch { }
            r.Juego = game != null;
            r.Cargador = game != null && File.Exists(Path.Combine(game, "winhttp.dll"));
            r.Revision = pack != null ? pack.Revision : 0;
            try { r.Instalada = InstalledInfo.Load(AppInfo.Profile).Revision; } catch { }
            try { r.ModsAlDia = pack != null && Installer.UpToDate(pack, AppInfo.Profile, true); } catch { }
            r.EntrarDirecto = Settings.Current.AutoConnect;

            string plog = Path.Combine(AppInfo.OriginalSaves, "Player.log"), prev = Path.Combine(AppInfo.OriginalSaves, "Player-prev.log");
            string blog = Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log");
            r.JuegoUltimaVez = Stamp(plog);
            r.ModsUltimaVez = Stamp(blog);
            r.AnteriorFin = Stamp(prev);
            r.JuegoVersion = GameVersion(plog) ?? GameVersion(prev) ?? "";
            r.RegistroMods = Support.ClipLog(Support.Redact(ModLog(blog), true), Support.MaxLog);
            r.RegistroJuego = Support.ClipLog(Support.Redact(GameLog(plog, prev), true), Support.MaxLog);
            r.RegistroLauncher = Support.ClipLog(Support.Redact(string.Join("\n", Tail(Path.Combine(AppInfo.LogDir, "launcher.log"), 60)), true), Support.MaxLauncherLog);
            r.Pistas = Support.Diagnose(r, facts);
            Fit(r);
            return r;
        }

        // Que el informe quepa en lo que acepta el server (los registros se recortan por el principio).
        static void Fit(SupportReport r)
        {
            for (int i = 0; i < 12 && Encoding.UTF8.GetByteCount(Json.Compact(r)) > Support.MaxBody - 4096; i++)
            {
                if (r.RegistroMods.Length >= r.RegistroJuego.Length) r.RegistroMods = Support.ClipLog(r.RegistroMods, r.RegistroMods.Length * 3 / 4);
                else r.RegistroJuego = Support.ClipLog(r.RegistroJuego, r.RegistroJuego.Length * 3 / 4);
            }
        }

        // Lee un registro aunque el juego lo tenga abierto (como mucho los últimos maxBytes).
        static List<string> ReadLines(string path, int maxBytes)
        {
            var l = new List<string>();
            if (!File.Exists(path)) return l;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bool cut = fs.Length > maxBytes;
                    if (cut) fs.Seek(-maxBytes, SeekOrigin.End);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        string line;
                        if (cut) sr.ReadLine();   // la primera, a medias
                        while ((line = sr.ReadLine()) != null) l.Add(line.TrimEnd());
                    }
                }
            }
            catch { }
            return l;
        }

        static List<string> Tail(string path, int n) { var l = ReadLines(path, 256 * 1024); return l.Skip(Math.Max(0, l.Count - n)).ToList(); }

        static string Stamp(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : ""; }
            catch { return ""; }
        }

        static readonly Regex rxVersion = new Regex(@"Valheim version: ?([^\s(]+)");
        static string GameVersion(string path)
        {
            foreach (var l in ReadLines(path, 2 * 1024 * 1024).Take(4000))
            {
                var m = rxVersion.Match(l);
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        // Quita las repetidas (con cuántas veces salen) y deja como mucho max líneas.
        static List<string> Dedupe(IEnumerable<string> lines, int max)
        {
            var order = new List<string>();
            var count = new Dictionary<string, int>();
            foreach (var l in lines)
            {
                string k = l.Length > 400 ? l.Substring(0, 400) + "…" : l;
                int c;
                if (count.TryGetValue(k, out c)) { count[k] = c + 1; continue; }
                count[k] = 1;
                order.Add(k);
            }
            var res = order.Select(k => count[k] > 1 ? k + "   (x" + count[k] + ")" : k).ToList();
            return res.Count > max ? res.Skip(res.Count - max).ToList() : res;
        }

        static readonly Regex rxBepStart = new Regex(@"^\[(Error|Fatal|Warning|Info|Message|Debug)\s*:");
        static readonly Regex rxBepBad = new Regex(@"^\[(Error|Fatal)\s*:");
        static readonly Regex rxBepWarn = new Regex(@"^\[Warning\s*:");
        static readonly Regex rxInteresting = new Regex(@"(?i)incompatible|mismatch|missing|could not|couldn't|failed|not found|version|exception|out of memory");
        static readonly Regex rxLoading = new Regex(@"Loading \[[^\]]+\]");

        // BepInEx: la cabecera, qué mods se cargaron (con versión), los errores (con un poco de su traza) y los avisos que importan.
        static string ModLog(string path)
        {
            var all = ReadLines(path, 8 * 1024 * 1024);
            if (all.Count == 0) return "";
            var head = all.Take(12).ToList();
            var loaded = all.Where(l => rxLoading.IsMatch(l)).Select(l => rxLoading.Match(l).Value).Distinct().Take(120).ToList();
            var bad = new List<string>();
            for (int i = 12; i < all.Count; i++)
            {
                string l = all[i];
                bool err = rxBepBad.IsMatch(l) || Support.rxCouldNotLoad.IsMatch(l);
                bool warn = !err && rxBepWarn.IsMatch(l) && rxInteresting.IsMatch(l);
                if (!err && !warn) continue;
                var sb = new StringBuilder(l);
                for (int k = 1; k <= 4 && i + k < all.Count && !rxBepStart.IsMatch(all[i + k]); k++) sb.Append("\n    ").Append(all[i + k].Trim());
                bad.Add(sb.ToString());
            }
            var o = new StringBuilder();
            o.Append(string.Join("\n", head));
            if (loaded.Count > 0) o.Append("\n\n-- Mods cargados --\n").Append(string.Join("\n", loaded));
            var b = Dedupe(bad, 120);
            o.Append("\n\n-- Errores y avisos (").Append(b.Count == 0 ? "ninguno" : b.Count.ToString()).Append(") --\n").Append(string.Join("\n", b));
            return o.ToString();
        }

        static readonly Regex rxGameImportant = new Regex(@"(?i)valheim version|exception|crash|error|failed|connect|password|disconnect|out of memory|renderer|version");
        static readonly Regex rxGameBad = new Regex(@"(?i)exception|crash|error|failed|out of memory");

        // Player.log: lo importante y el final de la última partida; de la anterior, solo los errores.
        static string GameLog(string path, string prev)
        {
            var o = new StringBuilder();
            var all = ReadLines(path, 8 * 1024 * 1024);
            if (all.Count > 0)
            {
                var imp = Dedupe(all.Where(l => rxGameImportant.IsMatch(l)), 150);
                o.Append(string.Join("\n", imp));
                o.Append("\n\n-- El final --\n").Append(string.Join("\n", all.Skip(Math.Max(0, all.Count - 40))));
            }
            var before = ReadLines(prev, 4 * 1024 * 1024);
            if (before.Count > 0)
            {
                var bad = Dedupe(before.Where(l => rxGameBad.IsMatch(l)), 40);
                if (bad.Count > 0) o.Append("\n\n-- Partida anterior: errores --\n").Append(string.Join("\n", bad));
            }
            return o.ToString();
        }

        // ------------------------------------------------------------ el PC (sin nada que identifique a nadie)

        static string OsName()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = k != null ? k.GetValue("ProductName") as string : null;
                    string disp = k != null ? (k.GetValue("DisplayVersion") as string ?? k.GetValue("ReleaseId") as string) : null;
                    string build = k != null ? k.GetValue("CurrentBuild") as string : null;
                    int b;
                    if (name != null && int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");   // Windows 11 sigue diciendo "Windows 10" en el registro
                    if (name != null) return name + (disp != null ? " " + disp : "") + (build != null ? " (compilación " + build + ")" : "") + (Environment.Is64BitOperatingSystem ? ", 64 bits" : ", 32 bits");
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        [StructLayout(LayoutKind.Sequential)]
        class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus m);

        static int RamGb()
        {
            try { var m = new MemoryStatus(); return GlobalMemoryStatusEx(m) ? (int)Math.Round(m.TotalPhys / 1073741824.0) : 0; }
            catch { return 0; }
        }

        static string Gpu()
        {
            try
            {
                var names = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string n = Support.OneLine(o["Name"] as string, 80);
                        if (n != "" && !names.Contains(n)) names.Add(n);
                    }
                return string.Join(" + ", names.Take(3));
            }
            catch { return ""; }
        }

        static int FreeGb(string path)
        {
            try { return (int)(new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace / 1073741824L); }
            catch { return -1; }
        }

        // ------------------------------------------------------------ mandarlo

        public class Reply
        {
            public bool Sent;              // ha llegado al server
            public List<string> Pistas;    // las del server (o las de aquí si no ha llegado)
            public string Ia;
            public string Error;           // por qué no ha llegado
            public string SavedTo;         // copia en el escritorio si no ha llegado
        }

        public static Reply Send(SupportReport r, Modpack pack)
        {
            var rep = new Reply { Pistas = r.Pistas };
            string json = Json.Compact(r);
            var bases = Bases(pack);
            if (bases.Count == 0) rep.Error = "este server no tiene la web abierta a internet";
            foreach (var b in bases)
            {
                try
                {
                    string body;
                    int code = Http.PostJson(b + Support.ApiPath.TrimStart('/'), json, 200000, out body);
                    Dictionary<string, object> o = null;
                    try { o = Json.ParseObj(body); } catch { }
                    object v;
                    if (code == 200 && o != null && o.TryGetValue("ok", out v) && v is bool && (bool)v)
                    {
                        LastGoodBase = b;
                        rep.Sent = true;
                        if (o.TryGetValue("pistas", out v) && v is IEnumerable && !(v is string)) rep.Pistas = ((IEnumerable)v).Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => !string.IsNullOrEmpty(x)).ToList();
                        if (o.TryGetValue("ia", out v)) rep.Ia = v as string;
                        Log.W("Problema enviado a Majin.");
                        return rep;
                    }
                    rep.Error = o != null && o.TryGetValue("mensaje", out v) && v is string ? (string)v : "el server respondió " + code;
                    break;   // ha respondido: no tiene sentido probar por otro camino
                }
                catch (Exception e) { rep.Error = "no he podido conectar con la web del server (" + e.Message + ")"; }
            }
            rep.SavedTo = SaveCopy(r);
            Log.W("No pude mandar el problema: {0}", rep.Error);
            return rep;
        }

        // Si no llega: una copia en el escritorio para mandársela a Majin por Discord.
        public static string SaveCopy(SupportReport r)
        {
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) desk = AppInfo.LogDir;
                string f = Path.Combine(desk, "Majinheim - problema.txt");
                File.WriteAllText(f, Support.Plain(r), new UTF8Encoding(true));
                return f;
            }
            catch { return null; }
        }

        // "Ver lo que se manda": el mismo texto, en logs\problema.txt.
        public static string Preview(SupportReport r)
        {
            Directory.CreateDirectory(AppInfo.LogDir);
            string f = Path.Combine(AppInfo.LogDir, "problema.txt");
            File.WriteAllText(f, Support.Plain(r), new UTF8Encoding(true));
            return f;
        }
    }
#endif

#if SERVER
    // ================================================================ server: recibir, diagnosticar y guardar

    public static class SupportDesk
    {
        static string Dir { get { return Path.Combine(AppInfo.LogDir, "problemas"); } }
        const int Keep = 50;
        static readonly object lk = new object();
        static readonly Dictionary<string, List<DateTime>> byIp = new Dictionary<string, List<DateTime>>();
        static readonly List<DateTime> all = new List<DateTime>();
        static readonly Regex rxId = new Regex(@"^\d{8}-\d{6}-[0-9a-f]{4}$");
        static List<Dictionary<string, object>> index;   // resumen de los guardados (para el panel), el más nuevo primero

        // Como mucho 3 cada 10 minutos y 10 al día desde la misma dirección, y 30 al día en total.
        static string Limit(string ip)
        {
            var now = DateTime.Now;
            lock (lk)
            {
                all.RemoveAll(t => now - t > TimeSpan.FromDays(1));
                if (all.Count >= 30) return "Hoy ya han llegado muchos problemas. Prueba mañana o escríbele a Majin por Discord.";
                List<DateTime> l;
                if (!byIp.TryGetValue(ip, out l)) byIp[ip] = l = new List<DateTime>();
                l.RemoveAll(t => now - t > TimeSpan.FromDays(1));
                if (l.Count >= 10) return "Ya has mandado muchos hoy. Majin los tiene: escríbele por Discord si es urgente.";
                if (l.Count(t => now - t < TimeSpan.FromMinutes(10)) >= 3) return "Espera unos minutos antes de mandar otro: Majin ya tiene los anteriores.";
                l.Add(now); all.Add(now);
                foreach (var k in byIp.Where(x => x.Value.Count == 0).Select(x => x.Key).ToList()) byIp.Remove(k);
            }
            return null;
        }

        static MiniHttp.Response Err(int code, string msg) { return MiniHttp.Response.JsonOf(code, new Dictionary<string, object> { { "ok", false }, { "mensaje", msg } }); }

        public static ServerFacts Facts()
        {
            try { var f = ServerFacts.FromSnapshot(ServerWatch.Snapshot()); f.OnDemand = Settings.Current.OnDemand; return f; }
            catch { return new ServerFacts(); }
        }

        // POST /api/problema (lo manda el launcher de jugador; no hace falta cuenta).
        public static MiniHttp.Response Handle(MiniHttp.Request req)
        {
            if (req.Method != "POST") return Err(405, "Esto lo usa el botón «Tengo un problema» del launcher.");
            if (req.Header("X-Majinheim") != "1") return Err(403, "Petición no permitida.");
            SupportReport r = null;
            try { r = Json.Parse<SupportReport>(req.BodyText); } catch { }
            if (r == null) return Err(400, "No entiendo el informe. ¿Tienes el launcher al día?");
            Clean(r);
            if (r.Texto.Length < 3) return Err(400, "Cuenta en una frase qué te pasa.");
            string wait = Limit(req.ClientIp);
            if (wait != null) return Err(429, wait);

            var now = DateTime.Now;
            r.Id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Rand4();
            r.Fecha = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var facts = Facts();
            r.Pistas = Support.Diagnose(r, facts);
            r.Ia = null;
            Save(r);
            Log.W("Problema de {0} recibido desde el launcher de jugador ({1}).", r.Nombre == "" ? "alguien sin nombre" : r.Nombre, r.Id);
            Discord.Send("🆘 **" + Esc(r.Nombre == "" ? "Alguien" : r.Nombre) + "** ha pulsado «Tengo un problema» en el launcher. Majin lo tiene en la web (panel de control, «Problemas de los amigos»).");
            if (Settings.Current.AiEnabled)
            {
                r.Ia = AskAi(r, facts);
                if (r.Ia != null) Save(r);
            }
            return MiniHttp.Response.JsonOf(200, new Dictionary<string, object> { { "ok", true }, { "id", r.Id }, { "pistas", r.Pistas }, { "ia", r.Ia } });
        }

        static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|").Replace("@", "@​"); }

        // Lo que llega de fuera se recorta y se limpia otra vez (por si viene de un launcher viejo o de otro sitio).
        static void Clean(SupportReport r)
        {
            r.Nombre = Support.OneLine(r.Nombre, Support.MaxName);
            r.Texto = Support.Text(r.Texto, Support.MaxText);
            r.Launcher = Support.OneLine(r.Launcher, 20);
            r.Sistema = Support.OneLine(r.Sistema, 120);
            r.Equipo = Support.OneLine(r.Equipo, 200);
            r.JuegoVersion = Support.OneLine(r.JuegoVersion, 40);
            r.JuegoUltimaVez = Support.OneLine(r.JuegoUltimaVez, 20);
            r.ModsUltimaVez = Support.OneLine(r.ModsUltimaVez, 20);
            r.AnteriorFin = Support.OneLine(r.AnteriorFin, 20);
            r.RamGb = Math.Max(0, Math.Min(r.RamGb, 4096));
            r.DiscoLibreGb = Math.Max(-1, Math.Min(r.DiscoLibreGb, 1000000));
            r.RegistroMods = Support.ClipLog(Support.Redact(Support.Text(r.RegistroMods, 200000), false), Support.MaxLog + 2000);
            r.RegistroJuego = Support.ClipLog(Support.Redact(Support.Text(r.RegistroJuego, 200000), false), Support.MaxLog + 2000);
            r.RegistroLauncher = Support.ClipLog(Support.Redact(Support.Text(r.RegistroLauncher, 50000), false), Support.MaxLauncherLog + 1000);
        }

        static string Rand4()
        {
            var b = new byte[2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        static void Save(SupportReport r)
        {
            lock (lk)
            {
                Directory.CreateDirectory(Dir);
                Json.Save(Path.Combine(Dir, r.Id + ".json"), r);
                LoadIndex();
                index.RemoveAll(x => (string)x["id"] == r.Id);
                index.Insert(0, Summary(r));
                foreach (var old in Directory.GetFiles(Dir, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(Keep))
                {
                    try { File.Delete(old); } catch { }
                    string id = Path.GetFileNameWithoutExtension(old);
                    index.RemoveAll(x => (string)x["id"] == id);
                }
            }
        }

        static Dictionary<string, object> Summary(SupportReport r)
        {
            return new Dictionary<string, object>
            {
                { "id", r.Id }, { "fecha", r.Fecha }, { "nombre", r.Nombre }, { "texto", Support.OneLine(r.Texto, 160) },
                { "pista", r.Pistas != null && r.Pistas.Count > 0 ? r.Pistas[0] : "" }, { "ia", !string.IsNullOrEmpty(r.Ia) },
            };
        }

        static void LoadIndex()
        {
            if (index != null) return;
            index = new List<Dictionary<string, object>>();
            if (!Directory.Exists(Dir)) return;
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).Take(Keep))
            {
                try { var r = Json.Load<SupportReport>(f); if (r != null && rxId.IsMatch(r.Id ?? "")) index.Add(Summary(r)); }
                catch (Exception e) { Log.W("Problemas: no pude leer {0}: {1}", Path.GetFileName(f), e.Message); }
            }
        }

        // Para el panel de administración: los últimos, en resumen.
        public static List<Dictionary<string, object>> Recent(int n)
        {
            lock (lk) { LoadIndex(); return index.Take(n).ToList(); }
        }

        public static int Count() { lock (lk) { LoadIndex(); return index.Count; } }

        public static SupportReport Get(string id)
        {
            if (!rxId.IsMatch(id ?? "")) return null;
            string f = Path.Combine(Dir, id + ".json");
            lock (lk)
            {
                if (!File.Exists(f)) return null;
                try { return Json.Load<SupportReport>(f); } catch { return null; }
            }
        }

        public static bool Delete(string id)
        {
            if (!rxId.IsMatch(id ?? "")) return false;
            string f = Path.Combine(Dir, id + ".json");
            lock (lk)
            {
                LoadIndex();
                index.RemoveAll(x => (string)x["id"] == id);
                if (!File.Exists(f)) return false;
                File.Delete(f);
                return true;
            }
        }

        // ------------------------------------------------------------ primer diagnóstico con la IA

        static readonly Regex rxErrLine = new Regex(@"(?i)^\[(?:Error|Fatal)|exception|crash|failed|ErrorVersion|ErrorPassword|ErrorConnectFailed|incompatible|mismatch");

        static string AskAi(SupportReport r, ServerFacts f)
        {
            try
            {
                var s = Settings.Current;
                var facts = new StringBuilder();
                facts.Append("LO QUE LE PASA (con sus palabras): ").Append(r.Texto).Append("\n");
                facts.Append("SERVER AHORA: ").Append(!f.Known ? "no se sabe" : f.State == "en marcha" ? "encendido" : f.State)
                     .Append(f.Version != "" ? ", Valheim " + Support.CleanVersion(f.Version) : "").Append(f.Revision > 0 ? ", lista de mods revisión " + f.Revision : "")
                     .Append(f.Known ? ", " + (f.Players.Count == 0 ? "sin nadie dentro" : f.Players.Count + " dentro") : "")
                     .Append(s.OnDemand ? ", cualquiera puede encenderlo con /encender en Discord" : ", solo Majin lo enciende").Append("\n");
                facts.Append("SU PC: ").Append(r.Sistema).Append("; ").Append(r.Equipo).Append("; Valheim ").Append(r.Juego ? (r.JuegoVersion != "" ? Support.CleanVersion(r.JuegoVersion) : "versión desconocida") : "no encontrado")
                     .Append("; lista de mods revisión ").Append(r.Revision).Append(r.ModsAlDia ? " instalada entera" : " sin terminar de instalar").Append("\n");
                facts.Append("COMPROBACIONES (seguras):\n");
                foreach (var p in r.Pistas) facts.Append("- ").Append(p).Append("\n");
                var faq = Help.Find(r.Texto + " " + string.Join(" ", r.Pistas), 4);
                if (faq.Count > 0)
                {
                    facts.Append("AYUDA DEL SERVER (respuestas oficiales):\n");
                    foreach (var h in faq) facts.Append("- ").Append(h["pregunta"]).Append(" → ").Append(h["respuesta"]).Append("\n");
                }
                var errs = (r.RegistroMods + "\n" + r.RegistroJuego).Split('\n').Where(l => rxErrLine.IsMatch(l)).Select(l => Support.OneLine(l, 220)).Distinct().Take(25).ToList();
                if (errs.Count > 0) facts.Append("ERRORES EN SUS REGISTROS (pistas, en inglés):\n").Append(string.Join("\n", errs)).Append("\n");
                string text = AiChat.Compose(
                    "Eres el asistente del server de Valheim «" + s.ServerName + "» de un grupo de amigos. Un amigo ha pulsado «Tengo un problema» en el launcher de jugador " +
                    "y le vas a dar un primer diagnóstico. Escribe como mucho 6 líneas, en tono cercano, claro y sin tecnicismos: qué crees que pasa y qué puede probar él, por orden. " +
                    "Básate en las COMPROBACIONES (son seguras) y en la AYUDA DEL SERVER; los errores de los registros son solo pistas. " +
                    "Si no está claro, dilo y dile que Majin ya tiene el informe y lo mirará. No pidas contraseñas ni datos personales, nunca digas la contraseña del server " +
                    "y no prometas cosas que no puedes hacer. Texto plano, sin Markdown ni listas con asteriscos.",
                    facts.ToString());
                if (text == null) return null;
                text = text.Replace("**", "").Trim();
                return text.Length > 1500 ? text.Substring(0, 1500).TrimEnd() + "…" : text;
            }
            catch (Exception e) { Log.W("Problemas: la IA no pudo hacer el diagnóstico: {0}", e.Message); return null; }
        }
    }

    // ================================================================ la página de ayuda (web/ayuda.json), también para la IA

    public static class Help
    {
        class Entry { public string Section, Question, Answer, Keys; }
        static List<Entry> cache;
        static DateTime cacheAt;

        static List<Entry> Entries()
        {
            if (cache != null && DateTime.Now - cacheAt < TimeSpan.FromMinutes(5)) return cache;
            var l = new List<Entry>();
            try
            {
                var raw = WebPanel.Resource("ayuda.json");
                if (raw != null)
                {
                    var o = Json.ParseObj(Encoding.UTF8.GetString(raw));
                    foreach (var sec in ((IEnumerable)o["secciones"]).OfType<Dictionary<string, object>>())
                        foreach (var q in ((IEnumerable)sec["preguntas"]).OfType<Dictionary<string, object>>())
                        {
                            object a, k;
                            q.TryGetValue("r", out a);
                            q.TryGetValue("claves", out k);
                            string answer = a is string ? (string)a : a is IEnumerable ? string.Join(" ", ((IEnumerable)a).Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) : "";
                            l.Add(new Entry { Section = sec["titulo"] as string ?? "", Question = q["p"] as string ?? "", Answer = Regex.Replace(answer, @"\s+", " ").Trim(), Keys = k as string ?? "" });
                        }
                }
            }
            catch (Exception e) { Log.W("Ayuda: no pude leer ayuda.json: {0}", e.Message); }
            cache = l; cacheAt = DateTime.Now;
            return l;
        }

        static readonly HashSet<string> Stop = new HashSet<string>("el la los las un una unos unas de del al a en y o que me mi se no si es por para con como cuando donde lo le ya hay pero mas muy tengo tiene esta este esto eso sale dice juego valheim server servidor launcher the and to of is it in on".Split(' '));

        static List<string> Words(string s)
        {
            return Regex.Split(AiChat.Fold(s ?? ""), @"[^a-z0-9]+").Where(w => w.Length >= 3 && !Stop.Contains(w)).Distinct().ToList();
        }

        // Las respuestas que mejor encajan con lo que se pregunta (por palabras, sin tildes).
        public static List<Dictionary<string, object>> Find(string query, int n)
        {
            var q = Words(query);
            if (q.Count == 0) return new List<Dictionary<string, object>>();
            return Entries()
                .Select(e =>
                {
                    var keys = Words(e.Keys + " " + e.Question);
                    var body = Words(e.Answer);
                    int score = q.Sum(w => keys.Any(k => k == w || (w.Length >= 5 && k.StartsWith(w.Substring(0, 5)))) ? 3 : body.Contains(w) ? 1 : 0);
                    return new { e, score };
                })
                .Where(x => x.score >= 3)
                .OrderByDescending(x => x.score).Take(n)
                .Select(x => new Dictionary<string, object> { { "tema", x.e.Section }, { "pregunta", x.e.Question }, { "respuesta", x.e.Answer } })
                .ToList();
        }

        // Para la herramienta "ayuda" de la IA.
        public static object Tool(string topic)
        {
            var found = Find(topic, 3);
            var o = new Dictionary<string, object>();
            if (found.Count > 0) o["respuestas"] = found;
            else o["nota"] = "No hay nada en la ayuda sobre eso. Temas que hay: " + string.Join("; ", Entries().Select(e => e.Question).Take(30)) + ".";
            if (Settings.Current.WebEnabled && Settings.Current.WebPublic) o["paginaDeAyuda"] = WebPanel.PublicUrl + "ayuda";
            o["siNoSeArregla"] = "Que pulse «Tengo un problema» en el launcher: le llega a Majin con los registros y le da un primer diagnóstico.";
            return o;
        }
    }
#endif
}
