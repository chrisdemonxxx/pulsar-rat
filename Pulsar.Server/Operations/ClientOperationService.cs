using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.Actions;
using Pulsar.Common.Messages.ClientManagement;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages.QuickCommands;
using Pulsar.Common.Messages.UserSupport.MessageBox;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using Pulsar.Server.Persistence;
using System;
using System.Linq;

namespace Pulsar.Server.Operations
{
    public enum ClientOperation
    {
        None,
        Shutdown,
        Restart,
        Standby,
        Lockscreen,
        Reconnect,
        Disconnect,
        Uninstall,
        QuickCommand,
        MessageBox
    }

    public enum ClientOperationStatus
    {
        Accepted,
        InvalidRequest,
        NotFound,
        Offline,
        Rejected,
        Unavailable,
        Failed
    }

    public sealed class ClientOperationRequest
    {
        public string ClientId { get; set; }
        public ClientOperation Operation { get; set; }
        public string Command { get; set; }
        public string Host { get; set; }
        public string Caption { get; set; }
        public string Text { get; set; }
        public string Button { get; set; }
        public string Icon { get; set; }
    }

    public sealed class ClientOperationResult
    {
        private ClientOperationResult(string clientId, ClientOperation operation, ClientOperationStatus status, string error)
        {
            ClientId = clientId;
            Operation = operation;
            Status = status;
            Error = error;
        }

        public string ClientId { get; }
        public ClientOperation Operation { get; }
        public ClientOperationStatus Status { get; }
        public string Error { get; }
        public bool Accepted => Status == ClientOperationStatus.Accepted;

        public static ClientOperationResult Create(string clientId, ClientOperation operation,
            ClientOperationStatus status, string error = null)
        {
            return new ClientOperationResult(clientId, operation, status, error);
        }
    }

    public sealed class ClientSurfaceRecord
    {
        public string Id { get; set; }
        public string UserAtPc { get; set; }
        public string Username { get; set; }
        public string PcName { get; set; }
        public string PublicIp { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string CountryWithCode { get; set; }
        public string Tag { get; set; }
        public string OperatingSystem { get; set; }
        public string Version { get; set; }
        public string AccountType { get; set; }
        public string EndPoint { get; set; }
        public bool Connected { get; set; }
        public bool Online { get; set; }
        public DateTime? FirstSeenUtc { get; set; }
        public DateTime? LastSeenUtc { get; set; }
    }

    /// <summary>
    /// One validated and audited entry point for the operations exposed by the server UI and Web API.
    /// It intentionally maps only to the existing client command surface.
    /// </summary>
    public sealed class ClientOperationService
    {
        public const int MaxCommandLength = 256 * 1024;
        public const int MaxHostLength = 2048;
        public const int MaxMessageTextLength = 256 * 1024;
        public const int MaxMessageCaptionLength = 4096;

        private readonly Func<Client[]> _getClients;
        private readonly Action<string, string> _audit;

        public ClientOperationService(Func<Client[]> getClients, Action<string, string> audit = null)
        {
            _getClients = getClients ?? throw new ArgumentNullException(nameof(getClients));
            _audit = audit;
        }

        public Client[] GetClients()
        {
            return (_getClients() ?? Array.Empty<Client>())
                .Where(IsManagedClient)
                .ToArray();
        }

        public ClientSurfaceRecord[] GetClientList()
        {
            return GetClients().Select(Project).ToArray();
        }

        public ClientSurfaceRecord GetClientDetail(string id)
        {
            if (!MessageInputLimits.IsValidClientId(id))
            {
                return null;
            }

            var live = FindClient(id);
            return live != null ? Project(live) : Project(OfflineClientRepository.GetClient(id));
        }

