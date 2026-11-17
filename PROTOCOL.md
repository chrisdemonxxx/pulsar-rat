# Pulsar Communication Protocol

Wire-level specification of the Pulsar C2 client/server protocol, derived from the executable sources of truth (not prose docs). Intended to allow a third-party client implementation in any language. Everything below was verified in code; file paths are cited per section.

Protocol version covered: **2.4.5**.

---

## 1. Overview & versioning

Sources: `Pulsar.Server/Utilities/ServerVersion.cs`, `Pulsar.Client/Config/Settings.cs`, `Pulsar.Common/Pulsar.Common.csproj`

- The protocol has no numeric version field on the wire. The only version exchanged is the human-readable string the client sends in `ClientIdentification.Version`.
- Server version: `ServerVersion.Current = "2.4.5"` (single source parsed by CI for release naming).
- Client reported version: `Settings.ReportedVersion`, which is the pinned constant `Settings.VersionOverride = "2.4.5"` unless it is empty (then the build-patched `Settings.VERSION`). The two must stay in sync.
- Serialization library: **MessagePack 3.1.4** (MessagePack-CSharp). A compatible implementation must speak the MessagePack binary format plus the LZ4BlockArray compression envelope (§3).

## 2. Transport layer

Sources: `Pulsar.Server/Networking/Server.cs`, `Pulsar.Server/Networking/Client.cs`, `Pulsar.Client/Networking/Client.cs`, `Pulsar.Common/DNS/HostsConverter.cs`

- Plain **TCP** stream (`NetworkStream`). **There is no TLS/SslStream anywhere in the codebase** — confidentiality/integrity are provided at the message layer by the secure envelope (§4).
- Server listens on IPv4 `0.0.0.0` or dual-stack IPv6 (`IPv6Only = 0`), backlog 1000, `NoDelay = true`, `ReuseAddress = true`. Multiple ports can be listened on simultaneously (`ListenMany`).
- TCP keep-alive on both ends: time = **25000 ms**, interval = **25000 ms** (`SetKeepAliveEx`).
- Framing, repeated per message, identical in both directions:
  ```
  [4-byte little-endian int32 payload-length][payload]
  ```
  - Length counts only the payload, not the 4 header bytes (`HEADER_SIZE = 4`).
  - Written with `BinaryPrimitives.WriteInt32LittleEndian`; read with `BitConverter.ToInt32`.
  - `length <= 0` aborts the connection (`InvalidDataException`). **No upper bound is enforced** — receivers must self-protect.
- Host list syntax (client config, not on the wire): `host:port;host:port;…` — split on `;`, port parsed from the substring after the **last** `:` as `ushort`. A bare `http(s)://` URL (no `;`) switches the client to pastebin mode, where the URL body is fetched and parsed with the same syntax (`HostsConverter.RawHostsToList`).

## 3. Serialization envelope

Source: `Pulsar.Common/Networking/PulsarMessagePackSerializer.cs`

- Options: `MessagePackSerializerOptions.Standard` with `CompositeResolver(DynamicEnumAsString, DynamicGeneric, DynamicUnion, DynamicObject, Standard)` and **`MessagePackCompression.Lz4BlockArray` applied unconditionally to every serialize call** (inner message *and* outer wrapper). A third-party client must implement MessagePack-CSharp's LZ4BlockArray framing; there is no negotiation or uncompressed mode.
- Every message is wrapped in `MessageWrapper` (`[MessagePackObject]`, integer keys → msgpack **array** of 2 elements):
  - `[Key(0)] TypeName` (string) — the **CLR full type name** (`Type.FullName`), e.g. `Pulsar.Common.Messages.PingRequest`. This is the message ID; there is no numeric ID table.
  - `[Key(1)] Data` (byte[]) — the inner message serialized with the same options (including LZ4BlockArray).
