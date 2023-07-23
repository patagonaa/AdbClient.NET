using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AdbClient
{
    /// <summary>
    /// A client to communicate to an Android device via the "adb sync" protocol
    /// </summary>
    /// <seealso href="https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/master/SYNC.TXT"/>
    public class AdbSyncClient : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        internal AdbSyncClient(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
        }

        /// <summary>
        /// Pull a file from the device
        /// </summary>
        /// <param name="path">The file on the device to read from</param>
        /// <param name="outStream">The stream the file contents are written to</param>
        /// <exception cref="AdbException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task Pull(string path, Stream outStream, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var adbStream = _tcpClient.GetStream();

                await SendRequestWithPath(adbStream, "RECV", path, cancellationToken);

                const int maxChunkSize = 64 * 1024; // SYNC_DATA_MAX
                var buffer = new byte[maxChunkSize].AsMemory();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var response = await GetResponse(adbStream, cancellationToken);
                    if (response == "DATA")
                    {
                        var dataLength = checked((int)await adbStream.ReadUInt32(cancellationToken));
                        await adbStream.ReadExact(buffer[..dataLength], cancellationToken);
                        await outStream.WriteAsync(buffer[..dataLength], cancellationToken);
                    }
                    else if (response == "DONE")
                    {
                        _ = await adbStream.ReadUInt32(cancellationToken);
                        break;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Invalid Response Type {response}");
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Push a file to the device
        /// </summary>
        /// <param name="path">The file on the device to write to</param>
        /// <param name="inStream">The stream the file contents are read from</param>
        /// <param name="modifiedDate">The file modified time to set (or <see langword="null"/> to set to current time)</param>
        /// <param name="permissions">The file permissions to set</param>
        /// <exception cref="AdbException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task Push(
            string path,
            Stream inStream,
            DateTimeOffset? modifiedDate = null,
            UnixFileMode permissions = UnixFileMode.OwnerRead | UnixFileMode.OwnerWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var adbStream = _tcpClient.GetStream();

                var permissionsMask = 0x01FF; // 0777;
                var pathWithPermissions = $"{path},0{Convert.ToString((int)permissions & permissionsMask, 8)}";
                await SendRequestWithPath(adbStream, "SEND", pathWithPermissions, cancellationToken);

                const int maxChunkSize = 64 * 1024; // SYNC_DATA_MAX
                var buffer = new byte[maxChunkSize].AsMemory();
                int readBytes;
                while ((readBytes = await inStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await SendRequestWithLength(adbStream, "DATA", readBytes, cancellationToken);
                    await adbStream.WriteAsync(buffer[..readBytes], cancellationToken);
                }
                await SendRequestWithLength(adbStream, "DONE", (int)(modifiedDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(), cancellationToken);
                await GetResponse(adbStream, cancellationToken);
                _ = await adbStream.ReadUInt32(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        [Obsolete("Use ListV2 instead (if your device supports it)")]
        public async Task<IList<StatEntry>> List(string path, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var stream = _tcpClient.GetStream();
                await SendRequestWithPath(stream, "LIST", path, cancellationToken);

                var toReturn = new List<StatEntry>();
                while (true)
                {
                    var response = await GetResponse(stream, cancellationToken);
                    if (response == "DONE")
                    {
                        // ADB sends an entire (empty) stat entry when done, so we have to skip it
                        var ignoreBuf = new byte[16];
                        await stream.ReadExact(ignoreBuf, cancellationToken);
                        break;
                    }
                    else if (response == "DENT")
                    {
                        var statEntry = await ReadStatEntry(stream, async () => $"{path.TrimEnd('/')}/{await ReadString(stream, cancellationToken)}", cancellationToken);
                        toReturn.Add(statEntry);
                    }
                    else if (response != "STAT")
                    {
                        throw new InvalidOperationException($"Invalid Response Type {response}");
                    }
                }

                return toReturn;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        [Obsolete("Use StatV2 instead (if your device supports it)")]
        public async Task<StatEntry> Stat(string path, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var stream = _tcpClient.GetStream();
                await SendRequestWithPath(stream, "STAT", path, cancellationToken);
                var response = await GetResponse(stream, cancellationToken);
                if (response != "STAT")
                    throw new InvalidOperationException($"Invalid Response Type {response}");
                var statEntry = await ReadStatEntry(stream, () => Task.FromResult(path), cancellationToken);
                return statEntry;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static async Task<StatEntry> ReadStatEntry(Stream stream, Func<Task<string>> getPath, CancellationToken cancellationToken)
        {
            var fileMode = await stream.ReadUInt32(cancellationToken);
            var fileSize = await stream.ReadUInt32(cancellationToken);
            var fileModifiedTime = await stream.ReadUInt32(cancellationToken);
            var path = await getPath();
            return new StatEntry(path, (UnixFileMode)fileMode, fileSize, DateTime.UnixEpoch.AddSeconds(fileModifiedTime));
        }

        /// <summary>
        /// Get information about each file in a directory.
        /// This uses lstat internally, so symlink info is returned rather than the info about the file it is pointing to.
        /// </summary>
        /// <param name="path">The path on the device to list</param>
        /// <returns>A list of directory entries</returns>
        /// <exception cref="AdbException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<IList<StatV2Entry>> ListV2(string path, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var stream = _tcpClient.GetStream();
                await SendRequestWithPath(stream, "LIS2", path, cancellationToken);

                var toReturn = new List<StatV2Entry>();
                while (true)
                {
                    var response = await GetResponse(stream, cancellationToken);
                    if (response == "DONE")
                    {
                        // ADB sends an entire (empty) stat entry when done, so we have to skip it
                        var ignoreBuf = new byte[72];
                        await stream.ReadExact(ignoreBuf, cancellationToken);
                        break;
                    }
                    else if (response == "DNT2")
                    {
                        var statEntry = await ReadStatV2Entry(stream, async () => $"{path.TrimEnd('/')}/{await ReadString(stream, cancellationToken)}", cancellationToken);
                        toReturn.Add(statEntry);
                    }
                    else if (response != "STAT")
                    {
                        throw new InvalidOperationException($"Invalid Response Type {response}");
                    }
                }

                return toReturn;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Get information about a single file
        /// </summary>
        /// <param name="path">The path on the device</param>
        /// <param name="lstat">Use <c>lstat()</c> instead of <c>stat()</c> (return symlink info rather than the info about the file it is pointing to)</param>
        /// <returns>Information about the file</returns>
        /// <exception cref="AdbException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<StatV2Entry> StatV2(string path, bool lstat = true, CancellationToken cancellationToken = default)
        {
            var command = lstat ? "LST2" : "STA2";
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var stream = _tcpClient.GetStream();
                await SendRequestWithPath(stream, command, path, cancellationToken);
                var response = await GetResponse(stream, cancellationToken);
                if (response != command)
                    throw new InvalidOperationException($"Invalid Response Type {response}");
                return await ReadStatV2Entry(stream, () => Task.FromResult(path), cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<StatV2Entry> ReadStatV2Entry(Stream stream, Func<Task<string>> getPath, CancellationToken cancellationToken)
        {
            var error = (AdbSyncErrorCode)await stream.ReadUInt32(cancellationToken);
            await stream.ReadUInt64(cancellationToken); // device ID
            await stream.ReadUInt64(cancellationToken); // Inode number
            var mode = await stream.ReadUInt32(cancellationToken);
            await stream.ReadUInt32(cancellationToken); // Number of hard links
            var uid = await stream.ReadUInt32(cancellationToken);
            var gid = await stream.ReadUInt32(cancellationToken);
            var size = await stream.ReadUInt64(cancellationToken);
            var atime = await stream.ReadInt64(cancellationToken);
            var mtime = await stream.ReadInt64(cancellationToken);
            var ctime = await stream.ReadInt64(cancellationToken);
            var path = await getPath();
            return new StatV2Entry(path, (UnixFileMode)mode, uid, gid, size, DateTime.UnixEpoch.AddSeconds(atime), DateTime.UnixEpoch.AddSeconds(mtime), DateTime.UnixEpoch.AddSeconds(ctime), error);
        }

        private static async Task SendRequestWithPath(Stream stream, string requestType, string path, CancellationToken cancellationToken)
        {
            int pathLengthBytes = AdbServicesClient.Encoding.GetByteCount(path);
            await SendRequestWithLength(stream, requestType, pathLengthBytes, cancellationToken);

            var pathBytes = AdbServicesClient.Encoding.GetBytes(path);
            await stream.WriteAsync(pathBytes.AsMemory(), cancellationToken);
        }

        private static async Task SendRequestWithLength(Stream stream, string requestType, int length, CancellationToken cancellationToken)
        {
            var requestBytes = new byte[8];
            AdbServicesClient.Encoding.GetBytes(requestType.AsSpan(), requestBytes.AsSpan(0, 4));

            BitConverter.GetBytes(length).CopyTo(requestBytes, 4);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(requestBytes, 4, 4);
            }

            await stream.WriteAsync(requestBytes.AsMemory(), cancellationToken);
        }

        private static async Task<string> GetResponse(Stream stream, CancellationToken cancellationToken)
        {
            var responseTypeBuffer = new byte[4];
            await stream.ReadExact(responseTypeBuffer.AsMemory(), cancellationToken);
            var responseType = AdbServicesClient.Encoding.GetString(responseTypeBuffer);
            if (responseType == "FAIL")
            {
                throw new AdbException(await ReadString(stream, cancellationToken));
            }
            return responseType;
        }

        private static async Task<string> ReadString(Stream stream, CancellationToken cancellationToken)
        {
            var responseLength = await stream.ReadUInt32(cancellationToken);
            var responseBuffer = new byte[responseLength];
            await stream.ReadExact(responseBuffer.AsMemory(), cancellationToken);
            return AdbServicesClient.Encoding.GetString(responseBuffer);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }
}
