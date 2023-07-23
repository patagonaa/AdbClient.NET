# AdbClient.NET
A .NET Client for the Android Debug Bridge (ADB)

[![Nuget](https://img.shields.io/nuget/v/patagona.AdbClient)](https://www.nuget.org/packages/patagona.AdbClient/)

## Features

- Simple
- Thread-Safe
- Implemented Services:
    - GetHostVersion (`host:version`)
    - GetDevices (`host:devices`)
    - TrackDevices (`host:track-devices`)
    - Sync (`sync:`)
        - Push (`SEND`)
        - Pull (`RECV`)
        - List (`LIST`)
        - ListV2 (`LIS2`)
        - Stat (`STAT`)
        - StatV2 (`LST2`/`STA2`)
    - Execute (`shell,v2:`)
    - ScreenCapture (`framebuffer:`)

## Compared to madb / SharpAdbClient

- Pros:
    - Simple
    - Thread-Safe
    - Async
    - CancellationToken support
- Cons:
    - Lots of services not (yet) implemented (pull requests are welcome)

## Examples

### Get ADB host version
```csharp
var adbClient = new AdbServicesClient();
int hostVersion = await adbClient.GetHostVersion();
```

### Get Devices
```csharp
var adbClient = new AdbServicesClient();
IList<(string Serial, AdbConnectionState State)> devices = await adbClient.GetDevices();
```

### Track Devices
This tracks all device changes (connect, disconnect, etc.) until the CancellationToken is cancelled (in this case, for 60 seconds)
```csharp
var adbClient = new AdbServicesClient();
var cts = new CancellationTokenSource(60000);
await foreach ((string Serial, AdbConnectionState State) deviceStateChange in adbClient.TrackDevices(cts.Token))
{
    [...]
}
```

### List root directory
```csharp
var adbClient = new AdbServicesClient();
using (var syncClient = await adbClient.GetSyncClient("abcdefghijklmnop"))
{
    IList<StatV2Entry> entries = syncClient.ListV2("/");
}
```

### Upload file to device
```csharp
using (var fs = File.OpenRead("test.mp3"))
using (var syncClient = await client.GetSyncClient("abcdefghijklmnop"))
{
    await syncClient.Push("/storage/emulated/0/Music/test.mp3", fs);
}
```

### Download file from device
```csharp
using (var fs = File.OpenWrite("test.mp3"))
using (var syncClient = await client.GetSyncClient("abcdefghijklmnop"))
{
    await syncClient.Pull("/storage/emulated/0/Music/test.mp3", fs);
}
```

### Execute command
#### Simple
```csharp
var adbClient = new AdbServicesClient();
int exitCode = await adbClient.Execute("abcdefghijklmnop", "touch", new string[] { "/storage/emulated/0/test.txt" }, null, null, null);
```

#### With stdout/stderr redirection
```csharp
var adbClient = new AdbServicesClient();
using var stdout = new MemoryStream();
using var stderr = new MemoryStream();
int exitCode = await adbClient.Execute("abcdefghijklmnop", "ls", new string[] { "-la", "/storage/emulated/0/test.txt" }, null, stdout, stderr);
if (exitCode != 0)
    throw new Exception(Encoding.UTF8.GetString(stderr.ToArray()));
Console.WriteLine(Encoding.UTF8.GetString(stdout.ToArray())); // prints directory listing
```

#### With everything redirected
```csharp
var adbClient = new AdbServicesClient();
using var stdin = new MemoryStream(Encoding.UTF8.GetBytes("Hello World"));
using var stdout = new MemoryStream();
using var stderr = new MemoryStream();
int exitCode = await adbClient.Execute("abcdefghijklmnop", "cat", new string[] {}, stdin, stdout, stderr);
if (exitCode != 0)
    throw new Exception(Encoding.UTF8.GetString(stderr.ToArray()));
Console.WriteLine(Encoding.UTF8.GetString(stdout.ToArray())); // prints "Hello World"
```

### Capture screen
#### Capture and save as png
```csharp
var adbClient = new AdbServicesClient();
using Image img = await adbClient.ScreenCapture("abcdefghijklmnop");
img.SaveAsPng("image.png");
```
