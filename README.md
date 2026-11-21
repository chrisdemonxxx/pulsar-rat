# Pulsar

Pulsar is a Windows-oriented C# remote-administration/research project. Use it only on systems you own or are explicitly authorized to test.

## Current build targets

| Component | Target | Role |
|---|---|---|
| `Pulsar.Client` | `net48` | Windows client, WinForms runtime, generated-stub template |
| `Pulsar.Server` | `net9.0-windows` | WinForms/WPF operator application and embedded Web API |
| `Pulsar.AdminClient` | `net9.0-windows` | Permissioned command-line administration client |
| `Pulsar.Relay` | `net9.0` | Blind TCP relay with round-robin upstreams |
| `Pulsar.Common` | `net48;net9.0-windows` | Shared messages, networking, crypto, and models |
| `Pulsar.Common.Tests` | `net48;net9.0-windows` | Shared-contract test suite |

The client intentionally targets `net48`, because the builder emits the patched managed client as a directly executable `.exe`. `net48` also remains the framework-compatible target for Common and its tests.

## Architecture

```text
Pulsar.Client / Pulsar.AdminClient
        |
        | plain TCP frames: LE32 length + MessagePack wrapper
        | Release: AES-256-GCM message envelope
        v
Pulsar.Server
  |-- WinForms/WPF operator UI
  |-- client message handlers
  |-- shared client-operation service
  |-- SQLite persisted-client records
  |-- embedded .NET 9 Kestrel Web API
  `-- client builder / optional Donut and x64 HVNC artifacts

Pulsar.Relay: blind byte pipe between client and server
```

The transport has no TLS. Release security is applied at the Pulsar message layer. The Web API is a separate, Kestrel-hosted plain-HTTP listener with an `X-Api-Key` requirement; deployments that need HTTPS must provide that externally.

See:

- [`PROTOCOL.md`](PROTOCOL.md) for framing, MessagePack, encryption, handshake, registration, correlation, limits, and message names;
- [`AGENTS.md`](AGENTS.md) for the verified developer architecture and CI commands;
- [`Updating_Clients.md`](Updating_Clients.md) for update, migration, and rollback rules;
- [`ROADMAP.md`](ROADMAP.md) for implemented and remaining work.

## Implemented capabilities

- length-prefixed, LZ4-compressed MessagePack messages with CLR type-name dispatch;
- Release AES-256-GCM envelopes derived from the pinned server certificate;
- identification, reconnect, deferred-assembly loading, and bounded connection queues;
- explicit handler registration with per-sender FIFO asynchronous dispatch;
- a shared, validated client-operation service used by the Web API and admin relay;
- Kestrel API endpoints for client state, persisted/offline records, logs, auto-tasks, SSE, and supported operations;
- a lightweight permissioned admin client with predicate-correlated responses and live events;
- a blind relay with round-robin failover, concurrency limits, and idle shutdown;
- Windows-service mode and optional client status/consent GUI;
- client generation with exact static-settings layout validation and adjacent non-secret manifests;
- optional x64 HVNC and Donut native artifacts.

Native features are environment-dependent. A successful managed build does not imply that browser injection or generated shellcode was executed successfully.

## Building on Windows

Use a .NET 9 SDK. For managed components:

```powershell
dotnet restore Pulsar.Common/Pulsar.Common.csproj
dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Release
dotnet build Pulsar.Relay/Pulsar.Relay.csproj -c Release
```

The client build copies `Client.exe` to `bin/Release/net9.0-windows/client.bin` and stages optional deferred assemblies for the server.

A full solution includes the native C++ HVNC project. Build that path with Visual Studio/MSBuild rather than relying on `dotnet build Pulsar.sln`:

```powershell
dotnet restore Pulsar.sln
msbuild Pulsar.sln /m /p:Configuration=Release /p:Platform=AnyCPU
```

Donut is built only by `ReleaseWithDonut`:

```powershell
git submodule update --init external/donut
dotnet build Pulsar.Server/Pulsar.Server.csproj -c ReleaseWithDonut
```

That configuration requires Visual Studio 2022 C++ Build Tools and an initialized `external/donut` submodule.

## Tests and CI checks

```powershell
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net48
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net9.0-windows
dotnet run --project tools/webapi-smoke/WebApiSmoke.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Debug
dotnet run --project tools/admin-relay-smoke/AdminRelaySmoke.csproj -c Debug
```

The checked-in GitHub and GitLab Windows workflows currently expect:

- managed component builds;
- 54 `net48` and 62 `net9.0-windows` test results;
- 18 builder/registration/version contract checks;
- generation and validation of a real stub manifest;
- Web API and admin-relay smoke projects;
- x64 HVNC and Donut artifact jobs.

These are CI definitions and expected inventories, not a statement that a local or live deployment was tested by this README.

## Web API

Configure `webApiEnabled`, `webApiPort`, and `webApiKey` under the server's runtime `PulsarStuff` settings. A restart is required after changing them. The server refuses to start the service with an empty key.

All `/api` and other non-shell requests require:

```text
X-Api-Key: <configured key>
```

The embedded dashboard shell at `/` is unauthenticated but contains no client data; its fetches require the key. Current source limits include a 512 KiB request body, 32 KiB request-header budget, 256 Kestrel connections, 64 SSE subscribers, and a 200-entry log ring.

## Generated client manifests

The server client builder emits `<output>.manifest.json` by default. It records:

- artifact and template SHA-256 hashes;
- certificate thumbprint;
- `ProtocolVersion.Current`;
- settings-layout schema version;
- transformed managed entry point;
- artifact type and non-secret feature flags.

It intentionally omits hosts, keys, signatures, certificate bytes, and decrypted client settings. The manifest supports provenance and validation; it cannot restore a lost certificate or serve as a rollback image.

The builder also rejects any changed settings field count/order and refuses incompatible versions, invalid output types, or an output/manifest collision.

## Compatibility and rollback

Do not use the displayed version string alone to decide compatibility. Adding a CLR message type is not backward compatible with a receiver that cannot decode it, and certificate/envelope changes create a trust-boundary change.

For a parallel migration, retain the old server, certificate, configuration, generated artifacts, and manifests; use a new endpoint/mutex; test a small group; and retire the old listener only after rollback remains possible. See [`Updating_Clients.md`](Updating_Clients.md).

## Runtime state

Generated runtime state is normally under the server output's `PulsarStuff/` directory, including settings, the SQLite client database, builder defaults, blocked/favorite lists, auto-tasks, and the server certificate. Treat `Pulsar.p12` as required migration/rollback material: losing it strands generated clients that were built for that certificate.

## Optional native artifacts

HVNC's supported server artifact is:

```text
bin/Release/net9.0-windows/HVNCInjection.x64.dll
```

It must be produced with the native x64 MSBuild project and depends on target process/browser conditions. Donut is separate and is produced only under `ReleaseWithDonut`. See [`FAQ.md`](FAQ.md) for native troubleshooting and explicit limitations.
