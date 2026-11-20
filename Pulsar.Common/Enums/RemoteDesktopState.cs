namespace Pulsar.Common.Enums
{
    public enum RemoteDesktopStatus
    {
        Start,
        Stop,
        Continue,
    }

    public enum RemoteWebcamStatus
    {
        Start,
        Stop,
        Continue,
    }

    public enum MediaStreamStatus
    {
        Starting = 0,
        Active = 1,
        Stopped = 2,
        Denied = 3,
        Unavailable = 4,
        InvalidDevice = 5,
        DeviceBusy = 6,
        FormatUnsupported = 7,
        Stalled = 8,
        Error = 9
    }
}
