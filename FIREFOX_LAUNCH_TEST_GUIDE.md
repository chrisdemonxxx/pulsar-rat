# Firefox HVNC Launch Test - Execution Guide

**Current Status**: Build complete, deployment complete, processes running  
**Objective**: Test Firefox launch with new exception handling in place

---

## Pre-Test Checklist

- [x] Pulsar Server running (PID 51208)
- [x] Client connected (PID 52512) 
- [x] Firefox installed (153.7.0)
- [x] Exception handling code deployed
- [x] Network connection verified (127.0.0.1:62843 -> 4783)

---

## Test Option 1: Interactive Server UI Test (Recommended)

### Prerequisites
- Server process must have HVNC form open
- Client must be connected and listed

### Test Steps

1. **Locate Server Process Window**
   - Find "Pulsar Server" window (should be running)
   - If minimized, click taskbar to bring to front

2. **Find Connected Client**
   - In the clients list, locate the connected client
   - Should show as "Client" or the configured name
   - Status should be "Connected" or similar

3. **Access HVNC Menu**
   - Right-click on the connected client
   - Or select the client and look for "HVNC" menu option
   - Or look for "Start Firefox" button/menu item

4. **Launch Firefox**
   - Click "HVNC → Start Firefox" (or equivalent menu)
   - **Expected Before Fix**: Client panel crash with "Object reference not set" error
   - **Expected After Fix**: Either:
     - Firefox launches on HVNC desktop (success)
     - Error dialog shows: "Firefox launch failed: [specific error]" (graceful failure)
     - Status updates shown in Server UI

5. **Capture Outcome**
   - Screenshot the result
   - Note exact error message if any
   - Check if Server stays responsive (not crashed)

### Expected Errors & Meanings

If Firefox launch fails (after fix), error message will indicate:

```
"Firefox launch failed: Firefox executable not found"
  → Firefox not installed at expected location
  
"Firefox launch failed: DLL bytes are empty or null"  
  → HVNC DLL bytes were not loaded properly
  
"Firefox launch failed: Access to the path is denied"
  → Permission issue accessing Firefox executable
  
"Firefox launch failed: The reflective DLL injection failed"
  → KDOTInjector couldn't inject into the process
```

---

## Test Option 2: Process Monitor Approach

If interactive test not possible, use this diagnostic:

```powershell
# 1. Clear any existing Firefox processes
Get-Process firefox -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Establish baseline
Write-Host "Baseline Firefox processes:"
Get-Process firefox -ErrorAction SilentlyContinue | Measure-Object

# 3. [Execute Firefox launch via Server UI here]

# 4. Check for new processes (wait 2-5 seconds after clicking)
Start-Sleep -Seconds 3
Write-Host "`nAfter launch attempt:"
Get-Process firefox -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, WorkingSet, StartTime

# 5. Analyze
#  - New Firefox process = success
#  - No new process + no crash = error was caught and reported
#  - Crash = exception handling not working
```

---

## Test Option 3: Simulated Launch (For Automated Testing)

Create a test client that sends the launch message:

```csharp
// Pseudo-code: Send StartHVNCProcess message to connected client
var message = new StartHVNCProcess
{
    Path = "Mozilla",  // Triggers Firefox launch
    DontCloneProfile = false,
    SessionId = Guid.NewGuid().ToString("N"),
    LaunchId = Guid.NewGuid().ToString("N")
};

// Send to Client - would need to implement via Server's connection handler
server.SendToClient(clientId, message);

