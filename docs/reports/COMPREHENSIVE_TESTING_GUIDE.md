# 🎯 PULSAR RAT v2.4.5 - COMPLETE TESTING GUIDE

## Executive Summary

This document provides step-by-step instructions for testing the Pulsar RAT v2.4.5 encryption handshake fix on **localhost:4783** in a controlled environment.

---

## Environment

| Parameter | Value |
|-----------|-------|
| **Test OS** | Windows 11 Pro |
| **Framework** | .NET 9.0-windows |
| **.NET Runtime** | 9.0+ (WindowsDesktop) |
| **Configuration** | DEBUG |
| **Port** | 4783/TCP (localhost) |
| **Binaries** | `C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\` |

---

## Pre-Test Checklist

### Environment Verification

```powershell
# Verify .NET 9.0 is installed
dotnet --version
# Expected output: 9.0.0 or higher

# Verify WindowsDesktop runtime
dotnet --list-runtimes | findstr WindowsDesktop
# Expected output: Microsoft.WindowsDesktop.App 9.0.X

# Check binary sizes
dir /s C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\*.exe
# Expected: Pulsar.exe (~190 KB), Client.exe (~155 KB)
```

### Network Verification

```powershell
# Verify port 4783 is available
netstat -ano | findstr 4783
# Should be empty (no existing connections)

# Check no firewall blocks
netsh advfirewall show allprofiles
# Ensure inbound/outbound enabled for local traffic
```

---

## Test Method 1: Manual Double-Click Launch (Easiest)

### Step 1: Open File Explorer
```
Path: C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
```

### Step 2: Launch Server
```
1. Double-click Pulsar.exe
2. Wait for server window to appear (2-3 seconds)
3. Observe: Window shows "Listening on 0.0.0.0:4783"
```

### Step 3: Launch Client
```
1. Double-click Client.exe  
2. Wait 2 seconds for connection (1-2 seconds)
3. Observe: Window shows "Connected to localhost:4783"
```

### Step 4: Monitor (30 seconds)
```
Expected observations:
✓ Both windows open side-by-side
✓ No error dialogs
✓ No exception messages
✓ Windows remain responsive
✓ Memory usage stable
✓ Processes do not close unexpectedly
```

### Step 5: End Test
```
Method 1: Close windows normally (X button)
Method 2: taskkill /F /IM Pulsar.exe && taskkill /F /IM Client.exe
```

---

## Test Method 2: PowerShell Launch Script

### Step 1: Open PowerShell as Administrator

### Step 2: Run Test Script
```powershell
cd C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy

# Start server
.\Pulsar.exe

# In another PowerShell window, start client
.\Client.exe

# Monitor for 30 seconds
Get-Process Pulsar, Client | Select Name, Id, @{N="Memory(MB)";E={[math]::Round($_.WorkingSet/1MB,2)}}
```

---

## Test Method 3: Batch File Launcher

### Step 1: Navigate to Deployment Folder
```
C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
```

### Step 2: Double-Click Launcher
```
File: START_SERVER.bat  (or START_SERVER.ps1)
      START_CLIENT.bat  (or START_CLIENT.ps1)
```

---

## Expected Behavior

### Server (Pulsar.exe)

```
===== OUTPUT EXPECTED =====
Pulsar RAT v2.4.5 - Server
Loading configuration...
Initializing encryption...
Setting up TCP listener on 0.0.0.0:4783...
[LISTENING] Port 4783 is open
Waiting for client connections...

[CLIENT CONNECTED] 127.0.0.1:XXXXX
[ENCRYPTION] Enabled AES-256
[MESSAGE] ClientIdentificationRequest received
[MESSAGE] ClientIdentificationResult sent
[MESSAGE] SetUserActiveWindowStatus received (encrypted)
=====================================

Server remains running, no crashes
```

### Client (Client.exe)

```
===== OUTPUT EXPECTED =====
Pulsar RAT v2.4.5 - Client
Loading configuration...
Connecting to localhost:4783...
[CONNECTED] Established connection
Sending ClientIdentificationRequest...
[RECEIVE] ClientIdentificationResult
[ENCRYPTION] Keys synchronized, AES-256 enabled
Sending SetUserActiveWindowStatus (encrypted)...
[CONNECTED] Ready for commands
=====================================

