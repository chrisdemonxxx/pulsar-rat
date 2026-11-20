using MessagePack;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Enums;

namespace Pulsar.Common.Messages.Audio
{
    [MessagePackObject]
    public class GetMicrophoneResponse : IMessage
    {
        [Key(1)]
        public byte[] Audio { get; set; }

        [Key(2)]
        public int Device { get; set; }

        [Key(3)]
        public string SessionId { get; set; }

        [Key(4)]
        public long Sequence { get; set; }

        [Key(5)]
        public long Timestamp { get; set; }

        [Key(6)]
        public MediaStreamStatus Status { get; set; } = MediaStreamStatus.Active;

        [Key(7)]
        public string ErrorCode { get; set; }

        [Key(8)]
        public string ErrorMessage { get; set; }

        [Key(9)]
        public int Channels { get; set; } = 1;
    }
}
