// Página "Web" de la edición Server: enciende el panel web y el acceso desde internet (HTTPS por la DDNS).
#if SERVER
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Majinheim
{
    public partial class MainForm
    {
        Label webState, webPublicState, webCheck, webRouter, webMap, webAdmin;
        CheckBox cWebOn, cWebPublic;
        bool webPopulating;

        Panel BuildWebPage()
        {
            var s = Settings.Current;
            webState = Ui.Lbl("● Panel web apagado", 15, FontStyle.Bold, Th.Muted);
            webPublicState = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            webPublicState.MaximumSize = new Size(420, 0);
            webCheck = Ui.Lbl("", 9.5f, FontStyle.Regular, Th.Muted);
            webAdmin = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            webAdmin.MaximumSize = new Size(420, 0);
            webMap = Ui.Lbl("", 10, FontStyle.Regular, Th.Muted);
            webMap.MaximumSize = new Size(420, 0);
            webCheck.MaximumSize = new Size(420, 0);
            webPopulating = true;
            cWebOn = Ui.Chk("Panel web encendido", s.WebEnabled);
            cWebPublic = Ui.Chk("Acceso desde internet (HTTPS, por la dirección pública)", s.WebPublic);
            webPopulating = false;
            cWebOn.CheckedChanged += (o, e) => { if (!webPopulating) SetWeb(cWebOn.Checked, cWebPublic.Checked); };
            cWebPublic.CheckedChanged += (o, e) => { if (!webPopulating) SetWeb(cWebOn.Checked, cWebPublic.Checked); };

            var left = Ui.Col(webState, webPublicState, webMap, cWebOn, cWebPublic,
                Ui.Row(Ui.Btn("Abrir la web", () => Ui.Open(Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl), true),
                       Ui.Btn("Copiar enlace para amigos", () => { Clipboard.SetText(WebPanel.PublicUrl); Status("Enlace copiado: " + WebPanel.PublicUrl); })),
                Ui.Row(Ui.Btn("Comprobar desde internet", CheckWebPublic),
                       Ui.Btn("Ver en este PC", () => Ui.Open(WebPanel.LocalUrl)),
                       Ui.Btn("Mapa", () => Ui.Open((Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "mapa/"))),
                webCheck,
                Ui.Lbl("Administrar desde la web", 11, FontStyle.Bold), webAdmin,
                Ui.Row(Ui.Btn("Poner o cambiar contraseña", SetAdminPassword),
                       Ui.Btn("Desactivar", () => { if (WebAdmin.HasPassword && Confirm("¿Desactivar la administración web? Nadie podrá encender ni apagar el server desde la web.")) { WebAdmin.RemovePassword(); UpdateWebState(); } }),
                       Ui.Btn("Abrir", () => Ui.Open((Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "admin")),
                       Ui.Btn("Registro", () => OpenIfExists(Path.Combine(AppInfo.LogDir, "web-admin.log")))));

            webRouter = Ui.Lbl("", 10, FontStyle.Regular, Th.Text);
            webRouter.MaximumSize = new Size(560, 0);
            var shows = Ui.Lbl("Qué ve la gente: si el server está encendido, quién está jugando y desde cuándo, las horas de la semana, " +
                "las últimas partidas, la lista de mods y el mapa del mundo (lo explorado y quién se deja ver en el mapa). " +
                "No hay botones: desde la web nadie puede tocar el server.", 9.5f, FontStyle.Regular, Th.Muted);
            shows.MaximumSize = new Size(560, 0);
            var right = Ui.Col(Ui.Lbl("En el router (una sola vez)", 11, FontStyle.Bold), webRouter,
                Ui.Lbl("Qué se ve", 11, FontStyle.Bold), shows,
                Ui.Row(Ui.Btn("Registro de visitas", () => OpenIfExists(Path.Combine(AppInfo.LogDir, "caddy-visitas.log"))),
                       Ui.Btn("Registro de HTTPS", () => OpenIfExists(Path.Combine(AppInfo.LogDir, "caddy.log")))));

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Bg };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.Controls.Add(Ui.Card(Ui.Col(Ui.Lbl("Estado", 11, FontStyle.Bold), left)), 0, 0);
            top.Controls.Add(Ui.Card(right), 1, 0);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Th.Bg };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(Ui.Lbl("Web del server", 18, FontStyle.Bold), 0, 0);
            var sub = Ui.Lbl("Una página para ver el estado del server desde el móvil o el PC. Tus amigos la abren desde su launcher con el botón \"Web del server\".", 9.5f, FontStyle.Regular, Th.Muted);
            sub.MaximumSize = new Size(900, 0);
            t.Controls.Add(sub, 0, 1);
            t.Controls.Add(top, 0, 2);
            var p = new Panel();
            p.Controls.Add(t);
            UpdateWebState();
            return p;
        }

        void SetAdminPassword()
        {
            string u = Ui.Ask(this, "Usuario de administrador", "Nombre de usuario para entrar a administrar desde la web.", WebAdmin.User != "" ? WebAdmin.User : "MajinSander");
            if (u == null) return;
            string a = Ui.Ask(this, "Contraseña de administrador", "Contraseña para administrar desde la web (mínimo " + WebAdmin.MinPassword + " caracteres, distinta de la del server). No se guarda en claro.", "", true);
            if (a == null) return;
            string b = Ui.Ask(this, "Contraseña de administrador", "Repite la contraseña.", "", true);
            if (b == null) return;
            if (a != b) { MessageBox.Show(this, "No coinciden.", "Majinheim"); return; }
            string err = WebAdmin.SetPassword(u, a);
            if (err != null) { MessageBox.Show(this, err, "Majinheim"); return; }
            Status("Administrador guardado. Se han cerrado las sesiones abiertas en otros dispositivos.");
            UpdateWebState();
        }

        void OpenIfExists(string f)
        {
            if (File.Exists(f)) Ui.Open(f);
            else MessageBox.Show(this, "Todavía no hay nada registrado.", "Majinheim");
        }

        void SetWeb(bool on, bool pub)
        {
            var s = Settings.Current;
            bool pubChanged = (on && pub) != (s.WebEnabled && s.WebPublic);
            s.WebEnabled = on; s.WebPublic = pub;
            Settings.Save();
            if (!(on && pub) || Caddy.Installed) WebPanel.Apply();
            if (on && pub && !Caddy.Installed)
            {
                RunBg("Preparando el acceso desde internet...", () => Caddy.Install(Status), () => { WebPanel.Apply(); AfterWebChange(pubChanged); });
                return;
            }
            AfterWebChange(pubChanged);
        }

        // Los launchers de los amigos enseñan el botón "Web del server" según la lista de mods publicada.
        void AfterWebChange(bool pubChanged)
        {
            UpdateWebState();
            if (!pubChanged) return;
            if (GitHub.Token() == null) { Status("Guardado. Pulsa \"Publicar lista de mods\" en Amigos para que tus amigos vean el botón de la web."); return; }
            RunPublish("Avisando a los launchers de tus amigos...", () => Publish.Pack(true));
        }

        void CheckWebPublic()
        {
            if (!Settings.Current.WebEnabled || !Settings.Current.WebPublic) { webCheck.Text = "Activa primero el acceso desde internet."; webCheck.ForeColor = Th.Warn; return; }
            string err = null;
            RunBg("Comprobando " + WebPanel.PublicUrl + " ...", () => { err = WebPanel.CheckPublic(); }, () =>
            {
                webCheck.Text = err == null ? "✔ Funciona: " + WebPanel.PublicUrl + " responde por HTTPS." : "✖ Desde internet " + err + ".";
                webCheck.ForeColor = err == null ? Th.Good : Th.Warn;
            });
        }

        void UpdateWebState()
        {
            if (webState == null) return;
            var s = Settings.Current;
            if (!s.WebEnabled) { webState.Text = "● Panel web apagado"; webState.ForeColor = Th.Muted; }
            else if (WebPanel.Error != null) { webState.Text = "● " + WebPanel.Error; webState.ForeColor = Th.Bad; }
            else if (WebPanel.Running) { webState.Text = "● Panel web funcionando"; webState.ForeColor = Th.Good; }
            else { webState.Text = "● Arrancando..."; webState.ForeColor = Th.Warn; }

            string pubTxt; Color pubColor;
            if (!s.WebEnabled || !s.WebPublic) { pubTxt = "Solo se ve en este PC: " + WebPanel.LocalUrl; pubColor = Th.Muted; }
            else if (!Caddy.Installed) { pubTxt = "Falta preparar el HTTPS (vuelve a marcar la casilla)."; pubColor = Th.Warn; }
            else if (Caddy.Running) { pubTxt = "Desde internet: " + WebPanel.PublicUrl + (Caddy.LastError != null ? "\nAviso de Caddy: " + Caddy.LastError : ""); pubColor = Caddy.LastError != null ? Th.Warn : Th.Good; }
            else { pubTxt = "El HTTPS no está en marcha" + (Caddy.LastError != null ? ": " + Caddy.LastError : " (reintentando).") ; pubColor = Th.Warn; }
            if (webPublicState.Text != pubTxt) webPublicState.Text = pubTxt;
            webPublicState.ForeColor = pubColor;

            string mapTxt = !s.WebEnabled ? "" : ServerWatch.MapUp ? "Mapa del mundo: funcionando (botón \"Mapa del mundo\" en la web)"
                : runner.Running ? "Mapa del mundo: no responde. ¿Está el mod WebMap en la lista de mods del server?" : "Mapa del mundo: se enciende con el server";
            if (webMap.Text != mapTxt) webMap.Text = mapTxt;
            string admTxt = WebAdmin.HasPassword ? "Activada: entra en " + (s.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "admin como " + WebAdmin.User + ". Los demás entran como invitados y solo ven." :"Desactivada (sin contraseña). Nadie puede tocar el server desde la web.";
            if (webAdmin.Text != admTxt) webAdmin.Text = admTxt;
            webAdmin.ForeColor = WebAdmin.HasPassword ? Th.Good : Th.Muted;
            webMap.ForeColor = ServerWatch.MapUp ? Th.Good : Th.Muted;

            string ip = (s.LanHost ?? "").Trim();
            if (ip == "") ip = Net.LocalIPv4().FirstOrDefault() ?? "este PC";
            string router = "Para que se vea desde fuera de casa, abre en el router estos puertos hacia " + ip + ":\n" +
                "   •  TCP 443  (la web, cifrada)\n" +
                "   •  TCP 80   (solo para sacar y renovar el certificado; la web nunca se sirve sin cifrar)\n" +
                "El juego sigue igual (UDP 2456-2457).\n\n" +
                "Si en el router tienes \"Acceso web desde WAN\" en el puerto 443, cámbialo a otro (por ejemplo 8443).\n" +
                "La dirección pública sale de la pestaña Amigos: " + WebPanel.PublicHost + ".";
            if (webRouter.Text != router) webRouter.Text = router;
        }
    }
}
#endif
