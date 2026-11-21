using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.VirtualMonitor
{
    [MessagePackObject]
    public class DoUninstallVirtualMonitorResponse : IMessage
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string Error { get; set; }
    }
}
