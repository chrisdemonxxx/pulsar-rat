using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.SystemInfo;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using System;
using System.Collections.Generic;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote system information.
    /// </summary>
    public class SystemInformationHandler : MessageProcessorBase<List<Tuple<string, string>>>
    {
        /// <summary>
        /// The client which is associated with this system information handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemInformationHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public SystemInformationHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetSystemInfoResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender client) => _client.Equals(client);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetSystemInfoResponse info:
                    Execute(sender, info);
                    break;
            }
        }

        /// <summary>
        /// Refreshes the system information of the client.
        /// </summary>
        public void RefreshSystemInformation()
        {
            _client.Send(new GetSystemInfo());
        }

        private void Execute(ISender client, GetSystemInfoResponse message)
        {
            OnReport(message.SystemInfos);
        }
    }
}
