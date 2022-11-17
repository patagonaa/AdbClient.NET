using System;

namespace AdbClient
{
    public class StatEntry
    {
        public string Path { get; }
        public UnixFileMode Mode { get; }
        public int Size { get; }
        public DateTime ModifiedTime { get; }

        public StatEntry(string path, UnixFileMode mode, int size, DateTime modifiedTime)
        {
            Path = path;
            Mode = mode;
            Size = size;
            ModifiedTime = modifiedTime;
        }

        public override string ToString()
        {
            return Path;
        }
    }
}
