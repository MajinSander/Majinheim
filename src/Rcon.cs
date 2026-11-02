// Mensajes al chat del juego (y otras órdenes) con el mod ValheimRcon, solo dentro de este PC (127.0.0.1).
// Protocolo RCON de Source: [tamaño int32][id int32][tipo int32][texto\0][\0], tipo 3 = entrar, 2 = orden.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Majinheim
{
    public static class Rcon
    {
        public const string ConfigName = "org.tristan.rcon.cfg";
        static readonly object lk = new object();
        public static string ConfigFile { get { return Path.Combine(AppInfo.Profile, @"BepInEx\config", ConfigName); } }
        public static int Port { get { return Settings.Current.Port + 2; } }
        static string Password { get { return Secret.Unprotect(Settings.Current.RconPassword); } }
        public static bool Installed { get { return File.Exists(ConfigFile); } }
        public static bool Ready { get { return Password != null && Installed; } }

        // Deja el .cfg del mod con una contraseña larga al azar (guardada cifrada en launcher.json), el puerto y solo 127.0.0.1.
        // Devuelve null si ha ido bien. Hay que hacerlo con el server apagado (el mod lee su .cfg al arrancar).
        public static string Configure()
        {
            if (!Installed) return "Falta el mod ValheimRcon en el server, o no ha arrancado nunca (su ajuste se crea la primera vez).";
            string pw = Password;
            if (pw == null)
            {
                var b = new byte[24];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
                pw = Convert.ToBase64String(b).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
                Settings.Current.RconPassword = Secret.Protect(pw);
                Settings.Save();
            }
            var lines = File.ReadAllLines(ConfigFile).ToList();
            bool sawPw = false, sawPort = false, sawWhite = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*([^#;\[=][^=]*?)\s*=\s*(.*)$");
                if (!m.Success) continue;
                string key = m.Groups[1].Value.Trim(), k = key.ToLowerInvariant();
                if (k.Contains("password")) { lines[i] = key + " = " + pw; sawPw = true; }
                else if (k == "port" || k.EndsWith(" port") || k.StartsWith("rcon port") || k.Contains("rconport")) { lines[i] = key + " = " + Port; sawPort = true; }
                else if (k.Contains("white")) { lines[i] = key + " = 127.0.0.1"; sawWhite = true; }
                else if (k.Contains("webhook")) lines[i] = key + " = ";   // nada de webhooks del mod: los avisos los da el launcher
            }
            if (!sawPw) return "No encuentro la contraseña en " + ConfigName + ": revisa el formato del mod.";
            File.WriteAllLines(ConfigFile, lines, new UTF8Encoding(false));
            Log.W("ValheimRcon configurado: puerto {0}, solo 127.0.0.1{1}{2}.", Port, sawPort ? "" : " (puerto por defecto del mod)", sawWhite ? "" : " (sin lista blanca en el .cfg)");
            return null;
        }

        // Ejecuta una orden y devuelve la respuesta. Lanza excepción si no se puede.
        public static string Exec(string command)
        {
            string pw = Password;
            if (pw == null) throw new Exception("Los avisos en el juego no están configurados.");
            lock (lk)
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000)) throw new Exception("el server no responde a las órdenes (¿está encendido?)");
                    c.EndConnect(ar);
                    c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
                    var s = c.GetStream();
                    Write(s, 1, 3, pw);
                    int id, type; string body;
                    do Read(s, out id, out type, out body); while (type == 0 && id == 1);   // algunos servidores mandan un paquete vacío antes
                    if (id == -1) throw new Exception("contraseña de órdenes rechazada");
                    Write(s, 2, 2, command);
                    Read(s, out id, out type, out body);
                    return body;
                }
        }

        // Mensaje al chat de todos los jugadores. Devuelve null si ha llegado, o el problema.
        public static string Say(string text)
        {
            text = Regex.Replace(text ?? "", @"[\r\n\t\x00-\x1f]+", " ").Trim();
            if (text.Length == 0) return "El mensaje está vacío.";
            if (text.Length > 200) text = text.Substring(0, 200);
            try { Exec("say " + text); return null; }
            catch (Exception e) { return e.Message; }
        }

        static void Write(Stream s, int id, int type, string body)
        {
            var b = Encoding.UTF8.GetBytes(body);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(4 + 4 + b.Length + 2); w.Write(id); w.Write(type); w.Write(b); w.Write((byte)0); w.Write((byte)0);
            var all = ms.ToArray();
            s.Write(all, 0, all.Length);
        }

        static void Read(Stream s, out int id, out int type, out string body)
        {
            var r = new BinaryReader(s);
            int size = r.ReadInt32();
            if (size < 10 || size > 1 << 20) throw new Exception("respuesta rara del server");
            id = r.ReadInt32(); type = r.ReadInt32();
            var b = r.ReadBytes(size - 8);
            body = Encoding.UTF8.GetString(b, 0, Math.Max(0, b.Length - 2));
        }
    }
}
