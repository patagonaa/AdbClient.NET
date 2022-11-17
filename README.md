# AdbClient.NET
A .NET Client for the Android Debug Bridge (ADB)

[![Nuget](https://img.shields.io/nuget/v/patagona.AdbClient)](https://www.nuget.org/packages/patagona.AdbClient/)

## Features

- Simple
- Thread-Safe
- Implemented Services:
    - GetHostVersion
    - GetDevices
    - Sync
        - Push
        - Pull
        - List
        - Stat
    - Execute

## Compared to madb / SharpAdbClient

- Pros:
    - Simple
    - Thread-Safe
    - Async
    - CancellationToken support
- Cons:
    - Lots of services not (yet) implemented (pull requests are welcome)