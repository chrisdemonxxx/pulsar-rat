using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class GetHVNCLaunchHistory : IMessage
    {
        [Key(1)]
        public string SessionId { get; set; } = string.Empty;
    }
}
