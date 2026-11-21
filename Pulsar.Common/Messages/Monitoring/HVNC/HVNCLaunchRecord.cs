using MessagePack;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class HVNCLaunchRecord
    {
        [Key(1)]
        public string SessionId { get; set; } = string.Empty;

        [Key(2)]
        public string LaunchId { get; set; } = string.Empty;

        [Key(3)]
        public string Category { get; set; } = string.Empty;

        [Key(4)]
        public string ExecutableIdentity { get; set; } = string.Empty;

        [Key(5)]
        public string ExecutableHash { get; set; } = string.Empty;

        [Key(6)]
        public int ProcessId { get; set; }

        [Key(7)]
        public long StartedUtcTicks { get; set; }

        [Key(8)]
        public long CompletedUtcTicks { get; set; }

        [Key(9)]
        public HVNCProcessStage Stage { get; set; }

        [Key(10)]
        public string ErrorCode { get; set; } = string.Empty;

        [Key(11)]
        public bool Retryable { get; set; }
    }
}