// Monitor for response
// - HVNCProcessStatus with stage "Completed" = success
// - HVNCProcessStatus with stage "Failed" = error was caught
// - No response + crash = exception handling failed
```

---

## Failure Investigation

If test fails, follow this diagnostic tree:

### Scenario A: "Panel Crashed" (Exception not caught)
- **Diagnosis**: Exception handling code not in deployment
- **Fix**: 
  - Verify Client.exe file size (should be 994 KB)
  - Check modification time (should be recent)
  - Rebuild and redeploy Client
- **Command**: `dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release`

### Scenario B: "Error Message Displayed" (Exception caught correctly)
- **Diagnosis**: Firefox launch is failing for a reason (see error text)
- **Action**: Read error message and address specific issue
  - If "Firefox not found": Verify installation
  - If "DLL bytes empty": Check HVNC native component
  - If "injection failed": Debug KDOTInjector logic

### Scenario C: "Firefox Launched (Black Screen)" (Process created but no rendering)
- **Diagnosis**: Firefox is running but not rendering on HVNC desktop
- **Investigation**: 
  - Is Firefox actually running? `Get-Process firefox`
  - Is it on the right desktop?
  - Display initialization issue in HVNC driver?
- **Action**: Separate investigation (not related to this fix)

### Scenario D: "No Observable Change"
- **Diagnosis**: May not be able to see HVNC desktop from main desktop
- **Solution**: Use Process Monitor approach (Option 2) instead
  - Check for Firefox process creation
  - Verify Server logs for messages
  - Inspect Client debug output

---

## Success Validation

Test passes if **any** of these are true:

1. ✅ **Firefox launches successfully** on HVNC desktop
   - Process appears: `Get-Process firefox` shows running process
   - Browser window visible on HVNC desktop
   - Keyboard/mouse injection works

2. ✅ **Error is reported gracefully** (instead of panel crash)
   - User sees error dialog with specific message
   - Server remains responsive
   - Client stays connected

3. ✅ **Status updates are sent**
   - Server receives HVNCProcessStatus messages
   - Shows "Preparing Firefox profile" then either "Completed" or "Failed"
   - Timing indicates proper async execution

---

## Collecting Debug Output

If manual test doesn't work, enable debug output:

```powershell
# Stop current processes
Get-Process Pulsar, Client | Stop-Process -Force

# Set environment variable for enhanced logging
$env:PULSAR_DEBUG_ENFORCE_ENCRYPTION = "1"
$env:PULSAR_DEBUG_LOGGING = "1"

# Restart Server and Client
# ... start processes ...

# After Firefox launch attempt, check log
Get-Content "$env:TEMP\pulsar_*.log" | Select-Object -Last 50
```

---

## Expected vs Actual Comparison

| Aspect | Before Fix | After Fix |
|--------|-----------|-----------|
| **On Click** | Immediate crash | Message sent to client |
| **Client Receives** | N/A (crashed) | StartHVNCProcess message |
| **Exception Thrown** | Yes | Yes (but caught) |
| **User Sees** | "Object reference not set" panel crash | Error message in Server UI |
| **Server Responsive** | No (crash ripples) | Yes (error handled locally) |
| **Debug Output** | None | Detailed exception info |
| **Firefox Runs** | N/A | Yes (if no other errors) |

---

## Documentation

After completing test:

1. **Document Result**: Create `FIREFOX_LAUNCH_TEST_RESULT.md`
   - Include date/time of test
   - Exact outcome (success/specific error)
   - Screenshot if possible
   - Any anomalies observed

2. **Update Status**: 
   - If SUCCESS: Mark HVNC Firefox support as working
   - If ERROR: Document the specific error for follow-up
   - If CRASH: Revert and investigate

3. **Next Phase**:
   - Test Chrome launch (similar code path)
   - Test Process execution through HVNC
   - Test keyboard/mouse injection if Firefox runs

---

## Troubleshooting Quick Reference

```bash
# Is Server running?
Get-Process Pulsar

# Is Client running?  
Get-Process Client

# Are they connected?
netstat -ano | Select-String ":4783" | Select-String "ESTABLISHED"

# Is Firefox installed?
Test-Path "$env:PROGRAMFILES\Mozilla Firefox\firefox.exe"

# Did binary get updated?
(Get-Item "TEST_DEPLOYMENT\Client\Client.exe").LastWriteTime

# Check for Firefox process after launch
Get-Process firefox -ErrorAction SilentlyContinue | Select-Object Id, Name, StartTime
```

---

## Next Steps After Success

1. Test Chrome/Edge/Brave launchers (same exception handling pattern)
2. Test other HVNC processes (Cmd, PowerShell, Explorer)
3. Test keyboard injection into Firefox
4. Test mouse injection into Firefox
5. Performance testing with multiple browser instances

---

**Ready to Execute Test**: ✅ YES

Recommend: **Start with Option 1 (Interactive UI Test)** for best visibility of behavior change.
