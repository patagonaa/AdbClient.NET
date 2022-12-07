using System;

namespace AdbClient
{
    [Flags]
    // https://man7.org/linux/man-pages/man7/inode.7.html st_mode
    public enum UnixFileMode : uint
    {
        //FileTypeMask = 0xF000,
        Socket = 0xC000,
        SymLink = 0xA000,
        RegularFile = 0x8000,
        BlockDevice = 0x6000,
        Directory = 0x4000,
        CharacterDevice = 0x2000,
        Fifo = 0x1000,

        SetUid = 0x0800,
        SetGid = 0x0400,
        Sticky = 0x0200,

        //OwnerPermissionsMask = 0x01C0,
        OwnerRead = 0x0100,
        OwnerWrite = 0x0080,
        OwnerExecute = 0x0040,

        //GroupPermissionsMask = 0x0038,
        GroupRead = 0x0020,
        GroupWrite = 0x0010,
        GroupExecute = 0x0008,

        //OthersPermissionMask = 0x0007,
        OthersRead = 0x0004,
        OthersWrite = 0x0002,
        OthersExecute = 0x0001
    }
}
