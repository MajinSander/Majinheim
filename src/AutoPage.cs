// Página "Automático" de la edición Server: avisos en Discord y tareas programadas (backups y reinicio diario).
#if SERVER
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Majinheim
{
    public partial class MainForm
    {
        TextBox tDiscToken, tDiscChannel;
        CheckBox cDiscJoins, cDiscUpdates, cNightly;
        ComboBox cbBackupHours, cbNightlyHour;
        Label discState, autoState;
        bool autoPopulating;

        // Estado de las tareas (solo en memoria; al abrir el launcher se recalcula).
        DateTime lastSchedBackup = DateTime.MinValue, lastUpdateCheck = DateTime.MinValue, nextAutoTick = DateTime.MinValue;
        DateTime lastNightly = DateTime.MinValue;
        bool schedBackupRunning;
        string lastRunnerStatus = "";
        bool wasUp;
        HashSet<string> announcedUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static readonly int[] BackupChoices = { 0, 2, 4, 6, 12, 24 };

        static ComboBox Combo(IEnumerable<string> items, int selected)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Th.Input, ForeColor = Th.Text, FlatStyle = FlatStyle.Flat, Font = Th.F(9.5f), Width = 150, Margin = new Padding(0, 2, 12, 6) };
            c.Items.AddRange(items.Cast<object>().ToArray());
            c.SelectedIndex = Math.Max(0, Math.Min(selected, c.Items.Count - 1));
            return c;
        }

        Panel BuildAutoPage()
        {
            var s = Settings.Current;
            autoPopulating = true;

            // ---- Discord
            tDiscToken = Ui.Txt("", 420);
            tDiscToken.UseSystemPasswordChar = true;
            tDiscChannel = Ui.Txt(s.DiscordChannel, 220);
            cDiscJoins = Ui.Chk("Avisar cuando alguien entra o sale (con el tiempo que ha jugado)", s.DiscordJoins);
            cDiscUpdates = Ui.Chk("Avisar cuando un mod tiene versión nueva", s.DiscordUpdates);
            cDiscJoins.CheckedChanged += (o, e) => { if (autoPopulating) return; s.DiscordJoins = cDiscJoins.Checked; Settings.Save(); };
            cDiscUpdates.CheckedChanged += (o, e) => { if (autoPopulating) return; s.DiscordUpdates = cDiscUpdates.Checked; Settings.Save(); };
            discState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            discState.MaximumSize = new Size(540, 0);
            var help = Ui.Lbl("Cómo se consigue (una sola vez):\n" +
                "1. En discord.com/developers/applications abre vuestro bot > Bot > \"Reset Token\" y cópialo aquí.\n" +
                "2. En OAuth2 > URL Generator marca \"bot\" y el permiso \"Send Messages\"; abre el enlace e invítalo a vuestro servidor.\n" +
                "3. En Discord: Ajustes > Avanzado > Modo desarrollador. Clic derecho en el canal > \"Copiar ID del canal\".\n" +
                "El token se guarda cifrado y solo sirve en este PC. Nunca se publica.", 9f, FontStyle.Regular, Th.Muted);
            help.MaximumSize = new Size(540, 0);
            var disc = Ui.Col(Ui.Lbl("Avisos en Discord", 11, FontStyle.Bold), discState,
                Ui.Lbl("Token del bot (déjalo vacío para mantener el guardado)", 9.5f), tDiscToken,
                Ui.Lbl("ID del canal", 9.5f), tDiscChannel,
                cDiscJoins, cDiscUpdates,
                Ui.Row(Ui.Btn("Guardar y probar", SaveDiscord, true),
                       Ui.Btn("Quitar", () => { if (!Confirm("¿Quitar el bot de Discord? Dejará de avisar.")) return; s.DiscordToken = ""; Settings.Save(); UpdateAutoState(); })),
                help);

            // ---- Tareas
            int bi = Array.IndexOf(BackupChoices, s.BackupEveryHours);
            cbBackupHours = Combo(BackupChoices.Select(h => h == 0 ? "Nunca" : "Cada " + h + " horas"), bi < 0 ? 3 : bi);
            cbBackupHours.SelectedIndexChanged += (o, e) => { if (autoPopulating) return; s.BackupEveryHours = BackupChoices[cbBackupHours.SelectedIndex]; Settings.Save(); UpdateAutoState(); };
            cNightly = Ui.Chk("Reiniciar el server una vez al día, a esta hora, si no hay nadie jugando", s.NightlyRestart);
            cbNightlyHour = Combo(Enumerable.Range(0, 24).Select(h => h.ToString("00") + ":00"), s.NightlyRestartHour);
            cNightly.CheckedChanged += (o, e) => { if (autoPopulating) return; s.NightlyRestart = cNightly.Checked; Settings.Save(); UpdateAutoState(); };
            cbNightlyHour.SelectedIndexChanged += (o, e) => { if (autoPopulating) return; s.NightlyRestartHour = cbNightlyHour.SelectedIndex; Settings.Save(); UpdateAutoState(); };
            autoState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            autoState.MaximumSize = new Size(480, 0);
            var why = Ui.Lbl("El backup se hace justo después de que Valheim guarde el mundo, para que la copia esté completa, y solo si alguien ha jugado desde el anterior. " +
                "Se guardan los " + Math.Max(5, s.KeepAutoBackups) + " últimos; los más viejos se borran solos.\n\n" +
                "El reinicio diario limpia la memoria del server. Si a esa hora hay alguien dentro, ese día no se reinicia.", 9f, FontStyle.Regular, Th.Muted);
            why.MaximumSize = new Size(480, 0);
            var tasks = Ui.Col(Ui.Lbl("Tareas automáticas", 11, FontStyle.Bold), autoState,
                Ui.Lbl("Backup del mundo mientras el server está encendido", 9.5f), cbBackupHours,
                cNightly, cbNightlyHour, why);
            autoPopulating = false;

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Bg };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 590));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.Controls.Add(Ui.Card(disc), 0, 0);
            top.Controls.Add(Ui.Card(tasks), 1, 0);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(Ui.Lbl("Automático", 18, FontStyle.Bold), 0, 0);
            var sub = Ui.Lbl("Lo que el launcher hace solo mientras está abierto: avisar en vuestro Discord y cuidar el server.", 9.5f, FontStyle.Regular, Th.Muted);
            sub.MaximumSize = new Size(900, 0);
            t.Controls.Add(sub, 0, 1);
            t.Controls.Add(top, 0, 2);
            var p = new Panel();
            p.Controls.Add(t);
            UpdateAutoState();
            return p;
        }

        void SaveDiscord()
        {
            var s = Settings.Current;
            string token = tDiscToken.Text.Trim(), channel = tDiscChannel.Text.Trim();
            if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(4).Trim();
            if (token == "") token = Secret.Unprotect(s.DiscordToken);
            string err = null;
            RunBg("Probando el bot de Discord...", () => { err = Discord.Test(token, channel); }, () =>
            {
                if (err != null) { discState.Text = "✖ No funciona: " + err + "."; discState.ForeColor = Th.Warn; return; }
                s.DiscordToken = Secret.Protect(token);
                s.DiscordChannel = channel;
                Settings.Save();
                tDiscToken.Text = "";
                Log.W("Discord configurado (canal {0}).", channel);
                UpdateAutoState();
                discState.Text = "✔ Funciona: el bot ha escrito en el canal. " + discState.Text;
            });
        }

        void UpdateAutoState()
        {
            if (discState == null) return;
            var s = Settings.Current;
            bool conf = Discord.Configured;
            discState.Text = !conf ? "Sin configurar. Cuando lo esté, el bot avisará de: server encendido, apagado o caído; quién entra y sale; backups que fallen; y mods con versión nueva."
                : Discord.LastError != null ? "Configurado, pero el último aviso falló: " + Discord.LastError + "."
                : "Configurado. Avisa en el canal " + s.DiscordChannel + (Discord.LastSent != DateTime.MinValue ? " (último aviso: " + Discord.LastSent.ToString("dd/MM HH:mm") + ")" : "") + ".";
            discState.ForeColor = !conf ? Th.Muted : Discord.LastError != null ? Th.Warn : Th.Good;

            var parts = new List<string>();
            parts.Add(s.BackupEveryHours <= 0 ? "Backups automáticos: desactivados (sigue habiendo uno cada vez que arranca el server)."
                : "Backups: cada " + s.BackupEveryHours + " h con el server encendido" + (lastSchedBackup != DateTime.MinValue ? "; el último, " + lastSchedBackup.ToString("dd/MM HH:mm") : "") + ".");
            parts.Add(s.NightlyRestart ? "Reinicio diario a las " + s.NightlyRestartHour.ToString("00") + ":00 si no hay nadie." : "Reinicio diario: desactivado.");
            autoState.Text = string.Join("\n", parts);
        }

        // ------------------------------------------------------------ enganches (se llama una vez al abrir)

        void InitAuto()
        {
            ServerWatch.Start();   // también hace falta con la web apagada: de aquí salen quién entra y quién sale
            var last = Backup.List().FirstOrDefault(b => b.Kind == "auto");
            if (last != null) lastSchedBackup = last.Date;
            wasUp = runner.Running;
            runner.StateChanged += OnRunnerState;
            ServerWatch.PlayerEvent += (what, name, min) =>
            {
                if (!Settings.Current.DiscordJoins) return;
                if (what == "entra") Discord.Send("➡️ **" + Esc(name) + "** ha entrado al server.");
                else Discord.Send("⬅️ **" + Esc(name) + "** ha salido (" + Dur(min) + ").");
            };
            WebAdmin.AdminExtras = AdminExtras;
            lastUpdateCheck = DateTime.Now;   // OnShown ya lanza la primera búsqueda de versiones
        }

        static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|").Replace("@", "@​"); }
        static string Dur(int min) { return min < 60 ? min + " min" : (min / 60) + " h " + (min % 60).ToString("00") + " min"; }

        void OnRunnerState()
        {
            string st = runner.Status ?? "";
            string msg = null;
            lock (announcedUpdates)
            {
                if (st == lastRunnerStatus) return;
                if (st == "En marcha" && !wasUp) { wasUp = true; msg = "🟢 **" + Esc(Settings.Current.ServerName) + "** está encendido. ¡A jugar!"; }
                else if (st.StartsWith("Crasheo")) { wasUp = false; msg = "🔴 El server se ha caído. Lo vuelvo a arrancar solo en unos segundos."; }
                else if (st == "Detenido" && (wasUp || lastRunnerStatus.StartsWith("Guardando"))) { wasUp = false; msg = "⚫ Server apagado (el mundo está guardado)."; }
                lastRunnerStatus = st;
            }
            if (msg != null) Discord.Send(msg);
        }

        // ------------------------------------------------------------ cada pocos segundos (desde TickServer)

        void AutoTick()
        {
            var now = DateTime.Now;
            if (now < nextAutoTick) return;
            nextAutoTick = now.AddSeconds(20);
            var s = Settings.Current;
            if (pages.ContainsKey("Automático") && pages["Automático"].Visible) UpdateAutoState();

            // Backup programado: justo después de un guardado de Valheim (entre 20 s y 10 min antes), si alguien ha jugado desde el último.
            if (s.BackupEveryHours > 0 && runner.Running && !schedBackupRunning && (now - lastSchedBackup).TotalHours >= s.BackupEveryHours)
            {
                var saved = ServerWatch.LastSave;
                bool fresh = saved.HasValue && (now - saved.Value).TotalSeconds >= 20 && (now - saved.Value).TotalMinutes <= 10;
                bool overdue = (now - lastSchedBackup).TotalHours >= s.BackupEveryHours + 1;   // Valheim guarda cada 30 min: si no vemos guardados, a la hora de retraso se hace igual
                if ((fresh || overdue) && ServerWatch.LastActivity > lastSchedBackup) RunScheduledBackup();
            }

            // Reinicio diario
            if (s.NightlyRestart && runner.Running && !busy && now.Hour == s.NightlyRestartHour && lastNightly.Date != now.Date
                && (now - runner.StartedAt).TotalHours >= 2)
            {
                if (runner.Players == 0 && ServerWatch.OnlineCount == 0)
                {
                    lastNightly = now;
                    Log.W("Reinicio diario programado.");
                    Discord.Send("🔄 Reinicio diario del server (no hay nadie jugando). Vuelve en un par de minutos.");
                    WebAction("reiniciar");
                }
            }

            // Versiones nuevas de mods: cada 12 h
            if ((now - lastUpdateCheck).TotalHours >= 12) { lastUpdateCheck = now; CheckUpdatesQuiet(); }
        }

        void RunScheduledBackup()
        {
            schedBackupRunning = true;
            Task.Factory.StartNew(() => Backup.Create("programado", Backup.WorldItems(), true)).ContinueWith(t =>
            {
                schedBackupRunning = false;
                if (t.Exception != null)
                {
                    string err = t.Exception.GetBaseException().Message;
                    Log.W("Backup programado falló: {0}", err);
                    Discord.Send("⚠️ El backup automático del mundo ha fallado: " + err);
                    lastSchedBackup = DateTime.Now.AddHours(-Math.Max(0, Settings.Current.BackupEveryHours - 1));   // reintenta en una hora
                }
                else lastSchedBackup = DateTime.Now;
                if (!IsDisposed) try { BeginInvoke((Action)(() => { UpdateAutoState(); if (pages.ContainsKey("Backups") && pages["Backups"].Visible) RefreshBackups(); })); } catch { }
            });
        }

        // Tras buscar versiones nuevas: avisa en Discord solo de las que no se habían avisado ya.
        void AfterUpdateCheck()
        {
            if (!Settings.Current.DiscordUpdates) return;
            var news = UpdateList().Where(u => announcedUpdates.Add(u["id"] + "@" + u["nueva"])).ToList();
            if (news.Count == 0) return;
            Discord.Send("🆕 " + (news.Count == 1 ? "Hay una versión nueva de un mod" : "Hay versiones nuevas de " + news.Count + " mods") + ":\n" +
                string.Join("\n", news.Take(15).Select(u => "• " + Esc((string)u["nombre"]) + ": " + u["actual"] + " → " + u["nueva"])) +
                "\nNo se instala nada solo: se actualiza desde el launcher del server, pestaña Mods.");
        }

        List<Dictionary<string, object>> UpdateList()
        {
            var r = new List<Dictionary<string, object>>();
            var mods = pack == null ? new List<ModEntry>() : pack.Mods.Where(m => m.Enabled && !m.Local).ToList();
            lock (latest)
                foreach (var m in mods)
                {
                    PkgInfo li;
                    if (latest.TryGetValue(m.Id, out li) && Ver.Compare(li.Version, m.Version) > 0)
                        r.Add(new Dictionary<string, object> { { "id", m.Id }, { "nombre", m.PkgName().Replace('_', ' ') }, { "actual", m.Version }, { "nueva", li.Version } });
                }
            return r;
        }

        // Lo que añade el launcher al panel de administración (solo lo ve el administrador).
        Dictionary<string, object> AdminExtras()
        {
            var s = Settings.Current;
            return new Dictionary<string, object>
            {
                { "backups", Backup.List().Take(15).Select(b => new Dictionary<string, object>
                    { { "fecha", b.Date.ToString("yyyy-MM-dd'T'HH:mm:sszzz") }, { "nombre", b.Label }, { "tipo", b.Kind }, { "mb", Math.Round(b.Size / 1048576.0, 1) } }).ToList() },
                { "actualizaciones", UpdateList() },
                { "tareas", new Dictionary<string, object>
                    {
                        { "backupHoras", s.BackupEveryHours },
                        { "ultimoBackupAuto", lastSchedBackup == DateTime.MinValue ? null : lastSchedBackup.ToString("yyyy-MM-dd'T'HH:mm:sszzz") },
                        { "reinicioDiario", s.NightlyRestart ? s.NightlyRestartHour.ToString("00") + ":00" : null },
                        { "discord", Discord.Configured ? (Discord.LastError == null ? "ok" : "error") : null },
                    }
                },
            };
        }
    }
}
#endif
