// Bot de Discord con comandos (/estado, /jugadores, /estadisticas, /web y, solo para administradores del Discord,
// /encender, /apagar, /reiniciar, /backup). Se conecta al gateway de Discord para recibir los comandos y responde por la API REST.
// Con el asistente IA activado, además contesta cuando lo mencionan (@bot): Discord solo le da el texto de los mensajes que
// lo mencionan (sin el permiso especial "Message Content"), así que no lee el resto de conversaciones. Edición Server.
#if SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Majinheim
{
    public static class DiscordBot
    {
        const string GatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
        const long PermAdministrator = 0x8, PermManageGuild = 0x20;

        static CancellationTokenSource cts;
        static int seq = -1;
        static string appId;
        static string registeredFor;
        static string botId;
        const int IntentGuildMessages = 1 << 9;   // avisos de mensajes nuevos (el texto, solo de los que mencionan al bot)
        public static string State { get; private set; }
        public static bool Connected { get; private set; }

        static string Token { get { return Secret.Unprotect(Settings.Current.DiscordToken); } }

        // Arranca o para el bot según los ajustes. Se puede llamar las veces que haga falta.
        public static void Apply()
        {
            Stop();
            if (!Settings.Current.DiscordCommands || Token == null) { State = Token == null ? null : "Comandos desactivados."; return; }
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            State = "Conectando...";
            var t = new Thread(() => Loop(ct)) { IsBackground = true, Name = "DiscordBot" };
            t.Start();
        }

        public static void Stop()
        {
            if (cts != null) { cts.Cancel(); cts = null; }
            Connected = false;
        }

        static void Loop(CancellationToken ct)
        {
            int backoff = 5;
            while (!ct.IsCancellationRequested)
            {
                string fatal = null;
                try { fatal = Session(ct).GetAwaiter().GetResult(); backoff = 5; }
                catch (Exception e) { if (!ct.IsCancellationRequested) { State = "Sin conexión con Discord (" + e.GetBaseException().Message + "). Reintentando."; } }
                Connected = false;
                if (fatal != null) { State = fatal; Log.W("Bot de Discord: {0}", fatal); return; }
                if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(backoff))) return;
                backoff = Math.Min(backoff * 2, 300);
            }
        }

        // Una conexión al gateway. Devuelve un error que no se arregla reintentando (token malo...) o null para reconectar.
        static async Task<string> Session(CancellationToken ct)
        {
            using (var ws = new ClientWebSocket())
            {
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await ws.ConnectAsync(new Uri(GatewayUrl), ct);
                var hello = await Receive(ws, ct);
                if (hello == null || Convert.ToInt32(hello["op"]) != 10) return null;
                int interval = Convert.ToInt32(((Dictionary<string, object>)hello["d"])["heartbeat_interval"]);
                var sendLock = new SemaphoreSlim(1, 1);
                bool acked = true;
                using (var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var hb = Task.Run(async () =>
                    {
                        await Task.Delay(new Random().Next(interval), hbCts.Token);
                        while (!hbCts.IsCancellationRequested)
                        {
                            if (!acked) { try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "sin respuesta", CancellationToken.None); } catch { } return; }
                            acked = false;
                            await Send(ws, sendLock, new Dictionary<string, object> { { "op", 1 }, { "d", seq < 0 ? null : (object)seq } }, hbCts.Token);
                            await Task.Delay(interval, hbCts.Token);
                        }
                    }, hbCts.Token);

                    await Send(ws, sendLock, new Dictionary<string, object>
                    {
                        { "op", 2 },
                        { "d", new Dictionary<string, object>
                            {
                                { "token", Token },
                                { "intents", Settings.Current.AiEnabled ? IntentGuildMessages : 0 },
                                { "properties", new Dictionary<string, object> { { "os", "windows" }, { "browser", "majinheim" }, { "device", "majinheim" } } },
                            }
                        },
                    }, ct);

                    try
                    {
                        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            var msg = await Receive(ws, ct);
                            if (msg == null) break;
                            int op = Convert.ToInt32(msg["op"]);
                            object s;
                            if (msg.TryGetValue("s", out s) && s != null) seq = Convert.ToInt32(s);
                            if (op == 11) { acked = true; continue; }
                            if (op == 1) { await Send(ws, sendLock, new Dictionary<string, object> { { "op", 1 }, { "d", seq } }, ct); continue; }
                            if (op == 7 || op == 9) break;   // Discord pide reconectar
                            if (op != 0) continue;
                            string t = msg["t"] as string;
                            var d = msg["d"] as Dictionary<string, object>;
                            if (t == "READY") OnReady(d);
                            else if (t == "INTERACTION_CREATE") { var dd = d; var _ = Task.Run(() => OnInteraction(dd)); }
                            else if (t == "MESSAGE_CREATE" && Settings.Current.AiEnabled) { var dd = d; var _ = Task.Run(() => OnMessage(dd)); }
                        }
                    }
                    finally { hbCts.Cancel(); }
                }
                var code = ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : 0;
                if (code == 4004) return "El token del bot no es válido. Vuelve a pegarlo en la pestaña Automático.";
                if (code == 4013 || code == 4014) return "Discord no deja conectar al bot con estos permisos.";
                return null;
            }
        }

        static async Task Send(ClientWebSocket ws, SemaphoreSlim lk, object o, CancellationToken ct)
        {
            var b = Encoding.UTF8.GetBytes(Json.Str(o));
            await lk.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, ct); }
            finally { lk.Release(); }
        }

        static async Task<Dictionary<string, object>> Receive(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[16384];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    ms.Write(buf, 0, r.Count);
                    if (ms.Length > 4 * 1024 * 1024) return null;
                    if (r.EndOfMessage) break;
                }
                return Json.ParseObj(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }

        static void OnReady(Dictionary<string, object> d)
        {
            Connected = true;
            var app = d["application"] as Dictionary<string, object>;
            appId = app != null ? app["id"] as string : null;
            var user = d["user"] as Dictionary<string, object>;
            botId = user != null ? user["id"] as string : null;
            State = "Conectado como " + (user != null ? user["username"] : "bot") + "." + (Settings.Current.AiEnabled ? " Responde cuando lo mencionan (@" + (user != null ? user["username"] : "bot") + ")." : "");
            if (appId != null && registeredFor != appId)
            {
                try { RegisterCommands(); registeredFor = appId; }
                catch (Exception e) { State = "Conectado, pero no pude registrar los comandos: " + e.Message; Log.W("Bot de Discord: registrar comandos: {0}", e.Message); }
            }
        }

        // ------------------------------------------------------------ comandos

        static readonly string[] AdminCommands = { "encender", "apagar", "reiniciar", "backup", "avisar", "salud", "actualizar" };

        static void RegisterCommands()
        {
            Func<string, string, bool, Dictionary<string, object>> cmd = (name, desc, admin) =>
            {
                var c = new Dictionary<string, object> { { "name", name }, { "description", desc }, { "type", 1 }, { "contexts", new[] { 0 } } };
                if (admin) c["default_member_permissions"] = PermAdministrator.ToString(CultureInfo.InvariantCulture);
                return c;
            };
            var list = new List<object>
            {
                cmd("estado", "Si el server está encendido, quién juega y el día en el mundo", false),
                cmd("jugadores", "Quién está jugando ahora y cuánto lleva", false),
                cmd("estadisticas", "Horas jugadas por cada vikingo", false),
                cmd("web", "Enlaces a la web del server y al mapa", false),
                cmd("encender", "Enciende el server (solo administradores)", true),
                cmd("apagar", "Guarda el mundo y apaga el server (solo administradores)", true),
                cmd("reiniciar", "Guarda el mundo y reinicia el server (solo administradores)", true),
                cmd("backup", "Hace un backup completo del server (solo administradores)", true),
                cmd("salud", "Revisión del server: backups, disco, HTTPS, mods, Valheim... (solo administradores)", true),
                cmd("actualizar", "Actualiza mods y Valheim con backup, prueba de arranque y vuelta atrás (solo administradores)", true),
            };
            var avisar = cmd("avisar", "Manda un mensaje al chat del juego (solo administradores)", true);
            avisar["options"] = new[] { new Dictionary<string, object> { { "type", 3 }, { "name", "mensaje" }, { "description", "Lo que verán los jugadores" }, { "required", true }, { "max_length", 200 } } };
            list.Add(avisar);
            Rest("PUT", "/applications/" + appId + "/commands", list);
            Log.W("Bot de Discord: comandos registrados.");
        }

        static void OnInteraction(Dictionary<string, object> d)
        {
            try
            {
                if (Convert.ToInt32(d["type"]) != 2) return;   // solo comandos
                string id = d["id"] as string, itoken = d["token"] as string;
                var data = d["data"] as Dictionary<string, object>;
                string name = data != null ? data["name"] as string : null;
                object mo; d.TryGetValue("member", out mo);
                var member = mo as Dictionary<string, object>;
                string reply; bool ephemeral = false;
                if (member == null) reply = "Los comandos solo funcionan dentro del servidor de Discord.";
                else if (AdminCommands.Contains(name))
                {
                    ephemeral = false;
                    var user = member["user"] as Dictionary<string, object>;
                    string who = user != null ? (string)user["username"] : "?";
                    long perms = 0;
                    long.TryParse(member["permissions"] as string ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out perms);
                    if ((perms & (PermAdministrator | PermManageGuild)) == 0) { reply = "Este comando es solo para los administradores del Discord."; ephemeral = true; }
                    else
                    {
                        string what = name == "encender" ? "iniciar" : name == "apagar" ? "parar" : name;
                        if (name == "salud")
                        {
                            var h = WebAdmin.Health;
                            reply = h == null ? "El launcher del server no está listo." : h();
                            ephemeral = true;   // solo lo ve quien lo pide
                        }
                        else if (name == "actualizar")
                        {
                            var m = WebAdmin.Maintain;
                            reply = m == null ? "El launcher del server no está listo." : m(who + " (Discord)");
                        }
                        else if (name == "avisar")
                        {
                            string text = Option(data, "mensaje");
                            var say = WebAdmin.Say;
                            string err = say == null ? "el launcher del server no está listo" : say(text);
                            reply = err == null ? "mensaje enviado al chat del juego: «" + Esc(text) + "»" : "no se pudo mandar el mensaje: " + err;
                            what = "aviso en el juego";
                        }
                        else
                        {
                            var act = WebAdmin.Action;
                            reply = act == null ? "El launcher del server no está listo." : act(what);
                        }
                        if (name != "salud")
                        {
                            WebAdmin.Audit("Discord", who + " · " + what + ": " + reply);
                            reply = "**" + Esc(who) + "**: " + reply;
                        }
                        if (reply.Length > 2000) reply = reply.Substring(0, 1999) + "…";
                    }
                }
                else reply = Answer(name);
                var resp = new Dictionary<string, object>
                {
                    { "type", 4 },
                    { "data", new Dictionary<string, object> { { "content", reply }, { "flags", ephemeral ? 64 : 0 }, { "allowed_mentions", new Dictionary<string, object> { { "parse", new string[0] } } } } },
                };
                Rest("POST", "/interactions/" + id + "/" + itoken + "/callback", resp, false);
            }
            catch (Exception e) { Log.W("Bot de Discord: comando falló: {0}", e.Message); }
        }

        // ------------------------------------------------------------ asistente IA: se le habla mencionándolo (@bot)

        static readonly Dictionary<string, DateTime> lastAsk = new Dictionary<string, DateTime>();
        const int AskCooldown = 10;   // segundos entre preguntas de la misma persona
        // Lo último hablado en cada canal, para poder seguir la conversación ("¿y ayer?"). Se olvida a los 10 minutos.
        static readonly Dictionary<string, List<string[]>> chats = new Dictionary<string, List<string[]>>();
        static readonly Dictionary<string, DateTime> chatAt = new Dictionary<string, DateTime>();

        static void OnMessage(Dictionary<string, object> d)
        {
            try
            {
                var author = d["author"] as Dictionary<string, object>;
                if (author == null || botId == null || !d.ContainsKey("guild_id")) return;
                object isBot;
                if (author.TryGetValue("bot", out isBot) && isBot is bool && (bool)isBot) return;   // ni a sí mismo ni a otros bots
                object mo;
                bool mentioned = d.TryGetValue("mentions", out mo) && mo is object[] &&
                    ((object[])mo).OfType<Dictionary<string, object>>().Any(u => (u["id"] as string) == botId);
                if (!mentioned) return;
                string channel = d["channel_id"] as string, msgId = d["id"] as string, guild = d["guild_id"] as string;
                string uid = author["id"] as string;
                string text = Regex.Replace(d["content"] as string ?? "", "<@!?" + botId + ">", "").Trim();
                var member = d.ContainsKey("member") ? d["member"] as Dictionary<string, object> : null;
                string who = (member != null && member.ContainsKey("nick") ? member["nick"] as string : null)
                    ?? (author.ContainsKey("global_name") ? author["global_name"] as string : null) ?? (author["username"] as string) ?? "?";

                if (text.Length == 0) { Reply(channel, msgId, Settings.Current.WorldExport
                    ? "¡Dime! Pregúntame lo que quieras del server: quién juega, quién jugó ayer, qué jefe falta, dónde está el hierro, cómo se hace algo, dónde está tu tumba..."
                    : "¡Dime! Pregúntame lo que quieras del server: quién juega, quién jugó ayer, cuántas horas lleva cada uno, qué jefe falta..."); return; }
                lock (lastAsk)
                {
                    DateTime prev;
                    if (lastAsk.TryGetValue(uid, out prev) && (DateTime.Now - prev).TotalSeconds < AskCooldown) { Reply(channel, msgId, "Dame unos segundos, que aún estoy con lo anterior."); return; }
                    lastAsk[uid] = DateTime.Now;
                }
                bool admin = IsAdmin(guild, member, uid);
                List<string[]> past;
                lock (chats)
                {
                    DateTime at;
                    if (!chats.TryGetValue(channel, out past) || !chatAt.TryGetValue(channel, out at) || (DateTime.Now - at).TotalMinutes > 10) past = new List<string[]>();
                    past = past.ToList();
                }
                try { Rest("POST", "/channels/" + channel + "/typing", new Dictionary<string, object>()); } catch { }   // "escribiendo..."
                var t0 = DateTime.Now;
                string answer = AiChat.Ask(text, who, admin, past);
                Log.W("Bot de Discord: pregunta a la IA de {0} respondida en {1:0.0} s.", who, (DateTime.Now - t0).TotalSeconds);
                lock (chats)
                {
                    past.Add(new[] { who + ": " + text, answer });
                    chats[channel] = past.Skip(Math.Max(0, past.Count - 3)).ToList();
                    chatAt[channel] = DateTime.Now;
                }
                Reply(channel, msgId, answer);
            }
            catch (Exception e) { Log.W("Bot de Discord: no pude responder a una mención: {0}", e.Message); }
        }

        // Responde al mensaje (como respuesta, sin avisar a nadie). Si no puede citarlo, lo manda normal.
        static void Reply(string channel, string msgId, string text)
        {
            if (text.Length > 2000) text = text.Substring(0, 1999) + "…";
            var body = new Dictionary<string, object>
            {
                { "content", text },
                { "allowed_mentions", new Dictionary<string, object> { { "parse", new string[0] }, { "replied_user", false } } },
                { "message_reference", new Dictionary<string, object> { { "message_id", msgId }, { "fail_if_not_exists", false } } },
            };
            try { Rest("POST", "/channels/" + channel + "/messages", body); }
            catch { body.Remove("message_reference"); Rest("POST", "/channels/" + channel + "/messages", body); }
        }

        // ¿Es administrador del Discord? (dueño o con un rol de Administrador / Gestionar servidor). Los roles se miran cada 10 min.
        static Dictionary<string, object> guildCache; static DateTime guildAt; static string guildFor;
        static bool IsAdmin(string guild, Dictionary<string, object> member, string uid)
        {
            try
            {
                Dictionary<string, object> g;
                lock (lastAsk)
                {
                    if (guildFor != guild || guildCache == null || (DateTime.Now - guildAt).TotalMinutes > 10)
                    { guildCache = RestGet("/guilds/" + guild); guildFor = guild; guildAt = DateTime.Now; }
                    g = guildCache;
                }
                if ((g["owner_id"] as string) == uid) return true;
                var mine = new HashSet<string> { guild };   // @everyone tiene el mismo id que el servidor
                object rs;
                if (member != null && member.TryGetValue("roles", out rs) && rs is object[]) foreach (var r in (object[])rs) mine.Add(r as string);
                long perms = 0;
                foreach (var role in ((object[])g["roles"]).OfType<Dictionary<string, object>>())
                {
                    if (!mine.Contains(role["id"] as string)) continue;
                    long p; long.TryParse(Convert.ToString(role["permissions"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out p);
                    perms |= p;
                }
                return (perms & (PermAdministrator | PermManageGuild)) != 0;
            }
            catch { return false; }
        }

        static string Option(Dictionary<string, object> data, string name)
        {
            object o;
            if (data == null || !data.TryGetValue("options", out o) || o == null) return "";
            foreach (var x in ((System.Collections.IEnumerable)o).Cast<object>().OfType<Dictionary<string, object>>())
                if ((x["name"] as string) == name) return Convert.ToString(x["value"], CultureInfo.InvariantCulture) ?? "";
            return "";
        }

        static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|").Replace("@", "@​"); }
        static string Dur(int min) { return min < 60 ? min + " min" : (min / 60) + " h " + (min % 60).ToString("00") + " min"; }

        // Respuestas públicas: lo mismo que ve cualquiera en la web, nada de red.
        public static string Answer(string name)
        {
            var snap = ServerWatch.Snapshot();
            var srv = (Dictionary<string, object>)snap["servidor"];
            var players = ((IEnumerable<object>)snap["jugadores"]).Cast<Dictionary<string, object>>().ToList();
            string estado = (string)srv["estado"];
            string web = Settings.Current.WebEnabled && Settings.Current.WebPublic ? WebPanel.PublicUrl : null;
            switch (name)
            {
                case "estado":
                {
                    var sb = new StringBuilder();
                    sb.Append(estado == "en marcha" ? "🟢 **" + Esc((string)srv["nombre"]) + "** está encendido" : estado == "arrancando" ? "🟡 El server está arrancando" : "⚫ El server está apagado");
                    if (srv["dia"] != null) sb.Append(" · día " + srv["dia"] + " en el mundo");
                    sb.Append(".\n");
                    if (estado != "apagado") sb.Append(players.Count == 0 ? "No hay nadie jugando." : "Jugando: " + string.Join(", ", players.Select(p => "**" + Esc((string)p["nombre"]) + "** (" + Dur(Convert.ToInt32(p["minutos"])) + ")")) + ".");
                    var jefes = Bosses.Public();
                    if (jefes.Count > 0)
                    {
                        var hechos = jefes.Where(j => (bool)j["derrotado"]).Select(j => (string)j["nombre"]).ToList();
                        sb.Append("\n⚔️ Jefes: " + hechos.Count + "/" + jefes.Count + (hechos.Count > 0 ? " (" + string.Join(", ", hechos) + ")" : ""));
                    }
                    var vst = Steam.Server();
                    if (vst != null && vst.UpdatePending) sb.Append("\n🆕 Hay una actualización de Valheim esperando: Steam la instala cuando el server está apagado.");
                    if (web != null) sb.Append("\n" + web);
                    return sb.ToString().TrimEnd();
                }
                case "jugadores":
                    if (estado == "apagado") return "⚫ El server está apagado.";
                    return players.Count == 0 ? "No hay nadie jugando ahora mismo." :
                        "Jugando ahora (" + players.Count + "):\n" + string.Join("\n", players.Select(p => "• **" + Esc((string)p["nombre"]) + "**: " + Dur(Convert.ToInt32(p["minutos"])) + (Convert.ToInt32(p["muertes"]) > 0 ? ", " + p["muertes"] + " muertes" : "")));
                case "estadisticas":
                {
                    var st = ServerWatch.Stats();
                    var list = ((IEnumerable<object>)st["jugadores"]).Cast<Dictionary<string, object>>().Take(10).ToList();
                    if (list.Count == 0) return "Todavía no hay partidas guardadas.";
                    var tot = (Dictionary<string, object>)st["total"];
                    return "📊 **" + Math.Round(Convert.ToInt32(tot["minutos"]) / 60.0) + " h** jugadas en " + tot["sesiones"] + " partidas.\n" +
                        string.Join("\n", list.Select((p, i) => (i + 1) + ". **" + Esc((string)p["nombre"]) + "**: " + Dur(Convert.ToInt32(p["minutos"])) + " (esta semana " + Dur(Convert.ToInt32(p["semana"])) + ")")) +
                        (web != null ? "\nMás en " + web + "estadisticas" : "");
                }
                case "web":
                    return web == null ? "La web del server todavía no está abierta a internet." :
                        "🌐 Web del server: " + web + "\n📊 Estadísticas: " + web + "estadisticas" + (snap["mapa"] != null ? "\n🗺️ Mapa del mundo: " + web + "mapa/" : "\n🗺️ El mapa se enciende con el server.");
            }
            return "No conozco ese comando.";
        }

        static Dictionary<string, object> RestGet(string path)
        {
            var r = (HttpWebRequest)WebRequest.Create(Discord.Api + path);
            r.UserAgent = "DiscordBot (https://github.com/" + AppInfo.GitHubRepo + ", " + AppInfo.Version + ")";
            r.Headers["Authorization"] = "Bot " + Token;
            r.Timeout = 15000;
            using (var resp = r.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return Json.ParseObj(sr.ReadToEnd());
        }

        static void Rest(string method, string path, object body, bool auth = true)
        {
            var r = (HttpWebRequest)WebRequest.Create(Discord.Api + path);
            r.Method = method;
            r.ContentType = "application/json";
            r.UserAgent = "DiscordBot (https://github.com/" + AppInfo.GitHubRepo + ", " + AppInfo.Version + ")";
            if (auth) r.Headers["Authorization"] = "Bot " + Token;
            r.Timeout = 15000;
            var b = Encoding.UTF8.GetBytes(Json.Str(body));
            using (var st = r.GetRequestStream()) st.Write(b, 0, b.Length);
            try { using (r.GetResponse()) { } }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                string detail = "";
                if (resp != null) try { using (var sr = new StreamReader(resp.GetResponseStream())) detail = sr.ReadToEnd(); } catch { }
                throw new Exception((resp != null ? "Discord respondió " + (int)resp.StatusCode : e.Message) + (detail.Length > 0 ? ": " + (detail.Length > 300 ? detail.Substring(0, 300) : detail) : ""));
            }
        }
    }
}
#endif
