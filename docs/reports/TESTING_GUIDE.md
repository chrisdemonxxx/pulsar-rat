# Pulsar RAT v2.4.5 - DEPLOYMENT & TESTING GUIDE

## 📦 DEPLOYMENT LOCATION
```
C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\
├── Pulsar.exe          (Server - 185.5 KB)
├── Client.exe          (Client - 154 KB)
├── START_SERVER.ps1    (Server launcher)
├── START_CLIENT.ps1    (Client launcher)
└── *.dll               (Dependencies)
```

## 🚀 QUICK START (Side-by-side testing)

### Terminal 1 - START SERVER:
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\START_SERVER.ps1"
```

**Expected Output:**
```
╔════════════════════════════════════════╗
║  Pulsar RAT v2.4.5 - SERVER LAUNCHER  ║
╚════════════════════════════════════════╝

[*] Starting Pulsar Server...
[*] Executable: C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Pulsar.exe
[*] Listening on: localhost:4783
[*] Configuration: net9.0-windows, DEBUG mode, DummyCertificate

═════════════════════════════════════════
[+] Server started. Waiting for client connection...
═════════════════════════════════════════
```

### Terminal 2 - START CLIENT (after server is running):
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\START_CLIENT.ps1"
```

**Expected Output:**
```
╔════════════════════════════════════════╗
║  Pulsar RAT v2.4.5 - CLIENT LAUNCHER  ║
╚════════════════════════════════════════╝

[*] Starting Pulsar Client...
[*] Executable: C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Client.exe
[*] Connecting to: localhost:4783
[*] Configuration: net9.0-windows, DEBUG mode, Hash verification DISABLED

═════════════════════════════════════════
[+] Client started. Attempting connection...
═════════════════════════════════════════

[*] Encryption Handshake:
  1. Connecting to server...
  2. Receiving ClientIdentificationResult...
  3. Sending encrypted SetUserActiveWindowStatus...
  4. All subsequent traffic encrypted
```

## ✅ WHAT TO TEST

### Test 1: Connection Establishment
```
[✓] Server starts without errors
[✓] Client starts without errors
[✓] Both processes remain running for 5+ seconds
[✓] No immediate crashes or exceptions
```

### Test 2: Encryption Handshake Sequence
Server logs should show:
```
[Connection established]
[ClientIdentificationResult sent]
[Encryption enabled: _encryptTraffic = true]
```

Client logs should show:
```
[Connecting to localhost:4783]
[Connection successful]
[Identification complete]
[SetUserActiveWindowStatus sent (ENCRYPTED)]
```

### Test 3: Stability Under Load
```
[✓] Client window updates smoothly
[✓] Server shows connected clients
[✓] No disconnects after 30+ seconds
[✓] No memory leaks (watch Process Explorer)
```

### Test 4: Message Encryption
Traffic should show:
```
[✓] Initial packets: ClientIdentificationRequest (unencrypted)
[✓] Server response: ClientIdentificationResult (unencrypted)
[✓] All subsequent packets: AES-encrypted
[✓] No unencrypted data after identification
```

## 🔍 DIAGNOSTICS

### Check Process Status:
```powershell
Get-Process -Name Pulsar, Client -ErrorAction SilentlyContinue | Select-Object Name, Id, WorkingSet
```

### Monitor Network Connections:
```powershell
netstat -ano | Select-String 4783
```

### View Server Logs:
```powershell
# Logs will appear in server window during runtime
```

### Check Binary Details:
```powershell
Get-Item "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Pulsar.exe" | Select-Object FullName, Length, LastWriteTime
Get-Item "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Client.exe" | Select-Object FullName, Length, LastWriteTime
```

## 🛠️ TROUBLESHOOTING

### Issue: "Address already in use"
**Cause:** Server already running
**Fix:** 
```powershell
Get-Process -Name Pulsar -ErrorAction SilentlyContinue | Stop-Process -Force
# Wait 2 seconds
```

