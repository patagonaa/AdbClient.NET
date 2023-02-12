namespace AdbClient
{
    public class AdbSyncException : AdbException
    {
        public AdbSyncException(AdbSyncErrorCode errorCode, string filePath)
            : base($"{filePath} error {errorCode}")
        {
            ErrorCode = errorCode;
        }

        public AdbSyncErrorCode ErrorCode { get; }
    }
}