        public ClientOperationResult Execute(ClientOperationRequest request, string source)
        {
            var operation = request?.Operation ?? ClientOperation.None;
            string clientId = request?.ClientId;

            string validationError = ValidateRequest(request);
            if (validationError != null)
                return AuditResult(clientId, operation, ClientOperationStatus.InvalidRequest, validationError, source);

            Client client = FindClient(clientId);
            if (client == null)
            {
                try
                {
                    var offline = OfflineClientRepository.GetClient(clientId);
                    return offline != null
                        ? AuditResult(clientId, operation, ClientOperationStatus.Offline, "client is offline", source)
                        : AuditResult(clientId, operation, ClientOperationStatus.NotFound, "client not found", source);
                }
                catch
                {
                    return AuditResult(clientId, operation, ClientOperationStatus.Unavailable,
                        "client state is unavailable", source);
                }
            }

            try
            {
                IMessage message = CreateMessage(request);
                if (message == null)
                    return AuditResult(clientId, operation, ClientOperationStatus.InvalidRequest,
                        "operation is not supported", source);

                if (!client.TrySend(message))
                    return AuditResult(clientId, operation, ClientOperationStatus.Rejected,
                        "client cannot accept the operation", source);

                return AuditResult(clientId, operation, ClientOperationStatus.Accepted, null, source);
            }
            catch (Exception ex)
            {
                TryAudit($"[Client operation] {SanitizeSource(source)} {FormatOperation(operation)} failed for {ShortId(clientId)}: {ex.GetType().Name}.", "error");
                return ClientOperationResult.Create(clientId, operation, ClientOperationStatus.Failed,
                    "operation failed");
            }
        }

        public static ClientSurfaceRecord Project(Client client)
        {
            if (client?.Value == null)
                return null;

            return new ClientSurfaceRecord
            {
                Id = client.Value.Id,
                UserAtPc = client.Value.UserAtPc,
                Username = client.Value.Username,
                PcName = client.Value.PcName,
                PublicIp = client.Value.PublicIP,
                Country = client.Value.Country,
                CountryCode = client.Value.CountryCode,
                CountryWithCode = client.Value.CountryWithCode,
                Tag = client.Value.Tag,
                OperatingSystem = client.Value.OperatingSystem,
                Version = client.Value.Version,
                AccountType = client.Value.AccountType,
                EndPoint = client.EndPoint?.ToString(),
                Connected = client.Connected,
                Online = client.Connected
            };
        }

        public static ClientSurfaceRecord Project(OfflineClientRecord record)
        {
            if (record == null)
                return null;

            return new ClientSurfaceRecord
            {
                Id = record.ClientId,
                UserAtPc = !string.IsNullOrEmpty(record.UserAtPc)
                    ? record.UserAtPc
                    : ComposeUserAtPc(record),
                Username = record.Username,
                PcName = record.PcName,
                PublicIp = record.PublicIP,
                Country = record.Country,
                CountryCode = record.CountryCode,
                CountryWithCode = ComposeCountry(record),
                Tag = record.Tag,
                OperatingSystem = record.OperatingSystem,
                Version = record.Version,
                AccountType = record.AccountType,
                EndPoint = null,
                Connected = false,
                Online = record.IsOnline,
                FirstSeenUtc = record.FirstSeenUtc,
                LastSeenUtc = record.LastSeenUtc
            };
        }

