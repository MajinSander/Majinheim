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
        CheckBox cWebOn, cWebPublic, cLiveAll;
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
            cLiveAll = Ui.Chk("Mapa en vivo: enseñar a todos, aunque no se hayan marcado como visibles en el juego", s.LiveShowAll);
            webPopulating = false;
            cLiveAll.CheckedChanged += (o, e) => { if (!webPopulating) { Settings.Current.LiveShowAll = cLiveAll.Checked; Settings.Save(); Status(cLiveAll.Checked ? "El mapa en vivo enseña a todos los que juegan." : "El mapa en vivo solo enseña a quien se marca como visible en el juego (tú, con tu cuenta de administrador, ves a todos)."); } };
            cWebOn.CheckedChanged += (o, e) => { if (!webPopulating) SetWeb(cWebOn.Checked, cWebPublic.Checked); };
            cWebPublic.CheckedChanged += (o, e) => { if (!webPopulating) SetWeb(cWebOn.Checked, cWebPublic.Checked); };

            var left = Ui.Col(webState, webPublicState, webMap, cWebOn, cWebPublic, cLiveAll,
                Ui.Row(Ui.Btn("Abrir la web", () => Ui.Open(Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl), true),
                       Ui.Btn("Copiar enlace para amigos", () => { Clipboard.SetText(WebPanel.PublicUrl); Status("Enlace copiado: " + WebPanel.PublicUrl); })),
                Ui.Row(Ui.Btn("Comprobar desde internet", CheckWebPublic),
                       Ui.Btn("Ver en este PC", () => Ui.Open(WebPanel.LocalUrl)),
                       Ui.Btn("Mapa", () => Ui.Open((Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "mapa/")),
                       Ui.Btn("Mapa en vivo", () => Ui.Open((Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "vivo"))),
                webCheck,
                Ui.Lbl("Administrar desde la web", 11, FontStyle.Bold), webAdmin,
                Ui.Row(Ui.Btn("Añadir o cambiar cuenta", EditWebUser),
                       Ui.Btn("Borrar cuenta", RemoveWebUser),
                       Ui.Btn("Cerrar sesiones", () => { if (Confirm("¿Cerrar la sesión en todos los dispositivos? Todos tendrán que volver a entrar.")) { WebAdmin.ForgetAllSessions(); Status("Sesiones cerradas en todos los dispositivos."); UpdateWebState(); } })),
                Ui.Row(Ui.Btn("Abrir", () => Ui.Open((Settings.Current.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "admin")),
                       Ui.Btn("Registro", () => OpenIfExists(Path.Combine(AppInfo.LogDir, "web-admin.log"))),
                       Ui.Btn("Quitar verificación en dos pasos", RemoveWebTotp)));

            webRouter = Ui.Lbl("", 10, FontStyle.Regular, Th.Text);
            webRouter.MaximumSize = new Size(560, 0);
            var shows = Ui.Lbl("Qué ve la gente: si el server está encendido, quién está jugando y desde cuándo, las horas de la semana, " +
                "las últimas partidas, la lista de mods y el mapa del mundo (lo explorado y quién se deja ver en el mapa). " +
                "Los botones del server, el registro y las IPs solo los ve quien entra con una cuenta de administrador.", 9.5f, FontStyle.Regular, Th.Muted);
            shows.MaximumSize = new Size(560, 0);
            var home = Ui.Lbl("Muchos routers no dejan abrir la dirección pública desde dentro de casa. En casa, el estado, las estadísticas " +
                "y el mapa se abren en la dirección local (arriba, \"En casa\"); para entrar con una cuenta hace falta la dirección segura. " +
                "En un PC de casa se consigue con una línea en su archivo hosts (C:\\Windows\\System32\\drivers\\etc\\hosts, abriendo el Bloc de notas " +
                "como administrador): cópiala con el botón y pégala al final.", 9.5f, FontStyle.Regular, Th.Muted);
            home.MaximumSize = new Size(560, 0);
            var right = Ui.Col(Ui.Lbl("En el router (una sola vez)", 11, FontStyle.Bold), webRouter,
                Ui.Lbl("En casa", 11, FontStyle.Bold), home,
                Ui.Row(Ui.Btn("Copiar línea para el PC de casa", () =>
                {
                    string lan = Caddy.LanAddress();
                    if (lan == null) { MessageBox.Show(this, "No encuentro la IP de este PC en la red de casa.", "Majinheim"); return; }
                    Clipboard.SetText(lan + " " + WebPanel.PublicHost);
                    Status("Copiado: " + lan + " " + WebPanel.PublicHost + " (pégalo al final del archivo hosts del PC de casa).");
                })),
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

        // Cuenta nueva o cambio de una existente: usuario, papel y contraseña (vacía = dejar la que tenía).
        void EditWebUser()
        {
            string u = Ui.Ask(this, "Cuenta de la web", "Nombre de usuario (si ya existe, la cambias):", "");
            if (u == null || u.Trim() == "") return;
            var old = WebAdmin.List().FirstOrDefault(x => string.Equals(x.Name, u.Trim(), StringComparison.OrdinalIgnoreCase));
            var ans = MessageBox.Show(this, "¿" + u.Trim() + " puede administrar el server (encender, apagar, reiniciar, backups, ver registro e IPs)?\n\n" +
                "Sí = administrador.   No = jugador (ve el estado y el mapa, como un invitado).", "Cuenta de la web", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                old != null && old.Role == WebAdmin.RoleAdmin ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2);
            if (ans == DialogResult.Cancel) return;
            string role = ans == DialogResult.Yes ? WebAdmin.RoleAdmin : WebAdmin.RolePlayer;
            string hint = role == WebAdmin.RoleAdmin ? " (mínimo " + WebAdmin.MinAdminPassword + " caracteres, distinta de la del server)" : "";
            string a = Ui.Ask(this, "Cuenta de la web", "Contraseña de " + u.Trim() + hint + "." + (old != null ? " Déjala vacía para no cambiarla." : "") + " No se guarda en claro.", "", true);
            if (a == null) return;
            if (a == "" && old != null) a = null;
            else
            {
                string b = Ui.Ask(this, "Cuenta de la web", "Repite la contraseña.", "", true);
                if (b == null) return;
                if (a != b) { MessageBox.Show(this, "No coinciden.", "Majinheim"); return; }
            }
            string err = WebAdmin.SetUser(u, role, a);
            if (err != null) { MessageBox.Show(this, err, "Majinheim"); return; }
            Status("Cuenta guardada: " + u.Trim() + (role == WebAdmin.RoleAdmin ? " (administrador)" : " (jugador)") + ".");
            UpdateWebState();
        }

        void RemoveWebUser()
        {
            var all = WebAdmin.List();
            if (all.Count == 0) { MessageBox.Show(this, "No hay cuentas.", "Majinheim"); return; }
            string u = Ui.Ask(this, "Borrar cuenta", "¿Qué cuenta borro? Hay: " + string.Join(", ", all.Select(x => x.Name)), "");
            if (u == null || u.Trim() == "") return;
            if (!Confirm("¿Borrar la cuenta " + u.Trim() + "? Se cerrarán sus sesiones.")) return;
            if (!WebAdmin.RemoveUser(u)) { MessageBox.Show(this, "No existe esa cuenta.", "Majinheim"); return; }
            Status("Cuenta borrada: " + u.Trim() + ".");
            UpdateWebState();
        }

        // Si alguien pierde el móvil con la app de los códigos: desde aquí (el PC del server) se le quita y vuelve a entrar solo con la contraseña.
        void RemoveWebTotp()
        {
            var with = WebAdmin.List().Where(WebAdmin.HasTotp).ToList();
            if (with.Count == 0) { MessageBox.Show(this, "Ninguna cuenta tiene la verificación en dos pasos activada. Se activa desde la web de administración, en «Seguridad de tu cuenta».", "Majinheim"); return; }
            string u = with.Count == 1 ? with[0].Name : Ui.Ask(this, "Verificación en dos pasos", "¿A qué cuenta se la quito? La tienen: " + string.Join(", ", with.Select(x => x.Name)), "");
            if (u == null || u.Trim() == "") return;
            if (!Confirm("¿Quitar la verificación en dos pasos de " + u.Trim() + "?\n\nPodrá entrar solo con la contraseña y volver a activarla desde la web (por ejemplo, con el móvil nuevo).")) return;
            string err = WebAdmin.RemoveTotp(u);
            if (err != null) { MessageBox.Show(this, err, "Majinheim"); return; }
            Status("Verificación en dos pasos quitada a " + u.Trim() + ".");
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
            string msg = null; bool ok = false;
            RunBg("Comprobando " + WebPanel.PublicUrl + " ...", () => { ok = WebPanel.CheckPublic(out msg); }, () =>
            {
                webCheck.Text = (ok ? "✔ " : "✖ ") + msg;
                webCheck.ForeColor = ok ? Th.Good : Th.Warn;
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
            else if (!Caddy.Installed) { pubTxt = Caddy.Installing ? "Preparando el HTTPS (descargando Caddy)..." : "Falta preparar el HTTPS" + (Caddy.LastError != null ? ": " + Caddy.LastError : "") + ". Se reintenta al abrir el launcher, o vuelve a marcar la casilla."; pubColor = Th.Warn; }
            else if (Caddy.Running)
            {
                string lan = Caddy.LanAddress();
                pubTxt = "Desde internet: " + WebPanel.PublicUrl + (lan != null ? "\nEn casa (solo ver): http://" + lan + ":" + WebPanel.Port + "/" : "") +
                    (Caddy.LastError != null ? "\nAviso de Caddy: " + Caddy.LastError : "");
                pubColor = Caddy.LastError != null ? Th.Warn : Th.Good;
            }
            else { pubTxt = "El HTTPS no está en marcha" + (Caddy.LastError != null ? ": " + Caddy.LastError : " (reintentando).") ; pubColor = Th.Warn; }
            if (webPublicState.Text != pubTxt) webPublicState.Text = pubTxt;
            webPublicState.ForeColor = pubColor;

            string mapTxt = !s.WebEnabled ? "" : ServerWatch.MapUp ? "Mapa del mundo: funcionando (botón \"Mapa del mundo\" en la web)"
                : runner.Running ? "Mapa del mundo: no responde. ¿Está el mod WebMap en la lista de mods del server?" : "Mapa del mundo: se enciende con el server";
            if (webMap.Text != mapTxt) webMap.Text = mapTxt;
            var users = WebAdmin.List();
            string admTxt = (WebAdmin.HasAdmin ? "Controles activados. Se entra en " + (s.WebPublic ? WebPanel.PublicUrl : WebPanel.LocalUrl) + "admin." : "Controles desactivados: no hay ningún administrador. Nadie puede tocar el server desde la web.") +
                "\nCuentas: " + (users.Count == 0 ? "ninguna" : string.Join(", ", users.Select(x => x.Name + (x.Role == WebAdmin.RoleAdmin ? (WebAdmin.HasTotp(x) ? " (admin, con código)" : " (admin)") : "")))) +
                ". Sin cuenta se entra como invitado (solo ver).";
            if (webAdmin.Text != admTxt) webAdmin.Text = admTxt;
            webAdmin.ForeColor = WebAdmin.HasAdmin ? Th.Good : Th.Muted;
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
