using Pulsar.Server.DiscordRPC;
using Pulsar.Server.Forms;
using Pulsar.Server.Networking;
using Pulsar.Server.Views;
using System;
using System.Windows;
using System.Windows.Forms;

namespace Pulsar.Server
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var options = StartupOptions.Parse(args);
            if (options.ShowStartupHelp)
            {
                Console.WriteLine(StartupOptions.GetHelpText());
                return;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);

            if (options.UseWpfShell)
            {
                RunWpfShell(options);
                return;
            }

            RunWinFormsShell();
        }

        private static void RunWpfShell(StartupOptions options)
        {
            var app = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            ServerRuntimeHost runtime = null;
            PulsarMainWindow mainWindow = null;
            try
            {
                runtime = new ServerRuntimeHost(options, (message, level) =>
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}"));
                runtime.Initialize();

                mainWindow = new PulsarMainWindow();
                mainWindow.SetServer(runtime.Server);
                runtime.StartupError += (_, message) =>
                {
                    if (mainWindow.Dispatcher.CheckAccess())
                    {
                        mainWindow.ViewModel.ReportRuntimeStatus(message, false);
                    }
                    else
                    {
                        mainWindow.Dispatcher.Invoke(() => mainWindow.ViewModel.ReportRuntimeStatus(message, false));
                    }
                };

                runtime.StartConfiguredServices();
                mainWindow.ViewModel.RefreshClientLists();

                app.MainWindow = mainWindow;
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WPF startup failed: {ex}");
                System.Windows.MessageBox.Show(
                    ex.Message + "\n\nFallback option: restart without --wpf to use the established WinForms shell.",
                    "Pulsar WPF Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                runtime?.Dispose();
                mainWindow?.ViewModel?.Dispose();
                app.Shutdown();
            }
        }

        private static void RunWinFormsShell()
        {
            using (FrmMain mainForm = new FrmMain())
            {
                DiscordRPCManager.Initialize(mainForm);

                var customMainForm = Plugins.UIExtensionManager.GetCustomMainForm();
                if (customMainForm != null)
                {
                    mainForm.Hide();
                    customMainForm.Show();

                    customMainForm.FormClosed += (s, e) => mainForm.Close();
                    customMainForm.Disposed += (s, e) =>
                    {
                        if (!mainForm.IsDisposed)
                        {
                            mainForm.Close();
                        }
                    };

                    System.Windows.Forms.Application.Run(customMainForm);
                }
                else
                {
                    System.Windows.Forms.Application.Run(mainForm);
                }

                DiscordRPCManager.Shutdown();
            }
        }
    }
}
