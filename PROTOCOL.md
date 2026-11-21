# Pulsar Communication Protocol

This document describes the current wire contract as implemented by the source. It is not a claim that every peer, browser, native runtime, or historical client can interoperate. A type/version/feature is compatible only when the receiving peer can decode and register it.

Protocol label covered: **2.4.5**.

## 1. Version and compatibility

Sources: `Pulsar.Common/ProtocolVersion.cs`, `Pulsar.Server/Utilities/ServerVersion.cs`, `Pulsar.Client/Config/Settings.cs`.

- `Pulsar.Common.ProtocolVersion.Current` is the source of truth for builder validation and the client `ReportedVersion`.
- `ServerVersion.Current` remains a literal because release automation parses it. Contract tests currently require these three surfaces to match.
- The wire has no numeric protocol-version field and no capability negotiation. The human-readable `ClientIdentification.Version` string is identification metadata, not proof of compatibility.
- Compatibility is determined by the exact framing, MessagePack option set, CLR type names and `[Key]` layouts, certificate-derived AES key, and registration/handshake behavior.
- A new message type is usable only after both decoding and handler registration exist on the receiver. A receiver that cannot resolve the CLR type name fails deserialization and disconnects. A known decoded type with no eligible processor is dropped and reported through `UnsupportedMessageReceived`.

## 2. TCP transport and frame limits

Sources: `Pulsar.Server/Networking/Server.cs`, `Pulsar.Server/Networking/Client.cs`, `Pulsar.Client/Networking/Client.cs`, `Pulsar.Relay/Program.cs`.

Pulsar's endpoint-to-endpoint transport is a plain TCP `NetworkStream`. The main protocol does not use TLS or `SslStream`; confidentiality/integrity are supplied by the message envelope in §4.

Each message frame is:

```text
[4-byte little-endian signed int32 payload length][payload]
```

The length excludes the four-byte header. Lengths must be positive and, by default, at most 16 MiB. Invalid or oversized frames terminate the connection.

Current listener behavior:

- IPv4 `Any` or IPv6 `Any` is selected from the listener option.
- backlog is 1024;
- accepted sockets have `NoDelay = true`;
- server TCP keep-alive is 10,000 ms time and interval;
- client TCP keep-alive is 25,000 ms time and interval;
- `PULSAR_MAX_SERVER_CONNECTIONS` defaults to 512 and is accepted in the range 1–16,384;
- each connection's outbound queue is bounded at 256 messages; exceeding the bound disconnects it.

Input limits are centralized in `MessageInputLimits`:

| Setting | Default | Environment override | Accepted range |
|---|---:|---|---:|
| Frame bytes | 16 MiB | `PULSAR_MAX_FRAME_BYTES` | 64 KiB–256 MiB |
| Decompressed bytes | 32 MiB | `PULSAR_MAX_DECOMPRESSED_BYTES` | 64 KiB–256 MiB |
| Collection items | 65,536 | `PULSAR_MAX_COLLECTION_ITEMS` | 1–1,000,000 |
| String length | 2 MiB | `PULSAR_MAX_STRING_LENGTH` | 256 B–16 MiB |

Other fixed bounds are a 512-character type name and object depth 64. Byte arrays are also constrained by the configured frame limit. Limits are process-start values; change the environment before starting the process.

`Pulsar.Relay` does not decode any of these structures. It byte-pipes accepted TCP streams to a round-robin upstream and defaults to 256 active relayed connections, a 10-second upstream connect timeout, and a five-minute idle timeout.

## 3. MessagePack envelope

Source: `Pulsar.Common/Networking/PulsarMessagePackSerializer.cs`.

The current package version is **MessagePack 3.1.11**. Serialization uses:

