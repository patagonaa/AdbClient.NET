# AdbClient.NET
A .NET Client for the Android Debug Bridge (ADB)

[![Nuget](https://img.shields.io/nuget/v/patagona.AdbClient)](https://www.nuget.org/packages/patagona.AdbClient/)

## Features

- Simple
- Thread-Safe
- Implemented Services:
    - GetHostVersion
    - GetDevices
    - TrackDevices
    - Sync
        - Push
        - Pull
        - List
        - Stat
        - StatV2
    - Execute

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
IList<(string Serial, string State)> devices = await adbClient.GetDevices();
```

### Track Devices
This tracks all device changes until the CancellationToken is cancelled (in this case, for 60 seconds)
```csharp
var adbClient = new AdbServicesClient();
var cts = new CancellationTokenSource(60000);
await foreach ((string Serial, string State) deviceStateChange in adbClient.TrackDevices(cts.Token))
{
    [...]
}
```

### List root directory
```csharp
var adbClient = new AdbServicesClient();
using(var syncClient = await adbClient.GetSyncClient("abcdefghijklmnop"))
{
    IList<StatEntry> entries = syncClient.List("/");
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