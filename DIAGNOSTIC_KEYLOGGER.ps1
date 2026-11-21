#!/usr/bin/env pwsh
# Keylogger Diagnostic Script - Find why logs aren't appearing

Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "KEYLOGGER DIAGNOSTIC - Troubleshooting Missing Logs" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# CHECK 1: Verify logs directory structure
# ============================================================================
Write-Host "[CHECK 1] Logs Directory" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$logsDir = "$env:APPDATA\Logs"
$tempDir = $env:TEMP

Write-Host "Expected logs directory: $logsDir"
if (Test-Path $logsDir) {
    Write-Host "✅ Directory EXISTS" -ForegroundColor Green
    $logFiles = Get-ChildItem -Path $logsDir -Filter "*.txt" -ErrorAction SilentlyContinue
    if ($logFiles) {
        Write-Host "✅ Log files found: $($logFiles.Count)" -ForegroundColor Green
        foreach ($file in $logFiles | Select-Object -Last 5) {
            Write-Host "   - $($file.Name) ($($file.Length) bytes, Modified: $($file.LastWriteTime))" -ForegroundColor Green
            # Show first 10 lines
            Write-Host "     Content preview:"
            Get-Content -Path $file.FullName -Head 10 | ForEach-Object { Write-Host "     > $_" }
        }
    } else {
        Write-Host "❌ No log files found in directory" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Directory DOES NOT EXIST" -ForegroundColor Red
    Write-Host "   Directory will be auto-created on first keystroke" -ForegroundColor Yellow
}

Write-Host ""

# ============================================================================
# CHECK 2: Check old temp directory (pre-fix location)
# ============================================================================
Write-Host "[CHECK 2] Old Temp Directory (Pre-Fix Location)" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

Write-Host "Old (buggy) location: $tempDir"
$oldLogs = Get-ChildItem -Path $tempDir -Filter "yyyy-MM-dd*.txt" -ErrorAction SilentlyContinue
if ($oldLogs) {
    Write-Host "⚠️  FOUND LOGS IN OLD LOCATION" -ForegroundColor Red
    foreach ($file in $oldLogs) {
        Write-Host "   - $($file.Name)" -ForegroundColor Red
        Write-Host "   ACTION: Delete these OR move to $logsDir"
    }
} else {
    Write-Host "✅ No logs in old temp directory (good)" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# CHECK 3: Verify client is keylogger-enabled
# ============================================================================
Write-Host "[CHECK 3] Client Binary Analysis" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$clientPath = "C:\Users\chris\projects\development\pulsar-rat\bin\Release\net48\Client.exe"
if (Test-Path $clientPath) {
    Write-Host "✅ Client template found: $clientPath" -ForegroundColor Green
    $clientInfo = Get-Item $clientPath
    Write-Host "   Size: $($clientInfo.Length) bytes" -ForegroundColor Green
    Write-Host "   Modified: $($clientInfo.LastWriteTime)" -ForegroundColor Green
} else {
    Write-Host "❌ Client template NOT found" -ForegroundColor Red
}

Write-Host ""

# ============================================================================
# CHECK 4: Check Keylogger.cs source code
# ============================================================================
Write-Host "[CHECK 4] Keylogger Source Code Fix" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$keyloggerPath = "C:\Users\chris\projects\development\pulsar-rat\Pulsar.Client\Logging\Keylogger.cs"
if (Test-Path $keyloggerPath) {
    Write-Host "✅ Keylogger.cs found" -ForegroundColor Green
    
    # Check for Settings.LOGSPATH
    $content = Get-Content $keyloggerPath -Raw
    $settingsUsage = [regex]::Matches($content, "Settings\.LOGSPATH").Count
    
    if ($settingsUsage -ge 2) {
        Write-Host "✅ Settings.LOGSPATH found in source: $settingsUsage times" -ForegroundColor Green
    } else {
        Write-Host "❌ Settings.LOGSPATH NOT found in source (fix not applied?)" -ForegroundColor Red
    }
    
    # Check for Path.GetTempPath() - should NOT be there
    $tempPathUsage = [regex]::Matches($content, 'Path\.GetTempPath\(\)').Count
    if ($tempPathUsage -gt 0) {
        Write-Host "❌ Path.GetTempPath() still found in source: $tempPathUsage times (bug not fixed!)" -ForegroundColor Red
    } else {
        Write-Host "✅ Path.GetTempPath() NOT in keylogger code (good)" -ForegroundColor Green
    }
} else {
    Write-Host "❌ Keylogger.cs NOT found" -ForegroundColor Red
}

Write-Host ""

# ============================================================================
# CHECK 5: Verify client process is running
# ============================================================================
Write-Host "[CHECK 5] Running Processes" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$clientProc = Get-Process -Name "Client" -ErrorAction SilentlyContinue
if ($clientProc) {
    Write-Host "✅ Client process running: PID $($clientProc.Id)" -ForegroundColor Green
    Write-Host "   Memory: $([math]::Round($clientProc.WorkingSet64 / 1MB, 2)) MB" -ForegroundColor Green
} else {
    Write-Host "❌ Client process NOT running" -ForegroundColor Red
    Write-Host "   Keylogger can only capture if client is running" -ForegroundColor Yellow
}

$serverProc = Get-Process -Name "Pulsar" -ErrorAction SilentlyContinue
if ($serverProc) {
    Write-Host "✅ Server (Pulsar.exe) running: PID $($serverProc.Id)" -ForegroundColor Green
} else {
    Write-Host "❌ Server process NOT running" -ForegroundColor Red
}

Write-Host ""

# ============================================================================
# CHECK 6: Verify firewall/network connectivity
# ============================================================================
Write-Host "[CHECK 6] Network Connectivity" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$serverPort = 9999
$serverListen = netstat -ano | Select-String ":$serverPort"
if ($serverListen) {
    Write-Host "✅ Server listening on port $serverPort" -ForegroundColor Green
    Write-Host "   $serverListen" -ForegroundColor Green
} else {
    Write-Host "⚠️  No listener on port $serverPort (check if server is running)" -ForegroundColor Yellow
}

Write-Host ""

# ============================================================================
# DIAGNOSIS SUMMARY
# ============================================================================
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "DIAGNOSIS SUMMARY" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Count issues
$issues = 0
if (-not (Test-Path $logsDir)) { $issues++ }
if (-not (Test-Path $clientPath)) { $issues++ }
if ($tempPathUsage -gt 0) { $issues++ }
if (-not $clientProc) { $issues++ }

if ($issues -eq 0) {
    Write-Host "✅ All checks passed - Configuration looks good!" -ForegroundColor Green
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host "1. Make sure client is running (check for Client.exe process)"
    Write-Host "2. Click inside a text editor (Notepad, Word, etc.)"
    Write-Host "3. Type some text"
    Write-Host "4. Wait 2-3 seconds for buffer to flush"
    Write-Host "5. Click 'Refresh' in FrmKeylogger"
    Write-Host ""
} else {
    Write-Host "❌ $issues issue(s) found - See details above" -ForegroundColor Red
    Write-Host ""
    Write-Host "RECOMMENDED FIXES:" -ForegroundColor Yellow
    Write-Host ""
    
    if (-not (Test-Path $logsDir)) {
        Write-Host "1. Logs directory missing:"
        Write-Host "   - Run: New-Item -Path '$logsDir' -Type Directory -Force"
        Write-Host ""
    }
    
    if (-not (Test-Path $clientPath)) {
        Write-Host "2. Client binary missing:"
        Write-Host "   - Run: dotnet build Pulsar.Client -c Release"
        Write-Host ""
    }
    
    if ($tempPathUsage -gt 0) {
        Write-Host "3. Keylogger fix not applied:"
        Write-Host "   - Verify Keylogger.cs has Settings.LOGSPATH"
        Write-Host "   - Run: dotnet build Pulsar.Client -c Release"
        Write-Host "   - Regenerate client stub in server"
        Write-Host ""
    }
    
    if (-not $clientProc) {
        Write-Host "4. Client not running:"
        Write-Host "   - Generate new client in server UI"
        Write-Host "   - Run: & 'path\to\Generated_Client.exe'"
        Write-Host "   - Wait 2-3 seconds for connection"
        Write-Host ""
    }
}

Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
