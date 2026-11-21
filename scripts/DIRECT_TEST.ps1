#!/usr/bin/env powershell

$deployPath = "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"
$serverExe = Join-Path $deployPath "Pulsar.exe"
$clientExe = Join-Path $deployPath "Client.exe"

Write-Host "`nDirect Process Test"
Write-Host "==================`n"

# Kill existing
Write-Host "[1] Cleaning up..."
Get-Process -Name Pulsar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name Client -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

# Start server - NORMAL window (not minimized)
Write-Host "[2] Starting Server (NORMAL window)..."
$srv = Start-Process -FilePath $serverExe -PassThru -WindowStyle Normal -ErrorAction SilentlyContinue
if (-not $srv) {
    Write-Host "ERROR: Failed to start server"
    exit 1
}

Start-Sleep 6

$srvRunning = $null -ne (Get-Process -Id $srv.Id -ErrorAction SilentlyContinue)
if ($srvRunning) {
    Write-Host "OK: Server running (PID: $($srv.Id))`n"
} else {
    Write-Host "FAIL: Server crashed`n"
    exit 1
}

# Start client - NORMAL window
Write-Host "[3] Starting Client (NORMAL window)..."
$cli = Start-Process -FilePath $clientExe -PassThru -WindowStyle Normal -ErrorAction SilentlyContinue
if (-not $cli) {
    Write-Host "ERROR: Failed to start client"
    exit 1
}

Start-Sleep 6

$cliRunning = $null -ne (Get-Process -Id $cli.Id -ErrorAction SilentlyContinue)
if ($cliRunning) {
    Write-Host "OK: Client running (PID: $($cli.Id))`n"
} else {
    Write-Host "FAIL: Client crashed`n"
    exit 1
}

# Monitor
Write-Host "========================================`n[4] Monitoring for 30 seconds...`n========================================`n"

$pass = $true
for ($i = 1; $i -le 30; $i++) {
    $srvOk = $null -ne (Get-Process -Id $srv.Id -ErrorAction SilentlyContinue)
    $cliOk = $null -ne (Get-Process -Id $cli.Id -ErrorAction SilentlyContinue)
    
    if (-not $srvOk -or -not $cliOk) {
        Write-Host "[${i}s] CRASH: Server=$srvOk, Client=$cliOk" -ForegroundColor Red
        $pass = $false
        break
    }
    
    Write-Host "[${i}s] Both running" -ForegroundColor Green
    Start-Sleep 1
}

Write-Host "`n========================================"
if ($pass) {
    Write-Host "SUCCESS - Both ran for 30+ seconds" -ForegroundColor Green
    Write-Host "========================================`n"
    
    # Keep windows open for observation
    Write-Host "Windows will stay open for 10 seconds..."
    Start-Sleep 10
} else {
    Write-Host "FAILED - See details above" -ForegroundColor Red
    Write-Host "========================================`n"
}

# Cleanup
Get-Process -Name Pulsar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name Client -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

exit $(if ($pass) { 0 } else { 1 })
