// Vida en el Discord (2.3): resumen de la semana, hitos del grupo y quedadas. Solo lógica y datos: los avisos los manda
// el launcher del server (GrupoAuto.cs) y las quedadas se crean con /quedada en Discord (DiscordBot.cs). Edición Server.
#if SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Majinheim
{
    public static class Community
    {
        static readonly CultureInfo Es = new CultureInfo("es-ES");
        static string Hm(int min) { return min < 60 ? min + " min" : (min / 60) + " h" + (min % 60 > 0 ? " " + (min % 60) + " min" : ""); }
        static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|").Replace("@", "@​"); }
        static int Minutes(DateTime a, DateTime b) { return Math.Max(0, (int)(b - a).TotalMinutes); }

        // ------------------------------------------------------------ resumen de la semana (domingos por la noche)

        public class Week
        {
            public DateTime From, To;
            public int Minutes, Sessions, PrevMinutes;
            public List<KeyValuePair<string, int>> Players = new List<KeyValuePair<string, int>>();   // nombre, minutos
            public List<KeyValuePair<string, int>> Deaths = new List<KeyValuePair<string, int>>();
            public List<string> Newcomers = new List<string>(), Bosses = new List<string>();
            public string LongestName; public int LongestMin; public DateTime LongestDay;
            public int? Day;
            public bool Empty { get { return Sessions == 0; } }
        }

        // La semana que acaba en 'to' (7 días). Cada partida cuenta lo que cae dentro de la semana.
        public static Week GetWeek(DateTime to)
        {
            var w = new Week { To = to, From = to.AddDays(-7) };
            var all = ServerWatch.Sessions(DateTime.MinValue);
            var inWeek = all.Where(x => x.End > w.From && x.Start < w.To).ToList();
            Func<ServerWatch.SessionView, int> min = x => Minutes(x.Start < w.From ? w.From : x.Start, x.End > w.To ? w.To : x.End);
            w.Sessions = inWeek.Count;
            w.Minutes = inWeek.Sum(min);
            w.PrevMinutes = all.Where(x => x.End > w.From.AddDays(-7) && x.Start < w.From)
                .Sum(x => Minutes(x.Start < w.From.AddDays(-7) ? w.From.AddDays(-7) : x.Start, x.End > w.From ? w.From : x.End));
            w.Players = inWeek.GroupBy(x => x.Name).Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(min))).Where(p => p.Value > 0).OrderByDescending(p => p.Value).ToList();
            w.Deaths = inWeek.Where(x => x.Start >= w.From).GroupBy(x => x.Name).Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(x => x.Deaths))).Where(p => p.Value > 0).OrderByDescending(p => p.Value).ToList();
            var firsts = all.GroupBy(x => x.Name).Select(g => new { g.Key, First = g.Min(x => x.Start) });
            w.Newcomers = firsts.Where(f => f.First >= w.From && f.First < w.To).Select(f => f.Key).ToList();
            var longest = inWeek.Where(x => !x.Live).OrderByDescending(x => x.End - x.Start).FirstOrDefault();
            if (longest != null) { w.LongestName = longest.Name; w.LongestMin = Minutes(longest.Start, longest.End); w.LongestDay = longest.Start; }
            foreach (var e in global::Majinheim.Bosses.Load())
            {
                if (e.Before) continue;
                var d = ServerWatch.ParseIsoPublic(e.Since);
                if (d >= w.From && d < w.To) w.Bosses.Add(global::Majinheim.Bosses.Name(e.Key));
            }
            try { w.Day = ServerWatch.CurrentDay(); } catch { }
            return w;
        }

        // Los datos en frases, tal cual: para la IA y para el resumen sin IA.
        public static string Facts(Week w)
        {
            var sb = new StringBuilder();
            sb.Append("Semana del ").Append(w.From.ToString("dddd d 'de' MMMM", Es)).Append(" al ").Append(w.To.ToString("dddd d 'de' MMMM", Es)).Append(".\n");
            if (w.Empty) { sb.Append("Esta semana nadie ha jugado.\n"); return sb.ToString(); }
            sb.Append("Tiempo jugado entre todos: ").Append(Hm(w.Minutes)).Append(", en ").Append(w.Sessions).Append(w.Sessions == 1 ? " partida" : " partidas")
              .Append(", entre ").Append(w.Players.Count).Append(w.Players.Count == 1 ? " vikingo" : " vikingos").Append(".\n");
            if (w.PrevMinutes > 0)
            {
                int diff = w.Minutes - w.PrevMinutes;
                sb.Append("La semana anterior se jugaron ").Append(Hm(w.PrevMinutes)).Append(": esta semana ").Append(diff == 0 ? "lo mismo" : Hm(Math.Abs(diff)) + (diff > 0 ? " más" : " menos")).Append(".\n");
            }
            else sb.Append("La semana anterior no jugó nadie.\n");
            sb.Append("Horas de cada uno: ").Append(string.Join(", ", w.Players.Select(p => p.Key + " " + Hm(p.Value)))).Append(".\n");
            if (w.LongestName != null) sb.Append("Partida más larga: ").Append(w.LongestName).Append(", ").Append(Hm(w.LongestMin)).Append(", el ").Append(w.LongestDay.ToString("dddd", Es)).Append(".\n");
            sb.Append("Muertes: ").Append(w.Deaths.Count == 0 ? "ninguna" : string.Join(", ", w.Deaths.Select(p => p.Key + " " + p.Value))).Append(".\n");
            if (w.Bosses.Count > 0) sb.Append("Jefes derrotados esta semana: ").Append(string.Join(", ", w.Bosses)).Append(".\n");
            if (w.Newcomers.Count > 0) sb.Append("Han jugado por primera vez: ").Append(string.Join(", ", w.Newcomers)).Append(".\n");
            if (w.Day.HasValue) sb.Append("Día del mundo ahora: ").Append(w.Day.Value).Append(".\n");
            return sb.ToString();
        }

        // Sin IA (o si la IA se lía): el mismo resumen, en limpio.
        public static string Plain(Week w)
        {
            var sb = new StringBuilder("📜 **Resumen de la semana en " + Esc(Settings.Current.ServerName) + "**\n");
            if (w.Empty) return sb.Append("Esta semana nadie ha pisado el mundo. ¿Quedamos? Usad /quedada para elegir día y hora.").ToString();
            sb.Append("⏱️ **").Append(Hm(w.Minutes)).Append("** jugadas en ").Append(w.Sessions).Append(w.Sessions == 1 ? " partida" : " partidas")
              .Append(" entre ").Append(w.Players.Count).Append(w.Players.Count == 1 ? " vikingo" : " vikingos");
            if (w.PrevMinutes > 0 && w.Minutes != w.PrevMinutes) sb.Append(" (").Append(Hm(Math.Abs(w.Minutes - w.PrevMinutes))).Append(w.Minutes > w.PrevMinutes ? " más" : " menos").Append(" que la semana anterior)");
            sb.Append(".\n");
            sb.Append("🏆 Quien más jugó: **").Append(Esc(w.Players[0].Key)).Append("**, ").Append(Hm(w.Players[0].Value)).Append(".\n");
            if (w.LongestName != null) sb.Append("⌛ Partida más larga: **").Append(Esc(w.LongestName)).Append("**, ").Append(Hm(w.LongestMin)).Append(", el ").Append(w.LongestDay.ToString("dddd", Es)).Append(".\n");
            sb.Append("💀 Muertes: ").Append(w.Deaths.Count == 0 ? "ninguna. ¡Bien jugado!" : string.Join(", ", w.Deaths.Select(p => Esc(p.Key) + " " + p.Value))).Append(w.Deaths.Count == 0 ? "\n" : ".\n");
            if (w.Bosses.Count > 0) sb.Append("⚔️ Jefes caídos: ").Append(string.Join(", ", w.Bosses)).Append(".\n");
            if (w.Newcomers.Count > 0) sb.Append("👋 Primera vez en el mundo: ").Append(string.Join(", ", w.Newcomers.Select(Esc))).Append(".\n");
            return sb.ToString().TrimEnd();
        }

        // El resumen escrito por la IA si está lista; si no, o si pone algún dato que no está, el normal.
        public static string WeeklyMessage(Week w, out bool byAi)
        {
            byAi = false;
            if (w.Empty || !Settings.Current.AiEnabled) return Plain(w);
            string text = AiChat.Compose(
                "Escribe el resumen de la semana del servidor de Valheim «" + Settings.Current.ServerName + "» para el Discord del grupo de amigos. " +
                "Tono cercano y con un poco de humor vikingo, en español, entre 5 y 9 líneas, con negritas de Discord (**) y algún emoji, sin tablas ni títulos con #. " +
                "Menciona el tiempo jugado, quién jugó más, la partida más larga, las muertes y, si los hay, jefes y gente nueva. " +
                "Usa SOLO estos datos, tal cual, sin inventar nada ni añadir cifras que no estén. Los tiempos, en horas y minutos como vienen. Termina animando a quedar (con /quedada).",
                Facts(w));
            if (string.IsNullOrEmpty(text)) return Plain(w);
            byAi = true;
            return "📜 **Resumen de la semana**\n" + text;
        }

        // ------------------------------------------------------------ hitos: 100 horas, récords, primera vez...

        static readonly int[] PlayerHours = { 10, 25, 50, 100, 150, 200, 300, 400, 500, 750, 1000 };
        static readonly int[] GroupHours = { 50, 100, 250, 500, 750, 1000, 1500, 2000, 3000, 5000 };
        static readonly int[] PlayerDeaths = { 50, 100, 250, 500, 1000 };
        const int RecordMinMinutes = 120;   // un récord de 20 minutos no es noticia

        // Hitos nuevos desde la última vez. La primera vez solo apunta lo que ya estaba conseguido, sin avisar.
        public static List<string> NewMilestones()
        {
            var s = Settings.Current;
            var seen = new HashSet<string>(s.MilestonesSeen ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var news = new List<KeyValuePair<string, string>>();
            var all = ServerWatch.Sessions(DateTime.MinValue);
            var players = all.GroupBy(x => x.Name).Select(g => new { Name = g.Key, Min = g.Sum(x => Minutes(x.Start, x.End)), Deaths = g.Sum(x => x.Deaths) }).ToList();
            foreach (var p in players)
            {
                if (seen.Add("nuevo|" + p.Name)) news.Add(new KeyValuePair<string, string>("nuevo", "👋 **" + Esc(p.Name) + "** entra por primera vez en " + Esc(s.ServerName) + ". ¡Que Odín le acompañe!"));
                foreach (int h in PlayerHours.Where(h => p.Min >= h * 60))
                    if (seen.Add("horas|" + p.Name + "|" + h)) news.Add(new KeyValuePair<string, string>("horas", "🎉 **" + Esc(p.Name) + "** llega a **" + h + " horas** en " + Esc(s.ServerName) + "."));
                foreach (int d in PlayerDeaths.Where(d => p.Deaths >= d))
                    if (seen.Add("muertes|" + p.Name + "|" + d)) news.Add(new KeyValuePair<string, string>("muertes", "💀 **" + Esc(p.Name) + "** ya ha muerto **" + d + " veces**. Las valquirias le conocen por su nombre."));
            }
            int total = players.Sum(p => p.Min);
            foreach (int h in GroupHours.Where(h => total >= h * 60))
                if (seen.Add("grupo|" + h)) news.Add(new KeyValuePair<string, string>("grupo", "🏰 Entre todos lleváis ya **" + h + " horas** en " + Esc(s.ServerName) + "."));
            var longest = all.Where(x => !x.Live).OrderByDescending(x => x.End - x.Start).FirstOrDefault();
            int lmin = longest == null ? 0 : Minutes(longest.Start, longest.End);
            bool record = lmin >= RecordMinMinutes && lmin > s.RecordMinutes;
            string recordText = record ? "⌛ ¡Nuevo récord! **" + Esc(longest.Name) + "** ha jugado la partida más larga: **" + Hm(lmin) + "**" + (s.RecordMinutes >= RecordMinMinutes ? " (el anterior era de " + Hm(s.RecordMinutes) + ")." : ".") : null;
            if (lmin > s.RecordMinutes) { s.RecordMinutes = lmin; record = record || !s.MilestonesReady; }

            bool first = !s.MilestonesReady;
            if (first || news.Count > 0 || record)
            {
                s.MilestonesSeen = seen.ToList();
                s.MilestonesReady = true;
                Settings.Save();
            }
            if (first) return new List<string>();
            var res = news.Where(n => n.Key != "nuevo" || players.Count > 1).Select(n => n.Value).ToList();
            if (recordText != null) res.Add(recordText);
            return res;
        }

        // ------------------------------------------------------------ quedadas (/quedada viernes 22:00)

        public class Person { public string Id { get; set; } public string Name { get; set; } }
        public class Meetup
        {
            public string Id { get; set; }
            public string When { get; set; }        // ISO, hora local
            public string Plan { get; set; }
            public string Channel { get; set; }     // canal de Discord donde se creó (allí se avisa)
            public string Message { get; set; }     // mensaje con los botones (para cerrarlo al terminar)
            public string Creator { get; set; }
            public string CreatorId { get; set; }
            public List<Person> People { get; set; }
            public bool Reminded { get; set; }
            public bool Done { get; set; }
            public bool Cancelled { get; set; }
            public Meetup() { People = new List<Person>(); }
            public DateTime At { get { DateTime d; return DateTime.TryParse(When, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue; } }
        }

        static readonly object mlk = new object();
        static string MeetupFile { get { return Path.Combine(AppInfo.LogDir, "quedadas.json"); } }

        public static List<Meetup> Meetups()
        {
            lock (mlk)
            {
                try { return File.Exists(MeetupFile) ? (Json.Load<List<Meetup>>(MeetupFile) ?? new List<Meetup>()) : new List<Meetup>(); }
                catch (Exception e) { Log.W("Quedadas: no pude leer quedadas.json: {0}", e.Message); return new List<Meetup>(); }
            }
        }

        // Cambia una quedada (o la crea) con el cerrojo cogido. Las viejas (más de 30 días) se olvidan.
        public static Meetup Change(string id, Func<Meetup, Meetup> f)
        {
            lock (mlk)
            {
                var list = Meetups();
                var m = id == null ? null : list.FirstOrDefault(x => x.Id == id);
                var r = f(m);
                if (r != null && m == null) list.Add(r);
                list.RemoveAll(x => x.At < DateTime.Now.AddDays(-30));
                Json.Save(MeetupFile, list);
                return r;
            }
        }

        // ¿Hay una quedada a punto de empezar o en marcha? (para no apagar el server vacío justo antes)
        public static bool MeetupSoon(DateTime now)
        {
            return Meetups().Any(m => !m.Cancelled && m.At != DateTime.MinValue && now >= m.At.AddMinutes(-60) && now <= m.At.AddMinutes(30));
        }

        static readonly string[] WeekDays = { "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado" };

        // "viernes 22:00", "hoy a las 21", "mañana 18h", "15/11 22:30", "el sábado a las 10 de la mañana"...
        public static bool ParseWhen(string text, DateTime now, out DateTime when, out string err)
        {
            when = DateTime.MinValue; err = null;
            string t = " " + AiChat.Fold(text ?? "") + " ";
            DateTime? day = null;
            var md = Regex.Match(t, @"(?<!\d)(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?(?!\d)");
            if (md.Success)
            {
                int dd = int.Parse(md.Groups[1].Value), mm = int.Parse(md.Groups[2].Value), yy = now.Year;
                if (md.Groups[3].Success) { yy = int.Parse(md.Groups[3].Value); if (yy < 100) yy += 2000; }
                if (mm < 1 || mm > 12 || dd < 1 || dd > DateTime.DaysInMonth(yy, mm)) { err = "Esa fecha no existe."; return false; }
                day = new DateTime(yy, mm, dd);
                if (!md.Groups[3].Success && day.Value < now.Date) day = day.Value.AddYears(1);
                t = t.Remove(md.Index, md.Length).Insert(md.Index, " ");
            }
            int wd = -1;
            for (int i = 0; i < 7; i++) if (Regex.IsMatch(t, @"\b" + WeekDays[i] + @"\b")) { wd = i; break; }
            string noMorning = Regex.Replace(t, @"\b(de|por) la manana\b", " ");   // en "mañana por la mañana", solo la primera es el día
            if (day == null)
            {
                if (wd >= 0)
                {
                    int ahead = (wd - (int)now.DayOfWeek + 7) % 7;
                    day = now.Date.AddDays(ahead);
                }
                else if (Regex.IsMatch(noMorning, @"\bpasado manana\b")) day = now.Date.AddDays(2);
                else if (Regex.IsMatch(noMorning, @"\bmanana\b")) day = now.Date.AddDays(1);
                else if (Regex.IsMatch(t, @"\bhoy\b|\besta (tarde|noche)\b")) day = now.Date;
                // Un día que no se sabe calcular no se deja pasar como si fuera hoy.
                else if (Regex.IsMatch(t, @"\b(ayer|anoche|anteayer|semana|finde|dentro|dias?|mes)\b")) { err = "No sé qué día es ese. Prueba con viernes 22:00, mañana 21:00 o 14/11 20:30."; return false; }
            }

            int hh = -1, mi = 0;
            var mt = Regex.Match(t, @"(?<!\d)(\d{1,2})[:.h](\d{2})(?!\d)");
            if (!mt.Success) mt = Regex.Match(t, @"\ba las (\d{1,2})(?!\d)");
            if (!mt.Success) mt = Regex.Match(t, @"(?<!\d)(\d{1,2}) ?(?:h|hs|hrs?|horas)\b");
            if (!mt.Success) mt = Regex.Match(t, @"(?<![\d/])(\d{1,2})(?![\d/])");
            if (mt.Success)
            {
                hh = int.Parse(mt.Groups[1].Value);
                if (mt.Groups.Count > 2 && mt.Groups[2].Success) mi = int.Parse(mt.Groups[2].Value);
            }
            if (hh < 0) { err = "Dime también la hora, por ejemplo: viernes 22:00."; return false; }
            // "de la noche": las 12 y de la 1 a las 5 ya son la madrugada del día siguiente.
            bool night = Regex.IsMatch(t, @"\b(de|por) la noche\b|\besta noche\b");
            bool afterMidnight = night && (hh == 12 || (hh >= 1 && hh <= 5));
            if (night && hh == 12) hh = 0;
            else if (!afterMidnight && (night || Regex.IsMatch(t, @"\b(de|por) la tarde\b|\besta tarde\b")) && hh >= 1 && hh < 12) hh += 12;
            if (hh > 23 || mi > 59) { err = "Esa hora no existe."; return false; }

            if (day == null) day = now.Date;
            when = day.Value.AddDays(afterMidnight ? 1 : 0).AddHours(hh).AddMinutes(mi);
            if (when <= now && md.Success == false && (wd >= 0 || !Regex.IsMatch(t, @"\bhoy\b|\besta (tarde|noche)\b"))) when = when.AddDays(wd >= 0 ? 7 : 1);   // "viernes 22:00" con el viernes ya pasado: el siguiente
            if (when <= now.AddMinutes(1)) { err = "Esa hora ya ha pasado."; return false; }
            if (when > now.AddDays(45)) { err = "Como mucho, con mes y medio de antelación."; return false; }
            return true;
        }

        public static long Unix(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        public static string Describe(Meetup m)
        {
            var at = m.At;
            var sb = new StringBuilder();
            if (m.Cancelled) sb.Append("~~");
            sb.Append("📅 **Quedada: ").Append(at.ToString("dddd d 'de' MMMM, HH:mm", Es)).Append("**");
            if (m.Cancelled) sb.Append("~~ · **cancelada**");
            else if (!m.Done) sb.Append(" (<t:").Append(Unix(at)).Append(":R>)");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(m.Plan)) sb.Append("🎯 ").Append(Esc(m.Plan)).Append('\n');
            sb.Append("🙋 ").Append(m.People.Count == 0 ? "Todavía no se ha apuntado nadie." : "Van (" + m.People.Count + "): " + string.Join(", ", m.People.Select(p => "**" + Esc(p.Name) + "**")) + ".");
            if (!m.Done && !m.Cancelled)
                sb.Append("\n").Append(Settings.Current.MeetupStart ? "Avisaré 15 minutos antes a los que vayan y encenderé el server si está apagado." : "Avisaré 15 minutos antes a los que vayan.");
            else if (m.Done && !m.Cancelled) sb.Append("\n_Ya pasó._");
            return sb.ToString();
        }
    }
}
#endif
