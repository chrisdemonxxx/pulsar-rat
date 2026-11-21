#!/usr/bin/env pwsh
# Quick Fix: Run This Now to Diagnose and Fix

Write-Host "`n════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "PULSAR - QUICK DIAGNOSIS & FIX" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

# Step 1: Check .NET Installation
Write-Host "Step 1: Checking .NET Runtime Installation" -ForegroundColor Yellow
Write-Host "============================================`n" -ForegroundColor Yellow

Write-Host "Checking for .NET 9.0..." -ForegroundColor Gray
$net9check = dotnet --list-runtimes 2>&1
if ($net9check -like "*9.0*") {
    Write-Host "✅ .NET 9.0 IS installed" -ForegroundColor Green
} else {
    Write-Host "❌ .NET 9.0 NOT FOUND" -ForegroundColor Red
    Write-Host "   ACTION: Install from https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
    Write-Host "   Choose: Windows Desktop Runtime 9.0.x`n" -ForegroundColor Yellow
}

Write-Host "`nChecking for .NET 4.8..." -ForegroundColor Gray
try {
    $net48 = reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Version 2>$null
    if ($net48) {
        Write-Host "✅ .NET 4.8 IS installed" -ForegroundColor Green
    } else {
        Write-Host "❌ .NET 4.8 NOT FOUND" -ForegroundColor Red
        Write-Host "   ACTION: Install from https://dotnet.microsoft.com/download/dotnet-framework/net48`n" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ Cannot determine .NET 4.8 status" -ForegroundColor Red
}

# Step 2: Check binaries
Write-Host "`nStep 2: Checking Deployed Binaries" -ForegroundColor Yellow
Write-Host "====================================" -ForegroundColor Yellow

$serverPath = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server\Pulsar.exe"
$clientPath = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Client\Client.exe"

if (Test-Path $serverPath) {
    $serverSize = (Get-Item $serverPath).Length / 1KB
    Write-Host "✅ Server binary (Pulsar.exe): $([math]::Round($serverSize)) KB" -ForegroundColor Green
} else {
    Write-Host "❌ Server binary NOT FOUND" -ForegroundColor Red
}

if (Test-Path $clientPath) {
    $clientSize = (Get-Item $clientPath).Length / 1KB
    Write-Host "✅ Client binary (Client.exe): $([math]::Round($clientSize)) KB" -ForegroundColor Green
} else {
    Write-Host "❌ Client binary NOT FOUND" -ForegroundColor Red
}

# Step 3: Try to run server
Write-Host "`nStep 3: Attempting to Launch Server" -ForegroundColor Yellow
Write-Host "====================================" -ForegroundColor Yellow

Write-Host "Starting Pulsar Server..." -ForegroundColor Gray
Write-Host "(GUI window should appear in 3 seconds)" -ForegroundColor Gray
Write-Host ""

try {
    Push-Location "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT\Server"
    & .\Pulsar.exe
} catch {
    Write-Host "ERROR: Could not start server" -ForegroundColor Red
    Write-Host "Exception: $_" -ForegroundColor Red
} finally {
    Pop-Location
}
