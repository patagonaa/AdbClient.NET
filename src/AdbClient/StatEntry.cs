using System;

namespace AdbClient
{
    public class StatEntry
    {
        public string FullPath { get; }
        public UnixFileMode Mode { get; }
        /// <summary>
        /// The size of the file. Be careful with this, sizes larger than 4 GiB just wrap around. Use StatV2 for those instead.
        /// </summary>
        public uint Size { get; }
        public DateTime ModifiedTime { get; }

        public StatEntry(string fullPath, UnixFileMode mode, uint size, DateTime modifiedTime)
        {
            FullPath = fullPath;
            Mode = mode;
            Size = size;
            ModifiedTime = modifiedTime;
        }

        public override string ToString()
        {
            return FullPath;
        }
    }
}
