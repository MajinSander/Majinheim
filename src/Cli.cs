// MajinheimCli.exe: misma logica que la edicion Server, por linea de comandos (diagnostico y pruebas).
using System;
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
                    default:
                        Console.WriteLine("Uso: MajinheimCli import <carpetaServerOriginal> | install | check | args | test [seg] | run | stop | backup | publish | release [notas] | source");
                        return 1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e);
                return 2;
            }
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
