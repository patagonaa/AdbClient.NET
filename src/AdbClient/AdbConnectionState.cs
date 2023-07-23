namespace AdbClient
{
    /// <summary>
    /// The ADB device <see href="https://android.googlesource.com/platform/packages/modules/adb/+/c36807b1a84e6ee64e3f2380bbcbbead6d9ae7e4/adb.h#104">connection state</see>
    /// </summary>
    public enum AdbConnectionState
    {
        Connecting,
        Authorizing,
        Unauthorized,
        NoPerm,
        Detached,
        Offline,
        Bootloader,
        Device,
        Host,
        Recovery,
        Sideload,
        Rescue,
        Unknown
    }
}
