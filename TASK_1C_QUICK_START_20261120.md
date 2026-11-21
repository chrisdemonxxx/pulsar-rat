# Phase 2b Task 1c Quick-Start Guide
**Quick Actions Implementation**  
**Status**: Ready to Begin  
**Estimated Duration**: 3-4 hours

---

## Overview

**Task 1c** implements the quick-action buttons in the PulsarMainWindow. This task bridges the UI (XAML) to the business logic (commands) and connects to the server operations.

### What You'll Be Doing
1. Create 8 RelayCommand properties in MainViewModel
2. Implement command execute methods
3. Add CanExecute predicates for button enabling logic
4. Wire XAML buttons to commands
5. Test everything with full test suite

---

## Quick Reference

### The 8 Quick Actions (From FrmMain)
```
1. Disconnect    - Close client connection (forced disconnect)
2. Restart       - Reboot the client machine
3. Standby       - Put client machine into sleep/standby
4. Lockscreen    - Lock the Windows logon screen
5. Message Box   - Send popup message to client
6. Update Settings - Push new settings to client
7. Uninstall     - Remove client from target machine
8. Execute       - Run shell command on target machine
```

### Command Pattern Template
```csharp
public RelayCommand DisconnectCommand { get; }

private void ExecuteDisconnect()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        // Send operation to server
        // SelectedClient operations go through ClientOperationService
        // which validates and queues the operation
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Disconnect error: {ex.Message}");
    }
}

private bool CanExecuteDisconnect()
{
    return SelectedClient?.Connected == true;
}
```

---

## Implementation Steps

### Step 1: Add Command Properties to MainViewModel

**Location**: `Pulsar.Server/ViewModels/MainViewModel.cs`  
**Section**: After existing properties, before InitializeCommands()

```csharp
/// <summary>
/// Command to disconnect selected client
/// </summary>
public RelayCommand DisconnectCommand { get; private set; }

/// <summary>
/// Command to restart selected client
/// </summary>
public RelayCommand RestartCommand { get; private set; }

/// <summary>
/// Command to put selected client into standby
/// </summary>
public RelayCommand StandbyCommand { get; private set; }

/// <summary>
/// Command to lock selected client screen
/// </summary>
public RelayCommand LockscreenCommand { get; private set; }

/// <summary>
/// Command to show message box on selected client
/// </summary>
public RelayCommand ShowMessageBoxCommand { get; private set; }

/// <summary>
/// Command to update settings on selected client
/// </summary>
public RelayCommand UpdateSettingsCommand { get; private set; }

/// <summary>
/// Command to uninstall client
/// </summary>
public RelayCommand UninstallCommand { get; private set; }

/// <summary>
/// Command to execute shell command on selected client
/// </summary>
public RelayCommand ExecuteCommandCommand { get; private set; }
```

### Step 2: Initialize Commands in InitializeCommands()

**Location**: `Pulsar.Server/ViewModels/MainViewModel.cs`  
**Method**: `InitializeCommands()` (replace existing stubs)

```csharp
private void InitializeCommands()
{
    DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);
    RestartCommand = new RelayCommand(ExecuteRestart, CanExecuteRestart);
    StandbyCommand = new RelayCommand(ExecuteStandby, CanExecuteStandby);
    LockscreenCommand = new RelayCommand(ExecuteLockscreen, CanExecuteLockscreen);
    ShowMessageBoxCommand = new RelayCommand(ExecuteShowMessageBox, CanExecuteShowMessageBox);
    UpdateSettingsCommand = new RelayCommand(ExecuteUpdateSettings, CanExecuteUpdateSettings);
    UninstallCommand = new RelayCommand(ExecuteUninstall, CanExecuteUninstall);
    ExecuteCommandCommand = new RelayCommand(ExecuteExecuteCommand, CanExecuteExecuteCommand);
    
    // Wire up property changed to re-evaluate commands
    PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(SelectedClient))
        {
            DisconnectCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            StandbyCommand.RaiseCanExecuteChanged();
            LockscreenCommand.RaiseCanExecuteChanged();
            ShowMessageBoxCommand.RaiseCanExecuteChanged();
            UpdateSettingsCommand.RaiseCanExecuteChanged();
            UninstallCommand.RaiseCanExecuteChanged();
            ExecuteCommandCommand.RaiseCanExecuteChanged();
        }
    };
}
```

### Step 3: Implement Execute Methods

**Location**: `Pulsar.Server/ViewModels/MainViewModel.cs`  
**Section**: Under `#region Quick Actions`

