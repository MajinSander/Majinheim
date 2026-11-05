// Interfaz grafica (WinForms). La misma ventana sirve a las dos ediciones; #if SERVER elige las páginas.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Majinheim
{
    static class Th
    {
        public static readonly Color Bg = Color.FromArgb(24, 27, 32);
        public static readonly Color Side = Color.FromArgb(16, 18, 22);
        public static readonly Color Card = Color.FromArgb(33, 38, 45);
        public static readonly Color Input = Color.FromArgb(44, 50, 59);
        public static readonly Color Border = Color.FromArgb(62, 70, 81);
        public static readonly Color Text = Color.FromArgb(232, 230, 227);
        public static readonly Color Muted = Color.FromArgb(150, 160, 170);
        public static readonly Color Accent = Color.FromArgb(201, 119, 43);
        public static readonly Color AccentHi = Color.FromArgb(224, 141, 62);
        public static readonly Color Good = Color.FromArgb(104, 192, 104);
        public static readonly Color Bad = Color.FromArgb(226, 96, 90);
        public static readonly Color Warn = Color.FromArgb(232, 190, 80);
        public static Font F(float size, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", size, st); }
        public static readonly Font Mono = new Font("Consolas", 9f);
    }

    static class Ui
    {
        public static Button Btn(string text, Action click, bool primary = false)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = primary ? Th.Accent : Th.Input, ForeColor = Color.White,
                Font = Th.F(9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 5, 10, 5),
                Margin = new Padding(0, 0, 8, 8), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = primary ? Th.Accent : Th.Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Th.AccentHi : Th.Border;
            b.Click += (s, e) => click();
            return b;
        }

        public static Label Lbl(string text, float size = 9.5f, FontStyle st = FontStyle.Regular, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = Th.F(size, st), ForeColor = color ?? Th.Text, BackColor = Color.Transparent, Margin = new Padding(0, 5, 8, 5) };
        }

        public static TextBox Txt(string val, int width = 260)
        {
            return new TextBox { Text = val ?? "", Width = width, BackColor = Th.Input, ForeColor = Th.Text, BorderStyle = BorderStyle.FixedSingle, Font = Th.F(9.5f), Margin = new Padding(0, 2, 12, 6) };
        }

        public static CheckBox Chk(string text, bool val)
        {
            return new CheckBox { Text = text, Checked = val, AutoSize = true, ForeColor = Th.Text, BackColor = Color.Transparent, Font = Th.F(9.5f), Margin = new Padding(0, 4, 12, 4), Cursor = Cursors.Hand };
        }

        public static FlowLayoutPanel Flow(FlowDirection dir, params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = dir, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent };
            f.Controls.AddRange(cs);
            return f;
        }
        public static FlowLayoutPanel Row(params Control[] cs) { return Flow(FlowDirection.LeftToRight, cs); }
        public static FlowLayoutPanel Col(params Control[] cs) { return Flow(FlowDirection.TopDown, cs); }

        public static Panel Card(Control inner, int pad = 14)
        {
            var p = new Panel { BackColor = Th.Card, Padding = new Padding(pad), Margin = new Padding(0, 0, 12, 12), Dock = DockStyle.Fill };
            inner.Dock = DockStyle.Fill;
            p.Controls.Add(inner);
            return p;
        }

        public static ListView List(bool checks, params string[] cols)
        {
            var l = new ListView { View = View.Details, FullRowSelect = true, CheckBoxes = checks, BackColor = Th.Card, ForeColor = Th.Text, BorderStyle = BorderStyle.None, Font = Th.F(9.5f), Dock = DockStyle.Fill, HideSelection = false, GridLines = false };
            foreach (var c in cols)
            {
                var parts = c.Split('|');
                l.Columns.Add(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 120);
            }
            return l;
        }

        public static RichTextBox LogBox()
        {
            return new RichTextBox { ReadOnly = true, BackColor = Color.FromArgb(13, 15, 18), ForeColor = Th.Text, BorderStyle = BorderStyle.None, Font = Th.Mono, Dock = DockStyle.Fill, WordWrap = false, DetectUrls = false, HideSelection = false };
        }

        public static void Append(RichTextBox box, IEnumerable<KeyValuePair<string, Color>> lines)
        {
            if (box.TextLength > 600000)
            {
                box.Select(0, box.TextLength / 3);
                box.SelectedText = "";
            }
            foreach (var l in lines)
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = l.Value;
                box.AppendText(l.Key + "\n");
            }
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }

        public static TableLayoutPanel Grid(int cols)
        {
            var t = new TableLayoutPanel { ColumnCount = cols, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0) };
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        public static string Ask(IWin32Window owner, string title, string prompt, string def, bool secret = false)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Th.Bg, ForeColor = Th.Text, Font = Th.F(9.5f), AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(560, 150) })
            {
                var lbl = Lbl(prompt); lbl.MaximumSize = new Size(530, 0); lbl.Location = new Point(14, 12);
                var tb = Txt(def, 530); tb.Location = new Point(14, 62);
                tb.UseSystemPasswordChar = secret;
                string result = null;
                var ok = Btn("Aceptar", () => { result = tb.Text; f.DialogResult = DialogResult.OK; }, true); ok.Location = new Point(360, 100);
                var cancel = Btn("Cancelar", () => f.DialogResult = DialogResult.Cancel); cancel.Location = new Point(460, 100);
                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }

        public static string Size(long b) { return b > 1048576 ? (b / 1048576.0).ToString("0.0") + " MB" : (b / 1024.0).ToString("0") + " KB"; }

        public static void Open(string target) { try { Process.Start(target); } catch (Exception e) { MessageBox.Show(e.Message); } }
    }

    public partial class MainForm : Form
    {
        Panel side, content;
        FlowLayoutPanel nav;
        Label statusLbl;
        ProgressBar progress;
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        readonly ConcurrentQueue<Tuple<RichTextBox, string, Color>> pending = new ConcurrentQueue<Tuple<RichTextBox, string, Color>>();
        readonly Timer uiTimer = new Timer { Interval = 400 };
        RichTextBox launcherLog;
        bool busy;
        readonly bool autostart;
        Modpack pack;
        public string StartPage;

        public MainForm(bool autostart)
        {
            this.autostart = autostart;
            Text = "Majinheim Launcher  " + AppInfo.EditionName + "  v" + AppInfo.Version;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Th.Bg; ForeColor = Th.Text; Font = Th.F(9.5f);
            ClientSize = new Size(1290, 790);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterScreen;

            content = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg, Padding = new Padding(24, 18, 12, 6) };
            side = new Panel { Dock = DockStyle.Left, Width = 215, BackColor = Th.Side };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Th.Side, Padding = new Padding(14, 6, 14, 6) };
            statusLbl = Ui.Lbl("Listo", 9, FontStyle.Regular, Th.Muted);
            statusLbl.Dock = DockStyle.Fill; statusLbl.AutoSize = false; statusLbl.Margin = new Padding(0);
            progress = new ProgressBar { Dock = DockStyle.Right, Width = 200, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false };
            bottom.Controls.Add(statusLbl);
            bottom.Controls.Add(progress);
            Controls.Add(content);
            Controls.Add(side);
            Controls.Add(bottom);

            nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Th.Side, Padding = new Padding(0, 6, 0, 0) };
            var logo = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Th.Side };
            var l1 = Ui.Lbl("MAJINHEIM", 19, FontStyle.Bold, Th.Accent); l1.Location = new Point(18, 16);
            var l2 = Ui.Lbl(AppInfo.IsServer ? "Edición Server" : "Launcher de jugador", 9.5f, FontStyle.Regular, Th.Muted); l2.Location = new Point(21, 54);
            logo.Controls.Add(l1); logo.Controls.Add(l2);
            var ver = Ui.Lbl("v" + AppInfo.Version + "  ·  Valheim 1.0", 8.5f, FontStyle.Regular, Th.Muted);
            ver.Dock = DockStyle.Bottom; ver.AutoSize = false; ver.Height = 28; ver.Padding = new Padding(20, 0, 0, 0);
            side.Controls.Add(nav);
            side.Controls.Add(logo);
            side.Controls.Add(ver);

            launcherLog = Ui.LogBox();
            Log.Line += l => Enqueue(launcherLog, l, l.Contains("ERROR") ? Th.Bad : l.Contains("AVISO") || l.Contains("Ojo") ? Th.Warn : Th.Muted);
            uiTimer.Tick += (s, e) => Tick();
            uiTimer.Start();