### Issue: "Connection refused"
**Cause:** Server not listening on port 4783
**Fix:**
1. Ensure server started successfully
2. Check if port 4783 is blocked by firewall
3. Verify no other process is using port 4783

### Issue: Immediate crash
**Cause:** Missing dependencies or framework version mismatch
**Fix:**
```powershell
dotnet --version           # Should be 10.0.400
dotnet --list-runtimes     # Should show Microsoft.WindowsDesktop.App 9.0.20+
```

### Issue: Encryption handshake fails
**Cause:** SetUserActiveWindowStatus sent before identification
**Fix:** Already fixed in this build! Rebuild if issue persists:
```powershell
cd "C:\Users\chris\Downloads\c2panel\pulsar"
dotnet clean
dotnet build Pulsar.Server\Pulsar.Server.csproj -c Debug
dotnet build Pulsar.Client\Pulsar.Client.csproj -c Debug
```

## 📊 PERFORMANCE BASELINE

| Metric | Expected | Warning |
|--------|----------|---------|
| Server startup time | < 2 sec | > 5 sec |
| Client startup time | < 2 sec | > 5 sec |
| Connection time | < 1 sec | > 3 sec |
| Handshake duration | < 0.5 sec | > 2 sec |
| Server memory (idle) | 50-150 MB | > 300 MB |
| Client memory (idle) | 30-100 MB | > 200 MB |

## 🔐 SECURITY CONFIGURATION (DEBUG MODE)

⚠️ **DEBUG MODE ONLY - NOT FOR PRODUCTION**

Current Settings:
```csharp
// Client
const bool SKIP_HASH_VERIFICATION = true;  // ← Unsafe!

// Server
DummyCertificate                            // ← Self-signed
_encryptTraffic = true                      // ← Encryption enabled
AES Encryption                              // ← Algorithm
Default Key Derivation                      // ← KDF
```

For Production:
```csharp
// Client
const bool SKIP_HASH_VERIFICATION = false; // ← Enable verification
// Implement proper certificate validation

// Server
Use proper X.509 certificate               // ← Production cert
Enable code obfuscation                     // ← Hide implementation
Strong key derivation (PBKDF2)             // ← Better KDF
Release build only                          // ← Remove debug symbols
```

## 📝 LOGGING & DEBUGGING

Both server and client output debug information. Key events to watch:

### Server (watch for):
```
[INFO] Listening on port 4783
[DEBUG] Client connected from [IP]:[Port]
[DEBUG] Identification request received
[DEBUG] Encryption enabled for client
[DEBUG] Message received: [Type]
[INFO] Client disconnected
```

### Client (watch for):
```
[DEBUG] Connecting to localhost:4783...
[DEBUG] Connection established
[DEBUG] Identification sent
[DEBUG] Identification result received
[DEBUG] Encryption enabled
[DEBUG] SetUserActiveWindowStatus sent (encrypted)
[DEBUG] Message sent: [Type]
```

## 🎯 SUCCESS CRITERIA

✅ **Test passes if:**
1. Both server and client start without crashes
2. Connection established and stable for 30+ seconds
3. All packets after identification are encrypted
4. SetUserActiveWindowStatus arrives AFTER ClientIdentificationResult
5. No error messages in either console

✅ **Encryption handshake verified if:**
1. Server sends ClientIdentificationResult
2. Client receives and processes result
3. Client sends encrypted SetUserActiveWindowStatus
4. All subsequent messages encrypted
5. No connection drops during handshake

## 📞 SUPPORT

For issues:
1. Check diagnostics section above
2. Review troubleshooting guide
3. Verify .NET 9.0 runtime installed: `dotnet --list-runtimes`
4. Check binaries exist: `ls C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\*.exe`
5. Review source code changes in Git history

---

**BUILD INFO**
- Framework: .NET 9.0-windows
- Configuration: DEBUG
- Build Date: November 9, 2026
- Status: ✅ Ready for Testing
