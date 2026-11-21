# Deferred Assemblies

This folder is populated automatically by building `Pulsar.Client` (msbuild target
`PrepareDeferredAssemblies` in `Pulsar.Client.csproj`). The build copies the following
dependencies here and removes them from the client output:

- `SharpDX*.dll` (remote desktop / virtual monitor rendering)
- `AForge*.dll` (webcam capture)
- `NAudio*.dll` (microphone/audio capture and playback)
- `Gma.System.MouseKeyHook.dll` (keylogger / active window tracking)

The contents are served to clients over the wire on identify
(`RequestDeferredAssemblies` → `DeferredAssembliesPackage`, see
`Pulsar.Server/Utilities/DeferredAssemblyProvider.cs` and
`Pulsar.Server/Messages/DeferredAssemblyHandler.cs`). Clients cache them under their
data directory and resolve them via `AppDomain.AssemblyResolve`
(`Pulsar.Client/Utilities/DeferredAssemblyManager.cs`).

Do not commit the DLLs (gitignored). If the folder is empty, build the client first —
otherwise remote desktop, webcam, audio and keylogging features will lack their
dependencies on the client side.