        private Client FindClient(string id)
        {
            return GetClients().FirstOrDefault(client =>
                string.Equals(client.Value?.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsManagedClient(Client client)
        {
            return client != null && client.Connected && client.Identified &&
                   client.Value != null && MessageInputLimits.IsValidClientId(client.Value.Id);
        }

        private static string ValidateRequest(ClientOperationRequest request)
        {
            if (request == null || !MessageInputLimits.IsValidClientId(request.ClientId))
                return "invalid client id";

            switch (request.Operation)
            {
                case ClientOperation.Shutdown:
                case ClientOperation.Restart:
                case ClientOperation.Standby:
                case ClientOperation.Lockscreen:
                case ClientOperation.Reconnect:
                case ClientOperation.Disconnect:
                case ClientOperation.Uninstall:
                    return null;
                case ClientOperation.QuickCommand:
                    if (string.IsNullOrWhiteSpace(request.Command))
                        return "command is required";
                    if (request.Command.Length > MaxCommandLength || (request.Host?.Length ?? 0) > MaxHostLength)
                        return "invalid request";
                    return null;
                case ClientOperation.MessageBox:
                    if (string.IsNullOrWhiteSpace(request.Text))
                        return "text is required";
                    if (request.Text.Length > MaxMessageTextLength ||
                        (request.Caption?.Length ?? 0) > MaxMessageCaptionLength ||
                        (!string.IsNullOrEmpty(request.Button) &&
                         !IsAllowedChoice(request.Button, "OK", "OKCancel", "YesNo", "YesNoCancel")) ||
                        (!string.IsNullOrEmpty(request.Icon) &&
                         !IsAllowedChoice(request.Icon, "None", "Information", "Warning", "Error", "Question")))
                    {
                        return "invalid request";
                    }
                    return null;
                default:
                    return "operation is not supported";
            }
        }

        private static IMessage CreateMessage(ClientOperationRequest request)
        {
            return request.Operation switch
            {
                ClientOperation.Shutdown => new DoShutdownAction { Action = ShutdownAction.Shutdown },
                ClientOperation.Restart => new DoShutdownAction { Action = ShutdownAction.Restart },
                ClientOperation.Standby => new DoShutdownAction { Action = ShutdownAction.Standby },
                ClientOperation.Lockscreen => new DoShutdownAction { Action = ShutdownAction.Lockscreen },
                ClientOperation.Reconnect => new DoClientReconnect(),
                ClientOperation.Disconnect => new DoClientDisconnect(),
                ClientOperation.Uninstall => new DoClientUninstall(),
                ClientOperation.QuickCommand => new DoSendQuickCommand
                {
                    Command = request.Command,
                    Host = request.Host
                },
                ClientOperation.MessageBox => new DoShowMessageBox
                {
                    Caption = request.Caption,
                    Text = request.Text,
                    Button = string.IsNullOrEmpty(request.Button) ? "OK" : request.Button,
                    Icon = string.IsNullOrEmpty(request.Icon) ? "Information" : request.Icon
                },
                _ => null
            };
        }

        private ClientOperationResult AuditResult(string clientId, ClientOperation operation,
            ClientOperationStatus status, string error, string source)
        {
            string outcome = status == ClientOperationStatus.Accepted ? "accepted" : "rejected";
            string level = status == ClientOperationStatus.Accepted ? "normal" : "warning";
            string detail = error == null ? string.Empty : $": {error}";
            TryAudit(
                $"[Client operation] {SanitizeSource(source)} {FormatOperation(operation)} for {ShortId(clientId)} was {outcome}{detail}.",
                level);
            return ClientOperationResult.Create(clientId, operation, status, error);
        }

        private void TryAudit(string message, string level)
        {
            try
            {
                _audit?.Invoke(message, level);
            }
            catch
            {
                // Audit sinks must not change whether an existing client operation is accepted.
            }
        }

        private static string SanitizeSource(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? "unknown surface" : source;
        }

        private static bool IsAllowedChoice(string value, params string[] allowed)
        {
            return allowed.Any(item => string.Equals(value, item, StringComparison.Ordinal));
        }

        private static string FormatOperation(ClientOperation operation)
        {
            return operation.ToString().ToLowerInvariant();
        }

        private static string ShortId(string id)
        {
            return string.IsNullOrEmpty(id) ? "unknown client" : id.Substring(0, Math.Min(8, id.Length));
        }

        private static string ComposeUserAtPc(OfflineClientRecord record)
        {
            string username = record.Username ?? string.Empty;
            string pcName = record.PcName ?? string.Empty;
            if (string.IsNullOrEmpty(username))
                return pcName;
            return string.IsNullOrEmpty(pcName) ? username : $"{username}@{pcName}";
        }

        private static string ComposeCountry(OfflineClientRecord record)
        {
            if (string.IsNullOrEmpty(record.CountryCode))
                return record.Country ?? string.Empty;
            return string.IsNullOrEmpty(record.Country)
                ? $"[{record.CountryCode}]"
                : $"{record.Country} [{record.CountryCode}]";
        }
    }
}
