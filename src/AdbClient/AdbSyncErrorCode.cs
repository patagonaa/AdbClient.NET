namespace AdbClient
{
    public enum AdbSyncErrorCode
    {
        NoError,
        EACCES = 13,
        EEXIST = 17,
        EFAULT = 14,
        EFBIG = 27,
        EINTR = 4,
        EINVAL = 22,
        EIO = 5,
        EISDIR = 21,
        ELOOP = 40,
        EMFILE = 24,
        ENAMETOOLONG = 36,
        ENFILE = 23,
        ENOENT = 2,
        ENOMEM = 12,
        ENOSPC = 28,
        ENOTDIR = 20,
        EOVERFLOW = 75,
        EPERM = 1,
        EROFS = 30,
        ETXTBSY = 26
    }
}
