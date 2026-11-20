using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Administration.ReverseProxy
{
    [MessagePackObject]
    public class ReverseProxyData : IMessage
    {
        [Key(1)]
        public int ConnectionId { get; set; }

        [Key(2)]
        public byte[] Data { get; set; }

        [Key(3)]
        public string ListenerSessionId { get; set; }

        [Key(4)]
        public string TunnelId { get; set; }

        [Key(5)]
        public long Sequence { get; set; }

        [Key(6)]
        public string OriginClientId { get; set; }
    }
}
