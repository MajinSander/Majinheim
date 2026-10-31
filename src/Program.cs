using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Majinheim Launcher")]
[assembly: AssemblyProduct("Majinheim")]
[assembly: AssemblyCompany("Majin")]
[assembly: AssemblyCopyright("Majin")]
[assembly: AssemblyVersion(Majinheim.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(Majinheim.AppInfo.Version + ".0")]

namespace Majinheim
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mx = new Mutex(true, "MajinheimLauncher_" + AppInfo.EditionName + "_" + AppInfo.Root.ToLowerInvariant().GetHashCode(), out created))
            {
                if (!created) { MessageBox.Show("El launcher ya está abierto (mira junto al reloj de Windows).", "Majinheim"); return; }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => { Log.W("ERROR: {0}", e.Exception); MessageBox.Show(e.Exception.Message, "Error"); };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.W("ERROR FATAL: {0}", e.ExceptionObject);
                Settings.Load();
                var form = new MainForm(args.Contains("--autostart"));
                int pi = Array.IndexOf(args, "--page");
                if (pi >= 0 && pi + 1 < args.Length) form.StartPage = args[pi + 1];
                Application.Run(form);
            }
        }
    }
}
