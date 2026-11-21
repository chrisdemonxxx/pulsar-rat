using MessagePack;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Messages.Monitoring.HVNC
{
    [MessagePackObject]
    public class GetHVNCBrowserCatalogResponse : IMessage
    {
        [Key(1)]
        public string SessionId { get; set; } = string.Empty;

        [Key(2)]
        public HVNCBrowserDefinition[] Browsers { get; set; } = new HVNCBrowserDefinition[0];
    }
}