#if SERVER
            BuildServer();
#else
            BuildClient();
#endif
            AddPage("Registro", "≡", PageWithTitle("Registro del launcher", "Todo lo que hace el launcher queda guardado en logs\\launcher.log.", launcherLog,
                Ui.Row(Ui.Btn("Abrir carpeta de logs", () => Ui.Open(AppInfo.LogDir)))));
        }

        // ------------------------------------------------------------ comun

        void AddPage(string name, string icon, Panel page)
        {
            page.Dock = DockStyle.Fill; page.Visible = false; page.BackColor = Th.Bg;
            content.Controls.Add(page);
            pages[name] = page;
            var b = new Button { Text = "   " + icon + "    " + name, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, Width = 215, Height = 46, ForeColor = Th.Text, BackColor = Th.Side, Font = Th.F(10.5f), Margin = new Padding(0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Th.Card;
            b.Click += (s, e) => ShowPage(name);
            nav.Controls.Add(b);
            navButtons[name] = b;
            if (pages.Count == 1) ShowPage(name);
        }

        void ShowPage(string name)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navButtons)
            {
                kv.Value.BackColor = kv.Key == name ? Th.Card : Th.Side;
                kv.Value.ForeColor = kv.Key == name ? Th.Accent : Th.Text;
            }
#if SERVER
            if (name == "Backups") RefreshBackups();
            if (name == "Ajustes de mods" && !CfgDirty()) ReloadCfg();
            if (name == "Asistente IA") RefreshAi();
#endif
        }

        // Página estandar: titulo, subtitulo, barra de botones y un control que llena el resto
        Panel PageWithTitle(string title, string subtitle, Control fill, Control toolbar)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(Ui.Lbl(title, 18, FontStyle.Bold), 0, 0);
            var sub = Ui.Lbl(subtitle, 9.5f, FontStyle.Regular, Th.Muted); sub.MaximumSize = new Size(900, 0);
            t.Controls.Add(sub, 0, 1);
            if (toolbar != null) t.Controls.Add(toolbar, 0, 2);
            var card = Ui.Card(fill, 6);
            t.Controls.Add(card, 0, 3);
            var p = new Panel();
            p.Controls.Add(t);
            return p;
        }

        void Enqueue(RichTextBox box, string line, Color c) { pending.Enqueue(Tuple.Create(box, line, c)); }

        void Tick()
        {
            if (!pending.IsEmpty)
            {
                var byBox = new Dictionary<RichTextBox, List<KeyValuePair<string, Color>>>();
                Tuple<RichTextBox, string, Color> it;
                int n = 0;
                while (n++ < 1500 && pending.TryDequeue(out it))
                {
                    if (!byBox.ContainsKey(it.Item1)) byBox[it.Item1] = new List<KeyValuePair<string, Color>>();
                    byBox[it.Item1].Add(new KeyValuePair<string, Color>(it.Item2, it.Item3));
                }
                foreach (var kv in byBox) Ui.Append(kv.Key, kv.Value);
            }
#if SERVER
            TickServer();
#endif
        }

        void Status(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Status(s))); } catch { } return; }
            statusLbl.Text = s;
        }

        void RunBg(string text, Action work, Action done)
        {
            if (busy) { MessageBox.Show(this, "Espera a que termine la tarea en curso.", "Majinheim", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            busy = true; progress.Visible = true; Status(text); UseWaitCursor = true;
            Task.Factory.StartNew(work).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    busy = false; progress.Visible = false; UseWaitCursor = false;
                    if (t.Exception != null)
                    {
                        var ex = t.Exception.GetBaseException();
                        Log.W("ERROR: {0}", ex.Message);
                        Status("Error: " + ex.Message);
                        MessageBox.Show(this, ex.Message, "Algo ha fallado", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        Status("Listo");
                        if (done != null) done();
                    }
                }));
            });
        }

        bool Confirm(string msg) { return MessageBox.Show(this, msg, "Majinheim", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        static Color LineColor(string l)
        {
            if (l.StartsWith("[Error") || l.StartsWith("[Fatal") || l.Contains("Exception")) return Th.Bad;
            if (l.StartsWith("[Warning")) return Th.Warn;
            if (l.Contains("Got character ZDOID") || l.Contains("Game server connected")) return Th.Good;
            if (l.StartsWith("[Message")) return Th.Accent;
            return Color.FromArgb(200, 204, 208);
        }

#if SERVER
        // ============================================================ EDICION SERVER

        readonly ServerRunner runner = new ServerRunner();
        NotifyIcon tray;
        bool exiting;
        int webTicks;
        Label srvState, srvPlayers, srvUptime, srvJoins, srvWarn;
        RichTextBox srvLog;
        Button bStart, bStop, bRestart;
        TextBox tName, tWorld, tPass, tPort, tExtra, tServerDir;
        CheckBox cPublic, cCross, cAuto, cWinStart;
        ListView modList;
        Label modsInfo, modsDirty;
        readonly Dictionary<string, PkgInfo> latest = new Dictionary<string, PkgInfo>(StringComparer.OrdinalIgnoreCase);
        bool dirty, populating;
        TextBox tRepo, tToken, tHost, tLan, tMsg, tInstr;
        Label pubState, routerInfo;
        ListView bkList;

        void BuildServer()
        {
            pack = Modpack.Load();
            srvLog = Ui.LogBox();
            runner.GameLog += l => Enqueue(srvLog, l, LineColor(l));
            runner.StateChanged += () => { if (!IsDisposed) try { BeginInvoke((Action)UpdateServerState); } catch { } };

            AddPage("Servidor", "▶", BuildServerPage());
            AddPage("Mods", "☰", BuildModsPage());
            AddPage("Ajustes de mods", "⚙", BuildConfigPage());
            AddPage("Amigos", "☺", BuildFriendsPage());
            AddPage("Backups", "↺", BuildBackupsPage());
            AddPage("Web", "◎", BuildWebPage());
            AddPage("Automático", "⏱", BuildAutoPage());
            AddPage("Asistente IA", "✦", BuildAiPage());

            tray = new NotifyIcon { Icon = Icon, Text = "Majinheim Server", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, (s, e) => ShowFromTray());
            menu.Items.Add("Detener server", null, (s, e) => runner.Stop(false));
            menu.Items.Add("Salir", null, (s, e) => { ShowFromTray(); Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowFromTray();
        }

        void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        Panel BuildServerPage()
        {
            var s = Settings.Current;
            srvState = Ui.Lbl("● Detenido", 17, FontStyle.Bold, Th.Muted);
            srvPlayers = Ui.Lbl("Jugadores conectados: 0");
            srvUptime = Ui.Lbl("Tiempo en marcha: -", 9.5f, FontStyle.Regular, Th.Muted);
            bStart = Ui.Btn("▶  Iniciar", StartServer, true);
            bStop = Ui.Btn("■  Detener", () => runner.Stop(false));
            bRestart = Ui.Btn("⟳  Reiniciar", RestartServer);
            cAuto = Ui.Chk("Reiniciar solo si se cae", s.AutoRestart);
            cAuto.CheckedChanged += (o, e) => { s.AutoRestart = cAuto.Checked; Settings.Save(); };
            cWinStart = Ui.Chk("Arrancar con Windows", s.AutoStartServer);
            cWinStart.CheckedChanged += (o, e) => SetWindowsStartup(cWinStart.Checked);
            srvJoins = Ui.Lbl("Últimos en entrar: -", 9, FontStyle.Regular, Th.Muted);
            srvJoins.MaximumSize = new Size(300, 0);
            srvWarn = Ui.Lbl("", 9, FontStyle.Regular, Th.Warn);
            srvWarn.MaximumSize = new Size(300, 0);
            var status = Ui.Col(srvState, srvPlayers, srvUptime, Ui.Row(bStart, bStop, bRestart), cAuto, cWinStart, srvJoins, srvWarn);

            tName = Ui.Txt(s.ServerName, 200); tWorld = Ui.Txt(s.WorldName, 200);
            tPass = Ui.Txt(s.Password, 200); tPass.UseSystemPasswordChar = true;
            tPort = Ui.Txt(s.Port.ToString(), 80);
            tExtra = Ui.Txt(s.ExtraArgs, 440);
            tServerDir = Ui.Txt(s.ServerDir, 340); tServerDir.ReadOnly = true;
            cPublic = Ui.Chk("Visible en la lista pública de servers", s.Public);
            cCross = Ui.Chk("Crossplay (Xbox / Game Pass)", s.Crossplay);
            var showPass = Ui.Chk("ver", false);
            showPass.CheckedChanged += (o, e) => tPass.UseSystemPasswordChar = !showPass.Checked;

            var g = Ui.Grid(4);
            g.Controls.Add(Ui.Lbl("Nombre del server"), 0, 0); g.Controls.Add(tName, 1, 0);
            g.Controls.Add(Ui.Lbl("Mundo"), 2, 0); g.Controls.Add(tWorld, 3, 0);
            g.Controls.Add(Ui.Lbl("Contraseña"), 0, 1); g.Controls.Add(Ui.Row(tPass, showPass), 1, 1);
            g.Controls.Add(Ui.Lbl("Puerto"), 2, 1); g.Controls.Add(tPort, 3, 1);
            g.Controls.Add(cPublic, 1, 2); g.SetColumnSpan(cPublic, 3);
            g.Controls.Add(cCross, 1, 3); g.SetColumnSpan(cCross, 3);
            g.Controls.Add(Ui.Lbl("Argumentos extra"), 0, 4); g.Controls.Add(tExtra, 1, 4); g.SetColumnSpan(tExtra, 3);
            var browse = Ui.Btn("Cambiar...", () =>
            {
                using (var d = new FolderBrowserDialog { Description = "Carpeta donde está valheim_server.exe", SelectedPath = tServerDir.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) tServerDir.Text = d.SelectedPath;
            });
            g.Controls.Add(Ui.Lbl("Server de Steam"), 0, 5); g.Controls.Add(Ui.Row(tServerDir, browse), 1, 5); g.SetColumnSpan(g.GetControlFromPosition(1, 5), 3);
            var settings = Ui.Col(Ui.Lbl("Ajustes del server", 11, FontStyle.Bold), g,
                Ui.Row(Ui.Btn("Guardar ajustes", () => SaveServerFields(true)),
                       Ui.Btn("Carpeta del mundo", () => Ui.Open(Path.Combine(AppInfo.SavesDir, "worlds_local"))),
                       Ui.Btn("Log completo", () => Ui.Open(Path.Combine(AppInfo.Profile, @"BepInEx\LogOutput.log")))));

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Bg };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 365));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.Controls.Add(Ui.Card(status), 0, 0);
            top.Controls.Add(Ui.Card(settings), 1, 0);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 345));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(Ui.Lbl("Servidor", 18, FontStyle.Bold), 0, 0);
            t.Controls.Add(top, 0, 1);
            t.Controls.Add(Ui.Lbl("Consola del servidor", 11, FontStyle.Bold), 0, 2);
            t.Controls.Add(Ui.Card(srvLog, 6), 0, 3);
            var p = new Panel();
            p.Controls.Add(t);
            UpdateServerState();
            return p;
        }

        bool SaveServerFields(bool notify)
        {
            var s = Settings.Current;
            int port;
            if (!int.TryParse(tPort.Text.Trim(), out port) || port < 1024 || port > 65000) { MessageBox.Show(this, "El puerto no es válido."); return false; }
            if (tName.Text.Trim() == "" || tWorld.Text.Trim() == "") { MessageBox.Show(this, "El nombre del server y del mundo no pueden estar vacíos."); return false; }
            if (tPass.Text.Length > 0 && tName.Text.Contains(tPass.Text)) { MessageBox.Show(this, "Valheim no deja que la contraseña forme parte del nombre del server."); return false; }
            s.ServerName = tName.Text.Trim(); s.WorldName = tWorld.Text.Trim(); s.Password = tPass.Text;
            s.Port = port; s.Public = cPublic.Checked; s.Crossplay = cCross.Checked; s.ExtraArgs = tExtra.Text; s.ServerDir = tServerDir.Text;
            Settings.Save();
            if (notify) Status(runner.Running ? "Ajustes guardados. Se aplicarán al reiniciar el server." : "Ajustes guardados.");
            UpdateInstructions();
            return true;
        }

        void StartServer()
        {
            if (!SaveServerFields(false)) return;
            if (dirty && !Confirm("Tienes cambios en los mods sin aplicar. El server arrancará con los mods instalados actualmente.\n\n¿Arrancar igualmente?")) return;
            RunBg("Arrancando el server...", () => runner.Start(), () => Status("Server arrancando. El mundo tarda un minuto en cargar."));
        }

        void RestartServer()
        {
            if (!SaveServerFields(false)) return;
            RunBg("Reiniciando el server...", () =>
            {
                runner.Stop(true);
                System.Threading.Thread.Sleep(3000);
                runner.Start();
            }, null);
        }

        void UpdateServerState()
        {
            bool on = runner.Running;
            string st = runner.Status;
            srvState.Text = "● " + (on && st == "Detenido" ? "En marcha" : st);
            srvState.ForeColor = !on ? Th.Muted : st == "En marcha" ? Th.Good : Th.Warn;
            bStart.Enabled = !on; bStop.Enabled = on; bRestart.Enabled = on;
            bStart.BackColor = on ? Th.Input : Th.Accent;
            bStart.ForeColor = on ? Th.Muted : Color.White;
            srvPlayers.Text = "Jugadores conectados: " + (on ? runner.Players : 0);
            if (tray != null)
            {
                string tip = "Majinheim Server: " + (on ? st + " (" + runner.Players + " jugadores)" : "detenido");
                tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }
        }

        void TickServer()
        {
            if (runner.Running)
            {
                var up = DateTime.Now - runner.StartedAt;
                srvUptime.Text = "Tiempo en marcha: " + (int)up.TotalHours + " h " + up.Minutes + " min";
            }
            else srvUptime.Text = "Tiempo en marcha: -";
            if (++webTicks % 8 == 0) UpdateWebState();
            AutoTick();
            lock (runner.RecentJoins)
                srvJoins.Text = "Últimos en entrar:\n" + (runner.RecentJoins.Count == 0 ? "-" : string.Join("\n", runner.RecentJoins.Take(6)));
        }

        void SetWindowsStartup(bool on)
        {
            Settings.Current.AutoStartServer = on;
            Settings.Save();
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (on) k.SetValue("MajinheimServer", "\"" + Application.ExecutablePath + "\" --autostart");
                else if (k.GetValue("MajinheimServer") != null) k.DeleteValue("MajinheimServer");
            }
            Log.W(on ? "El server arrancará al iniciar Windows." : "Arranque con Windows desactivado.");
        }

        // ------------------------------------------------------------ mods

        Panel BuildModsPage()
        {
            modList = Ui.List(true, "Mod|300", "Versión|80", "Última|80", "Fecha última|100", "Lado|90", "Nota|430");
            modList.ItemChecked += (s, e) =>
            {
                if (populating || !modList.Focused) return;   // solo cambios hechos por el usuario
                var m = (ModEntry)e.Item.Tag;
                if (m.Enabled == e.Item.Checked) return;   // WinForms lo dispara al crear la lista
                m.Enabled = e.Item.Checked;
                SetDirty(true);
                ColorItem(e.Item);
            };
            modList.DoubleClick += (s, e) => OpenSelectedWeb();
            modsInfo = Ui.Lbl("", 9.5f, FontStyle.Regular, Th.Muted);
            modsDirty = Ui.Lbl("", 10, FontStyle.Bold, Th.Warn);
            var bar = Ui.Col(
                Ui.Row(Ui.Btn("Buscar actualizaciones", CheckUpdates), Ui.Btn("Actualizar seleccionados", UpdateSelected),
                       Ui.Btn("Añadir mod...", AddMod), Ui.Btn("Quitar", RemoveSelected), Ui.Btn("Cambiar lado", CycleSide),
                       Ui.Btn("Ver en Thunderstore", OpenSelectedWeb), Ui.Btn("Deshacer cambios", ReloadPack)),
                Ui.Row(Ui.Btn("APLICAR CAMBIOS", ApplyMods, true), modsInfo, modsDirty),
                Ui.Lbl("Marca o desmarca para activar o desactivar.  Naranja = hay versión nueva.  Amarillo = lleva mucho sin actualizarse (vigilar).  Gris = desactivado.", 8.5f, FontStyle.Regular, Th.Muted));
            PopulateMods();
            return PageWithTitle("Mods", "El modpack que usa el server y que reciben tus amigos automaticamente.", modList, bar);
        }

        void PopulateMods()
        {
            populating = true;
            modList.BeginUpdate();
            modList.Items.Clear();
            foreach (var m in pack.Mods)
            {
                PkgInfo li;
                latest.TryGetValue(m.Id, out li);
                var it = new ListViewItem(m.Id) { Checked = m.Enabled, Tag = m, UseItemStyleForSubItems = true };
                it.SubItems.Add(m.Version);
                it.SubItems.Add(m.Local ? "propio" : li != null ? li.Version : "");
                it.SubItems.Add(li != null ? li.Date : "");
                it.SubItems.Add(m.Side == "client" ? "solo jugadores" : m.Side == "server" ? "solo server" : "ambos");
                it.SubItems.Add(m.Note);
                ColorItem(it);
                modList.Items.Add(it);
            }
            modList.EndUpdate();
            populating = false;
            var inst = InstalledInfo.Load(AppInfo.Profile);
            modsInfo.Text = string.Format("Revisión {0}  ·  {1} mods activos de {2}  ·  instalado en el server: revisión {3}{4}",
                pack.Revision, pack.Mods.Count(m => m.Enabled), pack.Mods.Count, inst.Revision,
                File.Exists(Doorstop.Preloader(AppInfo.Profile)) ? "" : " (SIN INSTALAR: pulsa Aplicar)");
            modsDirty.Text = dirty ? "  ●  Hay cambios sin aplicar" : "";
        }

        void ColorItem(ListViewItem it)
        {
            var m = (ModEntry)it.Tag;
            PkgInfo li;
            latest.TryGetValue(m.Id, out li);
            if (!m.Enabled) it.ForeColor = Color.FromArgb(110, 116, 124);
            else if (li != null && Ver.Compare(li.Version, m.Version) > 0) it.ForeColor = Th.AccentHi;
            else if (li != null && string.CompareOrdinal(li.Date, Settings.Current.StaleBefore) < 0) it.ForeColor = Th.Warn;
            else it.ForeColor = Th.Text;
        }

        void SetDirty(bool d) { dirty = d; modsDirty.Text = d ? "  ●  Hay cambios sin aplicar" : ""; }

        List<ModEntry> Selected() { return modList.SelectedItems.Cast<ListViewItem>().Select(i => (ModEntry)i.Tag).ToList(); }

        void CheckUpdates()
        {
            var mods = pack.Mods.Where(m => !m.Local).ToList();
            RunBg("Consultando Thunderstore...", () =>
            {
                Parallel.ForEach(mods, new ParallelOptions { MaxDegreeOfParallelism = 6 }, m =>
                {
                    try { var p = Thunderstore.Latest(m.Id); lock (latest) latest[m.Id] = p; }
                    catch (Exception e) { Log.W("No pude consultar {0}: {1}", m.Id, e.Message); }
                });
            }, () =>
            {
                PopulateMods();
                int n = pack.Mods.Count(m => latest.ContainsKey(m.Id) && Ver.Compare(latest[m.Id].Version, m.Version) > 0);
                Status(n == 0 ? "Todo al día." : n + " mods tienen versión nueva (en naranja).");
            });
        }

        // Al abrir: consulta Thunderstore en segundo plano sin bloquear la ventana
        void CheckUpdatesQuiet()
        {
            var mods = pack.Mods.Where(m => !m.Local).ToList();
            Task.Factory.StartNew(() =>
            {
                Parallel.ForEach(mods, new ParallelOptions { MaxDegreeOfParallelism = 6 }, m =>
                {
                    try { var p = Thunderstore.Latest(m.Id); lock (latest) latest[m.Id] = p; } catch { }
                });
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    PopulateMods();
                    int n = pack.Mods.Count(m => m.Enabled && latest.ContainsKey(m.Id) && Ver.Compare(latest[m.Id].Version, m.Version) > 0);
                    if (n > 0) { Status(n + " mods tienen versión nueva: míralo en la pestaña Mods."); Log.W("{0} mods tienen versión nueva en Thunderstore.", n); }
                    AfterUpdateCheck();
                }));
            });
        }

        void UpdateSelected()
        {
            var sel = Selected();
            if (sel.Count == 0)
            {
                if (latest.Count == 0) { MessageBox.Show(this, "Primero pulsa 'Buscar actualizaciones'."); return; }
                if (!Confirm("No has seleccionado ningun mod. ¿Actualizar todos los que tienen versión nueva?")) return;
                sel = pack.Mods.ToList();
            }
            int n = 0;
            foreach (var m in sel)
            {
                PkgInfo li;
                if (latest.TryGetValue(m.Id, out li) && Ver.Compare(li.Version, m.Version) > 0) { Log.W("{0}: {1} -> {2}", m.Id, m.Version, li.Version); m.Version = li.Version; n++; }
            }
            if (n > 0) SetDirty(true);
            PopulateMods();
            Status(n + " mods actualizados en la lista. Pulsa APLICAR CAMBIOS para instalarlos.");
        }

        void AddMod()
        {
            string input = Ui.Ask(this, "Añadir mod", "Pega el enlace del mod en Thunderstore (o escribe Autor-Nombre). Sus dependencias se añaden solas.", "");
            if (input == null) return;
            string id = Thunderstore.ParseId(input);
            if (id == null) { MessageBox.Show(this, "No reconozco ese mod. Copia el enlace desde thunderstore.io/c/valheim/..."); return; }
            var added = new List<string>();
            RunBg("Buscando " + id + "...", () =>
            {
                var queue = new Queue<string>();
                queue.Enqueue(id);
                while (queue.Count > 0)
                {
                    string cur = queue.Dequeue();
                    if (pack.Find(cur) != null) continue;
                    var p = Thunderstore.Latest(cur);
                    lock (latest) latest[p.Id] = p;
                    lock (pack) pack.Mods.Add(new ModEntry { Id = p.Id, Version = p.Version, Note = p.Description.Length > 80 ? p.Description.Substring(0, 80) : p.Description });
                    added.Add(p.Id + " " + p.Version);
                    foreach (var d in p.Deps)
                    {
                        string did, dver;
                        Thunderstore.SplitDep(d, out did, out dver);
                        if (pack.Find(did) == null) queue.Enqueue(did);
                    }
                }
            }, () =>
            {
                SetDirty(added.Count > 0);
                PopulateMods();
                MessageBox.Show(this, added.Count == 0 ? "Ese mod ya está en el modpack." : "Añadidos:\n\n" + string.Join("\n", added) + "\n\nPulsa APLICAR CAMBIOS para instalarlos.", "Majinheim");
            });
        }

        void RemoveSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            if (sel.Any(m => m.Id == AppInfo.BepInExId)) { MessageBox.Show(this, "BepInEx es imprescindible, no se puede quitar."); return; }
            if (!Confirm("¿Quitar del modpack?\n\n" + string.Join("\n", sel.Select(m => m.Id)) + "\n\n(Si solo quieres probar sin el, mejor desmárcalo.)")) return;
            foreach (var m in sel) pack.Mods.Remove(m);
            SetDirty(true);
            PopulateMods();
        }

        void CycleSide()
        {
            foreach (var m in Selected()) m.Side = m.Side == "both" ? "client" : m.Side == "client" ? "server" : "both";
            SetDirty(true);
            PopulateMods();
        }

        void OpenSelectedWeb()
        {
            var m = Selected().FirstOrDefault();
            if (m != null && !m.Local) Ui.Open(m.WebUrl());
        }

        void ReloadPack()
        {
            if (dirty && !Confirm("¿Descartar los cambios sin aplicar?")) return;
            pack = Modpack.Load();
            SetDirty(false);
            PopulateMods();
        }

        void ApplyMods()
        {
            bool wasRunning = runner.Running;
            if (wasRunning && !Confirm("El server está en marcha. Para cambiar los mods hay que pararlo (guardando el mundo), instalar y volver a arrancarlo.\n\n¿Hacerlo ahora?")) return;
            List<string> warnings = null;
            string published = "";
            RunBg("Aplicando mods...", () =>
            {
                if (wasRunning) runner.Stop(true);
                if (File.Exists(AppInfo.ModpackFile)) Backup.Create("modpack-antes-de-aplicar", new[] { new Backup.Item(AppInfo.ModpackFile, "modpack") }, true);
                if (dirty || !File.Exists(Doorstop.Preloader(AppInfo.Profile))) pack.Revision++;
                pack.LauncherVersion = AppInfo.Version;
                pack.Save();
                warnings = Installer.Install(pack, AppInfo.Profile, false, null, Status);
                if (wasRunning) runner.Start();
                try { published = Publish.Pack(true); }
                catch (Exception e) { published = "ERROR al publicar en GitHub: " + e.Message; Log.W(published); }
            }, () =>
            {
                SetDirty(false);
                PopulateMods();
                UpdateInstructions();
                string msg = "Mods aplicados (revisión " + pack.Revision + ").\n" + published + "\nTus amigos recibirán los cambios al abrir su launcher.";
                if (warnings != null && warnings.Count > 0) msg += "\n\nAvisos de dependencias:\n" + string.Join("\n", warnings);
                MessageBox.Show(this, msg, "Majinheim", MessageBoxButtons.OK, warnings != null && warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            });
        }

        // ------------------------------------------------------------ amigos

        Panel BuildFriendsPage()
        {
            var s = Settings.Current;
            tRepo = Ui.Txt(GitHub.Repo, 320);
            tToken = Ui.Txt(s.GitHubToken, 320); tToken.UseSystemPasswordChar = true;
            tHost = Ui.Txt(s.PublicHost, 260);
            tLan = Ui.Txt(s.LanHost, 140);
            pubState = Ui.Lbl("", 10, FontStyle.Bold, Th.Muted);
            var c1 = Ui.Col(Ui.Lbl("GitHub (de ahí sacan tus amigos la lista de mods y el launcher)", 11, FontStyle.Bold),
                Ui.Row(Ui.Lbl("Repositorio (usuario/nombre)"), tRepo),
                Ui.Row(Ui.Lbl("Clave de GitHub (token)       "), tToken, Ui.Lbl("opcional: vacía = usa la sesión de GitHub CLI de este PC", 9, FontStyle.Regular, Th.Muted)),
                Ui.Row(Ui.Lbl("Dirección pública (DDNS)"), tHost, Ui.Lbl("IP de casa del server (no se publica)"), tLan),
                Ui.Row(Ui.Btn("Guardar", SavePublish),
                       Ui.Btn("Publicar lista de mods", () => RunPublish("Publicando la lista de mods...", () => Publish.Pack(true))),
                       Ui.Btn("Publicar launcher " + AppInfo.Version, () => RunPublish("Subiendo el launcher a GitHub...", () => { Publish.Launcher(null); return "Launcher " + AppInfo.Version + " publicado: los launchers de tus amigos se actualizarán solos."; })),
                       Ui.Btn("Subir código", () => RunPublish("Subiendo el código...", () => Publish.Source() + " archivos de código subidos.")),
                       Ui.Btn("Abrir en GitHub", () => Ui.Open("https://github.com/" + GitHub.Repo))),
                pubState);

            tMsg = Ui.Txt(pack.Message, 640); tMsg.Multiline = true; tMsg.Height = 54;
            var c2 = Ui.Col(Ui.Lbl("Mensaje que verán en su launcher", 11, FontStyle.Bold), tMsg,
                Ui.Row(Ui.Btn("Guardar y publicar mensaje", () =>
                {
                    pack.Message = tMsg.Text.Trim(); var disk = Modpack.Load(); disk.Message = pack.Message; disk.Save();
                    RunPublish("Publicando el mensaje...", () => Publish.Pack(true));
                })));

            tInstr = Ui.Txt("", 640); tInstr.Multiline = true; tInstr.Height = 110; tInstr.ReadOnly = true;
            var c3 = Ui.Col(Ui.Lbl("Lo que tienes que mandar a tus amigos (una sola vez)", 11, FontStyle.Bold), tInstr,
                Ui.Row(Ui.Btn("Copiar", () => { Clipboard.SetText(tInstr.Text); Status("Copiado al portapapeles."); })));

            routerInfo = Ui.Lbl("", 9.5f, FontStyle.Regular, Th.Text);
            routerInfo.MaximumSize = new Size(700, 0);
            var c4 = Ui.Col(Ui.Lbl("Router ASUS: puertos a redirigir a este PC", 11, FontStyle.Bold), routerInfo);

            var col = Ui.Col(Ui.Lbl("Amigos", 18, FontStyle.Bold), Ui.Lbl("Tus amigos solo necesitan Majinheim.exe: baja la lista de mods de GitHub, los mods de Thunderstore y entra a " + AppInfo.DefaultHost + ".", 9.5f, FontStyle.Regular, Th.Muted));
            foreach (var c in new Control[] { c1, c2, c3, c4 })
            {
                var card = Ui.Card(c);
                card.Dock = DockStyle.None; card.AutoSize = true; card.AutoSizeMode = AutoSizeMode.GrowAndShrink; card.MinimumSize = new Size(700, 0);
                col.Controls.Add(card);
            }
            var p = new Panel { AutoScroll = true };
            p.Controls.Add(col);
            UpdateInstructions();
            return p;
        }

        void SavePublish()
        {
            var s = Settings.Current;
            s.GitHubRepo = tRepo.Text.Trim(); s.GitHubToken = tToken.Text.Trim(); s.PublicHost = tHost.Text.Trim(); s.LanHost = tLan.Text.Trim();
            Settings.Save();
            UpdateInstructions();
            Status("Guardado.");
        }

        void RunPublish(string what, Func<string> work)
        {
            SavePublish();
            string result = null;
            RunBg(what, () =>
            {
                try { result = work(); }
                catch (Exception e) { result = "ERROR: " + e.Message; Log.W("ERROR publicando: {0}", e.Message); }
            }, () =>
            {
                pubState.Text = "● " + result;
                pubState.ForeColor = result.StartsWith("ERROR") ? Th.Warn : Th.Good;
                Status(result);
            });
        }

        void UpdateInstructions()
        {
            if (tInstr == null) return;
            var s = Settings.Current;
            string web = "https://github.com/" + GitHub.Repo + "/releases/latest";
            tInstr.Text = "¡Nuevo launcher de Majinheim!\r\n1. Descarga Majinheim.exe de " + web + "\r\n2. Guárdalo en una carpeta propia y corta (por ejemplo C:\\Majinheim) y ábrelo.\r\n3. Pulsa JUGAR. Se baja los mods solo, se actualiza solo y entra al server.\r\nContraseña del server: " + s.Password + "\r\n(Si Windows avisa: Más información > Ejecutar de todas formas)";
            string ip = LocalIp();
            routerInfo.Text = "IP local de este PC: " + ip + "\n" +
                "  • UDP " + s.Port + "-" + (s.Port + 1) + "  →  " + ip + "   (el juego; es lo único que hace falta)\n" +
                "Ya no hace falta el puerto TCP 2460: la lista de mods y el launcher van por GitHub.";
        }
        static string LocalIp()
        {
            try
            {
                using (var sck = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    sck.Connect("8.8.8.8", 65530);
                    return ((IPEndPoint)sck.LocalEndPoint).Address.ToString();
                }
            }
            catch { return "?"; }
        }

        // ------------------------------------------------------------ backups

        Panel BuildBackupsPage()
        {
            bkList = Ui.List(false, "Fecha|160", "Tipo|80", "Descripción|320", "Tamaño|100", "Archivo|360");
            var info = Ui.Lbl("Se hace backup del mundo antes de cada arranque y de la configuración antes de cada cambio de mods.\n" +
                "Tu instalación original NO se toca nunca: el mundo original sigue en " + AppInfo.OriginalSaves + "\n" +
                "y hay una copia completa de todo en C:\\Users\\Majin\\ValheimBackups\\2026-10-31_antes-de-1.0", 9, FontStyle.Regular, Th.Muted);
            info.MaximumSize = new Size(900, 0);
            var bar = Ui.Col(Ui.Row(
                Ui.Btn("Crear backup ahora", () => RunBg("Creando backup...", () => Backup.Create("manual", Backup.FullItems(), false), RefreshBackups), true),
                Ui.Btn("Restaurar seleccionado", RestoreSelected),
                Ui.Btn("Abrir carpeta", () => Ui.Open(AppInfo.BackupDir))), info);
            return PageWithTitle("Backups", "Copias de seguridad del mundo, personajes y configuración.", bkList, bar);
        }

        void RefreshBackups()
        {
            bkList.BeginUpdate();
            bkList.Items.Clear();
            foreach (var b in Backup.List())
            {
                var it = new ListViewItem(b.Date.ToString("yyyy-MM-dd  HH:mm:ss")) { Tag = b };
                it.SubItems.Add(b.Kind);
                it.SubItems.Add(b.Label.Replace("-", " ").Replace("_", " "));
                it.SubItems.Add(Ui.Size(b.Size));
                it.SubItems.Add(Path.GetFileName(b.Path));
                if (b.Kind == "manual") it.ForeColor = Th.Accent;
                bkList.Items.Add(it);
            }
            bkList.EndUpdate();
        }

        void RestoreSelected()
        {
            if (bkList.SelectedItems.Count != 1) { MessageBox.Show(this, "Selecciona un backup."); return; }
            var b = (Backup.Info)bkList.SelectedItems[0].Tag;
            if (runner.Running) { MessageBox.Show(this, "Detén el server antes de restaurar."); return; }
            if (!Confirm("¿Restaurar este backup?\n\n" + Path.GetFileName(b.Path) + "\n\nAntes se guardará un backup de lo que hay ahora, por si acaso.")) return;
            RunBg("Restaurando...", () => Backup.Restore(b.Path), () => { RefreshBackups(); pack = Modpack.Load(); PopulateMods(); Settings.Load(); MessageBox.Show(this, "Restaurado.", "Majinheim"); });
        }

        // ------------------------------------------------------------ ciclo de vida

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (StartPage != null && pages.ContainsKey(StartPage)) ShowPage(StartPage);
            var s = Settings.Current;
            if (string.IsNullOrEmpty(s.ServerDir) || !File.Exists(ServerRunner.ServerExe()))
            {
                var d = Steam.FindApp("896660", "valheim_server.exe");
                if (d != null) { s.ServerDir = d; tServerDir.Text = d; Settings.Save(); }
            }
            if (!Directory.Exists(Path.Combine(AppInfo.SavesDir, "worlds_local")) && Directory.Exists(AppInfo.OriginalSaves) &&
                Confirm("Primera vez: ¿copiar tu mundo y personajes actuales al launcher?\n\n(Se COPIAN, el original no se toca.)"))
                RunBg("Copiando mundo...", () => Admin.ImportOriginal(s.ServerDir), null);
            if (!File.Exists(Path.Combine(AppInfo.DistDir, AppInfo.ClientExeName)))
                srvWarn.Text = "Falta distribucion\\" + AppInfo.ClientExeName + ": tus amigos no podran bajarse el launcher.";
            runner.TryAdopt();
            UpdateServerState();
            WebAdmin.Action = WebAction;
            WebAdmin.Say = SayInGame;
            InitAuto();
            WebPanel.Apply();
            CheckUpdatesQuiet();
            if (!File.Exists(Doorstop.Preloader(AppInfo.Profile))) { ShowPage("Mods"); Status("Los mods no están instalados: pulsa APLICAR CAMBIOS."); }
            else if (autostart && !runner.Running) StartServer();
        }

        // Lo que se puede hacer desde la administración web. Se ejecuta en la ventana del launcher, como los botones.
        string WebAction(string what)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(() => WebAction(what)));
            if (busy) return "El launcher está ocupado con otra tarea; prueba en un momento.";
            switch (what)
            {
                case "iniciar":
                    if (runner.Running) return "El server ya está en marcha.";
                    RunBg("Arrancando el server (desde la web)...", () => runner.Start(), () => Status("Server arrancando (pedido desde la web)."));
                    return "Arrancando. El mundo tarda un minuto en cargar.";
                case "parar":
                    if (!runner.Running) return "El server ya está apagado.";
                    bool warnP = WillWarn();
                    RunBg("Guardando el mundo y apagando (desde la web)...", () => { WarnPlayers("se apaga"); runner.Stop(true); }, null);
                    return warnP ? "Aviso a los jugadores en el chat y apago dentro de 1 minuto (se guarda el mundo)." : "Guardando el mundo y apagando.";
                case "reiniciar":
                    if (!runner.Running) return "El server está apagado: usa Encender.";
                    bool warnR = WillWarn();
                    RunBg("Reiniciando el server (desde la web)...", () => { WarnPlayers("se reinicia"); runner.Stop(true); System.Threading.Thread.Sleep(3000); runner.Start(); }, null);
                    return warnR ? "Aviso a los jugadores en el chat y reinicio dentro de 1 minuto." : "Reiniciando: se guarda el mundo, se apaga y vuelve a arrancar.";
                case "backup":
                    RunBg("Creando backup (desde la web)...", () => Backup.Create("web", Backup.FullItems(), false), () => { if (pages.ContainsKey("Backups") && pages["Backups"].Visible) RefreshBackups(); });
                    return "Haciendo backup completo (mundo, personajes y ajustes).";
            }
            return "Acción desconocida.";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && runner.Running && tray != null) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && runner.Running && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, "El server sigue en marcha.\n\nSí: detenerlo (guardando el mundo) y salir.\nNo: dejarlo en marcha y esconder el launcher junto al reloj.\nCancelar: volver.", "Majinheim", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                e.Cancel = true;
                if (r == DialogResult.Yes) RunBg("Guardando el mundo y cerrando...", () => runner.Stop(true), () => { exiting = true; Close(); });
                else if (r == DialogResult.No) { Hide(); tray.ShowBalloonTip(4000, "Majinheim", "El server sigue en marcha. Doble clic aquí para abrir el launcher.", ToolTipIcon.Info); }
                return;
            }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            WebPanel.Shutdown();
            DiscordBot.Stop();
            base.OnFormClosing(e);
        }

