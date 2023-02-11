using System;

namespace AdbClient
{
    public class StatV2Entry
    {
        public string FullPath { get; }
        public UnixFileMode Mode { get; }
        public uint UserId { get; }
        public uint GroupId { get; }
        public ulong Size { get; }
        public DateTime AccessTime { get; }
        public DateTime ModifiedTime { get; }
        public DateTime CreatedTime { get; }

        public StatV2Entry(string fullPath, UnixFileMode mode, uint uid, uint gid, ulong size, DateTime accessTime, DateTime modifiedTime, DateTime createdTime)
        {
            FullPath = fullPath;
            Mode = mode;
            UserId = uid;
            GroupId = gid;
            Size = size;
            AccessTime = accessTime;
            ModifiedTime = modifiedTime;
            CreatedTime = createdTime;
        }

        public override string ToString()
        {
            return FullPath;
        }
    }
}