- All message classes are `[MessagePackObject]` with integer `[Key(n)]` attributes → each message serializes as a msgpack **array** (not a map), in key order. Enums serialize as strings (`DynamicEnumAsStringResolver`).
- Deserialization: wrapper → resolve `TypeName` via `Type.GetType`, then by scanning all loaded assemblies; the type must implement `IMessage`. **Unknown type name throws → the connection is dropped** (exception propagates out of `AsyncReceive`). A registered type with no registered processor is silently dropped instead (§6).
- Nested DTOs live outside `Messages/` but are also `[MessagePackObject]` with int keys (see Appendix).

## 4. Security envelope

Sources: `Pulsar.Common/Networking/SecureMessageEnvelopeHelper.cs`, `Pulsar.Common/Cryptography/Aes256.cs`, `Pulsar.Common/Cryptography/Sha256.cs`, `Pulsar.Common/Messages/Other/SecureMessageEnvelope.cs`

- Applied to **every** message in Release builds, in both directions, including the handshake. Order of operations (**serialize-then-encrypt, then wrap-and-frame**):
  1. Inner message → MessagePack wrapper bytes (§3, LZ4-compressed).
  2. Those bytes are AES-256-GCM encrypted.
  3. Ciphertext is placed in `SecureMessageEnvelope` (`[Key(0)] byte[] Payload`).
  4. The envelope is itself serialized as a `MessageWrapper` (`TypeName = "Pulsar.Common.Messages.Other.SecureMessageEnvelope"`, again LZ4BlockArray) and framed per §2.
- Key derivation (both sides derive the identical key from the pinned server certificate — there is **no key exchange**):
  1. `thumbprint = certificate.Thumbprint` with spaces removed, upper-cased (SHA-1 hex of the cert).
  2. `keyMaterial = Base64( SHA256( UTF8(thumbprint) ) )`.
  3. `key = PBKDF2-SHA256(password=keyMaterial, salt=CONST, iterations=100000, output=32 bytes)`.
  4. `CONST` is the 32-byte value `SHA256("PULSAR_AES_256_KEY_DERIVE_SALT")` =
     `5A 23 F8 39 46 40 CB 9E 40 65 84 46 A0 4C 0B BA E8 2D C9 3D 04 70 E1 B2 A4 06 A9 0F D2 52 03 82`.
