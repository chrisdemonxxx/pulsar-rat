using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Browser
{
    [MessagePackObject]
    public class ExtractAllBrowsersResponse : IMessage
    {
        [Key(1)]
        public string TransferId { get; set; }

        [Key(2)]
        public string ZipFileName { get; set; }

        [Key(3)]
        public long FileSize { get; set; }

        [Key(4)]
        public int BrowsersExtracted { get; set; }

        [Key(5)]
        public string Error { get; set; }
    }
}
