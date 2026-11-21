# AGENTS.md

Pulsar is a Windows-oriented C# remote-administration/research codebase. Use it only in systems for which you have authorization.

## Verified project layout

- `Pulsar.Server` is the WinForms/WPF operator application. It targets `net9.0-windows` and produces `bin/Release/net9.0-windows/Pulsar.exe`.
- `Pulsar.Client` is the Windows client. It targets `net48` so generated single-file managed stubs run with the framework host Windows expects. Its build produces `Client.exe` and copies that assembly to the server build directory as `client.bin`, the client-builder template.
- `Pulsar.Common` contains shared protocol, message, networking, and cryptography code. It multi-targets `net48;net9.0-windows`.
- `Pulsar.Common.Tests` also multi-targets `net48;net9.0-windows`. The `net48` leg validates the framework-compatible Common and builder/client path; the `net9.0-windows` leg validates the server-facing runtime contract.
- `Pulsar.AdminClient` is a `net9.0-windows` console client for permissioned administration.
- `Pulsar.Relay` is a dependency-free `net9.0` TCP relay. It is a byte pipe and does not parse or terminate Pulsar messages.
- `HVNCInjection` is a native project. The supported server-side artifact is `HVNCInjection.x64.dll`.
- `external/donut` is the Donut submodule used by `ReleaseWithDonut`.
- `tools/stub-builder`, `tools/webapi-smoke`, and `tools/admin-relay-smoke` are standalone projects, not solution members.
- `docs/` and `scripts/` are not tracked implementation documentation sources; do not rely on or modify them.

## Current runtime architecture

### Transport and protocol

The transport is plain TCP, not TLS. Both directions use a four-byte little-endian payload length followed by a MessagePack wrapper. The wrapper carries the CLR full type name and serialized message bytes. MessagePack uses the 3.1.11 package and LZ4 block-array compression. Release traffic is wrapped in AES-256-GCM `SecureMessageEnvelope`; Debug traffic is plaintext by default and can opt into envelopes with `PULSAR_DEBUG_ENFORCE_ENCRYPTION=1`.

See `PROTOCOL.md` for wire details. Important current behavior:

- A frame has a default 16 MiB limit; MessagePack decoding has a 32 MiB decompressed limit, maximum depth 64, and collection/string/binary checks.
- Unknown CLR message type names fail deserialization and terminate the connection. A decoded known message with no eligible registered processor is dropped and raises `MessageHandler.UnsupportedMessageReceived` for diagnostics.
- The server allows 30 seconds for identification and has a default total-connection limit of 512.
- Send queues are bounded at 256 messages per connection; overflow disconnects that connection. Async message dispatch is FIFO per sender, with at most 256 queued batches, and at most 512 globally registered processors.
- `MessageWaiterRegistry` supports predicate-based response matching. Expired waiters are removed before completing with `null`, preventing a stale waiter from consuming a later response. It permits 1,024 pending waiters.

### Connection and component lifecycle

- The client sends `ClientIdentification` after TCP connect, waits for `ClientIdentificationResult`, then starts deferred-assembly requests and normal processing.
- The client connect loop rotates hosts, applies reconnect delay plus jitter, cancels reconnect waits during shutdown, resets per-session flags on state changes, and cleans processors, hooks, network state, and optional services on disposal.
- The server supports multiple listener ports. Listener shutdown disposes accept arguments and sockets, detaches clients, drains send queues, and suppresses duplicate disconnect handling.
- Server TCP keep-alive is currently 10 s/10 s; the client uses 25 s/25 s. These are transport properties, not TLS or application-heartbeat guarantees.

### Message registration and operations

New messages require a `[MessagePackObject]` `IMessage` type plus processors registered on the applicable side. Registration locations include `PulsarApplication.InitializeMessageProcessors` and server form/handler setup in `FrmMain`. Registration is not inferred from a message's namespace or file path.

The server Web API and admin relay both use `Pulsar.Server/Operations/ClientOperationService.cs`. This gives one validated mapping for shutdown, restart, standby, lockscreen, reconnect, disconnect, uninstall, quick-command, and message-box operations. It validates client IDs and field-specific length/choice limits, distinguishes invalid, missing, and offline targets, and only reports acceptance after `TrySend` accepts the outbound message.

### Admin relay

An admin connection first performs the ordinary client-identification handshake, then sends `AdminIdentification` containing a 64-hex-character SHA-256 digest of the configured admin key. Key comparison uses fixed-time equality. An empty server key disables the feature; unauthenticated admin-only requests disconnect the connection, and three failed key attempts disconnect that connection.

Authenticated admins are excluded from the managed-client list and receive list snapshots and live connect/disconnect events. Commands are restricted by the shared operation service; admin relay parameters are each capped at 256 KiB. `AdminRelayResult` has a `TargetId`, so the admin console correlates by target. There is no general request-ID or numeric message-ID field on the wire.

### Web API on .NET 9

`Pulsar.Server/Networking/WebApi/WebApiServer.cs` hosts ASP.NET Core Kestrel in the server process. It binds plain HTTP on the configured port; it does not configure HTTPS.

- `/`, `/dashboard`, and `/favicon.ico` are unauthenticated shells only.
- All other paths require `X-Api-Key`; SHA-256 digests of the supplied and configured keys are compared in fixed time.
- Endpoints expose version/stats, connected and persisted clients, offline clients, auto-tasks, logs, SSE events, and the validated client operations.
- Settings changes require a server process restart.
- Kestrel limits are 512 KiB request bodies, 32 KiB total request headers, 256 concurrent connections, and one-minute keep-alive.
- SSE has at most 64 subscribers and a 15-second keepalive; the in-memory log ring holds 200 entries.

