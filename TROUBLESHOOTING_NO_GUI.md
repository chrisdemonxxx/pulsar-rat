# Troubleshooting: No GUI Appears When Running Pulsar/Client

**Status:** Both executables are deployed and present, but neither showed a GUI.

**Root Causes:** 
- Pulsar.exe (Server) requires .NET 9.0 runtime
- Client.exe (Client) requires .NET 4.8 framework
- Potential missing dependencies or configuration issues

---

## Diagnostic Findings

### ✅ Executables Present
- `TEST_DEPLOYMENT\Server\Pulsar.exe` - **189 KB** ✅
- `TEST_DEPLOYMENT\Client\Client.exe` - **1 MB** ✅

### ✅ Dependencies Present
- `TEST_DEPLOYMENT\Server\MessagePack.dll` ✅
- `TEST_DEPLOYMENT\Server\Pulsar.Common.dll` ✅

### ❓ Runtime Status
- .NET 9.0 Runtime: **Status Unknown** (need verification)
- .NET 4.8 Framework: **Should be available** (desktop framework)

---

## Step 1: Verify .NET Runtimes Are Installed

### For Server (Pulsar.exe - requires .NET 9.0)

```powershell
# Check if .NET 9.0 is installed
dotnet --list-runtimes

# Look for a line like:
# Microsoft.WindowsDesktop 9.0.x [C:\Program Files\dotnet\shared\...]

# If not found, install from:
# https://dotnet.microsoft.com/download/dotnet/9.0
```

### For Client (Client.exe - requires .NET 4.8)

```powershell
# Check if .NET 4.8 is installed
reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Version

# Should show version 4.8.xxxxx
```

---

## Step 2: Verify Source Binaries Exist

Check if binaries were actually built:

```powershell
# Check actual build output
dir "C:\Users\chris\projects\development\pulsar-rat\bin\Release\net9.0-windows\Pulsar.exe"
dir "C:\Users\chris\projects\development\pulsar-rat\bin\Release\net48\Client.exe"

# These should exist and be reasonably sized
```

---

## Step 3: Try Running Directly With Error Output

### Try Server First

```powershell
cd "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server"

# Run with error output
try {
    & .\Pulsar.exe
} catch {
    Write-Host "ERROR: $_"
    Write-Host "Exception: $($_.Exception.Message)"
}

# Alternative - run with dotnet directly
cd "C:\Users\chris\projects\development\pulsar-rat"
dotnet bin\Release\net9.0-windows\Pulsar.exe
```

### Try Client

```powershell
cd "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client"

try {
    & .\Client.exe
} catch {
    Write-Host "ERROR: $_"
}

# Wait a moment to see if any error messages appear
Start-Sleep -Seconds 5
```

---

## Step 4: Check Windows Event Viewer for Crash Logs

```powershell
# Open Event Viewer
eventvwr

# Look in:
# 1. Windows Logs → Application
# 2. Windows Logs → System
# 3. Search for "Pulsar" or ".NET" or "Exception"

# May show:
# - "Application Error - The application failed to initialize properly"
# - ".NET Runtime version X.X.X"
# - Assembly load failures
```

---

## Step 5: Verify Certificate & Configuration

If the binaries are found but fail to load, it might be certificate/configuration related:

```powershell
# Check if certificate is available
Get-ChildItem "Cert:\CurrentUser\My" | Where-Object { $_.Subject -contains "Pulsar" }

# Check TEST_DEPLOYMENT for config files
dir "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\" -Recurse | Select-Object Name

# Look for:
# - *.config files
# - *.json settings
# - Certificate files (.pfx, .p12, .cer)
```

---

## Most Likely Scenarios & Fixes

### Scenario 1: .NET 9.0 Not Installed (Server)

**Symptom:** Pulsar.exe doesn't launch at all

**Fix:**
```powershell
# Option 1: Install from web
# Download from https://dotnet.microsoft.com/download/dotnet/9.0

# Option 2: If installed but not in PATH, run directly
"C:\Program Files\dotnet\dotnet.exe" "C:\Users\chris\projects\development\pulsar-rat\bin\Release\net9.0-windows\Pulsar.dll"
```

### Scenario 2: Windows Desktop Runtime Missing

**Symptom:** "Failed to initialize properly" or ".NET not found"

**Fix:**
```powershell
# Install Windows Desktop Runtime 9.0
# Download: https://dotnet.microsoft.com/download/dotnet/9.0
# Choose: "Windows Desktop Runtime 9.0.x"
```

### Scenario 3: .NET 4.8 Issues (Client)

**Symptom:** Client.exe starts but exits silently

**Fix:**
```powershell
# Install latest .NET 4.8
# Download: https://dotnet.microsoft.com/download/dotnet-framework/net48

# Then try running Client again
cd "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client"
.\Client.exe
```

### Scenario 4: Configuration Files Missing

**Symptom:** Application loads but crashes immediately

**Check:**
```powershell
# Verify these files exist in deployment
Test-Path "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server\appsettings.json"
Test-Path "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server\Pulsar.p12"
Test-Path "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client\appsettings.json"

# If missing, copy from source directory
Copy-Item "C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\*.json" `
  "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server" -Force
Copy-Item "C:\Users\chris\projects\development\pulsar-rat\Pulsar.Client\*.json" `
  "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client" -Force
Copy-Item "C:\Users\chris\projects\development\pulsar-rat\*.p12" `
  "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server" -Force
