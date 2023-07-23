using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
    /// <summary>
    /// Client that uses an ADB server to communicate with attached Android devices
    /// </summary>
    /// <seealso href="https://android.googlesource.com/platform/packages/modules/adb/+/c36807b1a84e6ee64e3f2380bbcbbead6d9ae7e4/SERVICES.TXT"/>
    public class AdbServicesClient
    {
        internal static readonly Encoding Encoding = Encoding.UTF8;
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

        /// <summary>
        /// Ask the ADB server for its internal version number.
        /// </summary>
        /// <exception cref="AdbException"></exception>
        public async Task<int> GetHostVersion(CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:version", cancellationToken);
            return int.Parse(await ReadStringResult(client, cancellationToken), NumberStyles.HexNumber);
        }

        /// <summary>
        /// Ask to return the list of available Android devices and their state.
        /// </summary>
        /// <exception cref="AdbException"></exception>
        public async Task<IList<(string Serial, AdbConnectionState State)>> GetDevices(CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:devices", cancellationToken);
            var result = await ReadStringResult(client, cancellationToken);
            return _deviceRegex.Matches(result).Select(x => (x.Groups["serial"].Value, GetConnectionStateFromString(x.Groups["state"].Value))).ToList();
        }

        /// <summary>
        /// This is a variant of <see cref="GetDevices(CancellationToken)"/> which doesn't close the connection.
        /// Instead, a new device list description is sent each time a device is added/removed
        /// or the state of a given device changes.
        /// </summary>
        /// <exception cref="AdbException"></exception>
        public async IAsyncEnumerable<(string Serial, AdbConnectionState State)> TrackDevices([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, "host:track-devices", cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ReadStringResult(client, cancellationToken);
                if (string.IsNullOrWhiteSpace(result))
                    continue;
                var match = _deviceRegex.Match(result);
                if (!match.Success)
                    throw new InvalidOperationException($"Invalid response: '{result}'");
                yield return (match.Groups["serial"].Value, GetConnectionStateFromString(match.Groups["state"].Value));
            }
        }

        /// <summary>
        /// Get a sync client to do file operations on the target device via the "adb sync" protocol
        /// </summary>
        /// <param name="serial">The serial number of the target device</param>
        /// <exception cref="AdbException"></exception>
        public async Task<AdbSyncClient> GetSyncClient(string serial, CancellationToken cancellationToken = default)
        {
            var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, $"host:transport:{serial}", cancellationToken);
            await ExecuteAdbCommand(client, $"sync:", cancellationToken);
            return new AdbSyncClient(client);
        }

        /// <summary>
        /// Get a screenshot from the target device
        /// </summary>
        /// <param name="serial">The serial number of the target device</param>
        /// <exception cref="AdbException"></exception>
        public async Task<Image> ScreenCapture(string serial, CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, $"host:transport:{serial}", cancellationToken);
            await ExecuteAdbCommand(client, $"framebuffer:", cancellationToken);
            var stream = client.GetStream();

            var version = await stream.ReadUInt32(cancellationToken);
            if (version != 2)
            {
                throw new InvalidOperationException($"Invalid version {version}");
            }
            var bpp = await stream.ReadUInt32(cancellationToken);
            var colorSpace = await stream.ReadUInt32(cancellationToken);
            var size = await stream.ReadUInt32(cancellationToken);
            var width = Convert.ToInt32(await stream.ReadUInt32(cancellationToken));
            var height = Convert.ToInt32(await stream.ReadUInt32(cancellationToken));
            var rOff = await stream.ReadUInt32(cancellationToken);
            var rLen = await stream.ReadUInt32(cancellationToken);
            var bOff = await stream.ReadUInt32(cancellationToken);
            var bLen = await stream.ReadUInt32(cancellationToken);
            var gOff = await stream.ReadUInt32(cancellationToken);
            var gLen = await stream.ReadUInt32(cancellationToken);
            var aOff = await stream.ReadUInt32(cancellationToken);
            var aLen = await stream.ReadUInt32(cancellationToken);

            var buffer = new byte[size];
            await stream.ReadExact(buffer.AsMemory(), cancellationToken);

            var pixfmt = (bpp, (rOff, rLen), (gOff, gLen), (bOff, bLen), (aOff, aLen));
            Image img = pixfmt switch
            {
                (32, (0, 8), (8, 8), (16, 8), (24, 8)) => Image.LoadPixelData<Rgba32>(buffer, width, height),
                (32, (0, 8), (8, 8), (16, 8), (_, 0)) => GetRgbx32(width, height, buffer),
                (24, (0, 8), (8, 8), (16, 8), (_, 0)) => Image.LoadPixelData<Rgb24>(buffer, width, height),
                (16, (11, 5), (5, 6), (0, 5), (_, 0)) => Image.LoadPixelData<Bgr565>(buffer, width, height),
                (32, (16, 8), (8, 8), (0, 8), (24, 8)) => Image.LoadPixelData<Bgra32>(buffer, width, height),
                _ => throw new InvalidOperationException($"Invalid Pixel format {pixfmt}"),
            };

            return img;

            static Image<Rgba32> GetRgbx32(int width, int height, byte[] buffer)
            {
                Image<Rgba32> image = Image.LoadPixelData<Rgba32>(buffer, width, height);
                image.Mutate(x => x.ProcessPixelRowsAsVector4(rows =>
                {
                    for (int i = 0; i < rows.Length; i++)
                    {
                        rows[i].W = 1;
                    }
                }));
                return image;
            }
        }

        /// <summary>
        /// Execute a shell command on the target device.
        /// </summary>
        /// <param name="serial">The serial number of the target device</param>
        /// <param name="command">The command to execute</param>
        /// <param name="args">The parameters to pass to the command</param>
        /// <param name="stdin">A stream to copy to the command's <c>stdin</c> or <see langword="null"/> to ignore</param>
        /// <param name="stdout">A stream the command's <c>stdout</c> is written to or <see langword="null"/> to ignore</param>
        /// <param name="stderr">A stream the command's <c>stderr</c> is written to or <see langword="null"/> to ignore</param>
        /// <returns>The command's exit code</returns>
        /// <exception cref="AdbException"></exception>
        public async Task<int> Execute(string serial, string command, IEnumerable<string> args, Stream? stdin, Stream? stdout, Stream? stderr, CancellationToken cancellationToken = default)
        {
            using var client = await GetConnectedClient(cancellationToken);
            await ExecuteAdbCommand(client, $"host:transport:{serial}", cancellationToken);
            await ExecuteAdbCommand(client, $"shell,v2,raw:{GetShellCommand(command, args)}", cancellationToken);

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

                    var (type, message) = await GetMessage(stream, cancellationToken);

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
                            throw new InvalidOperationException($"Invalid Shell Command {type}");
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
            static async Task<(ShellProtocolType Type, byte[] Content)> GetMessage(Stream stream, CancellationToken cancellationToken)
            {
                var header = new byte[5];
                await stream.ReadExact(header.AsMemory(), cancellationToken);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(header, 1, 4);
                }
                var bodyLength = BitConverter.ToUInt32(header, 1);
                var body = new byte[bodyLength];
                await stream.ReadExact(body.AsMemory(), cancellationToken);
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

        // https://android.googlesource.com/platform/packages/modules/adb/+/c36807b1a84e6ee64e3f2380bbcbbead6d9ae7e4/adb.cpp#117
        // https://android.googlesource.com/platform/system/core/+/9f0e6493e5e75c65cff1d456d11243b1114ce57a/diagnose_usb/diagnose_usb.cpp#83
        private AdbConnectionState GetConnectionStateFromString(string state)
        {
            return state switch
            {
                "offline" => AdbConnectionState.Offline,
                "bootloader" => AdbConnectionState.Bootloader,
                "device" => AdbConnectionState.Device,
                "host" => AdbConnectionState.Host,
                "recovery" => AdbConnectionState.Recovery,
                "rescue" => AdbConnectionState.Rescue,
                "sideload" => AdbConnectionState.Sideload,
                "unauthorized" => AdbConnectionState.Unauthorized,
                "authorizing" => AdbConnectionState.Authorizing,
                "connecting" => AdbConnectionState.Connecting,
                _ when state.StartsWith("no permissions") => AdbConnectionState.NoPerm, // "no permissions (reason); see <URL>"
                _ => AdbConnectionState.Unknown
            };
        }

        private async Task ExecuteAdbCommand(TcpClient tcpClient, string command, CancellationToken cancellationToken)
        {
            var stream = tcpClient.GetStream();
            var commandLength = Encoding.GetByteCount(command);
            var request = $"{commandLength:X4}{command}";

            await stream.WriteAsync(Encoding.GetBytes(request).AsMemory(), cancellationToken);

            var buffer = new byte[4];
            await stream.ReadExact(buffer.AsMemory(), cancellationToken);

            var responseType = Encoding.GetString(buffer);

            switch (responseType)
            {
                case "OKAY":
                    return;
                case "FAIL":
                    var response = await ReadStringResult(tcpClient, cancellationToken);
                    throw new AdbException(response);
                default:
                    throw new InvalidOperationException($"Invalid response type {responseType}");
            }
        }

        private async Task<string> ReadStringResult(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            var stream = tcpClient.GetStream();

            var buffer = new byte[4];
            await stream.ReadExact(buffer.AsMemory(), cancellationToken);

            var responseLength = int.Parse(Encoding.GetString(buffer), NumberStyles.HexNumber);
            var responseBuffer = new byte[responseLength];
            await stream.ReadExact(responseBuffer.AsMemory(), cancellationToken);
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