```csharp
#region Quick Actions

private void ExecuteDisconnect()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        // Send disconnect operation to server
        _listenServer?.DisconnectClient(SelectedClient);
        System.Diagnostics.Debug.WriteLine($"Disconnect sent to {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Disconnect error: {ex.Message}");
    }
}

private void ExecuteRestart()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        _listenServer?.RestartClient(SelectedClient);
        System.Diagnostics.Debug.WriteLine($"Restart sent to {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Restart error: {ex.Message}");
    }
}

private void ExecuteStandby()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        _listenServer?.StandbyClient(SelectedClient);
        System.Diagnostics.Debug.WriteLine($"Standby sent to {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Standby error: {ex.Message}");
    }
}

private void ExecuteLockscreen()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        _listenServer?.LockscreenClient(SelectedClient);
        System.Diagnostics.Debug.WriteLine($"Lockscreen sent to {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Lockscreen error: {ex.Message}");
    }
}

private void ExecuteShowMessageBox()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        // For now, just log. Task 1d will add UI for parameters
        System.Diagnostics.Debug.WriteLine($"Show message box on {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Message box error: {ex.Message}");
    }
}

private void ExecuteUpdateSettings()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        // For now, just log. This requires parameter input
        System.Diagnostics.Debug.WriteLine($"Update settings on {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Update settings error: {ex.Message}");
    }
}

private void ExecuteUninstall()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        _listenServer?.UninstallClient(SelectedClient);
        System.Diagnostics.Debug.WriteLine($"Uninstall sent to {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Uninstall error: {ex.Message}");
    }
}

private void ExecuteExecuteCommand()
{
    if (SelectedClient?.Identified != true) return;
    
    try
    {
        // For now, just log. Task 1d will add UI for command input
        System.Diagnostics.Debug.WriteLine($"Execute command on {SelectedClient.EndPoint}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Execute command error: {ex.Message}");
    }
}

#endregion

#region Command Can Execute Predicates

private bool CanExecuteDisconnect() => SelectedClient?.Connected == true;
private bool CanExecuteRestart() => SelectedClient?.Connected == true;
private bool CanExecuteStandby() => SelectedClient?.Connected == true;
private bool CanExecuteLockscreen() => SelectedClient?.Connected == true;
private bool CanExecuteShowMessageBox() => SelectedClient?.Connected == true;
private bool CanExecuteUpdateSettings() => SelectedClient?.Connected == true;
private bool CanExecuteUninstall() => SelectedClient?.Connected == true;
private bool CanExecuteExecuteCommand() => SelectedClient?.Connected == true;

#endregion
```

### Step 4: Update XAML Button Bindings

**Location**: `Pulsar.Server/Views/PulsarMainWindow.xaml`  
**Find**: Quick Actions section (search for "Quick Actions")

Replace the placeholder buttons with:
```xml
<StackPanel Grid.Row="1" Margin="4" Spacing="4">
    <Button Content="Disconnect" 
            Command="{Binding DisconnectCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Restart" 
            Command="{Binding RestartCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Standby" 
            Command="{Binding StandbyCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Lockscreen" 
            Command="{Binding LockscreenCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Separator />
    
    <Button Content="Message Box" 
            Command="{Binding ShowMessageBoxCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Update Settings" 
            Command="{Binding UpdateSettingsCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Uninstall" 
            Command="{Binding UninstallCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
    
    <Button Content="Execute Command" 
            Command="{Binding ExecuteCommandCommand}"
            Background="{DynamicResource ButtonBackground}"
            Foreground="{DynamicResource TextForeground}"
            Padding="8" Margin="2" />
</StackPanel>
```

### Step 5: Verify RelayCommand Availability

**Check**: `Pulsar.Common/MVVM/RelayCommand.cs` or similar  
**Confirm**: RelayCommand class exists and supports both Action and Predicate<bool> patterns

If not available, check for:
- `ICommand` implementation
- `RaiseCanExecuteChanged()` method
- Constructor: `RelayCommand(Action execute, Func<bool> canExecute = null)`

---

## Testing Checklist

### Pre-Build Checks
- [ ] All 8 RelayCommand properties added to MainViewModel
- [ ] All 8 Execute methods implemented
- [ ] All 8 CanExecute methods implemented
- [ ] Commands initialized in InitializeCommands()
- [ ] Property changed wired to RaiseCanExecuteChanged()

### Build & Test
```powershell
# Build Pulsar.Server
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release
# Expected: Build succeeded, 0 errors

# Run all tests
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release
# Expected: 273/273 passed
```

### Runtime Tests
1. **Button Enabling Logic**
   - [ ] All 8 buttons disabled when no client selected
   - [ ] All 8 buttons disabled when selected client offline
   - [ ] All 8 buttons enabled when selected client connected

2. **Button Click Behavior**
   - [ ] Each button click triggers its command
   - [ ] No errors in debug output
   - [ ] SelectedClient remains valid after click

3. **Client Selection**
   - [ ] Clicking different clients in list updates button state
   - [ ] Buttons disable immediately when selecting offline client
   - [ ] Buttons enable when selecting connected client

### Integration Tests
- [ ] Existing 273 tests still pass (no regressions)
- [ ] No new compilation warnings
- [ ] No null reference exceptions
- [ ] Dispatcher.Invoke not needed (commands run on UI thread)