```

---

## Complete Diagnostic Script

Run this to identify the exact problem:

```powershell
Write-Host "=== PULSAR TROUBLESHOOTING ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check .NET versions
Write-Host "1. Checking .NET Runtime:" -ForegroundColor Yellow
Write-Host "   .NET 9.0 required for Server..." -ForegroundColor Gray
$net9 = $(try { dotnet --list-runtimes | Select-String "9\." } catch { $null })
if ($net9) { Write-Host "   ✅ .NET 9.0 found" -ForegroundColor Green } 
else { Write-Host "   ❌ .NET 9.0 NOT found - Install from https://dotnet.microsoft.com" -ForegroundColor Red }

Write-Host "   .NET 4.8 required for Client..." -ForegroundColor Gray
$net48 = $(try { reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Version 2>$null } catch { $null })
if ($net48) { Write-Host "   ✅ .NET 4.8 found" -ForegroundColor Green } 
else { Write-Host "   ❌ .NET 4.8 NOT found - Install .NET Framework 4.8" -ForegroundColor Red }

# 2. Check binaries
Write-Host ""
Write-Host "2. Checking Binaries:" -ForegroundColor Yellow
$serverExe = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server\Pulsar.exe"
$clientExe = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client\Client.exe"

if (Test-Path $serverExe) { Write-Host "   ✅ Server (Pulsar.exe) found" -ForegroundColor Green } 
else { Write-Host "   ❌ Server (Pulsar.exe) NOT found" -ForegroundColor Red }

if (Test-Path $clientExe) { Write-Host "   ✅ Client (Client.exe) found" -ForegroundColor Green } 
else { Write-Host "   ❌ Client (Client.exe) NOT found" -ForegroundColor Red }

# 3. Check DLLs
Write-Host ""
Write-Host "3. Checking Dependencies:" -ForegroundColor Yellow
$serverDir = Split-Path $serverExe
$dlls = @("MessagePack.dll", "Pulsar.Common.dll")
foreach ($dll in $dlls) {
    $path = Join-Path $serverDir $dll
    if (Test-Path $path) { Write-Host "   ✅ $dll found" -ForegroundColor Green } 
    else { Write-Host "   ❌ $dll NOT found" -ForegroundColor Red }
}

# 4. Try to run server
Write-Host ""
Write-Host "4. Attempting to Run Server:" -ForegroundColor Yellow
Write-Host "   (This will appear in a GUI if successful)" -ForegroundColor Gray
Write-Host ""
Write-Host "   Run this command manually:" -ForegroundColor Cyan
Write-Host "   cd $serverDir" -ForegroundColor Cyan
Write-Host "   .\Pulsar.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "   If nothing appears, check:" -ForegroundColor Yellow
Write-Host "   1. Windows Event Viewer (eventvwr)" -ForegroundColor Yellow
Write-Host "   2. Install .NET 9.0 Desktop Runtime" -ForegroundColor Yellow
Write-Host "   3. Install .NET 4.8 Framework" -ForegroundColor Yellow
Write-Host ""
```

---

## Quick Fix: Install Required Runtimes

### Option 1: Automated Install (Windows Package Manager)

```powershell
# If you have winget installed
winget install Microsoft.DotNet.DesktopRuntime.9
winget install Microsoft.DotNet.Framework.4_8
```

### Option 2: Manual Download & Install

**For .NET 9.0 Desktop Runtime:**
1. Go to https://dotnet.microsoft.com/download/dotnet/9.0
2. Download "Windows Desktop Runtime x64" (or x86 depending on your system)
3. Run the installer
4. Restart command prompt

**For .NET 4.8:**
1. Go to https://dotnet.microsoft.com/download/dotnet-framework/net48
2. Download "Web Installer"
3. Run installer
4. Restart system

### Option 3: Check Existing Installation

```powershell
# See what you have
Write-Host "Installed .NET Runtimes:"
dotnet --info

Write-Host ""
Write-Host ".NET Framework versions:"
Get-ChildItem 'HKLM:\Software\Microsoft\NET Framework Setup\NDP' | Select-Object PSChildName
```

---

## If All Else Fails: Rebuild From Source

```powershell
cd "C:\Users\chris\projects\development\pulsar-rat"

# Clean previous builds
Remove-Item "bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "obj" -Recurse -Force -ErrorAction SilentlyContinue

# Rebuild
dotnet clean
dotnet restore
dotnet build Pulsar.Common/Pulsar.Common.csproj -c Release
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release
dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release

# Re-stage
.\DEPLOY_LOCAL_TEST.ps1

# Then try again
cd .\TEST_DEPLOYMENT\Server
.\Pulsar.exe
```

---

## Expected vs Actual

| What Should Happen | What's Happening | Root Cause |
|-------------------|------------------|-----------|
| Pulsar.exe → WinForms GUI appears | Nothing happens | .NET 9.0 missing or crashed |
| Client.exe → Connects to server | Nothing happens | .NET 4.8 missing or config issue |
| Logs generated | No logs found | Process didn't start |
| Connection confirmed | No connection | Processes didn't launch |

---

## Next Steps

1. **Run the diagnostic script** above
2. **Install missing .NET runtimes** if needed
3. **Try running Pulsar.exe directly** from PowerShell with error capture
4. **Check Windows Event Viewer** for specific error messages
5. **Report findings** so we can troubleshoot further

---

**What to include when reporting:**
- Output from the diagnostic script
- Error messages from Event Viewer
- .NET versions installed (from `dotnet --info`)
- Whether Pulsar.exe/Client.exe appear in Task Manager
- Any error dialogs that appeared

Generated: November 20, 2026