The standalone Web API and admin smoke projects exercise the local implementation. Treat their commands as repeatable checks, not evidence of any live deployment.

## Client builder and generated-stub safeguards

`Pulsar.Server/Build/ClientBuilder.cs` patches the `client.bin` assembly with Mono.Cecil:

- `ClientSettingsLayout` asserts the complete static-initializer order: 13 string fields, 13 boolean fields, one reconnect-delay slot, and one special-folder slot.
- Only the first 11 string slots are builder-patched/encrypted; `INSTALLPATH` and `LOGSPATH` remain the last two runtime-derived slots.
- A changed count, order, field pairing, or expected field name fails the build instead of silently shifting settings.
- Build validation requires `BuildOptions.Version == ProtocolVersion.Current`, reconnect delay 0–600,000 ms, install-path value 1–3, the correct output extension, and a manifest path distinct from the artifact.
- Managed obfuscation/packing happens before the transformed entry point is read.
- For shellcode, that entry point is passed to Donut as separate atomic arguments. Input/output paths may contain spaces, and the temporary payload assembly is deleted.

Each successful generated artifact gets `<output>.manifest.json` unless another path is supplied. The schema records artifact/output hashes, template hash, certificate thumbprint, protocol version, settings-layout version, final managed entry point, artifact type, and non-secret feature flags. It deliberately omits hosts, certificate material, keys, signatures, and decrypted configuration. The manifest is provenance/build metadata, not a recovery bundle and not wire data.

Do not append a client setting in only one `#if` branch or patch slot. Update `Settings`, both branches, `ClientSettingsLayout`, `ClientBuilder`, and the compatibility tests together.

## Verified repairs to preserve

Current source contains these fixes; documentation or later changes must not reintroduce the defects:

- `Settings.Initialize` is idempotent and verifies/commits decrypted state only after all fields are valid; certificate lookup is relative to the executable and no longer contains a developer-machine path. Unpatched release templates with an empty encryption key retain their bootstrap compatibility path.
- Frame, decompression, graph-depth, collection, string, byte-array, and input-log expansion are bounded. File-transfer and log readers no longer rely on unbounded decompression.
- Connection send/dispatch queues are bounded and cleanup clears pending sends and invalidates stale pump generations.
- Asynchronous message handling preserves order per sender and contains processor exceptions. Unsupported decoded messages become diagnostics instead of silently disappearing without evidence.
- Admin waiters are registered before send, expired waiters cannot consume future responses, relay responses can be correlated by target, and disconnect cleanup is idempotent.
- The Web API and admin relay share operation validation/auditing instead of maintaining divergent command mappings.
- The server-side `FileManagerHandler` no longer handles its own outbound `DoZipFolder` as an incoming message.
- Donut arguments preserve path boundaries and use the transformed entry point; its native build restores generated Donut source files after `nmake`.
- Universal-plugin response callback collections are bounded, deduplicated, and snapshotted during notification.

## Build and validation

Run from the repository root on Windows.

```powershell
# Managed projects
dotnet restore Pulsar.Common/Pulsar.Common.csproj
dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Release
dotnet build Pulsar.Relay/Pulsar.Relay.csproj -c Release

# All Common tests, framework-selected
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net48
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net9.0-windows

# Standalone checks
dotnet run --project tools/webapi-smoke/WebApiSmoke.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Debug
dotnet run --project tools/admin-relay-smoke/AdminRelaySmoke.csproj -c Debug
```

The GitHub and GitLab Windows workflows currently define this pipeline: managed builds, exact framework test inventories (54 `net48`, 62 `net9.0-windows`), 18 builder/registration/version contracts, a real generated-stub manifest check, both smoke harnesses, native x64 HVNC, and Donut artifact builds. These are workflow expectations, not a claim that they ran in a particular local checkout.

For native HVNC:

```powershell
msbuild HVNCInjection/HVNCInjection.vcxproj /m /p:Configuration=Release /p:Platform=x64 /p:SolutionDir="$((Get-Location).Path)\"
```

For a full managed/native package, use the Visual Studio MSBuild/VC++ toolchain. A plain `dotnet build Pulsar.sln` is not a reliable native-project build path:

```powershell
dotnet restore Pulsar.sln
msbuild Pulsar.sln /m /p:Configuration=Release /p:Platform=AnyCPU
```

`ReleaseWithDonut` additionally needs the initialized submodule and Visual Studio 2022 C++ Build Tools:

```powershell
git submodule update --init external/donut
dotnet build Pulsar.Server/Pulsar.Server.csproj -c ReleaseWithDonut
```

Plain `Release` does not build or copy Donut. Native HVNC must be present for HVNC injection; Donut and HVNC output are build artifacts, not runtime validation. The supported HVNC path is x64, and injector/browser/process compatibility remains environment-specific.

## Compatibility and migration

Compatibility is defined by the exact message type names/keys, framing, serializer options, certificate-derived encryption key, and handshake semantics—not only by the human-readable version string. Admin and consent message classes are additive only while the peer that receives them contains the corresponding type and processor. A peer that cannot decode a type terminates the connection; do not describe new message delivery to an old peer as guaranteed.

Use `Updating_Clients.md` for standard-update versus parallel-migration decisions and rollback. Keep the old server, `Pulsar.p12`, generated artifacts, manifests, and configuration until the replacement has passed the checks appropriate to the environment.
