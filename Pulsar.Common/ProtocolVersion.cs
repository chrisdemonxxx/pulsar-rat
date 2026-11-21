namespace Pulsar.Common
{
    /// <summary>
    /// Identifies the message contract supported by this source tree.
    /// This is sent as the existing human-readable client version string; it
    /// does not add or change a wire field.
    /// </summary>
    public static class ProtocolVersion
    {
        public const string Current = "2.4.5";
    }
}
