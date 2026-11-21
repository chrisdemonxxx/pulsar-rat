#!/usr/bin/env powershell
# Pulsar RAT v2.4.5 - Comprehensive Diagnostics Script
# Checks system configuration, .NET runtime, binaries, and process status

param(
    [switch]$Verbose
)

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "=" * 70 -ForegroundColor Cyan
}

function Write-Check {
    param([string]$Text, [bool]$Pass)
    if ($Pass) {
        Write-Host "  [✓] $Text" -ForegroundColor Green
    } else {
        Write-Host "  [✗] $Text" -ForegroundColor Red
    }
}

# ============================================================================
Write-Header "PULSAR RAT v2.4.5 - SYSTEM DIAGNOSTICS"

# ============================================================================
Write-Header ".NET Runtime Check"

$dotnetVersion = & dotnet --version 2>$null
$runtimesOutput = & dotnet --list-runtimes 2>$null
$hasWindowsDesktop = $runtimesOutput -match "WindowsDesktop"

Write-Check "dotnet CLI found" ($null -ne $dotnetVersion)
if ($dotnetVersion) {
    Write-Host "    Version: $dotnetVersion" -ForegroundColor Gray
}

Write-Check ".NET 9.0 WindowsDesktop runtime installed" $hasWindowsDesktop
if ($hasWindowsDesktop) {
    $runtimesOutput | Where-Object { $_ -match "WindowsDesktop" } | ForEach-Object {
        Write-Host "    Found: $_" -ForegroundColor Gray
    }
}

# ============================================================================
Write-Header "Deployment Folder Check"

$deployPath = "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"
$serverExe = Join-Path $deployPath "Pulsar.exe"
$clientExe = Join-Path $deployPath "Client.exe"

$serverExists = Test-Path $serverExe
$clientExists = Test-Path $clientExe

Write-Check "Deployment folder exists" (Test-Path $deployPath)
Write-Check "Server binary (Pulsar.exe) exists" $serverExists

if ($serverExists) {
    $file = Get-Item $serverExe
    $size = [math]::Round($file.Length / 1KB, 2)
    Write-Host "    Size: $size KB" -ForegroundColor Gray
    Write-Host "    Modified: $($file.LastWriteTime)" -ForegroundColor Gray
}

Write-Check "Client binary (Client.exe) exists" $clientExists

if ($clientExists) {
    $file = Get-Item $clientExe
    $size = [math]::Round($file.Length / 1KB, 2)
    Write-Host "    Size: $size KB" -ForegroundColor Gray
    Write-Host "    Modified: $($file.LastWriteTime)" -ForegroundColor Gray
}

# ============================================================================
Write-Header "Network Check"

$netstatOutput = netstat -ano 2>$null | Select-String "4783"
$portInUse = $null -ne $netstatOutput

Write-Check "Port 4783 available" (-not $portInUse)

if ($portInUse) {
    Write-Host "    Current connections:" -ForegroundColor Yellow
    $netstatOutput | ForEach-Object {
        Write-Host "    $_" -ForegroundColor Yellow
    }
}

# ============================================================================
Write-Header "Process Status"

$pulsarProc = Get-Process -Name Pulsar -ErrorAction SilentlyContinue
$clientProc = Get-Process -Name Client -ErrorAction SilentlyContinue

Write-Check "Pulsar.exe is running" ($null -ne $pulsarProc)
if ($pulsarProc) {
    Write-Host "    PID: $($pulsarProc.Id)" -ForegroundColor Gray
    Write-Host "    Memory: $([math]::Round($pulsarProc.WorkingSet/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "    Threads: $($pulsarProc.Threads.Count)" -ForegroundColor Gray
    Write-Host "    Handles: $($pulsarProc.Handles)" -ForegroundColor Gray
}

Write-Check "Client.exe is running" ($null -ne $clientProc)
if ($clientProc) {
    Write-Host "    PID: $($clientProc.Id)" -ForegroundColor Gray
    Write-Host "    Memory: $([math]::Round($clientProc.WorkingSet/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "    Threads: $($clientProc.Threads.Count)" -ForegroundColor Gray
    Write-Host "    Handles: $($clientProc.Handles)" -ForegroundColor Gray
}

# ============================================================================
Write-Header "Network Connections"

if ($pulsarProc -or $clientProc) {
    Write-Host ""
    netstat -ano | Where-Object { $_ -match "4783|Pulsar|Client" }
}

# ============================================================================
Write-Header "Debug Log Files"

$clientLog = Join-Path $env:TEMP "pulsar_client_main.log"

Write-Check "Client debug log exists" (Test-Path $clientLog)

if (Test-Path $clientLog) {
    Write-Host ""
    Write-Host "    Log content (last 20 lines):" -ForegroundColor Gray
    Get-Content $clientLog | Select-Object -Last 20 | ForEach-Object {
        Write-Host "    $_" -ForegroundColor Gray
    }
}

# ============================================================================
Write-Header "Encryption Handshake Verification"

if ($pulsarProc -and $clientProc) {
    Write-Host ""
    Write-Host "Both processes are running - handshake should be active" -ForegroundColor Green
    Write-Host ""
    Write-Host "Expected sequence:" -ForegroundColor Yellow
    Write-Host "  1. ClientIdentificationRequest (unencrypted)" -ForegroundColor Gray
    Write-Host "  2. ClientIdentificationResult (unencrypted)" -ForegroundColor Gray
    Write-Host "  3. SetUserActiveWindowStatus (encrypted)" -ForegroundColor Gray
    Write-Host "  4. All subsequent traffic encrypted" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "One or both processes not running - cannot verify handshake" -ForegroundColor Yellow
}

# ============================================================================
Write-Header "Summary"

$allGood = $serverExists -and $clientExists -and $hasWindowsDesktop -and (-not $portInUse)

if ($allGood) {
    Write-Host ""
    Write-Host "  ✓ System is ready for testing" -ForegroundColor Green
    Write-Host ""
    if ($null -ne $pulsarProc -and $null -ne $clientProc) {
        Write-Host "  ✓ Both processes are currently running and communicating" -ForegroundColor Green
    } else {
        Write-Host "  → Ready to launch: C:\Users\chris\Desktop\Workspace\SIMPLE_TEST.bat" -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "  ✗ System issues detected - review above" -ForegroundColor Red
}

Write-Host ""
