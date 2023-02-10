using System;

namespace AdbClient
{
    public class StatEntry
    {
        public string Path { get; }
        public UnixFileMode Mode { get; }
        /// <summary>
        /// The size of the file. Be careful with this, sizes larger than 4 GiB just wrap around. Use StatV2 for those instead.
        /// </summary>
        public uint Size { get; }
        public DateTime ModifiedTime { get; }

        public StatEntry(string path, UnixFileMode mode, uint size, DateTime modifiedTime)
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