- `MessagePackSerializerOptions.Standard`;
- `DynamicEnumAsStringResolver`;
- `DynamicGenericResolver`;
- `DynamicUnionResolver`;
- `DynamicObjectResolver`;
- `StandardResolver`;
- unconditional `MessagePackCompression.Lz4BlockArray`;
- untrusted-data maximum object depth and decompressed size from `MessageInputLimits`.

Every message is first serialized and then placed in `MessageWrapper`, an integer-key array with two elements:

| Key | Field | Meaning |
|---:|---|---|
| 0 | `TypeName` | CLR `Type.FullName` used as the message ID |
| 1 | `Data` | Inner message serialized with the same options and LZ4 compression |

The outer wrapper is serialized with the same options, including LZ4 compression. Message and supporting DTO classes use `[MessagePackObject]` and integer `[Key]` attributes, so their wire form is positional arrays rather than named maps.

Deserialization:

1. validates frame length;
2. decodes `MessageWrapper`;
3. rejects an empty/oversized type name or invalid data size;
4. resolves the type through `Type.GetType` and then loaded assemblies;
5. requires the type to implement `IMessage`;
6. decodes and validates the resulting object graph.

There is no union table or numeric message registry. Reordering integer keys, renaming types, or changing namespaces therefore changes the wire contract.

## 4. AES message envelope

Sources: `SecureMessageEnvelopeHelper`, `Aes256`, `Sha256`, `SecureMessageEnvelope`, and the client/server networking classes.

Release mode encrypts every outgoing ordinary message, including the normal identification exchange. The send order is:

1. serialize the ordinary message to wrapper bytes (§3);
2. encrypt with AES-256-GCM;
3. put `nonce || tag || ciphertext` in `SecureMessageEnvelope.Payload`;
4. serialize and LZ4-compress the envelope wrapper (§3);
5. prepend the four-byte frame length.

Current authenticated-encryption parameters are:

- AES-256-GCM;
- 12-byte random nonce;
- 16-byte tag;
- no additional authenticated data.

Key derivation:

1. remove spaces from the certificate thumbprint and uppercase it;
2. compute `SHA256(UTF8(thumbprint))`;
3. Base64-encode the 32-byte digest to form the password material;
4. apply PBKDF2-HMAC-SHA256 with 100,000 iterations and a 32-byte output;
5. use the constant 32-byte salt `SHA256("PULSAR_AES_256_KEY_DERIVE_SALT")`:

```text
5A 23 F8 39 46 40 CB 9E 40 65 84 46 A0 4C 0B BA
E8 2D C9 3D 04 70 E1 B2 A4 06 A9 0F D2 52 03 82
```

There is no key exchange and no TLS. The certificate is the pinned trust root for both encryption-key derivation and the build-signature check.

Release clients require a usable certificate and reject plaintext while encryption is enforced. Debug builds are plaintext by default; `PULSAR_DEBUG_ENFORCE_ENCRYPTION=1|true|yes` enables envelopes when a usable certificate is present. The receive path has explicit plaintext exceptions for `ClientIdentification` on the server side and `ClientIdentificationResult` on the client side; handshake state still accepts those messages only in the appropriate part of the lifecycle.

## 5. Connection lifecycle

Sources: `PulsarClient`, `PulsarServer`, client/server `Client`, and the deferred-assembly handlers.

### Server accept and identification

1. The server accepts a TCP connection and starts a 30-second identification timer.
2. The server accepts only `ClientIdentification` before identification. Any other decoded message disconnects.
3. The client identification has:

   | Key | Field |
   |---:|---|
   | 1 | `Version` |
   | 2 | `OperatingSystem` |
   | 3 | `AccountType` |
   | 4 | `Country` |
   | 5 | `CountryCode` |
   | 6 | `ImageIndex` |
   | 7 | `Id` |
   | 8 | `Username` |
   | 9 | `PcName` |
   | 10 | `Tag` |
   | 11 | `EncryptionKey` |
   | 12 | `Signature` |
   | 13 | `PublicIP` |