- Ciphertext layout: `nonce(12) || tag(16) || ciphertext`. A fresh 12-byte random nonce per encryption (CSPRNG). Implemented via `AesGcm` on .NET 5+, Windows CNG (`AuthenticatedAes`) on .NET Framework — plain AES-256-GCM either way, no AAD.
- Enforce modes (`EncryptTraffic`): **Release** — mandatory on both ends; a plaintext message received while enforced throws and drops the connection; a missing certificate on the server side refuses the connection. **Debug** — off unless `PULSAR_DEBUG_ENFORCE_ENCRYPTION=1|true|yes` and a usable certificate exists.
- Certificate pinning: the client embeds the server's X.509 certificate (AES-encrypted with the build's `ENCRYPTIONKEY` inside the binary; decrypted in `Settings.Initialize`). At startup the client self-verifies: RSA (from the cert's public key) `VerifyHash(SHA256(UTF8(ENCRYPTIONKEY)), SERVERSIGNATURE, SHA256, PKCS#1 v1.5)`. Since there is no TLS, this embedded cert + signature is the entire trust root — possession of the cert enables envelope decryption and handshake verification.

## 5. Connection lifecycle

Sources: `Pulsar.Client/Networking/PulsarClient.cs`, `Pulsar.Server/Networking/PulsarServer.cs`, `Pulsar.Server/Messages/DeferredAssemblyHandler.cs`, `Pulsar.Server/Messages/PingHandler.cs`, `Pulsar.Client/PulsarApplication.cs`

Handshake (all messages inside the secure envelope in Release):

1. Client connects and **immediately** sends `ClientIdentification` — the server allows only this type pre-identification; anything else → disconnect.
   `[MessagePackObject]`, keys: `1 Version`, `2 OperatingSystem`, `3 AccountType`, `4 Country`, `5 CountryCode`, `6 ImageIndex (int)`, `7 Id`, `8 Username`, `9 PcName`, `10 Tag`, `11 EncryptionKey`, `12 Signature (byte[])`, `13 PublicIP`.
2. Server validates (`IdentifyClient`): `Id.Length == 64` (hex HWID); in Release it additionally verifies the build secret: `RSA(cert.pubkey).VerifyHash(SHA256(UTF8(EncryptionKey)), Signature, SHA256, PKCS#1)` — the same check the client ran at startup (§4). In Debug every client is accepted.
3. On success the server replies `ClientIdentificationResult { [Key(1)] bool Result = true }` and marks the client identified; on failure it disconnects. The client, pre-identification, processes **only** `ClientIdentificationResult` and ignores everything else.
4. Deferred assemblies (optional, once per session): if the client is missing optional assemblies (SharpDX/AForge/NAudio/…), it sends `RequestDeferredAssemblies { [Key(1)] string[] Assemblies, [Key(2)] string ClientVersion }`. Server responds with one `DeferredAssembliesPackage { [Key(1)] List<DeferredAssemblyDescriptor>, [Key(2)] bool IsComplete = true }`; descriptor keys: `1 Name`, `2 Version`, `3 Data (byte[] assembly, may be null)`, `4 Sha256 (hex)`.
5. Steady state: bidirectional free-form message flow per §6.
- Liveness: TCP keep-alive only (25 s/25 s, §2). The app-level `PingRequest`/`PingResponse` (both empty `[MessagePackObject]` messages, no keys) exist **only** for UI latency measurement — the server's `PreviewHandler` sends `PingRequest`, the client's `PingHandler` replies `PingResponse`. They are not required to keep the session up.
- Reconnect (client): `RECONNECTDELAY` (Release build-patched, default 5000 ms) + uniform jitter 250–750 ms between attempts, round-robin over the host queue (`HostsManager`).

## 6. Message dispatch semantics

Sources: `Pulsar.Common/Messages/MessageHandler.cs`, `Pulsar.Common/Messages/Other/IMessageProcessor.cs`, `Pulsar.Client/PulsarApplication.cs` (`InitializeMessageProcessors`), `Pulsar.Server/Networking/PulsarServer.cs` (`OnClientRead`)

- Processors register globally via `MessageHandler.Register(IMessageProcessor)` (client: `PulsarApplication.InitializeMessageProcessors`; server: `FrmMain` ctor + per-form registrations).
- Dispatch: `MessageHandler.Process(sender, msg, dispatchAsync = true)` selects the snapshot of processors where `proc.CanExecute(msg) && proc.CanExecuteFrom(sender)` and runs each via `ThreadPool.UnsafeQueueUserWorkItem` (synchronous when `dispatchAsync: false`). Processor exceptions are swallowed (logged to `Debug`).
- `CanExecuteFrom` scoping is how per-client handlers (e.g. server `PingHandler`, remote-desktop handlers) bind to exactly one connection.
- Server-side read pipeline additionally thread-pools every identified message except `ClientIdentificationResult` (the only "high priority" type, run inline).
- Failure semantics: unknown `TypeName` → deserialize throws → connection drop (§3); known type with no matching processor → silently dropped (no NACK exists in the protocol).

## 7. Worked example: `PingRequest` / `PingResponse`

End-to-end, server → client, Release mode:

1. Server `PingHandler.SendPing()` records a `Stopwatch` timestamp and calls `client.Send(new PingRequest())`.
2. `SafeSendMessage` → `PrepareMessageForSend`: not yet an envelope and encryption is enforced → `SecureMessageEnvelopeHelper.Wrap`.
3. Inner serialize: `PingRequest` has no keys → msgpack empty array; wrapped as `["Pulsar.Common.Messages.PingRequest", <bytes>]`, LZ4BlockArray-compressed.
4. Encrypt (§4): fresh 12-byte nonce, output = `nonce || tag || ciphertext`.
5. Envelope serialize: `["Pulsar.Common.Messages.Other.SecureMessageEnvelope", <payload>]`, LZ4BlockArray again; frame = `LE32(len) || bytes`; written to the socket under `_sendMessageLock`.
6. Client `AsyncReceive`: reads 4-byte header, then `len` bytes; `PulsarMessagePackSerializer.Deserialize` yields `SecureMessageEnvelope`; `ProcessIncomingMessage` → `Unwrap` (decrypt, GCM tag verified) → inner deserialize yields `PingRequest`.
7. `MessageHandler.Process`: client `PingHandler` matches (`CanExecute` = `is PingRequest`) → replies `new PingResponse()` — the mirror image of steps 2–6.
8. Server `PingHandler` receives `PingResponse` and reports RTT in ms via `OnReport`.

## 8. Appendix: message type-name inventory

Verified by scanning `[MessagePackObject]` classes in `Pulsar.Common/Messages/**` (namespaces taken from the declarations, **not** folder names — several differ). `TypeName` on the wire is exactly the string shown. 141 `IMessage` types + 1 embedded DTO (`DeferredAssemblyDescriptor`).

- **(global namespace)**: `DoSuspendProcess` — note: file declares no namespace, wire name is literally `DoSuspendProcess`.
- **Pulsar.Common.Messages** (24): `DoClientReconnect`, `DoCreateRegistryValue`, `DoDeleteRegistryValue`, `DoExecuteUniversalCommand`, `DoLoadUniversalPlugin`, `DoRenameRegistryKey`, `DoStartupItemAdd`, `DoStartupItemRemove`, `DoUniversalPluginResponse`, `FileTransferRequest`, `GetChangeRegistryValueResponse`, `GetDebugLog`, `GetDeleteRegistryValueResponse`, `GetKeyloggerLogsDirectoryResponse`, `GetMonitorsResponse`, `GetPasswordsResponse`, `GetRegistryKeysResponse`, `GetSystemInfoResponse`, `PingRequest`, `PingResponse`, `SetStatus`, `SetUserActiveWindowStatus`, `SetUserClipboardStatus`, `SetUserStatus`
- **Administration.Actions** (1): `DoShutdownAction`
- **Administration.FileManager** (11): `DoPathDelete`, `DoPathRename`, `DoZipFolder`, `FileTransferCancel`, `FileTransferChunk`, `FileTransferComplete`, `GetDirectory`, `GetDirectoryResponse`, `GetDrives`, `GetDrivesResponse`, `SetStatusFileManager`
- **Administration.RegistryEditor** (10): `DoChangeRegistryValue`, `DoCreateRegistryKey`, `DoDeleteRegistryKey`, `DoLoadRegistryKey`, `DoRenameRegistryValue`, `GetCreateRegistryKeyResponse`, `GetCreateRegistryValueResponse`, `GetDeleteRegistryKeyResponse`, `GetRenameRegistryKeyResponse`, `GetRenameRegistryValueResponse`
- **Administration.RemoteShell** (2): `DoShellExecute`, `DoShellExecuteResponse`
- **Administration.ReverseProxy** (4): `ReverseProxyConnect`, `ReverseProxyConnectResponse`, `ReverseProxyData`, `ReverseProxyDisconnect`
- **Administration.StartupManager** (2): `GetStartupItems`, `GetStartupItemsResponse`
- **Administration.SystemInfo** (1): `GetSystemInfo`
- **Administration.TaskManager** (9): `DoProcessDump`, `DoProcessDumpResponse`, `DoProcessEnd`, `DoProcessResponse`, `DoProcessStart`, `DoSetTopMost`, `DoSetWindowState`, `GetProcesses`, `GetProcessesResponse`
- **Administration.TCPConnections** (3): `DoCloseConnection`, `GetConnections`, `GetConnectionsResponse`
- **Audio** (8): `GetMicrophone`, `GetMicrophoneDevice`, `GetMicrophoneDeviceResponse`, `GetMicrophoneResponse`, `GetOutput`, `GetOutputDevice`, `GetOutputDeviceResponse`, `GetOutputResponse`
- **ClientManagement** (4): `DoClearTempDirectory`, `DoClientDisconnect`, `DoClientUninstall`, `DoElevateSystem` *(lives in `UAC/` folder)*
- **ClientManagement.UAC** (3): `DoAskElevate`, `DoDeElevate`, `DoUACBypass`
- **ClientManagement.WinRE** (3): `AddCustomFileWinRE`, `DoAddWinREPersistence`, `DoRemoveWinREPersistence`
- **FunStuff** (8): `DoBlockKeyboardInput`, `DoBSOD`, `DoCDTray` *(file `DoOpenCloseCDTray.cs`)*, `DoChangeWallpaper`, `DoHideTaskbar`, `DoMonitorsOff`, `DoSendBinFile`, `DoSwapMouseButtons` *(file `DoMouseSwap.cs`)*
- **Monitoring.Clipboard** (4): `DoGetAddress`, `DoSendAddress`, `SendClipboardData`, `SetClipboardMonitoringEnabled`
- **Monitoring.HVNC** (6): `DoHVNCInput`, `GetHVNCDesktop`, `GetHVNCDesktopResponse`, `GetHVNCMonitors`, `GetHVNCMonitorsResponse`, `StartHVNCProcess`
- **Monitoring.KeyLogger** (1): `GetKeyloggerLogsDirectory`
- **Monitoring.Passwords** (1): `GetPasswords`
- **Monitoring.Query** (1): `GetBrowsers` — **Monitoring.Query.Browsers** (1): `GetBrowsersResponse` *(request/response namespaces crossed)*
- **Monitoring.RemoteDesktop** (6): `DoDrawingEvent`, `DoKeyboardEvent`, `DoMouseEvent`, `GetDesktop`, `GetDesktopResponse`, `GetMonitors`
- **Monitoring.VirtualMonitor** (2): `DoInstallVirtualMonitor`, `DoUninstallVirtualMonitor`
- **Other** (7): `ClientIdentification`, `ClientIdentificationResult`, `DeferredAssembliesPackage`, `DeferredAssemblyDescriptor` *(DTO, not IMessage)*, `RequestDeferredAssemblies`, `SecureMessageEnvelope`, `StartProcessOnMonitor`
- **Preview** (2): `GetPreviewImage`, `GetPreviewResponse`
- **QuickCommands** (5): `DoDisableTaskManager`, `DoDisableUAC`, `DoEnableTaskManager`, `DoEnableUAC`, `DoSendQuickCommand`
- **UserSupport.MessageBox** (2): `DoExecScript` *(lives in `RemoteScripting/` folder)*, `DoShowMessageBox`
- **UserSupport.RemoteChat** (5): `DoChat`, `DoChatAction`, `DoKillChatForm`, `DoStartChatForm`, `GetChat`
- **UserSupport.Website** (1): `DoVisitWebsite`
- **Webcam** (4): `GetAvailableWebcams`, `GetAvailableWebcamsResponse`, `GetWebcam`, `GetWebcamResponse` *(namespace is `Pulsar.Common.Messages.Webcam`, not `Monitoring.Webcam`)*

Supporting DTOs embedded inside messages (also `[MessagePackObject]`, int keys): `Pulsar.Common.Models.{Drive, FileChunk, FileSystemEntry, Process, RecoveredAccount, RegSeekerMatch, RegValueData, StartupItem, TcpConnection}`, `Pulsar.Common.Models.Query.Browsers`, `Pulsar.Common.Video.Resolution`, and the internal `MessageWrapper` itself (§3).
