using System;

namespace AdbClient
{
    public class StatV2Entry
    {
        public string Path { get; }
        public UnixFileMode Mode { get; }
        public uint UserId { get; }
        public uint GroupId { get; }
        public ulong Size { get; }
        public DateTime AccessTime { get; }
        public DateTime ModifiedTime { get; }
        public DateTime CreatedTime { get; }

        public StatV2Entry(string path, UnixFileMode mode, uint uid, uint gid, ulong size, DateTime accessTime, DateTime modifiedTime, DateTime createdTime)
        {
            Path = path;
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
            return Path;
        }
    }
}
