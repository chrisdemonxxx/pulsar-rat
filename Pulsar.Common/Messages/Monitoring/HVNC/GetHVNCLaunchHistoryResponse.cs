using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class GetHVNCLaunchHistoryResponse : IMessage
    {
        [Key(1)]
        public string SessionId { get; set; } = string.Empty;

        [Key(2)]
        public HVNCLaunchRecord[] Records { get; set; } = new HVNCLaunchRecord[0];
    }
}
