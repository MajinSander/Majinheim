// Avisos en Discord con el bot del grupo (solo escribir en un canal, por la API REST de Discord) y secretos cifrados.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Majinheim
{
    // Secretos guardados en launcher.json cifrados con DPAPI: solo este usuario de Windows en este PC puede leerlos.
    public static class Secret
    {
        const string Prefix = "dpapi:";
        static readonly byte[] Extra = Encoding.UTF8.GetBytes("Majinheim");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            return Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Extra, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Extra, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
    }

    public static class Discord
    {
        public const string Api = "https://discord.com/api/v10";
        static readonly object lk = new object();
        static readonly Queue<string> queue = new Queue<string>();
        static Thread worker;
        public static string LastError { get; private set; }
        public static DateTime LastSent { get; private set; }

        static string Token { get { return Secret.Unprotect(Settings.Current.DiscordToken); } }
        static string Channel { get { return (Settings.Current.DiscordChannel ?? "").Trim(); } }
        public static bool Configured { get { return Token != null && IsSnowflake(Channel); } }
        public static bool IsSnowflake(string s) { if (string.IsNullOrEmpty(s) || s.Length < 15 || s.Length > 22) return false; foreach (char c in s) if (c < '0' || c > '9') return false; return true; }

        // Pone el mensaje en cola y vuelve enseguida. Si Discord no está configurado, no hace nada.
        public static void Send(string text)
        {
            if (!Configured || string.IsNullOrEmpty(text)) return;
            lock (lk)
            {
                if (queue.Count > 50) return;   // Discord caído un buen rato: no acumular sin fin
                queue.Enqueue(text.Length > 1900 ? text.Substring(0, 1900) + "…" : text);
                if (worker == null || !worker.IsAlive)
                {
                    worker = new Thread(Pump) { IsBackground = true, Name = "Discord" };
                    worker.Start();
                }
                Monitor.PulseAll(lk);
            }
        }

        static void Pump()
        {
            while (true)
            {
                string msg;
                lock (lk)
                {
                    while (queue.Count == 0) if (!Monitor.Wait(lk, 60000)) { worker = null; return; }
                    msg = queue.Peek();
                }
                int wait = 0;
                for (int tries = 0; tries < 4; tries++)
                {
                    string err = Post(Token, Channel, msg, out wait);
                    if (err == null) { LastError = null; LastSent = DateTime.Now; break; }
                    LastError = err;
                    if (wait <= 0) { Log.W("Discord: {0}", err); break; }   // fallo que no se arregla reintentando (token, permisos...)
                    Thread.Sleep(Math.Min(wait, 60000));
                }
                lock (lk) if (queue.Count > 0) queue.Dequeue();
                Thread.Sleep(1200);   // de sobra por debajo del límite de Discord (5 mensajes cada 5 s por canal)
            }
        }

        // Prueba la configuración con un mensaje de verdad. Devuelve null si ha llegado, o el problema en español.
        public static string Test(string token, string channel)
        {
            if (string.IsNullOrEmpty(token)) return "Falta el token del bot.";
            if (!IsSnowflake(channel)) return "El ID del canal tiene que ser un número largo (clic derecho en el canal > Copiar ID del canal).";
            int wait;
            return Post(token, channel, "✅ **Majinheim** conectado: a partir de ahora aviso aquí de lo que pasa en el server.", out wait);
        }

        static string Post(string token, string channel, string text, out int retryMs)
        {
            retryMs = 0;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                var r = (HttpWebRequest)WebRequest.Create(Api + "/channels/" + channel + "/messages");
                r.Method = "POST";
                r.ContentType = "application/json";
                r.UserAgent = "DiscordBot (https://github.com/" + AppInfo.GitHubRepo + ", " + AppInfo.Version + ")";
                r.Headers["Authorization"] = "Bot " + token;
                r.Timeout = 15000;
                var body = Encoding.UTF8.GetBytes(Json.Str(new Dictionary<string, object>
                {
                    { "content", text },
                    { "allowed_mentions", new Dictionary<string, object> { { "parse", new string[0] } } },   // nunca @everyone ni menciones
                }));
                using (var st = r.GetRequestStream()) st.Write(body, 0, body.Length);
                using (r.GetResponse()) { }
                return null;
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp == null) { retryMs = 15000; return "sin conexión con Discord (" + e.Status + ")"; }
                int code = (int)resp.StatusCode;
                string detail = "";
                try { using (var sr = new StreamReader(resp.GetResponseStream())) detail = sr.ReadToEnd(); } catch { }
                if (code == 429)
                {
                    double s = 5;
                    var m = System.Text.RegularExpressions.Regex.Match(detail, "\"retry_after\"\\s*:\\s*([0-9.]+)");
                    if (m.Success) double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
                    retryMs = (int)(s * 1000) + 250;
                    return "Discord pide esperar";
                }
                if (code >= 500) { retryMs = 15000; return "Discord no responde (" + code + ")"; }
                if (code == 401) return "el token del bot no es válido (cópialo otra vez en el portal de desarrolladores de Discord)";
                if (code == 403) return "el bot no tiene permiso para escribir en ese canal (dale \"Ver canal\" y \"Enviar mensajes\")";
                if (code == 404) return "no encuentro el canal: ¿el bot está en vuestro servidor de Discord y el ID es correcto?";
                return "Discord respondió " + code;
            }
            catch (Exception e) { retryMs = 15000; return e.Message; }
        }
    }
}
