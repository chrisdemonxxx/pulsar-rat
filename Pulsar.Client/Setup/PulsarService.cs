using Pulsar.Client.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace Pulsar.Client.Setup
{
    /// <summary>
    /// Windows service host for the client. Started by the SCM via the '-service' command line
    /// argument (see <see cref="Program.Main"/>). Runs the regular WinForms message loop on a
    /// dedicated STA thread inside session 0 so the client stays connected even when no user
    /// is logged in.
    /// </summary>
    public class PulsarService : ServiceBase
    {
        private Thread _appThread;

        public PulsarService()
        {
            ServiceName = ServiceInstallerHelper.GetServiceNameForCurrentProcess();
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            // SCM gives ~30 seconds; only spawn the worker thread and return immediately.
            _appThread = new Thread(RunApplicationLoop)
            {
                IsBackground = false,
                Priority = ThreadPriority.Normal
            };
            _appThread.SetApartmentState(ApartmentState.STA);
            _appThread.Start();
        }

        protected override void OnStop()
        {
            Environment.Exit(0);
        }

        protected override void OnShutdown()
        {
            Environment.Exit(0);
        }

        /// <summary>
        /// Runs the client entrypoint on the service thread. Mirrors the bootstrap performed by
        /// <see cref="Program.Main"/> for the interactive path.
        /// </summary>
        private void RunApplicationLoop()
        {
            try
            {
                // enable TLS 1.2
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                Program.EnableDPIAwareness();

                // Set the unhandled exception mode to force all Windows Forms errors to go through our handler
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!Settings.Initialize())
                {
                    Environment.Exit(1);
                    return;
                }

                Program.SaveOriginalDesktop();

                Application.Run(new PulsarApplication());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Service application loop terminated: {ex}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Runs the service. Called from <see cref="Program.Main"/> when the '-service' argument is present.
        /// </summary>
        public static void RunService()
        {
            ServiceBase.Run(new PulsarService());
        }
    }
}
