// Página "Asistente IA" de la edición Server: IA local (Ollama) que responde en Discord con /preguntar.
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
        Label aiState, aiDetail, aiAnswer, aiModelInfo;
        CheckBox cAiOn;
        ComboBox cbAiModel;
        TextBox tAiUrl, tAiQuestion;
        Button bAiPull, bAiAsk;
        bool aiPopulating, aiPulling, aiAsking, aiChecking;
        AiChat.Check aiLast;

        Panel BuildAiPage()
        {
            var s = Settings.Current;
            aiPopulating = true;
            aiState = Ui.Lbl("● Comprobando...", 15, FontStyle.Bold, Th.Muted);
            aiDetail = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            aiDetail.MaximumSize = new Size(480, 0);
            cAiOn = Ui.Chk("Comando /preguntar en Discord (para todos los del Discord)", s.AiEnabled);
            cAiOn.CheckedChanged += (o, e) =>
            {
                if (aiPopulating) return;
                s.AiEnabled = cAiOn.Checked; Settings.Save();
                Log.W("Asistente IA {0}.", s.AiEnabled ? "activado" : "desactivado");
                DiscordBot.Apply();   // vuelve a conectar y registra (o quita) /preguntar
                RefreshAi();
            };

            cbAiModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, BackColor = Th.Input, ForeColor = Th.Text, FlatStyle = FlatStyle.Flat, Font = Th.F(9.5f), Width = 220, Margin = new Padding(0, 2, 12, 2) };
            cbAiModel.Items.AddRange(AiChat.Models.Select(m => (object)m[0]).ToArray());
            cbAiModel.Text = AiChat.Model;
            aiModelInfo = Ui.Lbl("", 9f, FontStyle.Regular, Th.Muted);
            aiModelInfo.MaximumSize = new Size(480, 0);
            Action saveModel = () =>
            {
                if (aiPopulating) return;
                string m = cbAiModel.Text.Trim();
                if (m == "" || m == s.OllamaModel) return;
                s.OllamaModel = m; Settings.Save();
                Log.W("Asistente IA: modelo {0}.", m);
                RefreshAi();
            };
            cbAiModel.SelectedIndexChanged += (o, e) => saveModel();
            cbAiModel.Leave += (o, e) => saveModel();

            tAiUrl = Ui.Txt(s.OllamaUrl, 220);
            tAiUrl.Leave += (o, e) =>
            {
                string u = tAiUrl.Text.Trim().TrimEnd('/');
                if (u == "") u = AiChat.DefaultUrl;
                Uri parsed;
                if (!Uri.TryCreate(u, UriKind.Absolute, out parsed) || (parsed.Scheme != "http" && parsed.Scheme != "https")) { tAiUrl.Text = s.OllamaUrl; return; }
                tAiUrl.Text = u;
                if (u == s.OllamaUrl) return;
                s.OllamaUrl = u; Settings.Save();
                RefreshAi();
            };

            bAiPull = Ui.Btn("Descargar el modelo", PullAiModel, true);
            var left = Ui.Col(aiState, aiDetail, cAiOn,
                Ui.Lbl("Modelo", 9.5f), cbAiModel, aiModelInfo,
                Ui.Row(bAiPull, Ui.Btn("Instalar Ollama", () => Ui.Open(AiChat.DownloadPage)), Ui.Btn("Comprobar", RefreshAi)),
                Ui.Lbl("Dirección de Ollama (no la cambies si está en este PC)", 9.5f), tAiUrl);
            aiPopulating = false;

            var how = Ui.Lbl("Cómo se prepara (una sola vez):\n" +
                "1. \"Instalar Ollama\" abre su página: descarga el instalador para Windows y ábrelo. Se queda funcionando en segundo plano, junto al reloj.\n" +
                "2. \"Descargar el modelo\": se baja la IA (unos 5 GB). Con la gráfica de este PC responde en pocos segundos.\n" +
                "3. Marca \"Comando /preguntar en Discord\". El bot tarda unos segundos en reconectarse.", 9f, FontStyle.Regular, Th.Muted);
            how.MaximumSize = new Size(480, 0);
            left.Controls.Add(how);

            // ---- Probar aquí
            tAiQuestion = Ui.Txt("¿Quién ha jugado más esta semana?", 500);
            tAiQuestion.MaxLength = AiChat.MaxQuestion;
            tAiQuestion.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AskAiHere(); } };
            bAiAsk = Ui.Btn("Preguntar", AskAiHere, true);
            aiAnswer = Ui.Lbl("", 10, FontStyle.Regular, Th.Text);
            aiAnswer.MaximumSize = new Size(560, 0);
            var what = Ui.Lbl("Qué sabe: si el server está encendido, quién juega y cuánto lleva, horas y partidas de cada vikingo, días y horas en que se juega, " +
                "muertes, jefes derrotados, mods, último backup y día del mundo. También responde dudas generales de Valheim.\n\n" +
                "Ejemplos: \"¿quién jugó ayer?\", \"¿cuántas horas lleva Benja este mes?\", \"¿a qué hora se suele jugar?\", \"¿qué jefe nos falta?\".\n\n" +
                "Solo lee: no puede encender, apagar ni cambiar nada. Funciona en este PC, así que las preguntas y los datos no salen a internet, " +
                "y nunca ve IDs de Steam ni IPs. A los administradores del Discord también les puede mirar el registro del server para buscar errores.", 9f, FontStyle.Regular, Th.Muted);
            what.MaximumSize = new Size(560, 0);
            var right = Ui.Col(Ui.Lbl("Probar aquí", 11, FontStyle.Bold),
                Ui.Lbl("Pregunta como lo harías en Discord (aquí respondes como administrador):", 9.5f, FontStyle.Regular, Th.Muted),
                tAiQuestion, bAiAsk, aiAnswer,
                Ui.Lbl("Qué puede responder", 11, FontStyle.Bold), what);

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Bg };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.Controls.Add(Ui.Card(Ui.Col(Ui.Lbl("Estado", 11, FontStyle.Bold), left)), 0, 0);
            top.Controls.Add(Ui.Card(right), 1, 0);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(Ui.Lbl("Asistente IA", 18, FontStyle.Bold), 0, 0);
            var sub = Ui.Lbl("Una IA que funciona en este PC y responde en vuestro Discord con /preguntar, en lenguaje normal, con los datos reales del server.", 9.5f, FontStyle.Regular, Th.Muted);
            sub.MaximumSize = new Size(900, 0);
            t.Controls.Add(sub, 0, 1);
            t.Controls.Add(top, 0, 2);
            var p = new Panel();
            p.Controls.Add(t);
            UpdateAiState();
            return p;
        }

        // Mira Ollama en segundo plano (puede tardar si no está) y repinta.
        void RefreshAi()
        {
            if (aiChecking || aiState == null) return;
            aiChecking = true;
            Task.Factory.StartNew(() => AiChat.Status()).ContinueWith(t =>
            {
                aiChecking = false;
                aiLast = t.Exception == null ? t.Result : null;
                if (!IsDisposed) try { BeginInvoke((Action)UpdateAiState); } catch { }
            });
        }

        void UpdateAiState()
        {
            if (aiState == null) return;
            var s = Settings.Current;
            var info = AiChat.Models.FirstOrDefault(m => m[0] == AiChat.Model);
            aiModelInfo.Text = info != null ? info[1] : "Modelo elegido a mano: tiene que aceptar herramientas (\"tools\") en Ollama.";
            if (aiPulling) return;   // la descarga va escribiendo su progreso
            var c = aiLast;
            bool ready = c != null && c.Up && c.HasModel;
            bAiPull.Enabled = c != null && c.Up && !c.HasModel;
            bAiAsk.Enabled = ready && !aiAsking;
            if (c == null) { aiState.Text = "● Comprobando..."; aiState.ForeColor = Th.Muted; aiDetail.Text = ""; return; }
            aiState.Text = !ready ? "● Sin preparar" : s.AiEnabled ? "● Funcionando" : "● Lista (apagada en Discord)";
            aiState.ForeColor = !ready ? Th.Warn : s.AiEnabled ? Th.Good : Th.Muted;
            string bot = !s.AiEnabled ? "" : !Discord.Configured ? "\nFalta configurar el bot de Discord (pestaña Automático)."
                : !s.DiscordCommands ? "\nLos comandos del bot están desactivados (pestaña Automático)." : "\nEn Discord: /preguntar.";
            aiDetail.Text = c.Text + bot;
        }

        void PullAiModel()
        {
            if (aiPulling) return;
            string model = AiChat.Model;
            if (!Confirm("¿Descargar el modelo " + model + "? Son varios GB: puede tardar un rato según la conexión.")) return;
            aiPulling = true;
            bAiPull.Enabled = false;
            aiState.Text = "● Descargando..."; aiState.ForeColor = Th.Accent;
            Log.W("Asistente IA: descargando el modelo {0}.", model);
            Action<string> show = txt => { if (!IsDisposed) try { BeginInvoke((Action)(() => { aiDetail.Text = txt; })); } catch { } };
            Task.Factory.StartNew(() => AiChat.Pull(model, show)).ContinueWith(t =>
            {
                aiPulling = false;
                string err = t.Exception != null ? t.Exception.GetBaseException().Message : null;
                if (err != null) Log.W("Asistente IA: la descarga del modelo falló: {0}", err);
                else Log.W("Asistente IA: modelo {0} descargado.", model);
                if (!IsDisposed) try { BeginInvoke((Action)(() =>
                {
                    if (err != null) MessageBox.Show(this, "No se pudo descargar el modelo: " + err, "Majinheim");
                    RefreshAi();
                })); } catch { }
            });
        }

        void AskAiHere()
        {
            if (aiAsking || !bAiAsk.Enabled) return;
            string q = tAiQuestion.Text.Trim();
            if (q == "") return;
            aiAsking = true; bAiAsk.Enabled = false;
            aiAnswer.ForeColor = Th.Muted; aiAnswer.Text = "Pensando...";
            var t0 = DateTime.Now;
            Task.Factory.StartNew(() => AiChat.Ask(q, null, true)).ContinueWith(t =>
            {
                aiAsking = false;
                string a = t.Exception != null ? "Error: " + t.Exception.GetBaseException().Message : t.Result;
                double secs = (DateTime.Now - t0).TotalSeconds;
                if (!IsDisposed) try { BeginInvoke((Action)(() =>
                {
                    aiAnswer.ForeColor = Th.Text;
                    aiAnswer.Text = a.Replace("**", "") + "\n\n(" + secs.ToString("0.0") + " s)";
                    UpdateAiState();
                })); } catch { }
            });
        }
    }
}
#endif