4. `Id` must be exactly 64 hexadecimal characters.
5. In Release, the server verifies the RSA signature over `SHA256(UTF8(EncryptionKey))` with the server certificate's public key. In Debug, identification is compatibility-oriented and does not perform this check.
6. Success sends `ClientIdentificationResult { Result = true }`, clears the timer, and makes the client available to identified-client services. Failure disconnects.
7. A client accepts normal processing only after `ClientIdentificationResult` sets its identified state.

The client resets identification and deferred-assembly request state on each connection-state change. After identification it sends the active-window status and, if needed, `RequestDeferredAssemblies`. The server replies with `DeferredAssembliesPackage`.

### Reconnect and shutdown

The client connects to hosts in rotation. If a host is unavailable it waits for `RECONNECTDELAY` plus 250–750 ms jitter. Pastebin/remote-host mode can use a longer retry recommendation from `HostsManager`.

Shutdown cancels the connect-loop waits and disconnects. Disposal releases the message processors, user-activity/clipboard/window hooks, network/proxy objects, tray icon, mutex, and optional services held by `PulsarApplication`.

The server listener can start multiple ports. Shutdown detaches event handlers, disposes all accept `SocketAsyncEventArgs`, closes listener sockets, snapshots/clears clients, and disconnects them. Duplicate disconnect processing is suppressed.

## 6. Message registration and dispatch

Sources: `Pulsar.Common/Messages/MessageHandler.cs`, `IMessageProcessor`, client/server registration sites.

- `Register` ignores duplicate processor references and rejects registration after 512 processors.
- Each received message snapshots processors where both `CanExecute(message)` and `CanExecuteFrom(sender)` succeed.
- Selection exceptions are contained and logged.
- With `dispatchAsync: true`, work is queued through one FIFO dispatcher per sender. Different senders may run concurrently; work from one sender remains ordered.
- Each sender's queue is capped at 256 pending dispatch batches. If it is full, that sender is disconnected.
- With `dispatchAsync: false`, all eligible processors run inline in snapshot order.
- Processor exceptions are contained so another eligible processor can still run.
- No eligible processor causes the message to be dropped and emits `UnsupportedMessageReceived`.
- There is no wire NACK for an unsupported decoded message.

Registration is explicit. New type definitions do not register themselves automatically, and a source-file folder is not a protocol namespace.

## 7. Response correlation

There is no general protocol request ID. Normal request/response handlers use feature-specific state, target identity, and sender scoping.

`Pulsar.Common.Networking.MessageWaiterRegistry` is used by the admin console for typed in-process correlation:

- registration occurs before sending a request;
- waiters are grouped by exact CLR message type;
- an optional predicate correlates fields such as `AdminRelayResult.TargetId`;
- the oldest matching waiter completes;
- timeout or cancellation completes it with `null`;
- timed-out waiters are removed so a later response cannot satisfy stale work;
- at most 1,024 waiters may be pending.

There is no unique request token in `DoAdminRelay` or `AdminRelayResult`. Concurrent relay requests for the same target/type can still be ambiguous to a third-party implementation; serialize such requests or add an explicit correlation design in a future wire revision.

## 8. Admin relay extension

Sources: `Pulsar.Common/Messages/AdminRelay`, `Pulsar.Server/Messages/AdminRelayHandler.cs`, `Pulsar.AdminClient`.

An admin first completes normal `ClientIdentification`. It then sends:

```text
AdminIdentification.AdminKeyHash = hex(SHA256(UTF8(configured admin key)))
```

The server:

- disables the feature if `adminClientKey` is empty;
- parses exactly 32 digest bytes/64 hex characters;
- uses fixed-time comparison;
- disconnects after three failed authentication attempts;
- disconnects unauthenticated list/relay requests;
- excludes authenticated admin connections from managed targets;
- limits each relay parameter to 256 KiB;
- maps only shutdown, restart, standby, lockscreen, reconnect, disconnect, uninstall, messagebox, and quick-command operations.

