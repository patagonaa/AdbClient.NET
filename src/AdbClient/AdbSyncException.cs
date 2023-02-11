namespace AdbClient
{
    public class AdbSyncException : AdbException
    {
        public AdbSyncException(AdbSyncErrorCode errorCode)
            : base($"Error code {errorCode}")
        {
            ErrorCode = errorCode;
        }

        public AdbSyncErrorCode ErrorCode { get; }
    }
}
