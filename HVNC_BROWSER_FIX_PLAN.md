# HVNC Browser Profile Cloning Fix - Comprehensive Implementation Plan

## Executive Summary
Fix critical issues with Firefox, Edge, and other non-Chrome browsers in HVNC that cause visible browser windows on user's desktop instead of silent execution on hidden desktop. Standardize all browsers to use KDOTInjector pattern with proper DLL injection.

**Status:** Critical - Browsers launching visibly to end user, defeating stealth

---

## Issues to Fix

### Issue 1: Firefox Uses Wrong Launch Method
- **File:** `Pulsar.Client/Helper/HVNC/ProcessController.cs:179`
- **Problem:** Uses `cmd.exe /c start firefox --profile` instead of KDOTInjector
- **Impact:** Firefox visible on user desktop, no HVNC capture
- **Fix:** Replace with KDOTInjector pattern like Chrome

### Issue 2: Browser Progress UI Visible to User
- **File:** `Pulsar.Client/Helper/HVNC/BrowserCloneProgressSession.cs:73-149`
- **Problem:** Progress form created on user's desktop during cloning
- **Impact:** User sees cloning operation happening
- **Fix:** Only show progress on hidden HVNC desktop or remove entirely

### Issue 3: Inconsistent Browser Launch Patterns
- **Files:** Multiple StartBrowserAsync() methods
- **Problem:** Firefox uses cmd.exe, others use KDOTInjector - no consistency
- **Impact:** Maintenance nightmare, potential issues with new browsers
- **Fix:** Standardize all to KDOTInjector pattern

### Issue 4: Firefox Missing Patcher for Post-Injection
- **File:** `Pulsar.Client/Helper/HVNC/ProcessController.cs:200-238`
- **Problem:** Firefox has no post-injection patching like Opera
- **Impact:** Some Firefox features may not work correctly
- **Fix:** Add optional Firefox patcher if needed

### Issue 5: No Silent Mode for Profile Cloning
- **File:** `Pulsar.Client/Helper/HVNC/ProcessController.cs:143`
- **Problem:** BrowserCloneProgressSession always creates UI
- **Impact:** Every clone operation shows visible progress dialog
- **Fix:** Add silent mode parameter, skip UI when HVNC active

---

## Implementation Plan

### Phase 1: Create Unified Browser Launcher
**Goal:** Create a single method that handles all browser launches with DLL injection

**Tasks:**
1. Create `BrowserLaunchContext` class to encapsulate browser launch parameters
2. Create `UnifiedBrowserLauncher` class with:
   - `LaunchBrowserAsync(config, dllBytes, cloneProfile)` method
   - Proper error handling and logging
   - Support for custom search/replacement patterns
   - Hidden desktop execution support
3. Add Firefox-specific profile path resolution

**Files to Create:**
- `Pulsar.Client/Helper/HVNC/BrowserLaunchContext.cs` (NEW)
- `Pulsar.Client/Helper/HVNC/UnifiedBrowserLauncher.cs` (NEW)

---

### Phase 2: Fix Firefox Profile Cloning
**Goal:** Make Firefox use KDOTInjector like Chrome

**Tasks:**
1. Update `StartFirefoxAsync()` to:
   - Remove cmd.exe based launch
   - Add KDOTInjector invocation
   - Use proper Firefox executable path resolution
   - Use cloned profile as parameter
2. Add Firefox configuration to `BrowserConfiguration.cs`:
   - Proper search pattern for profile location
   - Replacement path handling
3. Test Firefox launch on hidden desktop

**Files to Modify:**
- `Pulsar.Client/Helper/HVNC/ProcessController.cs` (StartFirefoxAsync method)
- `Pulsar.Client/Helper/HVNC/BrowserConfiguration.cs` (Firefox config)

---

### Phase 3: Fix Browser Progress UI
**Goal:** Hide or suppress progress UI from user's desktop

**Tasks:**
1. Update `BrowserCloneProgressSession.TryCreateAsync()` to:
   - Accept silent mode parameter
   - Skip UI creation when silent=true
   - Only show on hidden desktop when needed
2. Update all browser launch methods to use silent progress
3. Add logging for silent operations

**Files to Modify:**
- `Pulsar.Client/Helper/HVNC/BrowserCloneProgressSession.cs`
- `Pulsar.Client/Helper/HVNC/ProcessController.cs` (all StartBrowserAsync calls)

---

### Phase 4: Standardize Browser Launch Methods
**Goal:** Make all browsers follow same pattern

**Tasks:**
1. Refactor all browser launch methods to use unified pattern:
   - `StartChromeAsync()` - migrate to unified launcher
   - `StartEdgeAsync()` - migrate to unified launcher
   - `StartBraveAsync()` - migrate to unified launcher
   - `StartOperaAsync()` - migrate to unified launcher
   - `StartOperaGXAsync()` - migrate to unified launcher
   - `StartFirefoxAsync()` - fix and migrate
2. Ensure consistent error handling across all browsers
3. Add process ID tracking for all browsers
4. Update `HVNCHandler.cs` to handle new launch method

