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
        CheckBox cDiscJoins, cDiscUpdates, cDiscCommands, cNightly, cWarn;
        ComboBox cbBackupHours, cbNightlyHour;
        Label discState, autoState, rconState, offsiteState;
        DateTime nextBossPoll = DateTime.MinValue;
        bool bossPollRunning;
        bool autoPopulating;

        // Estado de las tareas (solo en memoria; al abrir el launcher se recalcula).
        DateTime lastSchedBackup = DateTime.MinValue, lastUpdateCheck = DateTime.MinValue, nextAutoTick = DateTime.MinValue;
        DateTime lastNightly = DateTime.MinValue, lastSteamCheck = DateTime.MinValue;
        string announcedBuild;   // actualización de Valheim pendiente de la que ya se ha avisado
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
            cDiscCommands = Ui.Chk("Comandos del bot: /estado, /jugadores, /estadisticas, /web para todos; /encender, /apagar, /reiniciar, /backup solo para administradores del Discord", s.DiscordCommands);
            cDiscCommands.MaximumSize = new Size(540, 0);
            cDiscCommands.CheckedChanged += (o, e) => { if (autoPopulating) return; s.DiscordCommands = cDiscCommands.Checked; Settings.Save(); DiscordBot.Apply(); UpdateAutoState(); };
            cDiscJoins.CheckedChanged += (o, e) => { if (autoPopulating) return; s.DiscordJoins = cDiscJoins.Checked; Settings.Save(); };
            cDiscUpdates.CheckedChanged += (o, e) => { if (autoPopulating) return; s.DiscordUpdates = cDiscUpdates.Checked; Settings.Save(); };
            discState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            discState.MaximumSize = new Size(540, 0);
            var help = Ui.Lbl("Cómo se consigue (una sola vez):\n" +
                "1. En discord.com/developers/applications abre vuestro bot > Bot > \"Reset Token\" y cópialo aquí.\n" +
                "2. En OAuth2 > URL Generator marca \"bot\" y \"applications.commands\" y el permiso \"Send Messages\"; abre el enlace e invítalo a vuestro servidor.\n" +
                "3. En Discord: Ajustes > Avanzado > Modo desarrollador. Clic derecho en el canal > \"Copiar ID del canal\".\n" +
                "El token se guarda cifrado y solo sirve en este PC. Nunca se publica.", 9f, FontStyle.Regular, Th.Muted);
            help.MaximumSize = new Size(540, 0);
            var disc = Ui.Col(Ui.Lbl("Avisos en Discord", 11, FontStyle.Bold), discState,
                Ui.Lbl("Token del bot (déjalo vacío para mantener el guardado)", 9.5f), tDiscToken,
                Ui.Lbl("ID del canal", 9.5f), tDiscChannel,
                cDiscJoins, cDiscUpdates, cDiscCommands,
                Ui.Row(Ui.Btn("Guardar y probar", SaveDiscord, true),
                       Ui.Btn("Quitar", () => { if (!Confirm("¿Quitar el bot de Discord? Dejará de avisar.")) return; s.DiscordToken = ""; Settings.Save(); DiscordBot.Stop(); UpdateAutoState(); })),
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
            cWarn = Ui.Chk("Avisar en el chat del juego 1 minuto antes de apagar o reiniciar (web, Discord y reinicio diario)", s.WarnBeforeStop);
            cWarn.MaximumSize = new Size(480, 0);
            cWarn.CheckedChanged += (o, e) => { if (autoPopulating) return; s.WarnBeforeStop = cWarn.Checked; Settings.Save(); };
            rconState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            rconState.MaximumSize = new Size(480, 0);
            offsiteState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            offsiteState.MaximumSize = new Size(480, 0);
            var offBtns = Ui.Row(Ui.Btn("Elegir carpeta...", ChooseOffsite), Ui.Btn("Copiar el último ahora", () =>
                {
                    var last = Backup.LastWorld();
                    if (Backup.OffsiteFolder == null) { MessageBox.Show(this, "Elige antes una carpeta.", "Majinheim"); return; }
                    if (last == null) { MessageBox.Show(this, "Todavía no hay ningún backup del mundo.", "Majinheim"); return; }
                    Backup.CopyOffsite(last.Path);
                    Status("Copiando " + System.IO.Path.GetFileName(last.Path) + " a la carpeta externa...");
                }), Ui.Btn("Quitar", () => { s.OffsiteDir = ""; Settings.Save(); UpdateAutoState(); }));
            var tasks = Ui.Col(Ui.Lbl("Tareas automáticas", 11, FontStyle.Bold), autoState,
                Ui.Lbl("Backup del mundo mientras el server está encendido", 9.5f), cbBackupHours,
                cNightly, cbNightlyHour, why,
                Ui.Lbl("Mensajes en el juego", 11, FontStyle.Bold), rconState, cWarn,
                Ui.Row(Ui.Btn("Preparar", PrepareRcon), Ui.Btn("Mandar un mensaje", () =>
                {
                    string msg = Ui.Ask(this, "Mensaje al juego", "Lo verán todos los que estén jugando, en el chat.", "");
                    if (msg == null) return;
                    string err = SayInGame(msg);
                    if (err == null) Status("Mensaje enviado al chat del juego."); else MessageBox.Show(this, "No se pudo: " + err, "Majinheim");
                })),
                Ui.Lbl("Copia fuera del PC", 11, FontStyle.Bold), offsiteState, offBtns);
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
                DiscordBot.Apply();
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
            if (conf && DiscordBot.State != null) discState.Text += "\nComandos: " + DiscordBot.State;
            discState.ForeColor = !conf ? Th.Muted : Discord.LastError != null ? Th.Warn : Th.Good;

            var parts = new List<string>();
            parts.Add(s.BackupEveryHours <= 0 ? "Backups automáticos: desactivados (sigue habiendo uno cada vez que arranca el server)."
                : "Backups: cada " + s.BackupEveryHours + " h con el server encendido" + (lastSchedBackup != DateTime.MinValue ? "; el último, " + lastSchedBackup.ToString("dd/MM HH:mm") : "") + ".");
            parts.Add(s.NightlyRestart ? "Reinicio diario a las " + s.NightlyRestartHour.ToString("00") + ":00 si no hay nadie." : "Reinicio diario: desactivado.");
            var vst = Steam.Server();
            if (vst != null && vst.UpdatePending) parts.Add("Hay una actualización de Valheim esperando: Steam la instala cuando el server está apagado.");
            autoState.Text = string.Join("\n", parts);
            rconState.Text = !Rcon.Installed ? "Hace falta el mod ValheimRcon en la lista de mods del server (solo server: los amigos no instalan nada). Después, con el server apagado, pulsa Preparar."
                : !Rcon.Ready ? "El mod está instalado. Con el server apagado, pulsa Preparar: le pone una contraseña larga al azar y solo acepta órdenes desde este PC."
                : "Listo: solo acepta órdenes desde este PC (puerto " + Rcon.Port + ", que no se abre en el router).";
            rconState.ForeColor = Rcon.Ready ? Th.Good : Th.Muted;
            if (offsiteState != null)
            {
                string od = Backup.OffsiteFolder;
                offsiteState.Text = od == null ? "Desactivada. Elige una carpeta en otro disco, un USB o OneDrive: cada backup del mundo se copiará también allí (se guardan los " + Backup.OffsiteKeep + " últimos). Así, si el disco del PC falla, el mundo no se pierde."
                    : Backup.OffsiteError != null ? "Último intento falló: " + Backup.OffsiteError + "\nCarpeta: " + od
                    : "Activada: " + od + (Backup.OffsiteLast != DateTime.MinValue ? "\nÚltima copia: " + Backup.OffsiteLast.ToString("dd/MM HH:mm") : "\nSe copia con el próximo backup del mundo.");
                offsiteState.ForeColor = od == null ? Th.Muted : Backup.OffsiteError != null ? Th.Bad : Th.Good;
            }
        }

        void ChooseOffsite()
        {
            using (var d = new FolderBrowserDialog { Description = "Carpeta para la copia de los backups (otro disco, USB u OneDrive). Dentro se crea Majinheim-backups.", ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(Settings.Current.OffsiteDir)) d.SelectedPath = Settings.Current.OffsiteDir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string full = System.IO.Path.GetFullPath(d.SelectedPath).TrimEnd('\\');
                if (full.StartsWith(System.IO.Path.GetFullPath(AppInfo.BackupDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show(this, "Esa carpeta es la de los backups normales: elige otra, mejor en otro disco.", "Majinheim"); return; }
                Settings.Current.OffsiteDir = full;
                Settings.Save();
                Log.W("Copia fuera del PC activada: {0}", full);
            }
            UpdateAutoState();
        }

        void PrepareRcon()
        {
            if (runner.Running) { MessageBox.Show(this, "Apaga el server primero: el mod lee su ajuste al arrancar.", "Majinheim"); return; }
            string err = Rcon.Configure();
            if (err != null) MessageBox.Show(this, err, "Majinheim");
            else Status("Mensajes en el juego preparados. Funcionan desde el próximo arranque del server.");
            UpdateAutoState();
        }

        // Mensaje al chat del juego. Devuelve null si ha llegado.
        string SayInGame(string text)
        {
            if (!Rcon.Ready) return "los mensajes en el juego no están preparados (pestaña Automático del launcher)";
            if (!runner.Running) return "el server está apagado";
            return Rcon.Say("[Majinheim] " + text);
        }

        bool WillWarn() { return Settings.Current.WarnBeforeStop && Rcon.Ready && runner.Running && (runner.Players > 0 || ServerWatch.OnlineCount > 0); }

        // Se llama en segundo plano antes de apagar o reiniciar: avisa y espera un minuto si hay alguien jugando.
        void WarnPlayers(string what)
        {
            if (!WillWarn()) return;
            if (Rcon.Say("[Majinheim] El server " + what + " en 1 minuto. Se guarda el mundo; buscad un sitio seguro.") != null) return;
            Status("Avisando a los jugadores: el server " + what + " en 1 minuto...");
            System.Threading.Thread.Sleep(50000);
            Rcon.Say("[Majinheim] El server " + what + " en 10 segundos.");
            System.Threading.Thread.Sleep(10000);
        }

        // ------------------------------------------------------------ enganches (se llama una vez al abrir)

        void InitAuto()
        {
            ServerWatch.Start();   // también hace falta con la web apagada: de aquí salen quién entra y quién sale
            var last = Backup.List().FirstOrDefault(b => b.Kind == "auto" && Backup.HasWorld(b));
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
            WebAdmin.RconExec = cmd =>
            {
                if (!Rcon.Ready) throw new Exception("la consola no está preparada (pestaña Automático, Mensajes en el juego)");
                if (!runner.Running) throw new Exception("el server está apagado");
                return Rcon.Exec(cmd);
            };
            lastUpdateCheck = DateTime.Now;   // OnShown ya lanza la primera búsqueda de versiones
            DiscordBot.Apply();
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

            // Jefes derrotados: se mira cada 2 minutos por RCON (globalKeys)
            if (runner.Running && Rcon.Ready && !bossPollRunning && now >= nextBossPoll && (now - runner.StartedAt).TotalSeconds > 90)
            {
                nextBossPoll = now.AddMinutes(2);
                bossPollRunning = true;
                Task.Factory.StartNew(PollBosses).ContinueWith(t => { bossPollRunning = false; });
            }

            // Versiones nuevas de mods: cada 12 h
            if ((now - lastUpdateCheck).TotalHours >= 12) { lastUpdateCheck = now; CheckUpdatesQuiet(); }

            // Actualizaciones de Valheim: cada 10 min
            if ((now - lastSteamCheck).TotalMinutes >= 10) { lastSteamCheck = now; CheckValheimBuild(); }
        }

        // Steam actualiza el server de Valheim solo, y solo con el server apagado. Avisa (una vez) de que hay una actualización
        // esperando y de que ya se ha instalado: con una versión nueva del juego, algún mod puede necesitar actualizarse.
        void CheckValheimBuild()
        {
            var st = Steam.Server();
            if (st == null || string.IsNullOrEmpty(st.BuildId)) return;
            var s = Settings.Current;
            if (string.IsNullOrEmpty(s.ValheimBuild)) { s.ValheimBuild = st.BuildId; Settings.Save(); }
            else if (s.ValheimBuild != st.BuildId)
            {
                Log.W("Steam ha actualizado Valheim en el server (build {0} -> {1}).", s.ValheimBuild, st.BuildId);
                Discord.Send("⬆️ Steam ha actualizado Valheim en el server. Si algún mod falla al arrancar, hará falta su versión nueva.");
                s.ValheimBuild = st.BuildId; Settings.Save();
            }
            if (st.UpdatePending && announcedBuild != st.TargetBuildId)
            {
                announcedBuild = st.TargetBuildId;
                Log.W("Hay una actualización de Valheim para el server (build {0}): Steam la instala con el server apagado.", st.TargetBuildId);
                Discord.Send("🆕 Hay una actualización de Valheim. Steam la instalará en el server la próxima vez que esté apagado; hasta entonces, quien ya tenga el juego actualizado puede no poder entrar.");
            }
        }

        void PollBosses()
        {
            string keys;
            try { keys = Rcon.Exec("globalKeys"); }
            catch { return; }   // server arrancando o sin RCON: ya se mirará en la próxima vuelta
            var found = System.Text.RegularExpressions.Regex.Matches(keys ?? "", @"defeated_[a-z_]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value);
            foreach (var k in Bosses.Update(found))
            {
                Log.W("Jefe derrotado: {0}", Bosses.Name(k));
                Discord.Send("⚔️ **¡Habéis derrotado a " + Bosses.Name(k) + "!** " + BossHint(k));
                if (Rcon.Ready) Rcon.Say("[Majinheim] ¡Jefe derrotado: " + Bosses.Name(k) + "!");
            }
        }

        static string BossHint(string key)
        {
            switch (key)
            {
                case "defeated_eikthyr": return "Con su asta se fabrica el pico: a por el cobre y el estaño de la Selva Negra.";
                case "defeated_gdking": return "Su llave abre las criptas hundidas del Pantano.";
                case "defeated_bonemass": return "Su espoleta encuentra la plata de las Montañas.";
                case "defeated_dragon": return "Con sus lágrimas se hace la mesa de artesano: rumbo a las Llanuras.";
                case "defeated_goblinking": return "Con su espíritu se hace la luz para las Tierras Brumosas.";
                case "defeated_queen": return "Las Tierras de Ceniza os esperan.";
                case "defeated_fader": return "Habéis completado Valheim. ¡Leyendas!";
            }
            return "";
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
            Discord.Send("🆕 **" + (news.Count == 1 ? "Versión nueva de un mod" : "Versiones nuevas de " + news.Count + " mods") + "**\n" +
                string.Join("\n", news.Take(15).Select(u => "• " + Esc((string)u["nombre"]) + ": " + u["actual"] + " → " + u["nueva"])));
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
                        { "copiaExterna", Backup.OffsiteFolder == null ? null : Backup.OffsiteError != null ? "error" : "ok" },
                        { "copiaExternaUltima", Backup.OffsiteLast == DateTime.MinValue ? null : Backup.OffsiteLast.ToString("yyyy-MM-dd'T'HH:mm:sszzz") },
                        { "consola", Rcon.Ready },
                    }
                },
            };
        }
    }
}
#endif
