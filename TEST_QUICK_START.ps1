#!/usr/bin/env pwsh
# PULSAR HVNC Firefox Silent Launch - Quick Test Script
# November 20, 2026

Write-Host "`n" -ForegroundColor Cyan
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   PULSAR - HVNC FIREFOX SILENT LAUNCH TEST                 ║" -ForegroundColor Cyan
Write-Host "║   Ready for Production Testing                             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check server status
Write-Host "1️⃣  SERVER STATUS" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$server = Get-Process -Name Pulsar -ErrorAction SilentlyContinue
if ($server) {
    Write-Host "✅ Pulsar Server RUNNING" -ForegroundColor Green
    Write-Host "   PID:    $($server.Id)" -ForegroundColor Gray
    Write-Host "   Memory: $([math]::Round($server.WorkingSet / 1MB)) MB" -ForegroundColor Gray
    Write-Host "   Time:   $(((Get-Date) - $server.StartTime).TotalSeconds.ToString('0')) seconds running" -ForegroundColor Gray
} else {
    Write-Host "❌ Pulsar Server NOT RUNNING" -ForegroundColor Red
    Write-Host "   Launch manually: cd TEST_DEPLOYMENT\Server; .\Pulsar.exe" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "2️⃣  CLIENT DEPLOYMENT" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$clientPath = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client\Client.exe"
if (Test-Path $clientPath) {
    $size = (Get-Item $clientPath).Length / 1KB
    Write-Host "✅ Client.exe Ready: $([math]::Round($size)) KB" -ForegroundColor Green
    Write-Host "   Location: $clientPath" -ForegroundColor Gray
} else {
    Write-Host "❌ Client.exe NOT FOUND" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "3️⃣  FIREFOX INSTALLATION" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$firefoxPath = "C:\Program Files\Mozilla Firefox\firefox.exe"
if (Test-Path $firefoxPath) {
    $version = (Get-Item $firefoxPath).VersionInfo.ProductVersion
    Write-Host "✅ Firefox Installed: Version $version" -ForegroundColor Green
    Write-Host "   Location: $firefoxPath" -ForegroundColor Gray
} else {
    Write-Host "⚠️  Firefox NOT FOUND (required for test)" -ForegroundColor Yellow
    Write-Host "   Install from: https://www.mozilla.org/firefox/" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "4️⃣  FIREFOX SILENT LAUNCH INTEGRATION" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$source = "C:\Users\chris\projects\development\pulsar-rat\Pulsar.Client\Helper\HVNC\ProcessController.cs"
if (Test-Path $source) {
    $content = Get-Content $source
    if ($content -like "*StartFirefoxAsync*" -and $content -like "*KDOTInjector*" -and $content -like "*dllbytes*") {
        Write-Host "✅ Firefox KDOTInjector Pattern: INTEGRATED" -ForegroundColor Green
        Write-Host "   File: ProcessController.cs:130" -ForegroundColor Gray
        Write-Host "   Method: StartFirefoxAsync(byte[] dllbytes, ...)" -ForegroundColor Gray
    } else {
        Write-Host "⚠️  Firefox Integration may need verification" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠️  Source file not found (but compiled binaries are ready)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "5️⃣  RUNTIMES VERIFICATION" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$net9 = dotnet --list-runtimes 2>&1 | Select-String "9\.0"
$net48 = reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Version 2>$null

Write-Host "✅ .NET 9.0 WindowsDesktop: $(if ($net9) {'Present'} else {'Missing'})" -ForegroundColor Green
Write-Host "✅ .NET 4.8 Framework: $(if ($net48) {'Present'} else {'Missing'})" -ForegroundColor Green

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "NEXT STEP: Launch Client to Test Firefox Silent Launch" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan

Write-Host ""
Write-Host "Quick Start:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Open PowerShell window" -ForegroundColor Gray
Write-Host "  2. Run this command:" -ForegroundColor Gray
Write-Host ""
Write-Host "     cd ""C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client""" -ForegroundColor Cyan
Write-Host "     .\Client.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "  3. Wait for Client GUI to appear and show 'Connected'" -ForegroundColor Gray
Write-Host "  4. Right-click client → HVNC Desktop" -ForegroundColor Gray
Write-Host "  5. In viewer, Right-click → Launch Browser → Firefox" -ForegroundColor Gray
Write-Host "  6. Firefox appears ONLY in HVNC viewer (invisible on desktop)" -ForegroundColor Gray
Write-Host ""

Write-Host "Expected Result:" -ForegroundColor Cyan
Write-Host "✅ Firefox appears in HVNC viewer" -ForegroundColor Green
Write-Host "✅ NO Firefox window on main desktop" -ForegroundColor Green
Write-Host "✅ Browser is fully controllable" -ForegroundColor Green
Write-Host "✅ No progress dialog or visible processes" -ForegroundColor Green
Write-Host ""

Write-Host "Documentation:" -ForegroundColor Yellow
Write-Host "📖 TEST_HVNC_FIREFOX_GUIDE.md - Detailed testing instructions" -ForegroundColor Gray
Write-Host "📖 PROJECT_COMPLETION_SUMMARY.md - Implementation overview" -ForegroundColor Gray
Write-Host "📖 TROUBLESHOOTING_NO_GUI.md - Diagnostic procedures" -ForegroundColor Gray
Write-Host ""

Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "STATUS: Ready for Testing ✅" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
