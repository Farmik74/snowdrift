using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Snowdrift
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Snowdrift.SingleInstance.v1", out created))
            {
                if (!created) return; // уже запущен
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log(e.ExceptionObject as Exception); };
                Application.Run(new App());
                GC.KeepAlive(mutex);
            }
        }

        static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "error.log"),
                    DateTime.Now + "  " + (ex == null ? "unknown" : ex.ToString()) + "\r\n\r\n");
            }
            catch (Exception) { }
        }
    }
}
