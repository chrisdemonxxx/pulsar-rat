using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class HVNCFrameTelemetry : IMessage
    {
        [Key(1)]
        public string SessionId { get; set; } = string.Empty;

        [Key(2)]
        public long FrameSequence { get; set; }

        [Key(3)]
        public int CurrentBacklog { get; set; }

        [Key(4)]
        public int MaximumBacklog { get; set; }

        [Key(5)]
        public int DroppedFrames { get; set; }

        [Key(6)]
        public int StaleFrames { get; set; }

        [Key(7)]
        public double CaptureFps { get; set; }

        [Key(8)]
        public double RenderFps { get; set; }

        [Key(9)]
        public double EndToEndMilliseconds { get; set; }

        [Key(10)]
        public double MaximumFrameAgeMilliseconds { get; set; }
    }
}