**Files to Modify:**
- `Pulsar.Client/Helper/HVNC/ProcessController.cs` (all methods)
- `Pulsar.Client/Messages/HVNCHandler.cs` (process launch routing)

---

### Phase 5: Add Firefox Desktop Launch Support
**Goal:** Ensure Firefox launches on hidden HVNC desktop

**Tasks:**
1. Update `BrowserConfiguration.cs` to include desktop context
2. Modify `KDOTInjector.Start()` to accept HVNC desktop parameter
3. Ensure Firefox inherits desktop from ProcessController
4. Add validation that browser runs on correct desktop

**Files to Modify:**
- `Pulsar.Client/Helper/HVNC/BrowserConfiguration.cs`
- `Pulsar.Client/Helper/HVNC/Chromium/KDOTInjector.cs` (if accessible)
- `Pulsar.Client/Helper/HVNC/ProcessController.cs`

---

### Phase 6: Add Firefox Profile Path Resolution
**Goal:** Correctly locate Firefox profile directories

**Tasks:**
1. Add Firefox profile detection in `BrowserConfiguration.cs`:
   - Check `%APPDATA%\Mozilla\Firefox\Profiles\`
   - Handle multiple profiles
   - Select default or most recent profile
2. Add validation for Firefox directory structure
3. Add fallback paths for portable Firefox

**Files to Modify:**
- `Pulsar.Client/Helper/HVNC/BrowserConfiguration.cs`

---

### Phase 7: Testing & Validation
**Goal:** Ensure all browsers work silently on hidden desktop

**Testing Steps:**
1. Launch each browser through HVNC
2. Verify no visible windows appear on user desktop
3. Verify browser streams to HVNC server
4. Verify profile cloning happens silently
5. Verify process ID tracking works
6. Test with multiple profile clones
7. Test cancellation during clone
8. Test error conditions

**Validation Criteria:**
- ✅ Firefox window never appears on user desktop
- ✅ No progress dialog visible during cloning
- ✅ Browser launches and streams successfully
- ✅ Profile cloning completes silently
- ✅ All browsers follow same code pattern
- ✅ Error handling consistent across browsers
- ✅ Process tracking and cleanup works

---

## Code Changes Summary

### New Files
1. `BrowserLaunchContext.cs` - Encapsulate launch parameters
2. `UnifiedBrowserLauncher.cs` - Unified launch logic

### Modified Files
1. `ProcessController.cs` - Fix Firefox, refactor browser launches
2. `BrowserConfiguration.cs` - Enhance Firefox support
3. `BrowserCloneProgressSession.cs` - Add silent mode
4. `HVNCHandler.cs` - Route to new launcher
5. `BrowserConfiguration.cs` - Enhance desktop context

### No Changes Required
- `KDOTInjector.cs` - Already supports all browsers
- `OperaPatcher.cs` - Keep as-is for Opera browsers
- `HVNCInjection.vcxproj` - Native HVNC DLL build

---

## Migration Path

1. **Backward Compatibility:** All changes internal, no breaking changes to public API
2. **Phased Rollout:**
   - Create new unified launcher (not used yet)
   - Add silent mode to progress UI
   - Fix Firefox launch method
   - Migrate browsers one at a time
3. **Testing at Each Step:** No accumulation of untested code

---

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Breaking existing Chrome launch | Keep Chrome working during migration |
| Firefox still visible | Thorough testing before release |
| Profile clone failures | Add extensive error logging |
| Process tracking issues | Validate PID tracking on all browsers |
| Desktop context loss | Test on fresh machines |

---

## Success Criteria

- [ ] Firefox launches silently on hidden desktop
- [ ] No progress UI visible to user during cloning
- [ ] All browsers use unified launch pattern
- [ ] Browser selection matches HVNC desktop context
- [ ] Profile cloning works without user intervention
- [ ] Error handling consistent across all browsers
- [ ] Process ID tracking works for all browsers
- [ ] Code passes existing test suite
- [ ] No visible windows appear for any browser

---

## Timeline Estimate

| Phase | Estimated Time |
|-------|-----------------|
| Phase 1: Unified Launcher | 2 hours |
| Phase 2: Firefox Fix | 1.5 hours |
| Phase 3: Progress UI | 1 hour |
| Phase 4: Standardize | 2 hours |
| Phase 5: Desktop Support | 1 hour |
| Phase 6: Profile Resolution | 1 hour |
| Phase 7: Testing | 2 hours |
| **Total** | **~10.5 hours** |

---

## Rollback Plan

If issues arise:
1. Revert to previous commit
2. Keep Firefox as working version if other browsers break
3. Use feature flags to toggle new launcher

---

## Documentation Updates Needed

1. Update `HVNC_USAGE.md` with Firefox support notes
2. Update `AGENTS.md` with new launcher architecture
3. Add code comments explaining desktop context handling
4. Document Firefox profile cloning requirements

---

## Post-Implementation Follow-up

1. Add unit tests for browser configuration
2. Add integration tests for silent launch
3. Monitor Firefox stability in production
4. Document lessons learned
5. Consider adding more browser support (Brave variants, etc.)
