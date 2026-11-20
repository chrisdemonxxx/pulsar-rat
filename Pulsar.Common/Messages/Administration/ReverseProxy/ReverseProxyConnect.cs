using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Administration.ReverseProxy
{
    [MessagePackObject]
    public class ReverseProxyConnect : IMessage
    {
        [Key(1)]
        public int ConnectionId { get; set; }

        [Key(2)]
        public string Target { get; set; }

        [Key(3)]
        public int Port { get; set; }

        [Key(4)]
        public string ListenerSessionId { get; set; }

        [Key(5)]
        public string TunnelId { get; set; }

        [Key(6)]
        public long Sequence { get; set; }

        [Key(7)]
        public string OriginClientId { get; set; }
    }
}
