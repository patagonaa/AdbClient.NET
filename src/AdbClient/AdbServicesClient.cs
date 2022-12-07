using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AdbClient
{
    // https://android.googlesource.com/platform/system/adb/+/refs/heads/master/SERVICES.TXT
    public class AdbServicesClient
    {
        public static readonly Encoding Encoding = Encoding.UTF8;
        private static readonly Regex _deviceRegex = new Regex(@"^(?<serial>[\S ]+?)\t(?<state>[\S ]+)$", RegexOptions.Multiline);
        private readonly IPEndPoint _endPoint;

        public AdbServicesClient()
            : this(new IPEndPoint(IPAddress.Loopback, 5037))
        {
        }

        public AdbServicesClient(IPEndPoint endPoint)
        {
            _endPoint = endPoint;
        }

        public async Task<int> GetHostVersion(CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:version");
            return int.Parse(await ReadStringResult(client), NumberStyles.HexNumber);
        }

        public async Task<IList<(string Serial, string State)>> GetDevices(CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:devices");
            var result = await ReadStringResult(client);
            return _deviceRegex.Matches(result).Select(x => (x.Groups["serial"].Value, x.Groups["state"].Value)).ToList();
        }

        public async IAsyncEnumerable<(string Serial, string State)> TrackDevices([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:track-devices");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ReadStringResult(client);
                if (string.IsNullOrWhiteSpace(result))
                    continue;
                var match = _deviceRegex.Match(result);
                if (!match.Success)
                    throw new InvalidOperationException($"Invalid response: '{result}'");
                yield return (match.Groups["serial"].Value, match.Groups["state"].Value);
            }
        }

        public async Task<AdbSyncClient> GetSyncClient(string serial, CancellationToken cancellationToken = default)
        {
            var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, $"host:transport:{serial}");
            await ExecuteAdbCommand(client, $"sync:");
            return new AdbSyncClient(client);
        }

        public async Task<int> Execute(string serial, string command, IEnumerable<string> parms, Stream? stdin, Stream? stdout, Stream? stderr, CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, $"host:transport:{serial}");
            await ExecuteAdbCommand(client, $"shell,v2,raw:{GetShellCommand(command, parms)}");

            var stream = client.GetStream();

            var stdInCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task? stdInTask = null;
            if (stdin != null)
                stdInTask = Task.Run(() => SendStdIn(stdin, stream, stdInCancellation.Token));
            int? returnCode = null;
            try
            {
                while (returnCode == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (type, message) = await GetMessage(stream);

                    switch (type)
                    {
                        case ShellProtocolType.kIdStdout:
                            if (stdout != null)
                                await stdout.WriteAsync(message);
                            break;
                        case ShellProtocolType.kIdStderr:
                            if (stderr != null)
                                await stderr.WriteAsync(message);
                            break;
                        case ShellProtocolType.kIdExit:
                            returnCode = message[0];
                            break;
                        default:
                            throw new Exception($"Invalid Shell Command {type}");
                    }
                }
            }
            finally
            {
                stdInCancellation.Cancel();
            }
            if (stdInTask != null)
                await stdInTask;

            return returnCode.Value;

            static async Task SendStdIn(Stream stdin, Stream stream, CancellationToken cancellationToken)
            {
                try
                {
                    var headerLen = 5;
                    var bufferLen = 1024; // may be anything
                    var buffer = new byte[headerLen + bufferLen].AsMemory();

                    int readLen;
                    while ((readLen = await stdin.ReadAsync(buffer[headerLen..], cancellationToken)) != 0)
                    {
                        buffer.Span[0] = (byte)ShellProtocolType.kIdStdin;
                        var lengthBytes = BitConverter.GetBytes(readLen);
                        if (!BitConverter.IsLittleEndian)
                        {

                            Array.Reverse(lengthBytes);
                        }
                        lengthBytes.CopyTo(buffer[1..]);
                        await stream.WriteAsync(buffer[..(headerLen + readLen)], cancellationToken);
                    }

                    await stream.WriteAsync(new byte[] { (byte)ShellProtocolType.kIdCloseStdin, 0, 0, 0, 0 });
                }
                catch (OperationCanceledException)
                {
                }
            }
            static async Task<(ShellProtocolType Type, byte[] Content)> GetMessage(Stream stream)
            {
                var header = new byte[5];
                await stream.ReadExact(header.AsMemory());
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(header, 1, 4);
                }
                var bodyLength = BitConverter.ToUInt32(header, 1);
                var body = new byte[bodyLength];
                await stream.ReadExact(body.AsMemory());
                return ((ShellProtocolType)header[0], body);
            }
        }

        private enum ShellProtocolType : byte
        {
            kIdStdin = 0,
            kIdStdout = 1,
            kIdStderr = 2,
            kIdExit = 3,
            kIdCloseStdin = 4,
            kIdWindowSizeChange = 5,
            kIdInvalid = 255,
        }

        private string GetShellCommand(string command, IEnumerable<string> parms)
        {
            var sb = new StringBuilder(100);
            sb.Append(command);
            foreach (var parm in parms)
            {
                sb.Append(" '");
                sb.Append(parm.Replace("'", "'\\''"));
                sb.Append("'");
            }
            return sb.ToString();
        }

        private async Task ExecuteAdbCommand(TcpClient tcpClient, string command)
        {
            var stream = tcpClient.GetStream();
            var commandLength = Encoding.GetByteCount(command);
            var request = $"{commandLength:X4}{command}";

            await stream.WriteAsync(Encoding.GetBytes(request).AsMemory());

            var buffer = new byte[4];
            await stream.ReadExact(buffer.AsMemory());

            var responseType = Encoding.GetString(buffer);

            switch (responseType)
            {
                case "OKAY":
                    return;
                case "FAIL":
                    var response = await ReadStringResult(tcpClient);
                    throw new AdbException(response);
                default:
                    throw new InvalidOperationException($"Invalid response type {responseType}");
            }
        }

        private async Task<string> ReadStringResult(TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();

            var buffer = new byte[4];
            await stream.ReadExact(buffer.AsMemory());

            var responseLength = int.Parse(Encoding.GetString(buffer), NumberStyles.HexNumber);
            var responseBuffer = new byte[responseLength];
            await stream.ReadExact(responseBuffer.AsMemory());
            return Encoding.GetString(responseBuffer);
        }

        private async Task<TcpClient> GetConnectedClient(CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient();
#if NETSTANDARD2_1
            await tcpClient.ConnectAsync(_endPoint.Address, _endPoint.Port);
#else
            await tcpClient.ConnectAsync(_endPoint, cancellationToken);
#endif
            return tcpClient;
        }
    }
}
