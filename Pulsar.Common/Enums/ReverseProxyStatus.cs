namespace Pulsar.Common.Enums
{
    public enum ReverseProxyStatus
    {
        Idle = 0,
        Starting = 1,
        Listening = 2,
        PendingConnect = 3,
        Connected = 4,
        HalfClosed = 5,
        Stopped = 6,
        Failed = 7,
        Stale = 8
    }
}
