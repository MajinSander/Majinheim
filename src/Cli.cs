// MajinheimCli.exe: misma logica que la edicion Server, por linea de comandos (diagnostico y pruebas).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Majinheim
{
    static class Cli
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Log.Line += Console.WriteLine;
            Settings.Load();
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            try
            {
                switch (cmd)
                {
                    case "import":
                        foreach (var d in Admin.ImportOriginal(args[1])) Console.WriteLine(d);
                        return 0;
                    case "install":
                        {
                            if (new ServerRunner().TryAdopt()) { Console.WriteLine("El server está en marcha: detenlo antes de instalar mods."); return 1; }
                            var pk = Modpack.Load();
                            var w = Installer.Install(pk, AppInfo.Profile, false, null, Console.WriteLine);
                            Console.WriteLine(w.Count == 0 ? "OK, sin avisos" : w.Count + " avisos");
                            return 0;
                        }
                    case "check":
                        foreach (var m in Modpack.Load().Mods)
                        {
                            try
                            {
                                var p = Thunderstore.Latest(m.Id);
                                Console.WriteLine("{0,-50} {1,-10} ultima {2,-10} {3} {4}", m.Id, m.Version, p.Version, p.Date, Ver.Compare(p.Version, m.Version) > 0 ? "<< ACTUALIZABLE" : "");
                            }
                            catch (Exception e) { Console.WriteLine("{0,-50} ERROR {1}", m.Id, e.Message); }
                        }
                        return 0;
                    case "args":
                        Console.WriteLine(new ServerRunner().BuildArgs());
                        return 0;
                    case "test":
                    case "run":
                        return Run(cmd == "test", args.Length > 1 ? int.Parse(args[1]) : 600);
                    case "stop":
                        {
                            var r = new ServerRunner();
                            if (!r.TryAdopt()) { Console.WriteLine("No hay ningun server de Majinheim en marcha."); return 1; }
                            r.Stop(true);
                            return 0;
                        }
                    case "publish":
                        Console.WriteLine(Publish.Pack(true));
                        return 0;
                    case "release":
                        Publish.Launcher(args.Length > 1 ? args[1] : null);
                        Console.WriteLine("Launcher " + AppInfo.Version + " publicado en GitHub");
                        return 0;
                    case "source":
                        Console.WriteLine(Publish.Source() + " archivos subidos");
                        return 0;
                    case "backup":
                        Backup.Create("manual", Backup.FullItems(), false);
                        return 0;
                    case "usuarios":
                        foreach (var u in WebAdmin.List()) Console.WriteLine("{0,-20} {1}", u.Name, u.Role);
                        Console.WriteLine("{0} sesiones abiertas", WebAdmin.SessionCount());
                        return 0;
                    case "usuario":
                        {
                            // usuario <nombre> <admin|jugador>: la contraseña se lee de la entrada (una línea), nunca de los argumentos.
                            if (args.Length < 3) { Console.WriteLine("Uso: MajinheimCli usuario <nombre> <admin|jugador>   (la contraseña por la entrada)"); return 1; }
                            if (LauncherOpen()) return 1;
                            string pw = Console.In.ReadLine();
                            string err = WebAdmin.SetUser(args[1], args[2], string.IsNullOrEmpty(pw) ? null : pw);
                            Console.WriteLine(err ?? "Cuenta guardada: " + args[1].Trim() + " (" + args[2] + ")");
                            return err == null ? 0 : 1;
                        }
                    case "borrar-usuario":
                        if (args.Length < 2) { Console.WriteLine("Uso: MajinheimCli borrar-usuario <nombre>"); return 1; }
                        if (LauncherOpen()) return 1;
                        Console.WriteLine(WebAdmin.RemoveUser(args[1]) ? "Cuenta borrada." : "No existe esa cuenta.");
                        return 0;
                    case "ia":
                        {
                            // ia: estado de Ollama y del modelo.  ia descargar: baja el modelo elegido.
                            if (args.Length > 1 && args[1].ToLowerInvariant() == "descargar")
                            {
                                string last = null;
                                AiChat.Pull(AiChat.Model, t => { if (t != last) { last = t; Console.WriteLine(t); } });
                            }
                            var c = AiChat.Status();
                            Console.WriteLine(c.Text);
                            Console.WriteLine("Asistente IA en Discord: " + (Settings.Current.AiEnabled ? "activado" : "desactivado") + ". Modelo: " + AiChat.Model + ".");
                            if (c.Installed.Count > 0) Console.WriteLine("Modelos descargados: " + string.Join(", ", c.Installed));
                            return c.Up && c.HasModel ? 0 : 1;
                        }
                    case "salud":
                        {
                            var pk = Modpack.Load();
                            var latest = new Dictionary<string, PkgInfo>(StringComparer.OrdinalIgnoreCase);
                            Console.WriteLine("Mirando Thunderstore...");
                            ModWatch.Refresh(pk, latest);
                            var items = Health.Check(null, latest, pk);
                            foreach (var i in items) Console.WriteLine("{0} {1,-30} {2}", Health.Dot(i.State), i.Name, i.Detail);
                            Console.WriteLine(Health.Summary(items));
                            return items.Any(i => i.State == "mal") ? 1 : 0;
                        }
                    case "comprobar-backups":
                        {
                            var r = BackupCheck.Run(!LauncherRunning());
                            Console.WriteLine(r.Text);
                            return r.Ok ? 0 : 1;
                        }
                    case "errores":
                        {
                            // Errores del último arranque (BepInEx\LogOutput.log) que no salían cuando el server funcionaba bien.
                            var known = LogCheck.Known();
                            var lines = new List<string>();
                            if (File.Exists(LogCheck.LogFile))
                                using (var fs = new FileStream(LogCheck.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                                using (var sr = new StreamReader(fs)) lines.AddRange(sr.ReadToEnd().Split('\n'));
                            var seen = new HashSet<string>();
                            int nKnown = 0, nNew = 0;
                            foreach (var l in lines)
                            {
                                string sig = LogCheck.Signature(l);
                                if (sig == null || !seen.Add(sig)) continue;
                                if (known.Contains(sig)) { nKnown++; continue; }
                                nNew++;
                                string t = l.Trim();
                                Console.WriteLine("NUEVO " + (LogCheck.FromLoader(t) ? "(cargador de mods) " : "") + (t.Length > 300 ? t.Substring(0, 300) : t));
                            }
                            Console.WriteLine("{0} errores distintos: {1} nuevos y {2} conocidos ({3} en la lista de conocidos).", nNew + nKnown, nNew, nKnown, known.Count);
                            return 0;
                        }
                    case "actualizar":
                        {
                            // actualizar [reintentar]: el mantenimiento con prueba, con el server apagado y el launcher cerrado.
                            if (LauncherOpen()) return 1;
                            var r = new ServerRunner();
                            if (r.TryAdopt()) { Console.WriteLine("El server está en marcha: desde la consola no sé si hay alguien jugando. Apágalo antes o usa el launcher (pestaña Salud)."); return 1; }
                            var o = new Maintenance.Options { Who = "consola", Manual = true, RetrySkipped = args.Length > 1 && args[1].ToLowerInvariant() == "reintentar" };
                            var rep = Maintenance.Run(o, new Maintenance.Context { Runner = r, Status = Console.WriteLine });
                            Console.WriteLine(rep.Text);
                            foreach (var d in rep.Details) Console.WriteLine("  " + d);
                            Discord.Flush(15000);
                            return rep.Level == "mal" ? 1 : 0;
                        }
                    case "mundo":
                        {
                            // mundo: prepara el mod Majinheim Mundo (con el server apagado) y dice qué sabe la IA del mundo.
                            if (System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0)
                            {
                                Console.WriteLine("Preparando el mod Majinheim Mundo...");
                                WorldMod.EnsureFresh();
                                Console.WriteLine(WorldMod.LastError == null ? (WorldMod.Installed ? "Mod listo en el perfil del server." : "Mod desactivado (WorldExport).") : "No se ha podido preparar: " + WorldMod.LastError);
                                if (WorldMod.LastError != null && File.Exists(WorldMod.ErrorLog)) Console.WriteLine(File.ReadAllText(WorldMod.ErrorLog));
                            }
                            else Console.WriteLine("El server está en marcha: el mod " + (WorldMod.Installed ? "está instalado." : "NO está instalado (se prepara al arrancar el server)."));
                            var ps = System.Diagnostics.Process.GetProcessesByName("valheim_server");
                            var it = WorldData.Check(ps.Length > 0, ps.Length > 0 ? ps[0].StartTime : DateTime.MinValue);
                            Console.WriteLine("{0} {1}", Health.Dot(it.State), it.Detail);
                            return WorldMod.LastError == null ? 0 : 1;
                        }
                    case "buscar":
                    case "receta":
                    case "tumbas":
                        {
                            // Lo mismo que consulta la IA, para probarlo sin Discord: buscar <objeto> | receta <objeto> | tumbas [jugador]
                            string q = string.Join(" ", args.Skip(1)).Trim();
                            if (q == "" && cmd != "tumbas") { Console.WriteLine("Uso: MajinheimCli " + cmd + " <objeto>"); return 1; }
                            object res = cmd == "buscar" ? WorldData.FindItem(q) : cmd == "receta" ? WorldData.Craft(q) : WorldData.Tombs(q);
                            Console.WriteLine(Json.Str(res));
                            return 0;
                        }
                    case "resumen":
                        {
                            // resumen [enviar]: el resumen de la semana (los datos, sin IA y, si está activada, con IA). Con "enviar", al Discord.
                            ServerWatch.LoadHistory();
                            var w = Community.GetWeek(DateTime.Now);
                            Console.WriteLine("--- Datos\n" + Community.Facts(w));
                            Console.WriteLine("--- Sin IA\n" + Community.Plain(w));
                            bool ai;
                            string text = Community.WeeklyMessage(w, out ai);
                            if (ai) Console.WriteLine("\n--- Con IA\n" + text);
                            if (args.Length > 1 && args[1].ToLowerInvariant() == "enviar") { Discord.Send(text); Discord.Flush(15000); Console.WriteLine("\nEnviado a Discord."); }
                            return 0;
                        }
                    case "cuando":
                        {
                            // cuando <texto>: cómo entiende /quedada una fecha ("viernes 22:00").
                            DateTime when; string err;
                            string q = string.Join(" ", args.Skip(1));
                            Console.WriteLine(Community.ParseWhen(q, DateTime.Now, out when, out err) ? when.ToString("dddd d 'de' MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("es-ES")) : "No lo entiendo: " + err);
                            return 0;
                        }
                    default:
                        Console.WriteLine("Uso: MajinheimCli import <carpetaServerOriginal> | install | check | args | test [seg] | run | stop | backup | publish | release [notas] | source | usuarios | usuario <nombre> <admin|jugador> | borrar-usuario <nombre> | ia [descargar] | salud | comprobar-backups | errores | actualizar [reintentar] | mundo | buscar <objeto> | receta <objeto> | tumbas [jugador] | resumen [enviar] | cuando <texto>");
                        return 1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e);
                return 2;
            }
        }

        static bool LauncherRunning() { return System.Diagnostics.Process.GetProcessesByName("MajinheimServer").Length > 0; }

        // El launcher del server tiene los ajustes en memoria y al guardar pisaría lo que se cambie desde aquí.
        static bool LauncherOpen()
        {
            if (!LauncherRunning()) return false;
            Console.WriteLine("Cierra antes el launcher del server (MajinheimServer.exe): si no, al guardar sus ajustes pisaría este cambio.");
            return true;
        }

        // test: arranca, espera a que el mundo cargue (o falle) y cierra limpio. run: hasta que se cierre.
        static int Run(bool test, int timeout)
        {
            var r = new ServerRunner();
            bool loaded = false;
            int errors = 0;
            r.GameLog += l =>
            {
                if (l.StartsWith("[Error") || l.StartsWith("[Fatal") || l.Contains("Exception")) errors++;
                if (l.Contains("Game server connected") || l.Contains("Load world:") || l.Contains("Loading") || l.Contains("[Error") || l.Contains("[Warning") || l.Contains("[Fatal") || l.Contains("Exception") || l.Contains("Valheim version") || l.Contains("Connections"))
                    Console.WriteLine("  | " + (l.Length > 300 ? l.Substring(0, 300) : l));
                if (l.Contains("Game server connected")) loaded = true;
            };
            r.Start();
            var t0 = DateTime.Now;
            while (r.Running && (DateTime.Now - t0).TotalSeconds < timeout)
            {
                Thread.Sleep(1000);
                if (test && loaded) { Thread.Sleep(20000); break; }
            }
            Console.WriteLine("Mundo cargado: {0}  errores en log: {1}", loaded, errors);
            if (r.Running) r.Stop(true);
            Thread.Sleep(2000);
            return loaded ? 0 : 3;
        }
    }
}
