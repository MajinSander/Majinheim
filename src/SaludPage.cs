// Pestaña "Salud" de la edición Server (2.1, "Cero sustos"): revisión de todo lo que hace falta para que el server funcione,
// mantenimiento con red de seguridad (mods y Valheim al día con backup, prueba de arranque y vuelta atrás) e historial.
#if SERVER
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Majinheim
{
    public partial class MainForm
    {
        ListView healthList, maintList;
        Label healthSummary;
        CheckBox cAutoUpd;
        bool healthRunning, backupCheckRunning;
        volatile bool maintRunning;
        DateTime nextHealth = DateTime.Now.AddSeconds(40);          // primera revisión, al poco de abrir
        DateTime nextBackupCheck = DateTime.Now.AddMinutes(15);     // la semanal, sin prisa al abrir

        Panel BuildHealthPage()
        {
            var s = Settings.Current;
            healthSummary = Ui.Lbl("Haciendo la primera revisión...", 13, FontStyle.Bold, Th.Muted);
            healthList = Ui.List(false, "Estado|90", "Qué|230", "Detalle|780");
            maintList = Ui.List(false, "Fecha|130", "Resultado|95", "Pedido por|170", "Qué pasó|800");
            cAutoUpd = Ui.Chk("", s.AutoUpdateMods);
            cAutoUpd.MaximumSize = new Size(1000, 0);
            cAutoUpd.CheckedChanged += (o, e) =>
            {
                if (s.AutoUpdateMods == cAutoUpd.Checked) return;
                s.AutoUpdateMods = cAutoUpd.Checked; Settings.Save();
                Log.W(s.AutoUpdateMods ? "Mantenimiento diario activado." : "Mantenimiento diario desactivado: mods y Valheim se actualizan a mano.");
                RunHealth(false);
            };
            var bar = Ui.Col(healthSummary,
                Ui.Row(Ui.Btn("Revisar ahora", () => RunHealth(true)),
                       Ui.Btn("Actualizar ahora (con prueba)", () => ManualMaintenance(false), true),
                       Ui.Btn("Comprobar backups", () => RunBackupCheck(true)),
                       Ui.Btn("Reintentar las que fallaron", () => ManualMaintenance(true)),
                       Ui.Btn("Abrir registro de BepInEx", () => Ui.Open(LogCheck.LogFile))),
                cAutoUpd);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            t.Controls.Add(Ui.Lbl("Salud", 18, FontStyle.Bold), 0, 0);
            var sub = Ui.Lbl("Todo lo que hace falta para que el server funcione sin sustos. Se revisa solo cada 10 minutos y, si algo importante se estropea, avisa en Discord.", 9.5f, FontStyle.Regular, Th.Muted);
            sub.MaximumSize = new Size(900, 0);
            t.Controls.Add(sub, 0, 1);
            t.Controls.Add(bar, 0, 2);
            t.Controls.Add(Ui.Card(healthList, 6), 0, 3);
            t.Controls.Add(Ui.Lbl("Mantenimientos", 11, FontStyle.Bold), 0, 4);
            t.Controls.Add(Ui.Card(maintList, 6), 0, 5);
            var p = new Panel();
            p.Controls.Add(t);
            ShowHealth();
            return p;
        }

        void ShowHealth()
        {
            if (healthList == null) return;
            var s = Settings.Current;
            cAutoUpd.Text = "Mantenimiento diario: a las " + s.NightlyRestartHour.ToString("00") + ":00 (la hora del reinicio diario, se cambia en Automático) y solo si no hay nadie jugando, " +
                "actualizar los mods y Valheim: backup antes, prueba de arranque y vuelta atrás si algo falla. Se avisa en Discord.";
            if (cAutoUpd.Checked != s.AutoUpdateMods) cAutoUpd.Checked = s.AutoUpdateMods;

            var items = Health.Last;
            if (items != null)
            {
                int bad = items.Count(i => i.State == "mal"), warn = items.Count(i => i.State == "aviso");
                healthSummary.Text = "● " + Health.Summary(items) + "   (revisado a las " + Health.LastAt.ToString("HH:mm") + ")";
                healthSummary.ForeColor = bad > 0 ? Th.Bad : warn > 0 ? Th.Warn : Th.Good;
                healthList.BeginUpdate();
                healthList.Items.Clear();
                foreach (var i in items.OrderBy(x => x.State == "mal" ? 0 : x.State == "aviso" ? 1 : x.State == "ok" ? 2 : 3))
                {
                    var it = new ListViewItem(i.State == "ok" ? "● Bien" : i.State == "aviso" ? "● Aviso" : i.State == "mal" ? "● Mal" : "○ Info") { UseItemStyleForSubItems = false };
                    it.ForeColor = i.State == "ok" ? Th.Good : i.State == "aviso" ? Th.Warn : i.State == "mal" ? Th.Bad : Th.Muted;
                    it.SubItems.Add(i.Name, Th.Text, healthList.BackColor, healthList.Font);
                    it.SubItems.Add(i.Detail, i.State == "info" ? Th.Muted : Th.Text, healthList.BackColor, healthList.Font);
                    it.ToolTipText = i.Detail;
                    healthList.Items.Add(it);
                }
                healthList.EndUpdate();
            }

            maintList.BeginUpdate();
            maintList.Items.Clear();
            foreach (var h in Maintenance.History())
            {
                DateTime d;
                bool ok = DateTime.TryParse(h.Fecha, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d);
                var it = new ListViewItem(ok ? d.ToString("dd/MM  HH:mm") : h.Fecha) { UseItemStyleForSubItems = false };
                it.SubItems.Add(h.Nivel == "ok" ? "Bien" : h.Nivel == "aviso" ? "Con avisos" : "Falló", h.Nivel == "ok" ? Th.Good : h.Nivel == "aviso" ? Th.Warn : Th.Bad, maintList.BackColor, maintList.Font);
                it.SubItems.Add(h.Quien);
                it.SubItems.Add(h.Resumen);
                it.ToolTipText = h.Resumen;
                maintList.Items.Add(it);
            }
            if (maintList.Items.Count == 0) maintList.Items.Add(new ListViewItem(new[] { "", "", "", "Todavía no se ha hecho ninguno." }) { ForeColor = Th.Muted });
            maintList.EndUpdate();
        }

        // ------------------------------------------------------------ revisión de salud (en segundo plano: mira la red, el HTTPS, la IA...)

        void RunHealth(bool manual)
        {
            if (healthRunning) { if (manual) Status("Ya estoy revisando: un momento."); return; }
            healthRunning = true;
            nextHealth = DateTime.Now.AddMinutes(10);
            if (manual) Status("Revisando la salud del server...");
            Task.Factory.StartNew(() =>
            {
                var items = Health.Check(runner, latest, Modpack.Load());
                foreach (var a in Health.Alerts(items)) Discord.Send(a);
                return items;
            }).ContinueWith(t =>
            {
                healthRunning = false;
                if (t.Exception != null) Log.W("Salud: la revisión falló: {0}", t.Exception.GetBaseException().Message);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        ShowHealth();
                        if (manual && Health.Last != null) Status("Revisión hecha: " + Health.Summary(Health.Last));
                    }));
                }
                catch { }
            });
        }

        // ------------------------------------------------------------ comprobación de backups (cada semana, o al pulsar el botón)

        void RunBackupCheck(bool manual)
        {
            if (backupCheckRunning) { if (manual) Status("Ya estoy comprobando los backups: un momento."); return; }
            backupCheckRunning = true;
            nextBackupCheck = DateTime.Now.AddHours(1);
            if (manual) Status("Comprobando los backups (se abren y se leen enteros)...");
            Task.Factory.StartNew(() => BackupCheck.Run()).ContinueWith(t =>
            {
                backupCheckRunning = false;
                var r = t.Exception == null ? t.Result : new BackupCheck.Result { Text = "No he podido comprobarlos: " + t.Exception.GetBaseException().Message };
                if (!r.Ok && !manual) Discord.Send("🔴 **Comprobación semanal de backups**: " + r.Text);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        RunHealth(false);
                        if (manual) MessageBox.Show(this, r.Text, "Comprobación de backups", MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }));
                }
                catch { }
            });
        }

        // ------------------------------------------------------------ mantenimiento con red de seguridad

        bool SomeonePlaying() { return runner.Running && (runner.Players > 0 || ServerWatch.OnlineCount > 0); }

        void ManualMaintenance(bool retrySkipped)
        {
            if (busy || maintRunning) { MessageBox.Show(this, "Espera a que termine la tarea en curso.", "Majinheim"); return; }
            if (SomeonePlaying()) { MessageBox.Show(this, "Hay gente jugando. El mantenimiento apaga el server un rato: espera a que no haya nadie.", "Majinheim"); return; }
            if (retrySkipped && (Settings.Current.SkippedUpdates == null || Settings.Current.SkippedUpdates.Count == 0))
            { MessageBox.Show(this, "No hay ninguna versión que haya fallado la prueba.", "Majinheim"); return; }
            if (dirty && !Confirm("Tienes cambios en la pestaña Mods sin aplicar. El mantenimiento parte de la lista guardada y esos cambios se perderían.\n\n¿Seguir igualmente?")) return;
            if (!Confirm((retrySkipped ? "Voy a volver a probar las versiones de mods que fallaron la última vez, junto con lo que haya nuevo." : "Voy a mirar si hay versiones nuevas de los mods y de Valheim.") +
                "\n\nSi hay algo: backup del mundo, los ajustes y la lista de mods; apago el server; actualizo; pruebo que arranca y, si algo falla, vuelvo a como estaba. Tarda unos minutos y se avisa en Discord.\n\n¿Empezar?")) return;
            StartMaintenance(new Maintenance.Options { RetrySkipped = retrySkipped, Who = "launcher", Manual = true }, rep =>
            {
                if (rep != null) MessageBox.Show(this, rep.Text + (rep.Details.Count > 0 ? "\n\n" + string.Join("\n", rep.Details.Take(8)) : ""), "Mantenimiento", MessageBoxButtons.OK,
                    rep.Level == "ok" ? MessageBoxIcon.Information : rep.Level == "aviso" ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            });
        }

        // Lo piden la web y Discord (/actualizar). Se ejecuta en la ventana del launcher, como los botones.
        string RemoteMaintenance(string who)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(() => RemoteMaintenance(who)));
            if (maintRunning) return "Ya hay un mantenimiento en marcha: el resultado sale en Discord al terminar.";
            if (busy) return "El launcher está ocupado con otra tarea; prueba en un momento.";
            if (SomeonePlaying()) return "Hay gente jugando: el mantenimiento apaga el server un rato, así que espera a que no haya nadie.";
            if (dirty) return "En el launcher hay cambios de mods sin aplicar: hay que aplicarlos o deshacerlos antes.";
            StartMaintenance(new Maintenance.Options { Who = who, Manual = true }, null);
            return "Mirando si hay versiones nuevas de los mods y de Valheim. Si las hay: backup, actualizo, pruebo que arranca y, si algo falla, vuelvo atrás. El resultado sale en Discord en unos minutos.";
        }

        void StartMaintenance(Maintenance.Options o, Action<Maintenance.Report> after)
        {
            if (busy || maintRunning) return;
            maintRunning = true;
            Maintenance.Report rep = null;
            var ctx = new Maintenance.Context { Runner = runner, Status = Status, Playing = SomeonePlaying, Latest = latest };
            RunBg("Mantenimiento: empezando...", () =>
            {
                try { rep = Maintenance.Run(o, ctx); }
                finally { maintRunning = false; }
            }, () =>
            {
                pack = Modpack.Load();
                SetDirty(false);
                PopulateMods();
                UpdateServerState();
                RunHealth(false);
                ShowHealth();
                if (rep != null) Status("Mantenimiento: " + rep.Text);
                if (after != null) after(rep);
            });
        }

        // A la hora del reinicio diario: mantenimiento (si está activado) y, si no había nada que actualizar, el reinicio de siempre.
        void DailyTick(DateTime now)
        {
            var s = Settings.Current;
            string today = now.ToString("yyyy-MM-dd");
            if (now.Hour != s.NightlyRestartHour || s.MaintenanceDay == today || busy || maintRunning || SomeonePlaying()) return;
            bool restart = s.NightlyRestart && runner.Running && (now - runner.StartedAt).TotalHours >= 2;
            if (s.AutoUpdateMods && !dirty)
            {
                s.MaintenanceDay = today; Settings.Save();
                StartMaintenance(new Maintenance.Options { Who = "mantenimiento diario" }, rep =>
                {
                    if (rep != null && !rep.DidSomething && restart && runner.Running && !SomeonePlaying()) NightlyRestartNow();
                });
            }
            else if (restart)
            {
                s.MaintenanceDay = today; Settings.Save();
                NightlyRestartNow();
            }
        }

        void NightlyRestartNow()
        {
            Log.W("Reinicio diario programado.");
            Discord.Send("🔄 Reinicio diario del server (no hay nadie jugando). Vuelve en un par de minutos.");
            WebAction("reiniciar");
        }

        // Mods obsoletos o abandonados: se avisa una vez en Discord (tras cada búsqueda de versiones nuevas).
        void NoticeModsToWatch()
        {
            if (!Settings.Current.DiscordUpdates || pack == null) return;
            var news = ModWatch.NewNotices(Modpack.Load(), latest);
            if (news.Count == 0) return;
            Discord.Send("👀 **" + (news.Count == 1 ? "Un mod a vigilar" : news.Count + " mods a vigilar") + "**\n" + string.Join("\n", news.Take(15)) +
                "\nDe momento funcionan. Si con una versión nueva de Valheim dejan de ir, tocará buscarles relevo.");
        }

        // Para la web de administración.
        Dictionary<string, object> HealthExtras()
        {
            var s = Settings.Current;
            var items = Health.Last;
            return new Dictionary<string, object>
            {
                { "salud", items == null ? null : new Dictionary<string, object>
                    {
                        { "resumen", Health.Summary(items) },
                        { "fecha", Health.LastAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz") },
                        { "items", items.Select(i => new Dictionary<string, object> { { "clave", i.Key }, { "nombre", i.Name }, { "estado", i.State }, { "detalle", i.Detail } }).ToList() },
                    }
                },
                { "mantenimiento", new Dictionary<string, object>
                    {
                        { "auto", s.AutoUpdateMods },
                        { "hora", s.NightlyRestartHour.ToString("00") + ":00" },
                        { "enCurso", maintRunning },
                        { "historial", Maintenance.History().Take(5).Select(h => new Dictionary<string, object> { { "fecha", h.Fecha }, { "nivel", h.Nivel }, { "resumen", h.Resumen }, { "quien", h.Quien } }).ToList() },
                    }
                },
            };
        }
    }
}
#endif
