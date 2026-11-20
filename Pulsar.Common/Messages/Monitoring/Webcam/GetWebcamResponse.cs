using MessagePack;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Video;
using Pulsar.Common.Enums;

namespace Pulsar.Common.Messages.Webcam
{
    [MessagePackObject]
    public class GetWebcamResponse : IMessage
    {
        [Key(1)]
        public byte[] Image { get; set; }

        [Key(2)]
        public int Quality { get; set; }

        [Key(3)]
        public int Monitor { get; set; }

        [Key(4)]
        public Resolution Resolution { get; set; }

        [Key(5)]
        public long Timestamp { get; set; }

        [Key(6)]
        public bool IsLastRequestedFrame { get; set; }

        [Key(7)]
        public float FrameRate { get; set; }

        [Key(8)]
        public string SessionId { get; set; }

        [Key(9)]
        public long Sequence { get; set; }

        [Key(10)]
        public MediaStreamStatus Status { get; set; } = MediaStreamStatus.Active;

        [Key(11)]
        public string ErrorCode { get; set; }

        [Key(12)]
        public string ErrorMessage { get; set; }
    }
}
