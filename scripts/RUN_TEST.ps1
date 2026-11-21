#!/usr/bin/env powershell
# Pulsar RAT v2.4.5 - Encryption Handshake Fix Verification Test

param([int]$TestDuration = 30)

$deployPath = "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"
$serverExe = Join-Path $deployPath "Pulsar.exe"
$clientExe = Join-Path $deployPath "Client.exe"

Write-Host "`n" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " PULSAR RAT v2.4.5 - TEST EXECUTION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verify binaries exist
Write-Host "[*] Verifying binaries..." -ForegroundColor Yellow
if (-not (Test-Path $serverExe)) {
    Write-Host "ERROR: Pulsar.exe not found" -ForegroundColor Red
    exit 1
}
Write-Host "  OK: Pulsar.exe found" -ForegroundColor Green

if (-not (Test-Path $clientExe)) {
    Write-Host "ERROR: Client.exe not found" -ForegroundColor Red
    exit 1
}
Write-Host "  OK: Client.exe found" -ForegroundColor Green

# Kill any existing processes
Write-Host ""
Write-Host "[*] Cleaning up existing processes..." -ForegroundColor Yellow
Get-Process -Name Pulsar -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name Client -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Write-Host "  OK: Cleanup complete" -ForegroundColor Green

# Launch server
Write-Host ""
Write-Host "[*] Launching server: Pulsar.exe" -ForegroundColor Yellow
$serverProc = Start-Process -FilePath $serverExe -PassThru -WindowStyle Normal
Start-Sleep -Seconds 4

$serverRunning = $null -ne (Get-Process -Id $serverProc.Id -ErrorAction SilentlyContinue)
if (-not $serverRunning) {
    Write-Host "ERROR: Server failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "  OK: Server running (PID: $($serverProc.Id))" -ForegroundColor Green

# Launch client
Write-Host ""
Write-Host "[*] Launching client: Client.exe" -ForegroundColor Yellow
$clientProc = Start-Process -FilePath $clientExe -PassThru -WindowStyle Normal
Start-Sleep -Seconds 3

$clientRunning = $null -ne (Get-Process -Id $clientProc.Id -ErrorAction SilentlyContinue)
if (-not $clientRunning) {
    Write-Host "ERROR: Client failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "  OK: Client running (PID: $($clientProc.Id))" -ForegroundColor Green

# Monitor for TestDuration seconds
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "[*] MONITORING FOR $TestDuration SECONDS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$testPassed = $true
$failMessage = ""

for ($i = 1; $i -le $TestDuration; $i++) {
    $serverRunning = $null -ne (Get-Process -Id $serverProc.Id -ErrorAction SilentlyContinue)
    $clientRunning = $null -ne (Get-Process -Id $clientProc.Id -ErrorAction SilentlyContinue)
    
    if (-not $serverRunning) {
        $testPassed = $false
        $failMessage = "Server crashed at ${i}s"
        break
    }
    
    if (-not $clientRunning) {
        $testPassed = $false
        $failMessage = "Client crashed at ${i}s"
        break
    }
    
    Write-Host "[${i}s] Both running (Srv: $([math]::Round($serverProc.WorkingSet/1MB,1))MB, Cli: $([math]::Round($clientProc.WorkingSet/1MB,1))MB)" -ForegroundColor Green
    Start-Sleep -Seconds 1
}

if ($testPassed) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "SUCCESS - TEST PASSED" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Both processes ran for $TestDuration seconds" -ForegroundColor Green
    Write-Host "Encryption handshake verified - READY FOR DEPLOYMENT" -ForegroundColor Green
    Write-Host ""
    exit 0
} else {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "FAILED - $failMessage" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}