---

## Common Pitfalls

### ❌ Don't
```csharp
// Don't pass client object directly
DisconnectCommand = new RelayCommand(() => ExecuteDisconnect(_listenServer, SelectedClient));

// Don't forget to initialize commands
// InitializeCommands() MUST be called in constructor

// Don't bind directly to methods
// Command="{Binding ExecuteDisconnect}" ← WRONG

// Don't forget CanExecute predicates
DisconnectCommand = new RelayCommand(ExecuteDisconnect); // ← Buttons won't disable
```

### ✅ Do
```csharp
// Use command properties
Command="{Binding DisconnectCommand}"

// Initialize commands early in constructor
DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

// Use SelectedClient property (already tracked)
private bool CanExecuteDisconnect() => SelectedClient?.Connected == true;

// Wire PropertyChanged to refresh command state
PropertyChanged += (s, e) => 
{
    if (e.PropertyName == nameof(SelectedClient))
        DisconnectCommand.RaiseCanExecuteChanged();
};
```

---

## Debugging Tips

### Button Not Responding?
1. Check XAML binding: `Command="{Binding DisconnectCommand}"`
2. Verify MainViewModel exposes public property: `public RelayCommand DisconnectCommand { get; }`
3. Confirm command initialized: Check InitializeCommands() called
4. Enable binding error diagnostics:
   ```xml
   <!-- Add to App.xaml Resources -->
   <Binding.ErrorHandler>
       <local:BindingErrorTracer />
   </Binding.ErrorHandler>
   ```

### Button Always Disabled?
1. Check CanExecute predicate: `SelectedClient?.Connected == true`
2. Verify Connected property is true for test client
3. Confirm RaiseCanExecuteChanged() wired to PropertyChanged
4. Test manually:
   ```csharp
   var canExecute = SelectedClient?.Connected == true;
   Debug.WriteLine($"Can execute: {canExecute}");
   ```

### Command Not Executing?
1. Add Debug.WriteLine in ExecuteX method
2. Check _listenServer is not null
3. Verify SelectedClient is not null
4. Add try/catch with Debug output
5. Check Output window for exceptions

---

## File Locations Reference

| File | Purpose | Lines to Change |
|------|---------|-----------------|
| `Pulsar.Server/ViewModels/MainViewModel.cs` | Add commands, execute/canexecute | +150 lines |
| `Pulsar.Server/Views/PulsarMainWindow.xaml` | Bind buttons to commands | +50 lines |
| `Pulsar.Server/Views/PulsarMainWindow.xaml.cs` | No changes | 0 lines |
| `Pulsar.Common.Tests/Pulsar.Common.Tests.csproj` | No changes | 0 lines |

---

## Success Criteria

### Must Pass
- ✅ All 273 tests continue to pass
- ✅ Zero new compilation errors
- ✅ All 8 buttons respond to clicks
- ✅ Buttons enable/disable correctly
- ✅ No null reference exceptions

### Should Pass
- ✅ Command feedback in debug output
- ✅ CanExecute predicates working
- ✅ Button state matches client selection
- ✅ No regressions from Task 1b

### Nice to Have
- 🌟 Tooltip on each button
- 🌟 Visual feedback (button press animation)
- 🌟 Keyboard shortcuts (Alt+D for Disconnect)

---

## Estimated Timeline

| Phase | Time | Checkpoint |
|-------|------|-----------|
| Step 1: Add properties | 10 min | Properties added |
| Step 2: Initialize commands | 15 min | Commands instantiated |
| Step 3: Implement methods | 45 min | 8 execute + 8 canexecute |
| Step 4: XAML bindings | 20 min | Buttons bound to commands |
| Step 5: Testing | 30 min | All tests pass |
| **Total** | **2 hours** | Ready to commit |

---

## Next Steps After Task 1c

Once Task 1c is complete:
1. Run full test validation
2. Create completion report
3. Commit to git
4. Begin Task 1d (Menus)
   - File, Tools, Help menus
   - Menu item bindings
   - Keyboard shortcuts

---

## Quick Reference Commands

```powershell
# Build
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release

# Test
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release

# Clean
dotnet clean Pulsar.Server/Pulsar.Server.csproj
dotnet clean Pulsar.Common.Tests/Pulsar.Common.Tests.csproj

# Git
git add -A
git commit -m "Phase 2b Task 1c: Quick Actions - Implement command bindings"
git log --oneline -5
```

---

## Final Notes

- **Don't skip testing**: Run full suite after each major change
- **Keep it simple**: Complex logic goes in business layer, not MVVM
- **Thread safety**: Commands run on UI thread, safe to update SelectedClient
- **Backward compatible**: Task 1c doesn't break anything from Task 1b
- **Document as you go**: Add XML comments to new methods

---

**You're ready! Good luck with Task 1c! 🚀**