#else
        // ============================================================ EDICION CLIENTE

        Label plTitle, plMsg, plState;
        ListView plMods;
        Button bPlay, bWeb;
        CheckBox cConnect;
        TextBox tGameDir, tRepoC, tLanC;
        bool online;

        void BuildClient()
        {
            pack = Modpack.Load();
            AddPage("Jugar", "▶", BuildPlayPage());
            AddPage("Ajustes", "⚙", BuildClientSettings());
        }

        Panel BuildPlayPage()
        {
            plTitle = Ui.Lbl(pack.Name, 26, FontStyle.Bold, Th.Text);
            plMsg = Ui.Lbl("", 11, FontStyle.Regular, Th.Muted); plMsg.MaximumSize = new Size(820, 0);
            plState = Ui.Lbl("Conectando con el server...", 10, FontStyle.Regular, Th.Muted);
            bPlay = Ui.Btn("JUGAR", Play, true);
            bPlay.AutoSize = false; bPlay.Size = new Size(260, 64); bPlay.Font = Th.F(18, FontStyle.Bold);
            bWeb = Ui.Btn("◎  Web del server", OpenServerWeb);
            cConnect = Ui.Chk("Entrar directamente al server", Settings.Current.AutoConnect);
            cConnect.CheckedChanged += (o, e) => { Settings.Current.AutoConnect = cConnect.Checked; Settings.Save(); };
            var hero = Ui.Col(plTitle, plMsg, plState,
                Ui.Row(bPlay, Ui.Col(cConnect, Ui.Row(Ui.Btn("Jugar sin mods", () => Launch(false)), Ui.Btn("Buscar actualizaciones", () => Sync(false)), bWeb))));

            plMods = Ui.List(false, "Mod|330", "Versión|90", "Para qué sirve|460");
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var heroCard = Ui.Card(hero, 20); heroCard.Dock = DockStyle.None; heroCard.AutoSize = true; heroCard.AutoSizeMode = AutoSizeMode.GrowAndShrink; heroCard.MinimumSize = new Size(880, 0);
            t.Controls.Add(heroCard, 0, 0);
            t.Controls.Add(Ui.Lbl("Mods del server", 11, FontStyle.Bold), 0, 1);
            t.Controls.Add(Ui.Card(plMods, 6), 0, 2);
            var p = new Panel();
            p.Controls.Add(t);
            ShowPack();
            return p;
        }

        Panel BuildClientSettings()
        {
            var s = Settings.Current;
            tGameDir = Ui.Txt(s.GameDir, 520);
            tRepoC = Ui.Txt(GitHub.Repo, 320);
            tLanC = Ui.Txt(s.LanHost, 160);
            var col = Ui.Col(
                Ui.Lbl("Carpeta de Valheim", 11, FontStyle.Bold),
                Ui.Row(tGameDir, Ui.Btn("Buscar en Steam", () => { Settings.Current.GameDir = ""; var d = Player.GameDir(); tGameDir.Text = d ?? ""; if (d == null) MessageBox.Show(this, "No encuentro Valheim. Indica la carpeta a mano."); }),
                       Ui.Btn("Elegir...", () => { using (var d = new FolderBrowserDialog { Description = "Carpeta donde está valheim.exe" }) if (d.ShowDialog(this) == DialogResult.OK) tGameDir.Text = d.SelectedPath; })),
                Ui.Lbl("Lista de mods (GitHub)", 11, FontStyle.Bold),
                Ui.Row(tRepoC, Ui.Lbl("(no lo cambies salvo que Majin te lo diga)", 9, FontStyle.Regular, Th.Muted)),
                Ui.Lbl("IP del server en tu casa", 11, FontStyle.Bold),
                Ui.Row(tLanC, Ui.Lbl("(solo si juegas en la misma casa que el server; si no, déjalo vacío)", 9, FontStyle.Regular, Th.Muted)),
                Ui.Row(Ui.Btn("Guardar", () =>
                {
                    if (tGameDir.Text != "" && !File.Exists(Path.Combine(tGameDir.Text, "valheim.exe"))) { MessageBox.Show(this, "En esa carpeta no está valheim.exe."); return; }
                    Settings.Current.GameDir = tGameDir.Text; Settings.Current.GitHubRepo = tRepoC.Text.Trim(); Settings.Current.LanHost = tLanC.Text.Trim(); Settings.Save(); Status("Guardado.");
                }, true)),
                Ui.Lbl("Mantenimiento", 11, FontStyle.Bold),
                Ui.Row(Ui.Btn("Reinstalar mods", () => { var f = Path.Combine(AppInfo.Profile, "installed.json"); if (File.Exists(f)) File.Delete(f); Sync(false); }),
                       Ui.Btn("Abrir carpeta de mods", () => Ui.Open(AppInfo.Profile)),
                       Ui.Btn("Abrir carpeta de config", () => Ui.Open(Path.Combine(AppInfo.Profile, @"BepInEx\config")))),
                Ui.Lbl("Los mods viven en la carpeta de este launcher, no en el juego. En la carpeta de Valheim solo se anade winhttp.dll (el cargador),\n" +
                       "desactivado por defecto: si abres Valheim desde Steam juegas sin mods. Si ya había uno, se guarda un backup en backups\\.", 9, FontStyle.Regular, Th.Muted));
            var card = Ui.Card(col, 20); card.Dock = DockStyle.None; card.AutoSize = true; card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var outer = Ui.Col(Ui.Lbl("Ajustes", 18, FontStyle.Bold), card);
            var p = new Panel { AutoScroll = true };
            p.Controls.Add(outer);
            return p;
        }

        void ShowPack()
        {
            plTitle.Text = pack.Name;
            plMsg.Text = pack.Message ?? "";
            bWeb.Visible = SafeWebUrl(pack.WebUrl) != null;
            plMods.BeginUpdate();
            plMods.Items.Clear();
            foreach (var m in pack.Mods)
            {
                var it = new ListViewItem(m.Id);
                it.SubItems.Add(m.Version);
                it.SubItems.Add(m.Note);
                plMods.Items.Add(it);
            }
            plMods.EndUpdate();
            bool installed = File.Exists(AppInfo.ModpackFile) && Installer.UpToDate(pack, AppInfo.Profile, true);
            plState.Text = (online ? "● Lista de mods al día" : File.Exists(AppInfo.ModpackFile) ? "● Sin conexión con GitHub (uso la última lista de mods descargada)" : "● Aún no he podido bajar la lista de mods") +
                "   ·   revisión " + pack.Revision + "   ·   " + pack.Mods.Count + " mods   ·   " + (installed ? "mods listos" : "faltan mods por descargar");
            plState.ForeColor = online ? Th.Good : Th.Warn;
        }

        void Sync(bool thenLaunch)
        {
            List<string> warnings = null;
            Modpack got = null;
            bool on = false;
            RunBg("Sincronizando mods...", () => { got = Player.Sync(Status, out on, out warnings); }, () =>
            {
                pack = got; online = on;
                ShowPack();
                if (Player.NewLauncher != null &&
                    Confirm("Hay una versión nueva del launcher (" + Player.NewLauncher.Version + ").\n\n" +
                        (string.IsNullOrWhiteSpace(Player.NewLauncher.Notes) ? "" : "Novedades:\n" + Player.NewLauncher.Notes.Replace("**", "") + "\n\n") + "¿Actualizar ahora? Tarda unos segundos."))
                {
                    RunBg("Actualizando launcher...", () => Player.SelfUpdate(Status), () => { Application.Restart(); Environment.Exit(0); });
                    return;
                }
                if (warnings != null && warnings.Count > 0) Log.W("AVISO: {0}", string.Join(" | ", warnings));
                if (thenLaunch) Launch(true);
                else Status(online ? "Todo listo." : "Sin conexión con el server de mods; usando lo último descargado.");
            });
        }

        void Play() { Sync(true); }

        // Solo abre direcciones https:// (la lista de mods viene de internet: nada de programas ni carpetas).
        static string SafeWebUrl(string u)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(u) || !Uri.TryCreate(u.Trim(), UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            return uri.AbsoluteUri;
        }

        void OpenServerWeb()
        {
            string u = SafeWebUrl(pack.WebUrl);
            if (u != null) Ui.Open(u);
        }

        void Launch(bool modded)
        {
            try
            {
                Player.Launch(pack, modded, cConnect.Checked);
                Status(modded ? "Abriendo Valheim con mods... (el primer arranque con mods tarda un poco más)" : "Abriendo Valheim sin mods...");
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "No pude abrir Valheim", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (StartPage != null && pages.ContainsKey(StartPage)) ShowPage(StartPage);
            Player.CleanupOldExe();
            if (Player.GameDir() == null) Log.W("AVISO: no encuentro Valheim en Steam; indicalo en Ajustes.");
            Sync(false);
        }
#endif
    }
}
