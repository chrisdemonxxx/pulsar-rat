# FAQ

## Why is the shellcode option unavailable?

Donut is an optional native build dependency. A normal `Release` build of the server does not build or copy `donut.exe`.

Use `ReleaseWithDonut` on Windows with the Donut submodule and Visual Studio 2022 C++ Build Tools:

```powershell
git submodule update --init external/donut
dotnet build Pulsar.Server/Pulsar.Server.csproj -c ReleaseWithDonut
```

The target runs `Pulsar.Server/build-donut.bat`, which:

1. locates a Visual Studio 2022 `vcvarsall.bat`;
2. runs `nmake -f Makefile.msvc` in `external/donut`;
3. preserves/restores Donut's generated loader source files;
4. fails if `donut.exe` was not produced;
5. lets the project copy Donut into `bin/Release/net9.0-windows`.

Expected artifacts after a successful native build:

```text
external/donut/donut.exe
bin/Release/net9.0-windows/donut.exe
```

The server's builder invokes Donut with an argument list rather than a concatenated command line, so an input/output path containing spaces stays one argument. It also resolves the final managed entry point after obfuscation/packing rather than assuming the pre-transform name.

If Donut output is built elsewhere, pass that executable explicitly through the builder's Donut path. A downloaded binary may be version-incompatible with the checked-in submodule and is not the CI baseline. Creating a `.bin` shellcode artifact does not prove that it executes on a particular target.

## Why is HVNC injection unavailable after a managed build?

HVNC injection requires the native x64 file:

```text
bin/Release/net9.0-windows/HVNCInjection.x64.dll
```

Build it with the Visual Studio/MSBuild C++ toolchain:

```powershell
msbuild HVNCInjection/HVNCInjection.vcxproj /m /p:Configuration=Release /p:Platform=x64 /p:SolutionDir="$((Get-Location).Path)\"
```

A managed-only build does not supply this DLL. The server reads it from its executable directory when preparing supported Chromium browser starts. The client performs reflective loading in the selected process; browser version, profile layout, process architecture, privileges, and Windows configuration remain environment-specific. The source has no automatic fallback for a missing or incompatible x64 injector.

## Does `Pulsar.Client` target .NET Framework 4.8?

No. The current client targets `net9.0-windows`. The server and admin client also target `net9.0-windows`. `Pulsar.Common` and `Pulsar.Common.Tests` multi-target `net48;net9.0-windows`; that does not make the client a `net48` application.

## Is the TCP transport TLS?

No. The transport is plain TCP. Release traffic uses the AES-256-GCM `SecureMessageEnvelope` at the Pulsar message layer. The Kestrel Web API is separately plain HTTP in the source; any HTTPS/reverse-proxy boundary is deployment-owned. See `PROTOCOL.md`.

## Why did an old peer disconnect after a new message was added?

The message ID is the CLR full type name. Unknown type names fail MessagePack deserialization and disconnect the receiver. A known type with no registered processor is decoded and then dropped with an unsupported-message diagnostic. Treat additions as receiver compatibility changes and use the process in `Updating_Clients.md`.

## How do I verify the documentation's build/test commands without executing a client?

Use the managed tests and standalone local harnesses:

```powershell
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net48
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net9.0-windows
dotnet run --project tools/webapi-smoke/WebApiSmoke.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Debug
dotnet run --project tools/admin-relay-smoke/AdminRelaySmoke.csproj -c Debug
```

The builder/registration tests and CI manifest check can validate a generated artifact without launching it. They do not prove runtime behavior on a separate machine.
