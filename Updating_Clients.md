# Updating Clients

Choose an update method from the compatibility impact, not from the apparent size of the code change. The current wire contract includes framing, CLR message type names, MessagePack keys/options, AES-envelope behavior, the certificate-derived key, and handshake state. The human-readable protocol version is useful metadata, but it is not a negotiation mechanism.

## 1. Determine the compatibility boundary

### Standard in-place update

A standard update is reasonable only when all of the following remain true:

- existing message type names and `[Key]` layouts are unchanged;
- framing, MessagePack version/options, compression, encryption mode, and certificate-derived key behavior are unchanged;
- the server can continue processing every message the replacement client can send during overlap;
- the replacement has the same trust material needed to authenticate;
- the change does not require a new mutex, endpoint, or native runtime artifact.

Use the existing update mechanism or your controlled deployment process. Retain the previous client artifact and generated manifest. Verify that the replacement artifact's SHA-256, protocol version, certificate thumbprint, settings layout version, and entry-point metadata match the intended server/certificate.

An update is not compatible merely because compilation succeeds. Unknown CLR `TypeName` values fail deserialization and disconnect the receiver. "New server + old client" and "old server + new client" must be considered separately, especially when the new client can emit a newly added type.

### Parallel migration

Use a separate server instance and migration endpoint when any of the following changed:

- a message key/type/name, framing rule, serializer option, or AES envelope;
- server certificate or build-secret trust;
- identification requirements or protocol version in a compatibility-sensitive way;
- a mutex that would otherwise collide with the old instance;
- a listener port/host list or native payload dependency;
- behavior that sends a new message type to a peer that has not registered it.

Recommended migration:

1. Preserve the old server state, `Pulsar.p12`, configuration, generated clients, manifests, and logs.
2. Build a replacement from the current `client.bin`. A generated stub gets an adjacent manifest by default.
3. Use a distinct endpoint and mutex. If trust material changes, treat the server and client set as a new trust domain.
4. Start the replacement server while the old server remains available.
5. Migrate a limited set of clients and run the checks that cover identification, reconnect, representative messages, and cleanup.
6. Move the remaining clients in controlled batches.
7. Retire the old listener only after the replacement inventory is complete and rollback conditions are understood.

Do not infer success from a socket connection alone. Check successful identification and the intended request/response or feature path.

## 2. Manifest and layout expectations

The builder validates a generated stub's static settings layout before patching. It rejects a protocol-version mismatch and a changed field count/order instead of shifting values. A normal generated artifact writes:

```text
<artifact>.manifest.json
```

The manifest includes the output and template SHA-256 hashes, certificate thumbprint, protocol version, settings-layout version, final managed entry point, artifact type, and boolean build flags. It does not contain hosts, encryption keys, the certificate, signatures, or decrypted settings. Copying a manifest does not recover a server or certificate.

For shellcode builds, validate both the generated `.bin` and its manifest, but also retain the managed input/output metadata needed to rebuild it. Donut output creation is not proof of execution compatibility.

## 3. Rollback

Rollback is safe only when the old endpoint and trust material can still accept the rolled-back client.

1. Stop or isolate the replacement listener from new migrations.
2. Confirm the previous server, `Pulsar.p12`, configuration, and old generated artifact still exist.
3. Redeploy the exact previous artifact rather than reconstructing it from a manifest alone.
4. Re-enable the old listener and check identification with a previously compatible client.
5. Move clients back in controlled batches if their deployed artifact/trust material allows it.

Expect manual remediation when:

- a migrated client carries only the new certificate/signing material;
- the new client emits a type the old server cannot decode;
- a mutex prevents the old and new installed binaries from coexisting as expected;
- a client changed persistence/service behavior;
- the old artifact or private certificate is unavailable.

There is no automatic downgrade protocol and no rollback guarantee in the manifest. Never delete old binaries or `Pulsar.p12` during a migration; loss of the certificate private key orphans clients generated for that trust root.

## 4. Configuration-specific checks

### Web API

Web API enablement, port, and key are server settings and require a process restart. The API is plain HTTP and binds to the configured any-address port; deployment-specific firewall/reverse-proxy/TLS controls are external to this code.

### Admin client

A Release admin client requires the server certificate with its private key for the normal identification signature and message-envelope encryption. The server must also have the matching admin key configured. The admin client performs no arbitrary-message forwarding; it uses the server's whitelisted operation service.

### HVNC and Donut

Keep native artifacts with the server package being tested:

- `HVNCInjection.x64.dll` is required for the Chromium injection paths and is not supplied by a plain managed-only build.
- `donut.exe` is produced/copied only by `ReleaseWithDonut`.

Do not promote a managed build to "native-compatible" merely because it contains the UI or message classes. Validate the x64 native artifact and the target's process/browser environment separately.

## 5. Commands

```powershell
dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net9.0-windows
dotnet run --project tools/webapi-smoke/WebApiSmoke.csproj -c Release
dotnet build Pulsar.AdminClient/Pulsar.AdminClient.csproj -c Debug
dotnet run --project tools/admin-relay-smoke/AdminRelaySmoke.csproj -c Debug
```

Run the framework-specific tests and smoke checks appropriate to the change. These commands do not replace migration, rollback, service, network, or native-runtime validation in the intended environment.
