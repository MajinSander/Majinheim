// Vida en el Discord (2.3), la parte del launcher del server: resumen de la semana, hitos, quedadas (aviso 15 minutos
// antes y encender el server) y server bajo demanda (se apaga solo cuando lleva un rato vacío). Edición Server.
#if SERVER
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Majinheim
{
    public partial class MainForm
    {
        CheckBox cWeekly, cMilestones, cMeetups, cMeetupStart, cOnDemand;
        ComboBox cbIdle;
        DateTime nextMilestones = DateTime.Now.AddMinutes(1);
        bool weeklyRunning, milestonesRunning, idleStopping;
        int idleStopMinutes;
        static readonly int[] IdleChoices = { 15, 30, 45, 60, 90, 120 };

        // Las casillas de la pestaña Automático (en la tarjeta de Discord).
        Control[] BuildCommunityControls()
        {
            var s = Settings.Current;
            cWeekly = Ui.Chk("Resumen de la semana los domingos a las 21:00 (lo escribe la IA si está activada)", s.WeeklySummary);
            cMilestones = Ui.Chk("Hitos: alguien llega a 100 horas, récord de partida más larga, alguien entra por primera vez...", s.Milestones);
            cMeetups = Ui.Chk("Quedadas con /quedada: el bot apunta quién va y les avisa 15 minutos antes", s.Meetups);
            cMeetupStart = Ui.Chk("y enciende el server si está apagado", s.MeetupStart);
            cMeetupStart.Margin = new Padding(22, cMeetupStart.Margin.Top, 0, cMeetupStart.Margin.Bottom);
            cOnDemand = Ui.Chk("Server bajo demanda: cualquiera del Discord puede encenderlo con /encender, y se apaga solo cuando lleva sin nadie", s.OnDemand);
            int ii = Array.IndexOf(IdleChoices, s.OnDemandIdleMinutes);
            cbIdle = Combo(IdleChoices.Select(m => m + " minutos"), ii < 0 ? 1 : ii);
            cbIdle.Margin = new Padding(22, 0, 0, 6);
            foreach (var c in new Control[] { cWeekly, cMilestones, cMeetups, cMeetupStart, cOnDemand }) c.MaximumSize = new Size(540, 0);
            cWeekly.CheckedChanged += (o, e) => { if (autoPopulating) return; s.WeeklySummary = cWeekly.Checked; Settings.Save(); };
            cMilestones.CheckedChanged += (o, e) => { if (autoPopulating) return; s.Milestones = cMilestones.Checked; Settings.Save(); };
            cMeetups.CheckedChanged += (o, e) => { if (autoPopulating) return; s.Meetups = cMeetups.Checked; Settings.Save(); cMeetupStart.Enabled = s.Meetups; DiscordBot.Apply(); UpdateAutoState(); };
            cMeetupStart.CheckedChanged += (o, e) => { if (autoPopulating) return; s.MeetupStart = cMeetupStart.Checked; Settings.Save(); };
            cOnDemand.CheckedChanged += (o, e) =>
            {
                if (autoPopulating) return;
                s.OnDemand = cOnDemand.Checked; Settings.Save();
                cbIdle.Enabled = s.OnDemand;
                Log.W("Server bajo demanda {0}.", s.OnDemand ? "activado: cualquiera del Discord lo enciende y se apaga solo vacío" : "desactivado");
                DiscordBot.Apply();   // /encender pasa a ser para todos (o vuelve a ser de administradores)
                UpdateAutoState();
            };
            cbIdle.SelectedIndexChanged += (o, e) => { if (autoPopulating) return; s.OnDemandIdleMinutes = IdleChoices[cbIdle.SelectedIndex]; Settings.Save(); };
            cMeetupStart.Enabled = s.Meetups;
            cbIdle.Enabled = s.OnDemand;
            return new Control[] { Ui.Lbl("Vida en el Discord", 11, FontStyle.Bold), cWeekly, cMilestones, cMeetups, cMeetupStart, cOnDemand, cbIdle };
        }

        // Cada 20 segundos, desde AutoTick.
        void CommunityTick(DateTime now)
        {
            var s = Settings.Current;

            // Hitos (cada 2 minutos). Con los hitos apagados se siguen apuntando, para no soltar luego los viejos de golpe.
            if (now >= nextMilestones && !milestonesRunning)
            {
                nextMilestones = now.AddMinutes(2);
                milestonesRunning = true;
                bool tell = s.Milestones && Discord.Configured;
                Task.Factory.StartNew(() => { foreach (var m in Community.NewMilestones()) if (tell) Discord.Send(m); })
                    .ContinueWith(t => { milestonesRunning = false; if (t.Exception != null) Log.W("Hitos: {0}", t.Exception.GetBaseException().Message); });
            }

            // Resumen de la semana: domingo a partir de las 21:00, una vez.
            string today = now.ToString("yyyy-MM-dd");
            if (now.DayOfWeek == DayOfWeek.Sunday && now.Hour >= 21 && s.WeeklySummary && Discord.Configured && s.WeeklyDone != today && !weeklyRunning)
            {
                weeklyRunning = true;
                s.WeeklyDone = today; Settings.Save();
                Task.Factory.StartNew(() =>
                {
                    var w = Community.GetWeek(now);
                    if (w.Empty && w.PrevMinutes == 0) { Log.W("Resumen de la semana: dos semanas sin jugar, no lo mando."); return; }
                    bool ai;
                    string text = Community.WeeklyMessage(w, out ai);
                    Discord.Send(text);
                    Log.W("Resumen de la semana enviado{0}.", ai ? " (escrito por la IA)" : "");
                }).ContinueWith(t => { weeklyRunning = false; if (t.Exception != null) Log.W("Resumen de la semana: {0}", t.Exception.GetBaseException().Message); });
            }

            MeetupTick(now);
            IdleTick(now);
        }

        void MeetupTick(DateTime now)
        {
            foreach (var m in Community.Meetups())
            {
                if (m.Done) continue;
                var at = m.At;
                if (at == DateTime.MinValue) continue;
                if (!m.Cancelled && !m.Reminded && now >= at.AddMinutes(-15))
                {
                    Community.Change(m.Id, x => { if (x != null) x.Reminded = true; return x; });
                    if (now > at.AddMinutes(30)) continue;   // el launcher estaba cerrado a esa hora: ya no tiene sentido avisar
                    string note;
                    if (runner.Running) note = "🟢 El server está encendido. ¡Nos vemos dentro!";
                    else if (!Settings.Current.MeetupStart) note = "⚫ El server está apagado: un administrador lo puede encender con /encender.";
                    else if (maintRunning) note = "🛠️ El server está en mantenimiento: estará listo en unos minutos.";
                    else
                    {
                        string r = WebAction("iniciar");
                        Log.W("Quedada de las {0:HH:mm}: enciendo el server ({1}).", at, r);
                        note = r.StartsWith("Arrancando") ? "🟡 Estoy encendiendo el server: en un par de minutos está listo." : "⚫ No he podido encender el server: " + r;
                    }
                    var copy = m;
                    Task.Factory.StartNew(() => DiscordBot.MeetupReminder(copy, note));
                }
                if (now >= at.AddHours(3))
                {
                    var done = Community.Change(m.Id, x => { if (x != null) x.Done = true; return x; });
                    if (done != null && !done.Cancelled) Task.Factory.StartNew(() => DiscordBot.CloseMeetupMessage(done));
                }
            }
        }

        // Server bajo demanda: si lleva el rato elegido sin nadie (y no hay una quedada a punto), se guarda el mundo y se apaga.
        void IdleTick(DateTime now)
        {
            var s = Settings.Current;
            if (!s.OnDemand || idleStopping || busy || maintRunning || !runner.Running || runner.Status != "En marcha") return;
            if (ServerWatch.OnlineCount > 0 || runner.Players > 0) return;
            var since = ServerWatch.LastActivity > runner.StartedAt ? ServerWatch.LastActivity : runner.StartedAt;
            int idle = (int)(now - since).TotalMinutes;
            if (idle < s.OnDemandIdleMinutes || Community.MeetupSoon(now)) return;
            idleStopping = true;
            idleStopMinutes = idle;
            Log.W("Server bajo demanda: lleva {0} min sin nadie, lo apago.", idle);
            RunBg("Apagando el server (lleva " + idle + " min sin nadie)...", () => runner.Stop(true), () => { idleStopping = false; });
        }

        // Para OnRunnerState: el mensaje de "apagado" cuando lo ha apagado el modo bajo demanda.
        string IdleStopMessage()
        {
            if (idleStopMinutes <= 0) return null;
            string msg = "💤 Server apagado: llevaba " + idleStopMinutes + " minutos sin nadie (el mundo está guardado). Para encenderlo, /encender en Discord.";
            idleStopMinutes = 0;
            return msg;
        }
    }
}
#endif
