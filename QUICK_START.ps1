#!/usr/bin/env pwsh
<#
.SYNOPSIS
Firefox HVNC Silent Launch - Quick Start Testing
.DESCRIPTION
Step-by-step guide for validating Firefox HVNC silent launch capability
#>

$TestRoot = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT"
$ProjectRoot = "C:\Users\chris\projects\development\pulsar-rat"

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Firefox HVNC Silent Launch - Quick Start" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

# Verification
Write-Host "Pre-flight Checks:" -ForegroundColor Yellow
Write-Host "==================" -ForegroundColor Yellow
Write-Host ""

$Checks = @{
    "Client.exe" = "$TestRoot\Client\Client.exe"
    "Pulsar.exe" = "$TestRoot\Server\Pulsar.exe"
    "Firefox" = "C:\Program Files\Mozilla Firefox\firefox.exe"
}

foreach ($Check in $Checks.GetEnumerator()) {
    if (Test-Path $Check.Value) {
        Write-Host "[OK] $($Check.Name)" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $($Check.Name) - $($Check.Value)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "TESTING STEPS" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[Step 1] Start Pulsar Server" -ForegroundColor Magenta
Write-Host "─────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "Open a NEW PowerShell window and run:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  cd $TestRoot" -ForegroundColor Cyan
Write-Host "  .\Server\Pulsar.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "Wait for the Pulsar server GUI to appear..." -ForegroundColor Gray
Write-Host ""

Write-Host "[Step 2] Start Pulsar Client" -ForegroundColor Magenta
Write-Host "─────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "Open ANOTHER NEW PowerShell window and run:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  cd $TestRoot" -ForegroundColor Cyan
Write-Host "  .\Client\Client.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "Wait for client to connect to the server..." -ForegroundColor Gray
Write-Host "You should see connection confirmation in Pulsar server window" -ForegroundColor Gray
Write-Host ""

Write-Host "[Step 3] Launch Firefox via HVNC" -ForegroundColor Magenta
Write-Host "─────────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "In the Pulsar server window:" -ForegroundColor Yellow
Write-Host "  1. Click on the connected client in the list" -ForegroundColor Cyan
Write-Host "  2. Navigate to: HVNC Desktop menu" -ForegroundColor Cyan
Write-Host "  3. Select: Start Process" -ForegroundColor Cyan
Write-Host "  4. Enter Process Name: Mozilla" -ForegroundColor Cyan
Write-Host "  5. Click Start" -ForegroundColor Cyan
Write-Host ""

Write-Host "[Step 4] Validate Silent Launch" -ForegroundColor Magenta
Write-Host "─────────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "SUCCESS indicators:" -ForegroundColor Yellow
Write-Host "  [CHECK] No Firefox window appears on user desktop" -ForegroundColor Cyan
Write-Host "  [CHECK] HVNC Desktop window shows Firefox active" -ForegroundColor Cyan
Write-Host "  [CHECK] Server logs confirm process start" -ForegroundColor Cyan
Write-Host "  [CHECK] Client profile clone directory created" -ForegroundColor Cyan
Write-Host ""

Write-Host "FAILURE indicators:" -ForegroundColor Yellow
Write-Host "  [FAIL] Visible Firefox window on desktop" -ForegroundColor Red
Write-Host "  [FAIL] HVNC Desktop remains empty/black" -ForegroundColor Red
Write-Host "  [FAIL] Error message in Pulsar GUI" -ForegroundColor Red
Write-Host "  [FAIL] No profile directory created" -ForegroundColor Red
Write-Host ""

Write-Host "[Step 5] Monitor Logs for Injection Confirmation" -ForegroundColor Magenta
Write-Host "──────────────────────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "In a NEW PowerShell window, monitor logs:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Get-Content '$TestRoot\Logs\*.log' -Tail 20 -Wait" -ForegroundColor Cyan
Write-Host ""
Write-Host "Look for messages like:" -ForegroundColor Gray
Write-Host "  - 'KDOTInjector.Start() succeeded'" -ForegroundColor Cyan
Write-Host "  - 'Firefox process created with PID: XXXX'" -ForegroundColor Cyan
Write-Host "  - 'Profile clone directory: ...'" -ForegroundColor Cyan
Write-Host ""

Write-Host "[Step 6] Test Browser Controls" -ForegroundColor Magenta
Write-Host "──────────────────────────────" -ForegroundColor Magenta
Write-Host ""
Write-Host "If Firefox appears in HVNC Desktop:" -ForegroundColor Yellow
Write-Host "  1. Test keyboard input (type text, press keys)" -ForegroundColor Cyan
Write-Host "  2. Test mouse movement (move cursor around)" -ForegroundColor Cyan
Write-Host "  3. Test mouse clicks (single/double click)" -ForegroundColor Cyan
Write-Host "  4. Navigate to a website" -ForegroundColor Cyan
Write-Host "  5. Check form input functionality" -ForegroundColor Cyan
Write-Host ""

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "COMPARISON WITH CHROME/EDGE" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To validate Firefox consistency:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Repeat the above steps with Chrome:" -ForegroundColor Cyan
Write-Host "   Process Name: Chrome" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Repeat the above steps with Edge:" -ForegroundColor Cyan
Write-Host "   Process Name: MicrosoftEdge" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. Compare behavior:" -ForegroundColor Cyan
Write-Host "   - All should launch silently with no visible window" -ForegroundColor Cyan
Write-Host "   - All should appear in HVNC Desktop" -ForegroundColor Cyan
Write-Host "   - All should respond to keyboard/mouse input" -ForegroundColor Cyan
Write-Host "   - All should follow identical KDOTInjector pattern" -ForegroundColor Cyan
Write-Host ""

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "DOCUMENTATION" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Complete documentation available:" -ForegroundColor Yellow
Write-Host "  SESSION_SUMMARY.md - Full session context" -ForegroundColor Cyan
Write-Host "  QUICK_REFERENCE.md - Developer reference" -ForegroundColor Cyan
Write-Host "  DELIVERABLES.md - Complete modifications list" -ForegroundColor Cyan
Write-Host "  BUILD_COMPLETE.md - Build verification" -ForegroundColor Cyan
Write-Host "  DEPLOYMENT_REPORT.md - This deployment status" -ForegroundColor Cyan
Write-Host ""

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "TROUBLESHOOTING" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "If Firefox doesn't appear in HVNC Desktop:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Check logs for injection errors:" -ForegroundColor Cyan
Write-Host "   Get-Content '$TestRoot\Logs\*.log' | Select-String 'Error\|Exception' -Context 2" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Verify Firefox installation:" -ForegroundColor Cyan
Write-Host "   Test-Path 'C:\Program Files\Mozilla Firefox\firefox.exe'" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Test with Chrome first (simpler for debugging):" -ForegroundColor Cyan
Write-Host "   Process Name: Chrome" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Check Windows Event Viewer for process creation errors:" -ForegroundColor Cyan
Write-Host "   eventvwr.msc -> Windows Logs -> System" -ForegroundColor Gray
Write-Host ""

Write-Host "If keyboard/mouse input doesn't work:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Verify HVNC desktop is active (should be in focus)" -ForegroundColor Cyan
Write-Host "2. Check process permissions (may need admin elevation)" -ForegroundColor Cyan
Write-Host "3. Test with simpler commands first (mouse move before click)" -ForegroundColor Cyan
Write-Host "4. Review HVNC input handler logs" -ForegroundColor Cyan
Write-Host ""

Write-Host "========================================================" -ForegroundColor Green
Write-Host "READY FOR TESTING - BEGIN WITH STEP 1" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host ""