`AdminRelayResult` correlates by `TargetId`; `AdminClientEvent` carries connect/disconnect state. `GetAdminClientList` and `GetAdminClientListResponse` are separate list request/response messages. The admin extension does not give the console arbitrary `IMessage` forwarding.

These message types are additive to the source tree, but an old peer that cannot resolve them is not compatible with that exchange.

## 9. Worked `PingRequest` / `PingResponse`

End-to-end, server to client in Release:

1. server `PingHandler` records latency state and calls `client.Send(new PingRequest())`;
2. the send pump serializes an AES `SecureMessageEnvelope` containing the serialized inner request;
3. the wrapper is LZ4-compressed, framed, and written under the connection send lock;
4. client receive validates the frame length, decodes the envelope, verifies/decrypts it, and decodes `PingRequest`;
5. the client processor replies with `PingResponse`;
6. the response repeats the same path in reverse;
7. the server handler reports round-trip latency.

Ping is a UI/latency feature, not the only liveness mechanism. TCP keep-alive and disconnect detection remain independent.

## 10. Message type-name inventory

At the current source revision there are **149 `IMessage` classes plus two MessagePack DTO classes in the message tree** (`AdminClientInfo` and `DeferredAssemblyDescriptor`). The following inventory records the full CLR namespace/type names because path folders are not authoritative.

