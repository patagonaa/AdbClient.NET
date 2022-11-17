using System;

namespace AdbClient
{
    public class AdbException : Exception
    {
        public AdbException(string reason)
            : base(reason)
        {
        }
    }
}
