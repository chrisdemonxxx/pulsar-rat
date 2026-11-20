using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Administration.ReverseProxy
{
    [MessagePackObject]
    public class ReverseProxyDisconnect : IMessage
    {
        [Key(1)]
        public int ConnectionId { get; set; }

        [Key(2)]
        public string ListenerSessionId { get; set; }

        [Key(3)]
        public string TunnelId { get; set; }

        [Key(4)]
        public long Sequence { get; set; }

        [Key(5)]
        public string OriginClientId { get; set; }

        [Key(6)]
        public Pulsar.Common.Enums.ReverseProxyStatus Status { get; set; } = Pulsar.Common.Enums.ReverseProxyStatus.Stopped;

        [Key(7)]
        public string Reason { get; set; }
    }
}