- **global namespace**: `DoSuspendProcess`
- **`Pulsar.Common.Messages`**: `DoClientReconnect`, `DoCreateRegistryValue`, `DoDeleteRegistryValue`, `DoExecuteUniversalCommand`, `DoLoadUniversalPlugin`, `DoRenameRegistryKey`, `DoStartupItemAdd`, `DoStartupItemRemove`, `DoUniversalPluginResponse`, `FileTransferRequest`, `GetChangeRegistryValueResponse`, `GetDebugLog`, `GetDeleteRegistryValueResponse`, `GetKeyloggerLogsDirectoryResponse`, `GetMonitorsResponse`, `GetPasswordsResponse`, `GetRegistryKeysResponse`, `GetSystemInfoResponse`, `PingRequest`, `PingResponse`, `SetStatus`, `SetUserActiveWindowStatus`, `SetUserClipboardStatus`, `SetUserStatus`
- **`Administration.Actions`**: `DoShutdownAction`
- **`Administration.FileManager`**: `DoPathDelete`, `DoPathRename`, `DoZipFolder`, `FileTransferCancel`, `FileTransferChunk`, `FileTransferComplete`, `GetDirectory`, `GetDirectoryResponse`, `GetDrives`, `GetDrivesResponse`, `SetStatusFileManager`
- **`Administration.RegistryEditor`**: `DoChangeRegistryValue`, `DoCreateRegistryKey`, `DoDeleteRegistryKey`, `DoLoadRegistryKey`, `DoRenameRegistryValue`, `GetCreateRegistryKeyResponse`, `GetCreateRegistryValueResponse`, `GetDeleteRegistryKeyResponse`, `GetRenameRegistryKeyResponse`, `GetRenameRegistryValueResponse`
- **`Administration.RemoteShell`**: `DoShellExecute`, `DoShellExecuteResponse`
- **`Administration.ReverseProxy`**: `ReverseProxyConnect`, `ReverseProxyConnectResponse`, `ReverseProxyData`, `ReverseProxyDisconnect`
- **`Administration.StartupManager`**: `GetStartupItems`, `GetStartupItemsResponse`
- **`Administration.SystemInfo`**: `GetSystemInfo`
- **`Administration.TaskManager`**: `DoProcessDump`, `DoProcessDumpResponse`, `DoProcessEnd`, `DoProcessResponse`, `DoProcessStart`, `DoSetTopMost`, `DoSetWindowState`, `GetProcesses`, `GetProcessesResponse`
- **`Administration.TCPConnections`**: `DoCloseConnection`, `GetConnections`, `GetConnectionsResponse`
- **`Audio`**: `GetMicrophone`, `GetMicrophoneDevice`, `GetMicrophoneDeviceResponse`, `GetMicrophoneResponse`, `GetOutput`, `GetOutputDevice`, `GetOutputDeviceResponse`, `GetOutputResponse`
- **`ClientManagement`**: `DoClearTempDirectory`, `DoClientDisconnect`, `DoClientUninstall`, `DoElevateSystem`
- **`ClientManagement.UAC`**: `DoAskElevate`, `DoDeElevate`, `DoUACBypass`
- **`ClientManagement.WinRE`**: `AddCustomFileWinRE`, `DoAddWinREPersistence`, `DoRemoveWinREPersistence`
- **`FunStuff`**: `DoBlockKeyboardInput`, `DoBSOD`, `DoCDTray`, `DoChangeWallpaper`, `DoHideTaskbar`, `DoMonitorsOff`, `DoSendBinFile`, `DoSwapMouseButtons`
- **`Monitoring.Clipboard`**: `DoGetAddress`, `DoSendAddress`, `SendClipboardData`, `SetClipboardMonitoringEnabled`
- **`Monitoring.HVNC`**: `DoHVNCInput`, `GetHVNCDesktop`, `GetHVNCDesktopResponse`, `GetHVNCMonitors`, `GetHVNCMonitorsResponse`, `StartHVNCProcess`
- **`Monitoring.KeyLogger`**: `GetKeyloggerLogsDirectory`
- **`Monitoring.Passwords`**: `GetPasswords`
- **`Monitoring.Query`**: `GetBrowsers`; **`Monitoring.Query.Browsers`**: `GetBrowsersResponse`
- **`Monitoring.RemoteDesktop`**: `DoDrawingEvent`, `DoKeyboardEvent`, `DoMouseEvent`, `DoRemoteDesktopPermissionResponse`, `GetDesktop`, `GetDesktopResponse`, `GetMonitors`
- **`Monitoring.VirtualMonitor`**: `DoInstallVirtualMonitor`, `DoUninstallVirtualMonitor`
- **`Other`**: `ClientIdentification`, `ClientIdentificationResult`, `DeferredAssembliesPackage`, `RequestDeferredAssemblies`, `SecureMessageEnvelope`, `StartProcessOnMonitor`
- **`Preview`**: `GetPreviewImage`, `GetPreviewResponse`
- **`QuickCommands`**: `DoDisableTaskManager`, `DoDisableUAC`, `DoEnableTaskManager`, `DoEnableUAC`, `DoSendQuickCommand`
- **`UserSupport.MessageBox`**: `DoExecScript`, `DoShowMessageBox`
- **`UserSupport.RemoteChat`**: `DoChat`, `DoChatAction`, `DoKillChatForm`, `DoStartChatForm`, `GetChat`
- **`UserSupport.Website`**: `DoVisitWebsite`
- **`Webcam`**: `GetAvailableWebcams`, `GetAvailableWebcamsResponse`, `GetWebcam`, `GetWebcamResponse`
- **`Pulsar.Common.Messages.AdminRelay`**: `AdminClientEvent`, `AdminIdentification`, `AdminIdentificationResult`, `AdminRelayResult`, `DoAdminRelay`, `GetAdminClientList`, `GetAdminClientListResponse`

Names in this section omit their shared prefix where shown. On the wire, use the complete CLR name, including the namespace.

Supporting DTOs outside the message directory also use `[MessagePackObject]`; examples include `Pulsar.Common.Models.{Drive, FileChunk, FileSystemEntry, Process, RecoveredAccount, RegSeekerMatch, RegValueData, StartupItem, TcpConnection}`, `Pulsar.Common.Models.Query.Browsers`, and `Pulsar.Common.Video.Resolution`.
