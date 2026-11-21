# Pulsar Roadmap

This file tracks implemented source capabilities and remaining work. "Implemented" means the current source contains the feature; it does not assert that a binary was deployed or exercised in a particular environment. See `AGENTS.md` and `PROTOCOL.md` for the verified build and wire contracts.

## Implemented

### Embedded Web API

`Pulsar.Server/Networking/WebApi/WebApiServer.cs` hosts a .NET 9 ASP.NET Core/Kestrel service in the server process. It serves:

- version, stats, connected-client, persisted-client, offline-client, auto-task, and recent-log queries;
- a bounded SSE stream for connect/disconnect/log/heartbeat events;
- validated client operations through the shared `ClientOperationService`;
- an embedded dashboard shell.

The service is plain HTTP on the configured port. `/`, `/dashboard`, and `/favicon.ico` contain no client data. Every other path requires `X-Api-Key`; the server refuses to start with an empty configured key. Settings require a process restart. A standalone `tools/webapi-smoke` check is defined by the CI workflows.

### Transparent protocol documentation

`PROTOCOL.md` describes the current framing, MessagePack envelope, AES message envelope, handshake, limits, handler registration, correlation behavior, and compatibility constraints.

The protocol does not carry a numeric negotiation field. The human-readable client version is currently sourced from `Pulsar.Common.ProtocolVersion.Current`; matching that string alone does not prove wire compatibility.

### CLI relay

`Pulsar.Relay` provides a `net9.0` TCP relay with:

- one or more upstreams selected round-robin with failover;
- bounded active connections and configurable idle timeout;
- bidirectional byte piping with no MessagePack or AES parsing;
- graceful Ctrl+C shutdown and connection counters.

The relay does not authenticate or terminate the end-to-end message envelope.

### Permissioned admin clients

`Pulsar.AdminClient` is a `net9.0-windows` console client. It completes the regular identification handshake, authenticates with the configured admin key, lists managed clients, consumes live client events, and relays a whitelist of existing operations.

The server excludes authenticated admin connections from the managed-client list, bounds relay parameters, disconnects unauthenticated admin requests, and disconnects after three failed key attempts. Admin message types are additive, but an older server that cannot decode them is not compatible with an admin client.

### Client installation as a Windows service

The builder's `INSTALLSERVICE` option registers the installed client with `sc.exe` and launches it with `-service`. `PulsarService` starts the normal WinForms loop on an STA service thread. Installation requires elevation; session-0 behavior has no implication about interactive UI availability.

### Basic client GUI and remote-desktop consent

`SHOWCLIENTGUI` gates a tray/status UI. The same option also gates the consent path for a fresh remote-desktop session. The consent dialog times out to denial. Mid-session reconfiguration does not ask again.

The consent response is a distinct message type. Treat compatibility as receiver-dependent: an old peer that cannot decode the type will fail the receive path rather than transparently ignoring it.

### Generated-client validation

The client builder now validates the exact settings slot layout and protocol version, reads the final entry point after transformations, quotes Donut arguments atomically, and emits an adjacent manifest with hashes and non-secret build metadata. This work is enforced by builder compatibility tests and CI-generated-stub checks.

## Partially implemented / environment-dependent

### GUI modernization

Remote desktop rendering already uses the WPF `ElementHost` path, but the rest of the operator UI remains WinForms/WPF mixed. A complete UI overhaul is still open.

### HVNC

HVNC framing, controls, and capture are present, but process/browser behavior depends on:

- the generated x64 `HVNCInjection.x64.dll`;
- Visual Studio C++ build tooling;
- target process architecture, browser version, profile layout, and local Windows controls.

The source has no verified fallback for a missing or incompatible native DLL. Native artifact creation is covered separately in CI; it is not proof of end-to-end injection on every target.

### Donut shellcode output

`ReleaseWithDonut` builds the submodule with Visual Studio 2022 C++ tools and copies `donut.exe`. The builder uses the final transformed managed entry point and supports paths with spaces. Donut generation is a native-tool integration; successful creation of a `.bin` is not itself evidence that the payload ran successfully in a target process.

### Browser profile discovery

The recovery crawler can discover Chromium-derived profile layouts under `%LocalAppData%` and `%AppData%` without relying only on a fixed path list. Application updates and browser-specific storage changes can still make individual recovery behavior environment-dependent.

## Still open

### Cross-platform support

The client, server, admin console, UI, service, native injection, and many features depend on Windows APIs. The relay is cross-platform-capable as a .NET console application, but it does not make the full project cross-platform.

### Broader protocol negotiation

The current protocol has exact CLR type-name dispatch and no capability negotiation or numeric version handshake. Future work could add an explicitly negotiated capability envelope, but that would be a new migration boundary.

### Long-running runtime verification

The repository defines repeatable unit and local smoke commands plus CI jobs, but no documentation should substitute those checks for deployment-specific soak, lifecycle, restart, and native-injection tests.
