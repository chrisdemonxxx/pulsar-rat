# GUI Modernization Strategy - Phase 2
## Pulsar RAT Operator Console
**Date**: 2026-11-20  
**Status**: PLANNING (Build Verification Complete)  
**Objective**: Migrate from WinForms/WPF hybrid to consistent WPF architecture

---

## Executive Summary

The current Pulsar operator console uses a **mixed architecture**:
- **Primary**: WinForms (68 files, legacy Windows.Forms)
- **Secondary**: WPF (5 controls embedded via ElementHost)
- **Issue**: Fragmented codebase with duplicated styling, event handling, and data binding

**Modernization Goal**: Unified WPF architecture with MVVM pattern, modern rendering pipeline, and consistent styling.

**Benefits**:
✅ Single rendering model (DirectX via WPF)  
✅ MVVM-compatible throughout  
✅ Modern data binding (INotifyPropertyChanged)  
✅ Easier theming (Attached behaviors vs WinForms properties)  
✅ Future-proof for .NET 10+ (WinForms is legacy)  
✅ Reduced codebase complexity (~40% fewer files)

---

## Current Architecture Analysis

### WinForms Components (68 files)

#### Main Container
- **FrmMain.cs** (Primary operator window)
  - TabControl with 10+ tabs
  - Hosts 3 ElementHost controls for WPF views
  - Menu bar, toolbar, status bar
  - Client tree view (tree-based navigation)
  - 1,500+ lines of imperative code

#### Modal Dialogs (20+ forms)
- FrmConnections (client list management)
- FrmBuilder (client stub generation)
- FrmSettings (application configuration)
- FrmHVNC (remote desktop session)
- FrmRemote* (feature windows: Chat, Shell, Webcam, etc.)
- FrmRegistry*, FrmTask* (specialized editors)

