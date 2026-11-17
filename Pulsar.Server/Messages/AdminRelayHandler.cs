using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.Actions;
using Pulsar.Common.Messages.AdminRelay;
using Pulsar.Common.Messages.ClientManagement;
using Pulsar.Common.Messages.QuickCommands;
using Pulsar.Common.Messages.UserSupport.MessageBox;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Models;
using Pulsar.Server.Networking;
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
        private readonly ConcurrentDictionary<Client, byte> _admins = new ConcurrentDictionary<Client, byte>();
        private int _serverEventsSubscribed;

        public AdminRelayHandler(Func<PulsarServer> getServer, Action<string, string> log) : base(true)
        {
            _getServer = getServer ?? throw new ArgumentNullException(nameof(getServer));
            _log = log;
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
                _log?.Invoke($"[Admin] '{client.Value?.UserAtPc}' requested admin access but no admin key is configured.", "error");
                return;
            }

            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
            byte[] provided = ParseHex(message.AdminKeyHash);

            if (provided == null || !CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                client.Send(new AdminIdentificationResult
                {
                    Accepted = false,
                    DeniedReason = "invalid admin key"
                });
                _log?.Invoke($"[Admin] '{client.Value?.UserAtPc}' failed admin authentication.", "error");
                return;
            }

            _admins[client] = 1;
            EnsureServerEventsSubscribed();

            // mark the connection in the server UI so it is visually distinct from implants
            client.Value.Tag = "ADMIN";

            client.Send(new AdminIdentificationResult
            {
                Accepted = true,
                Clients = GetManagedClients()
            });

            _log?.Invoke($"[Admin] '{client.Value?.UserAtPc}' authenticated as admin.", "normal");
        }

        private void Execute(Client client, GetAdminClientList message)
        {
            if (!IsAdmin(client))
                return; // not authenticated for admin features — ignore silently

            client.Send(new GetAdminClientListResponse
            {
                Clients = GetManagedClients()
            });
        }

        private void Execute(Client client, DoAdminRelay message)
        {
            if (!IsAdmin(client))
                return;

            if (string.IsNullOrWhiteSpace(message?.TargetId) || message.Command == AdminRelayCommand.None)
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

            bool forwarded;
            try
            {
                forwarded = TryForwardCommand(target, message.Command, message.Param1, message.Param2);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Admin] relay to '{target.Value?.UserAtPc}' failed: {ex.Message}", "error");
                forwarded = false;
            }

            client.Send(new AdminRelayResult
            {
                TargetId = message.TargetId,
                Accepted = forwarded,
                Reason = forwarded ? null : "unsupported command"
            });

            _log?.Invoke($"[Admin] '{client.Value?.UserAtPc}' relayed {message.Command} to '{target.Value?.UserAtPc}'.", forwarded ? "normal" : "error");
        }

        /// <summary>
        /// Maps the whitelisted admin command to the regular message type and sends it to the target.
        /// </summary>
        private static bool TryForwardCommand(Client target, AdminRelayCommand command, string param1, string param2)
        {
            switch (command)
            {
                case AdminRelayCommand.Shutdown:
                    target.Send(new DoShutdownAction { Action = ShutdownAction.Shutdown });
                    return true;
                case AdminRelayCommand.Restart:
                    target.Send(new DoShutdownAction { Action = ShutdownAction.Restart });
                    return true;
                case AdminRelayCommand.Standby:
                    target.Send(new DoShutdownAction { Action = ShutdownAction.Standby });
                    return true;
                case AdminRelayCommand.Lockscreen:
                    target.Send(new DoShutdownAction { Action = ShutdownAction.Lockscreen });
                    return true;
                case AdminRelayCommand.Reconnect:
                    target.Send(new DoClientReconnect());
                    return true;
                case AdminRelayCommand.Disconnect:
                    target.Send(new DoClientDisconnect());
                    return true;
                case AdminRelayCommand.Uninstall:
                    target.Send(new DoClientUninstall());
                    return true;
                case AdminRelayCommand.MessageBox:
                    target.Send(new DoShowMessageBox
                    {
                        Caption = string.IsNullOrEmpty(param1) ? "Pulsar" : param1,
                        Text = param2,
                        Button = "OK",
                        Icon = "Information"
                    });
                    return !string.IsNullOrEmpty(param2);
                case AdminRelayCommand.QuickCommand:
                    target.Send(new DoSendQuickCommand { Command = param1, Host = param2 });
                    return !string.IsNullOrEmpty(param1);
                default:
                    return false;
            }
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
        }
    }
}
