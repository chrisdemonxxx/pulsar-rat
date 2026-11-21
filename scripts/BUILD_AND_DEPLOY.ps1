#!/usr/bin/env powershell
# Pulsar RAT v2.4.5 - Full Build & Deploy Script

$ErrorActionPreference = "Continue"

Write-Host @"
========================================================================
   PULSAR RAT v2.4.5 - FULL BUILD & DEPLOYMENT
========================================================================

"@ -ForegroundColor Cyan

$projectRoot = "C:\Users\chris\Downloads\c2panel\pulsar"
$deployPath = "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"

if (-not (Test-Path $projectRoot)) {
    Write-Host "[ERROR] Project root not found: $projectRoot" -ForegroundColor Red
    exit 1
}

Write-Host "[*] Project Root: $projectRoot" -ForegroundColor Yellow
Write-Host "[*] Deploy Path: $deployPath" -ForegroundColor Yellow
Write-Host ""

# Step 1: Clean
Write-Host "[1/4] CLEANING PREVIOUS BUILDS..." -ForegroundColor Cyan
Push-Location $projectRoot
dotnet clean -c Debug --nologo 2>&1 | Where-Object {$_ -match "Cleaned|Removing" -or $_.Length -gt 0} | Select-Object -First 10

# Step 2: Restore
Write-Host ""
Write-Host "[2/4] RESTORING DEPENDENCIES..." -ForegroundColor Cyan
dotnet restore --nologo 2>&1 | Select-String "Restored|packages" | Select-Object -First 5

# Step 3: Build
Write-Host ""
Write-Host "[3/4] BUILDING (net9.0-windows DEBUG)..." -ForegroundColor Green
$buildOutput = @()
$buildOutput += dotnet build -c Debug --no-restore --nologo 2>&1

# Parse build results
$succeeded = $buildOutput -match "Build succeeded" -or $buildOutput -match "succeeded"
$haserrors = $buildOutput -match "error|Error" -and $buildOutput -notmatch "error:"

if ($succeeded -and -not $haserrors) {
    Write-Host "   [OK] Build succeeded" -ForegroundColor Green
} else {
    Write-Host "   [INFO] Build output:" -ForegroundColor Yellow
    $buildOutput | Select-Object -Last 20 | ForEach-Object { Write-Host "   $_" }
}

# Step 4: Deploy
Write-Host ""
Write-Host "[4/4] DEPLOYING BINARIES..." -ForegroundColor Cyan

# Find binaries
$serverExe = Get-ChildItem -Path ".\bin\Debug\Pulsar.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
$clientExe = Get-ChildItem -Path ".\bin\Debug\Client.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

if ($serverExe) {
    $size = [math]::Round($serverExe.Length / 1KB, 2)
    Write-Host "   [OK] Found Pulsar.exe ($size KB)" -ForegroundColor Green
    Copy-Item $serverExe.FullName "$deployPath\Pulsar.exe" -Force
    Write-Host "   [OK] Deployed to PulsarRAT_Deploy" -ForegroundColor Green
} else {
    Write-Host "   [WARN] Pulsar.exe not found in bin\Debug" -ForegroundColor Yellow
}

if ($clientExe) {
    $size = [math]::Round($clientExe.Length / 1KB, 2)
    Write-Host "   [OK] Found Client.exe ($size KB)" -ForegroundColor Green
    Copy-Item $clientExe.FullName "$deployPath\Client.exe" -Force
    Write-Host "   [OK] Deployed to PulsarRAT_Deploy" -ForegroundColor Green
} else {
    Write-Host "   [WARN] Client.exe not found in bin\Debug" -ForegroundColor Yellow
}

Pop-Location

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Green
Write-Host "BUILD & DEPLOYMENT COMPLETE" -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Green
Write-Host ""

# Verify deployment
Write-Host "[*] Verifying deployment..." -ForegroundColor Cyan
$deployedServer = Get-Item "$deployPath\Pulsar.exe" -ErrorAction SilentlyContinue
$deployedClient = Get-Item "$deployPath\Client.exe" -ErrorAction SilentlyContinue

if ($deployedServer) {
    Write-Host "   [OK] Pulsar.exe: $([math]::Round($deployedServer.Length/1KB, 2)) KB ($(Get-Date($deployedServer.LastWriteTime) -Format 'HH:mm:ss'))" -ForegroundColor Green
}
if ($deployedClient) {
    Write-Host "   [OK] Client.exe: $([math]::Round($deployedClient.Length/1KB, 2)) KB ($(Get-Date($deployedClient.LastWriteTime) -Format 'HH:mm:ss'))" -ForegroundColor Green
}

Write-Host ""
Write-Host "Ready for testing!" -ForegroundColor Green