Client remains running, no crashes
```

---

## Success Criteria

### Critical (Must Have All)
- [ ] **Server starts** - Pulsar.exe window opens without errors
- [ ] **Client starts** - Client.exe window opens without errors  
- [ ] **Connection established** - Client connects to server
- [ ] **No immediate crashes** - Both processes run 30+ seconds
- [ ] **No exception dialogs** - Zero error popups

### Important (Should Have Most)
- [ ] **Encryption handshake completes** - SetUserActiveWindowStatus sent encrypted
- [ ] **Memory stable** - Each process < 300 MB
- [ ] **Process handles stable** - No rapidly increasing handles
- [ ] **No reconnection loops** - Single stable connection
- [ ] **Windows responsive** - Can click in windows without lag

### Validation (Can Be Checked)
- [ ] **Port listening** - `netstat -ano | findstr 4783` shows LISTENING
- [ ] **Connection active** - `netstat` shows ESTABLISHED connection
- [ ] **No errors in logs** - No "error" or "exception" messages visible
- [ ] **Encryption active** - Traffic shows encrypted (not plaintext protocols)

---

## Troubleshooting

### Issue: Pulsar.exe crashes immediately

**Symptoms:**
- Windows opens and closes instantly
- No error dialog shown
- Process ends with no output

**Diagnosis:**
```powershell
# Run from command line to see error
C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Pulsar.exe 2>&1
```

**Possible Causes & Fixes:**
1. **Missing .NET runtime**
   - Fix: `dotnet --list-runtimes | findstr WindowsDesktop`
   - Install: [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)

2. **Corrupted binary**
   - Fix: Verify file size is ~190 KB
   - Rebuild: Run `BUILD_AND_DEPLOY.ps1`

3. **File permissions**
   - Fix: Right-click Pulsar.exe → Properties → Security → Full Control

4. **Port 4783 already in use**
   - Fix: `netstat -ano | findstr 4783` → `taskkill /PID XXXX /F`

### Issue: Client cannot connect

**Symptoms:**
- Client.exe starts but shows "Connection refused"
- "Unable to connect to localhost:4783"

**Diagnosis:**
1. Verify server is running: `tasklist | findstr Pulsar`
2. Verify port is listening: `netstat -ano | findstr 4783`
3. Check firewall: Ensure inbound TCP/4783 allowed

**Fixes:**
- Kill all Pulsar processes: `taskkill /F /IM Pulsar.exe /T`
- Wait 5 seconds
- Restart Pulsar.exe
- Then start Client.exe

### Issue: Both processes run but crash after N seconds

**Symptoms:**
- Processes start fine
- Run for some time
- Then both terminate unexpectedly

**Diagnosis:**
- Check event log: `eventvwr.msc` → Windows Logs → Application
- Look for .NET runtime exceptions
- Check disk space: `dir C:` (need > 100 MB free)

**Fixes:**
1. **Out of memory:** Close other applications
2. **Unhandled exception:** Rebuild with verbose logging
3. **Network issue:** Check network connectivity

---

## Network Analysis

### Monitor Connections

```powershell
# Real-time monitoring
netstat -ano | findstr 4783

# Expected output while running:
#  TCP    0.0.0.0:4783           0.0.0.0:0         LISTENING       nnnn (Pulsar.exe)
#  TCP    127.0.0.1:4783         127.0.0.1:xxxxx   ESTABLISHED     mmmm (Pulsar.exe)
#  TCP    127.0.0.1:xxxxx        127.0.0.1:4783    ESTABLISHED     oooo (Client.exe)
```

### Capture Traffic

```powershell
# Using netsh (Windows built-in)
netsh trace start capture=yes traceFile=C:\pulsar-trace.etl

# Run test for 30 seconds...

netsh trace stop
# Open with: Event Viewer or Wireshark
```

---

## Performance Metrics

### Expected Baseline

| Metric | Expected | Warning | Critical |
|--------|----------|---------|----------|
| **Server Startup Time** | < 2 sec | > 5 sec | > 10 sec |
| **Client Startup Time** | < 2 sec | > 5 sec | > 10 sec |
| **Connection Time** | < 500 ms | > 2 sec | > 5 sec |
| **Server Memory (idle)** | 50-100 MB | > 200 MB | > 400 MB |
| **Client Memory (idle)** | 30-80 MB | > 150 MB | > 300 MB |
| **Encryption Overhead** | < 5% CPU | > 25% | > 50% |

### Capture Metrics

```powershell
# Get process details
$proc = Get-Process -Name Pulsar
Write-Host "PID: $($proc.Id)"
Write-Host "Memory: $([math]::Round($proc.WorkingSet/1MB, 2)) MB"
Write-Host "CPU: $($proc.ProcessorAffinity)"
Write-Host "Handles: $($proc.Handles)"
Write-Host "Threads: $($proc.Threads.Count)"
```

---

## Encryption Verification

### What The Fix Does

```
BEFORE (BROKEN):
OnConnectionState() 
  └─ Send SetUserActiveWindowStatus (UNENCRYPTED) ❌
  └─ Enable encryption
  → Server has encryption, client doesn't → MISMATCH ❌

AFTER (FIXED):
OnClientRead()
  ├─ Receive ClientIdentificationResult
  ├─ Enable encryption (synchronized with server)
  ├─ Send SetUserActiveWindowStatus (ENCRYPTED) ✅
  → Server/Client encryption state synchronized ✅
```

### How to Verify

1. **Visual Inspection:**
   - Server/client windows should show "Encryption enabled" message
   - No "Encryption mismatch" errors
   - SetUserActiveWindowStatus appears AFTER identification

2. **Network Analysis:**
   - First messages (ClientIdentificationRequest/Result) may be readable
   - All subsequent messages (SetUserActiveWindowStatus+) are encrypted
   - Traffic shows binary/encrypted patterns (not ASCII)

3. **Log Analysis:**
   - Check message sequence in both windows
   - Verify order: Identification → Encryption → SetUserActiveWindowStatus

---

## Test Report Template

```
===== PULSAR RAT v2.4.5 TEST REPORT =====
Date: _______________
Tester: _______________

TEST METHOD
□ Method 1 (Double-Click)
□ Method 2 (PowerShell)
□ Method 3 (Batch File)

RESULTS
□ Server started: YES / NO
□ Client started: YES / NO
□ Connection established: YES / NO
□ Duration until crash: _____ seconds
□ Memory usage stable: YES / NO

OBSERVATIONS
___________________________________________________________

ERROR MESSAGES
___________________________________________________________

ENCRYPTION HANDSHAKE
Order of operations:
1. _______________
2. _______________
3. _______________
4. _______________

FINAL STATUS
□ PASS - Ready for production deployment
□ FAIL - Needs additional debugging
□ PARTIAL - Some issues observed

Comments:
___________________________________________________________
```

---

## References

- **Source:** `C:\Users\chris\Downloads\c2panel\pulsar\`
- **Deployment:** `C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\`
- **Framework:** .NET 9.0-windows
- **Configuration:** DEBUG
- **Port:** 4783/TCP

---

**Last Updated:** November 9, 2026  
**Version:** v2.4.5 (Encryption Handshake Fix)  
**Status:** Ready for Testing