#### Specialized Components
- **DarkMode/** (custom theme manager)
  - DarkModeManager.cs (manual property setting)
  - DarkMode.cs (color definitions)
  - **Issue**: WinForms uses Color properties; not composable
- **HVNCPopUp/** (floating window for HVNC)
- **RemoteDesktopPopUp/** (monitor selection dialog)
- **ScreenCaptureHider/** (privacy layer for desktop capture)

### WPF Components (5 controls + supporting code)

#### Controls in `Controls/Wpf/`
- **StatsView.xaml** + StatsViewModel
  - Charts and statistics display
  - Uses LiveChartsCore for graphing
  
- **ClientsListView.xaml** + ClientListEntry
  - Client list with favorites/grouping
  - Better data binding than WinForms equivalent
  
- **HeatMapView.xaml** + HeatMapViewModel
  - Geographic heatmap of connected clients
  - SkiaSharp rendering
  
- **HVNCWorkspaceView.xaml** + HVNCWorkspaceViewModel
  - HVNC multi-monitor workspace
  - Layout management
  
- **ProcessTreeView.xaml**
  - Hierarchical process tree
  - Minimal codebehind

#### ElementHost Wrappers (Integration Layer)
- StatsElementHost.cs (WinForms → WPF bridge)
- ClientsListElementHost.cs (embedded in tabPage)
- HeatMapElementHost.cs (embedded in tabPage)
- RemoteDesktopElementHost.cs (renders streaming video)

**Issue**: ElementHost adds interop overhead; WPF content is isolated from WinForms event model

### Mixed Architecture Issues

| Issue | Impact | Severity |
|-------|--------|----------|
| **Dual theming systems** | DarkMode works in WinForms only; WPF uses separate styling | HIGH |
| **ElementHost interop overhead** | Mouse wheel scroll doesn't propagate; focus management complex | HIGH |
| **Duplicated event handling** | TreeView navigation logic in WinForms; list selection in WPF | MEDIUM |
| **Inconsistent styling** | Buttons, colors, fonts differ between WinForms and WPF | MEDIUM |
| **Legacy WinForms rendering** | GDI+ instead of DirectX (older performance profile) | LOW |
| **Designer file bloat** | 20+ *.Designer.cs files (auto-generated, 3,000+ lines total) | MEDIUM |

---

## Target Architecture (WPF-First)

### Proposed Structure

```
Pulsar.Server/
├── Views/
│   ├── MainWindow.xaml                    (Primary operator console)
│   ├── MainWindowViewModel.cs
│   ├── Dialogs/
│   │   ├── ClientBuilderDialog.xaml
│   │   ├── SettingsDialog.xaml
│   │   ├── CertificateDialog.xaml
│   │   └── ... (other dialogs)
│   ├── Panels/
│   │   ├── StatsPanel.xaml                (current StatsView)
│   │   ├── ClientsListPanel.xaml          (current ClientsListView)
│   │   ├── HeatMapPanel.xaml              (current HeatMapView)
│   │   └── HVNCWorkspacePanel.xaml        (current HVNCWorkspaceView)
│   └── Popups/
│       ├── HVNCFloatingWindow.xaml
│       ├── MonitorSelectionPopup.xaml
│       └── ScreenCaptureHider.xaml
├── ViewModels/
│   ├── MainWindowViewModel.cs
│   ├── Dialogs/
│   │   ├── ClientBuilderViewModel.cs
│   │   └── ... (dialog VMs)
│   └── Panels/
│       ├── StatsViewModel.cs              (retained from current)
│       └── ... (panel VMs)
├── Services/
│   ├── ThemeService.cs                   (unified theming)
│   ├── DialogService.cs                  (modal dialog host)
│   ├── ClientOperationService.cs         (retained, no change)
│   └── ... (other services)
├── Behaviors/
│   ├── TreeViewSelectionBehavior.cs
│   ├── DataGridSortBehavior.cs
│   └── ... (attached behaviors)
├── Resources/
│   ├── Themes/
│   │   ├── LightTheme.xaml
│   │   ├── DarkTheme.xaml
│   │   └── ThemeDictionary.xaml
│   ├── Styles/
│   │   ├── Buttons.xaml
│   │   ├── Windows.xaml
│   │   ├── DataGrids.xaml
│   │   └── ... (control styles)
│   └── Converters/
│       └── ... (value converters)
└── Controls/
    └── (custom controls only, not auto-generated Designer files)
```

### Benefits of Target Architecture

✅ **Single XAML-based model** - No auto-generated Designer.cs files  
✅ **Unified theming** - ResourceDictionaries applied globally  
✅ **MVVM throughout** - INotifyPropertyChanged, ICommand, Bindings  
✅ **No ElementHost overhead** - Direct WPF rendering  
✅ **Better DPI scaling** - WPF handles per-monitor DPI natively  
✅ **Cleaner codebase** - ~30 fewer files (no Designer pairs)  
✅ **Modern async patterns** - async/await throughout  

---

## Migration Strategy

### Phase 2a: Foundation (Week 1)
**Goal**: Create WPF base infrastructure

**Tasks**:
1. Create `Pulsar.Server/Views/` folder structure
2. Create `Pulsar.Server/Resources/` with theme system
3. Create `PulsarWindow.xaml` base class (inherits Window)
   - Handles theming attachment
   - Applies default styles
   - Manages window state
4. Create `PulsarDialog.xaml` base class
   - Modal dialog infrastructure
   - Consistent styling
5. Create `ThemeService.cs`
   - Unified dark/light mode
   - Replaces DarkMode/ folder logic
   - Publishes `ThemeChanged` event

**Deliverables**:
- ✅ Folder structure created
- ✅ Theme resources defined (Light, Dark)
- ✅ Base window/dialog classes with styling
- ✅ ThemeService working
- ✅ Build test passes

**Effort**: ~4-6 hours  
**Risk**: Low (new code, doesn't touch existing)

---

### Phase 2b: Critical Dialog Migration (Week 1-2)
**Goal**: Move most-used dialogs to WPF

**Priority Order**:
1. **FrmSettings** (Simple, high impact)
   - Single tab control, mostly text inputs
   - Minimal business logic
   - **Target**: SettingsDialog.xaml + ViewModel
   - **Reuse**: Validation from current form

2. **FrmBuilder** (Medium, high value)
   - Client generation UI
   - Complex form with tabs
   - **Target**: ClientBuilderDialog.xaml + ViewModel
   - **Retain**: ClientBuilder.cs (business logic unchanged)

3. **FrmConnections** (Medium, tactical)
   - Shows client list alternative to main view
   - **Target**: ConnectionsDialog.xaml
   - **Note**: Main ClientsListView already in WPF

4. **FrmHVNC** (Medium, special handling)
   - Embeds RemoteDesktopElementHost
   - **Target**: HVNCWindow.xaml
   - **Keep**: RemoteDesktopElementHost.cs (DirectX rendering)

**Deliverables**:
- ✅ 4 dialogs migrated to WPF
- ✅ Existing event handlers preserved
- ✅ Keyboard shortcuts mapped to Commands
- ✅ Build test passes
- ✅ All 4 dialogs open and function correctly

**Effort**: ~8-12 hours (2-3 hours per dialog)  
**Risk**: Medium (must preserve existing functionality)

---

### Phase 2c: Main Window Migration (Week 2-3)
**Goal**: Migrate FrmMain to MainWindow.xaml

**Tasks**:
1. Create MainWindow.xaml
   - Tab structure
   - Menu bar
   - Toolbar
   - Status bar
   - Docking pane layout (optional: consider MahApps.Metro for Office-like layout)

2. Create MainWindowViewModel
   - TabSelectedCommand
   - ClientSelectCommand
   - Feature launch commands (HVNC, Chat, etc.)
   - Bind to existing services (no changes to ClientOperationService)

3. Migrate client tree
   - Replace WinForms TreeView with WPF TreeView
   - Create TreeViewViewModel for MVVM binding
   - Add TreeViewSelectionBehavior for multi-select

4. Migrate existing WPF panels
   - Remove ElementHost wrappers
   - Embed StatsPanel, ClientsListPanel, HeatMapPanel directly
   - Remove **interop overhead** (~5-10% performance improvement)

5. Integrate 4 migrated dialogs
   - Replace Show() calls with IDialogService.ShowDialog()
   - Retain existing state management

**Deliverables**:
- ✅ MainWindow.xaml + ViewModel created
- ✅ All tabs functional
- ✅ Client tree working
- ✅ Feature dialogs launch correctly
- ✅ All 4 previously migrated dialogs integrated
- ✅ Build test passes
- ✅ Application launches and shows all data

**Effort**: ~12-16 hours (2-3 days)  
**Risk**: High (core functionality; extensive testing needed)

---

### Phase 2d: Remaining Dialog Migration (Week 3-4)
**Goal**: Migrate remaining 15+ dialogs

**High Priority** (Tactical use):
- FrmRemoteShell, FrmRemoteChat, FrmRemoteScripting
- FrmTaskManager, FrmTaskExecute
- FrmRemoteDesktop (alternative viewer)

**Medium Priority** (Less frequently used):
- FrmRegistryEditor, FrmReg*Edit dialogs
- FrmPasswordRecovery
- FrmPluginManager
- FrmStartupManager

**Low Priority** (Specialist/rare):
- FrmRemoteWebcam, FrmRemoteMic, FrmRemoteSystemAudio
- FrmMemoryDump, FrmHVNC (alternative HVNC window)
- FrmSystemInformation, FrmCustomFileStarter

**Batch approach**:
- Group by complexity (simple forms first)
- Reuse patterns from earlier migrations
- Create XAML templates for similar dialogs

**Deliverables**:
- ✅ All 15+ dialogs migrated
- ✅ Build test passes
- ✅ All existing features functional

**Effort**: ~20-24 hours (4-5 days, parallelizable)  
**Risk**: Medium (repetitive, low complexity)

---

### Phase 2e: Cleanup & Optimization (Week 4)
**Goal**: Remove WinForms artifacts, optimize rendering

**Tasks**:
1. **Remove WinForms folders** (after verification)
   - Delete `/Forms/` folder
   - Delete WinForms dependencies from .csproj
   - Delete `/Controls/HVNCPopUp/`, `/Controls/RemoteDesktopPopUp/`, `/Controls/ScreenCaptureHider/` (move to WPF)

2. **Optimize theme system**
   - Profile StyleInitialization time
   - Lazy-load ResourceDictionaries for large dialogs
   - Cache compiled XAML resources

3. **Performance tuning**
   - Profile main window rendering
   - Optimize TreeView virtualization
   - Measure ElementHost removal benefit

4. **Testing**
   - Full regression test (all features)
   - Keyboard/mouse input verification
   - Theme switching verification
   - Multi-monitor support

**Deliverables**:
- ✅ WinForms code completely removed
- ✅ No .resx files in project
- ✅ Performance baseline documented
- ✅ Full regression test passed

**Effort**: ~8-12 hours  
**Risk**: Low (cleanup and optimization)

---

## Implementation Checklist

### Pre-Migration
```
[ ] 1. Backup current source (git branch: main → branch: pre-wpf-migration)
[ ] 2. Create new branch: wpf-modernization
[ ] 3. Install WPF NuGet dependencies (if needed)
[ ] 4. Review existing MVVM patterns (from current WPF controls)
[ ] 5. Document current keyboard shortcuts and event handlers
```

### Foundation (Phase 2a)
```
[ ] 1. Create folder structure
[ ] 2. Create PulsarWindow base class
[ ] 3. Create PulsarDialog base class
[ ] 4. Create theme resources (Light, Dark)
[ ] 5. Create ThemeService
[ ] 6. Build and test
```

### Critical Dialogs (Phase 2b)
```
[ ] 1. FrmSettings → SettingsDialog.xaml
[ ] 2. FrmBuilder → ClientBuilderDialog.xaml
[ ] 3. FrmConnections → ConnectionsDialog.xaml
[ ] 4. FrmHVNC → HVNCWindow.xaml
[ ] 5. Build and test
[ ] 6. Manual verification of each dialog
```

### Main Window (Phase 2c)
```
[ ] 1. Create MainWindow.xaml
[ ] 2. Create MainWindowViewModel
[ ] 3. Migrate client tree
[ ] 4. Migrate menu bar + toolbar
[ ] 5. Remove ElementHost wrappers
[ ] 6. Integrate 4 migrated dialogs
[ ] 7. Build and test
[ ] 8. Full manual verification
```

### Remaining Dialogs (Phase 2d)
```
[ ] 1. FrmRemoteShell, Chat, Scripting
[ ] 2. FrmTask*, FrmRemoteDesktop
[ ] 3. FrmRegistry*, FrmPassword*, FrmPlugin*
[ ] 4. FrmStartup*, FrmRemote* (audio/video)
[ ] 5. Build and test
[ ] 6. Spot-check verification
```

### Cleanup (Phase 2e)
```
[ ] 1. Remove WinForms code
[ ] 2. Remove .resx files
[ ] 3. Optimize theme loading
[ ] 4. Performance benchmarking
[ ] 5. Full regression test
[ ] 6. Merge to main
```

---

## Technical Details

### Theme System (ThemeService.cs)

```csharp
public class ThemeService : INotifyPropertyChanged
{
    public enum Theme { Light, Dark }
    
    private Theme _currentTheme = Theme.Dark;
    public Theme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ApplyTheme(value);
                OnPropertyChanged();
            }
        }
    }
    
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
    
    public void ApplyTheme(Theme theme)
    {
        var dict = theme == Theme.Dark
            ? new ResourceDictionary { Source = new Uri("Resources/Themes/DarkTheme.xaml", UriKind.Relative) }
            : new ResourceDictionary { Source = new Uri("Resources/Themes/LightTheme.xaml", UriKind.Relative) };
        
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dict);
        
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
    }
}
```

### Base Window Class (PulsarWindow.xaml)

```xaml
<Window x:Class="Pulsar.Server.Views.PulsarWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Style="{DynamicResource PulsarWindowStyle}"
        Background="{DynamicResource WindowBackgroundBrush}"
        Foreground="{DynamicResource WindowForegroundBrush}"
        FontSize="11"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <ContentPresenter Content="{TemplateBinding Content}" />
    </Grid>
</Window>
```

### Dialog Service Pattern

```csharp
public interface IDialogService
{
    bool? ShowDialog<TViewModel>(TViewModel viewModel) where TViewModel : class;
    void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class;
}

// Usage in MainWindowViewModel:
var builderVm = new ClientBuilderViewModel();
var result = _dialogService.ShowDialog(builderVm);
if (result == true)
{
    // User clicked OK
}
```

---

## Risk Mitigation

### Risk: Breaking Existing Features
**Mitigation**:
- Each phase creates new code alongside old
- Never delete old code until new is verified
- Full regression test after each phase
- Parallel testing on separate branch

### Risk: Performance Degradation
**Mitigation**:
- Baseline current performance (FPS, memory)
- Monitor during migration
- Use Virtualization in ItemsControl
- Lazy-load heavy resources
- Measure ElementHost removal benefit

### Risk: Keyboard/Mouse Issues with WPF
**Mitigation**:
- Document keyboard shortcuts before migration
- Use XAML InputBindings for all shortcuts
- Use AttachedBehaviors for complex input handling
- Test extensively with keyboard-only workflow

### Risk: Theme System Complexity
**Mitigation**:
- Use ResourceDictionary + DynamicResource pattern (proven)
- Keep separate Light/Dark .xaml files
- Avoid code-behind theme logic
- Use themes from existing WPF controls as reference

---

## Success Criteria

```
✅ Build succeeds with zero WinForms references
✅ All 273 unit tests pass (unchanged)
✅ Application launches and displays main window
✅ All client list features work (filtering, sorting, grouping)
✅ All statistics panels render correctly
✅ All modal dialogs open and function
✅ Keyboard shortcuts work (documented list)
✅ Mouse input works (clicks, wheel, drag/drop)
✅ Theme switching works (live update)
✅ No performance regression (FPS maintained)
✅ No resource leaks (memory stable)
✅ Code metrics improved (fewer files, less duplication)
```

---

## Timeline

| Phase | Duration | Start | End | Deliverable |
|-------|----------|-------|-----|-------------|
| 2a: Foundation | 1 week | Nov 20 | Nov 27 | WPF infrastructure ✅ |
| 2b: Critical Dialogs | 1 week | Nov 27 | Dec 4 | 4 dialogs migrated ✅ |
| 2c: Main Window | 1 week | Dec 4 | Dec 11 | Operator console live ✅ |
| 2d: Remaining Dialogs | 1 week | Dec 11 | Dec 18 | Full feature parity ✅ |
| 2e: Cleanup | 1 week | Dec 18 | Dec 25 | Production-ready WPF ✅ |
| **TOTAL** | **~5 weeks** | Nov 20 | Dec 25 | **Unified WPF UI** ✅ |

---

## File Migration Reference

### WinForms → WPF Mapping

| WinForms | WPF | Type |
|----------|-----|------|
| FrmMain.cs | MainWindow.xaml | PRIMARY WINDOW |
| FrmHVNC.cs | HVNCWindow.xaml | SPECIALIZED WINDOW |
| FrmSettings.cs | SettingsDialog.xaml | DIALOG |
| FrmBuilder.cs | ClientBuilderDialog.xaml | DIALOG |
| FrmConnections.cs | ConnectionsDialog.xaml | DIALOG |
| FrmRemote*.cs | Remote*Panel.xaml | PANEL/TAB |
| FrmRegistry*.cs | Registry*Dialog.xaml | DIALOG |
| FrmTask*.cs | Task*Dialog.xaml | DIALOG |
| FrmPassword*.cs | PasswordRecoveryDialog.xaml | DIALOG |
| FrmPlugin*.cs | PluginManagerDialog.xaml | DIALOG |
| FrmStartup*.cs | StartupManagerDialog.xaml | DIALOG |
| FrmCustomFileStarter.cs | CustomFileStarterDialog.xaml | DIALOG |
| FrmKeywords.cs | KeywordsDialog.xaml | DIALOG |
| FrmKeylogger.cs | KeyloggerDialog.xaml | DIALOG |
| FrmNickname.cs | NicknameDialog.xaml | DIALOG |
| FrmReverseProxy.cs | ReverseProxyDialog.xaml | DIALOG |
| FrmShowMessagebox.cs | MessageBoxDialog.xaml | DIALOG |
| FrmVisitWebsite.cs | VisitWebsiteDialog.xaml | DIALOG |
| FrmAbout.cs | AboutDialog.xaml | DIALOG |
| FrmCertificate.cs | CertificateDialog.xaml | DIALOG |
| FrmCustomFileStarter.cs | CustomFileStarterDialog.xaml | DIALOG |
| FrmRemoteChat.cs | RemoteChatPanel.xaml | PANEL |
| FrmRemoteDesktop.cs | RemoteDesktopWindow.xaml | WINDOW |
| FrmRemoteExecution.cs | RemoteExecutionPanel.xaml | PANEL |
| FrmRemoteMic.cs | RemoteMicPanel.xaml | PANEL |
| FrmRemoteScripting.cs | RemoteScriptingPanel.xaml | PANEL |
| FrmRemoteShell.cs | RemoteShellPanel.xaml | PANEL |
| FrmRemoteSystemAudio.cs | RemoteSystemAudioPanel.xaml | PANEL |
| FrmRemoteWebcam.cs | RemoteWebcamPanel.xaml | PANEL |
| FrmStartupAdd.cs | StartupAddDialog.xaml | DIALOG |
| FrmSystemInformation.cs | SystemInformationDialog.xaml | DIALOG |
| FrmTaskCommand.cs | TaskCommandDialog.xaml | DIALOG |
| FrmTaskExecute.cs | TaskExecuteDialog.xaml | DIALOG |
| FrmTaskManager.cs | TaskManagerDialog.xaml | DIALOG |
| FrmMemoryDump.cs | MemoryDumpDialog.xaml | DIALOG |

---

## Dependencies & Compatibility

### Retained NuGet Packages
✅ LiveChartsCore.SkiaSharpView.WPF (chart rendering)  
✅ SkiaSharp.Views.WPF (canvas rendering)  
✅ OpenTK.GLWpfControl (3D visualization, if used)  
✅ MessagePack (protocol, unchanged)  

### New/Optional WPF Packages
- **MahApps.Metro** (Optional: office-style windowing, ribbons)
- **ControlzEx** (Optional: advanced control library)
- **Extended.Wpf.Toolkit** (Optional: additional controls)

**Recommendation**: Start without additional packages, add only if needed.

### .NET Target Compatibility
✅ net9.0-windows (primary)  
✅ net48 (not needed; Server is .NET 9.0 only)  

---

## Post-Migration Verification

### Manual Test Cases

```
Test: 1. LAUNCH APPLICATION
  [ ] 1.1 Application starts without errors
  [ ] 1.2 Main window renders correctly
  [ ] 1.3 All tabs visible and clickable
  
Test: 2. CLIENT OPERATIONS
  [ ] 2.1 Client tree shows connected clients
  [ ] 2.2 Selecting client updates detail pane
  [ ] 2.3 Right-click context menu works
  [ ] 2.4 Double-click opens detail view
  
Test: 3. FEATURE DIALOGS
  [ ] 3.1 HVNC → Explorer opens window
  [ ] 3.2 HVNC → Firefox launches browser
  [ ] 3.3 Remote → Chat opens messaging window
  [ ] 3.4 Remote → Shell opens command prompt
  
Test: 4. STATISTICS
  [ ] 4.1 Stats panel renders charts
  [ ] 4.2 Heatmap shows geographic distribution
  [ ] 4.3 Client list displays with favorites
  
Test: 5. KEYBOARD NAVIGATION
  [ ] 5.1 Tab key cycles through controls
  [ ] 5.2 Alt+key accelerators work
  [ ] 5.3 Arrow keys navigate tree/lists
  [ ] 5.4 Escape closes dialogs
  
Test: 6. THEME SWITCHING
  [ ] 6.1 Dark theme applies to all windows
  [ ] 6.2 Light theme applies to all windows
  [ ] 6.3 Theme persists across app restart
  [ ] 6.4 Active dialogs update live
  
Test: 7. MOUSE INPUT
  [ ] 7.1 Click behavior matches old version
  [ ] 7.2 Scroll wheel works in lists/trees
  [ ] 7.3 Drag/drop works (if supported)
  [ ] 7.4 Right-click context menus appear
  
Test: 8. PERFORMANCE
  [ ] 8.1 Startup time < 3 seconds
  [ ] 8.2 List scrolling is smooth (60 FPS)
  [ ] 8.3 Memory usage stable (< 500 MB idle)
  [ ] 8.4 CPU usage < 5% idle
```

---

## Decision Points

### A/B: Layout Framework
**Option A: Standard DockPanel** (Current)
- Pro: Familiar, minimal learning curve
- Con: Limited automatic layout
- **Recommendation**: Use for Phase 2a-2c, revisit in Phase 2e

**Option B: MahApps.Metro with Ribbon** (Advanced)
- Pro: Office-style modern look
- Con: Steeper learning curve, different paradigm
- **Recommendation**: Consider after core migration

### A/B: Dialog Hosting
**Option A: Show()/ShowDialog()** (Simple)
- Pro: Familiar pattern
- Con: Modal dialogs block other windows
- **Recommendation**: Use for most dialogs

**Option B: IDialogService with Message Box** (Advanced)
- Pro: Testable, decoupled
- Con: Requires refactoring ViewModels
- **Recommendation**: Use for future new dialogs

### A/B: State Management
**Option A: Properties with INotifyPropertyChanged** (Current)
- Pro: Works with existing ViewModels
- Con: Manual change notifications
- **Recommendation**: Continue for Phase 2

**Option B: Reactive Extensions (Rx.NET)** (Advanced)
- Pro: Powerful stream-based patterns
- Con: Steep learning curve
- **Recommendation**: Avoid for now

---

## Conclusion

The GUI modernization from WinForms/WPF hybrid to unified WPF will:

1. **Eliminate technical debt** - Remove 68 WinForms files and 20+ auto-generated Designer files
2. **Improve consistency** - Single rendering model, unified theming system
3. **Enhance maintainability** - MVVM pattern throughout, reduced code duplication
4. **Enable future growth** - Native .NET 9.0+ features, easier to extend
5. **Modernize UX** - Better DPI scaling, smoother rendering, live theme switching

The 5-week timeline is aggressive but achievable with parallel work and proper testing. Each phase is independent and can be verified before proceeding.

**Recommendation**: Proceed with Phase 2a (Foundation) starting immediately after build verification sign-off.

---

**End of Strategy Document**
