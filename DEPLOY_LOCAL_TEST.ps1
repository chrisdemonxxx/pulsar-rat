#!/usr/bin/env pwsh
# Simplified deployment script - stages build artifacts to test directory

$ProjectRoot = "C:\Users\chris\projects\development\pulsar-rat"
$TestRoot = "$ProjectRoot\TEST_DEPLOYMENT"
$ClientBuild = "$ProjectRoot\bin\Release\net48"
$ServerBuild = "$ProjectRoot\bin\Release\net9.0-windows"

Write-Host "Firefox HVNC Fix - Deploying to Test Environment" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Create directories
Write-Host "[1] Creating deployment directories..." -ForegroundColor Yellow
$Directories = @($TestRoot, "$TestRoot\Client", "$TestRoot\Server", "$TestRoot\Logs", "$TestRoot\TestResults")
foreach ($dir in $Directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "    Created: $dir" -ForegroundColor Green
    } else {
        Write-Host "    Exists:  $dir" -ForegroundColor Gray
    }
}
Write-Host ""

# Copy Client artifacts
Write-Host "[2] Staging Client artifacts from $ClientBuild..." -ForegroundColor Yellow
if (Test-Path $ClientBuild) {
    $ClientArtifacts = @("Client.exe", "Pulsar.Common.dll", "MessagePack.dll", "LZ4.dll")
    foreach ($artifact in $ClientArtifacts) {
        $Source = "$ClientBuild\$artifact"
        $Dest = "$TestRoot\Client\$artifact"
        if (Test-Path $Source) {
            Copy-Item -Path $Source -Destination $Dest -Force
            $Size = (Get-Item $Dest).Length / 1MB
            Write-Host "    OK: $artifact" -ForegroundColor Green
        } else {
            Write-Host "    Missing: $artifact" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "    Directory not found: $ClientBuild" -ForegroundColor Red
}
Write-Host ""

# Copy Server artifacts
Write-Host "[3] Staging Server artifacts from $ServerBuild..." -ForegroundColor Yellow
if (Test-Path $ServerBuild) {
    $ServerArtifacts = @("Pulsar.exe", "Pulsar.Common.dll", "MessagePack.dll", "LZ4.dll")
    foreach ($artifact in $ServerArtifacts) {
        $Source = "$ServerBuild\$artifact"
        $Dest = "$TestRoot\Server\$artifact"
        if (Test-Path $Source) {
            Copy-Item -Path $Source -Destination $Dest -Force
            $Size = (Get-Item $Dest).Length / 1MB
            Write-Host "    OK: $artifact" -ForegroundColor Green
        } else {
            Write-Host "    Missing: $artifact" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "    Directory not found: $ServerBuild" -ForegroundColor Red
}
Write-Host ""

# Validate
Write-Host "[4] Validating deployment..." -ForegroundColor Yellow
$Valid = $true
if (Test-Path "$TestRoot\Client\Client.exe") {
    $Size = (Get-Item "$TestRoot\Client\Client.exe").Length / 1MB
    Write-Host "    OK: Client.exe ($('{0:F2}' -f $Size) MB)" -ForegroundColor Green
} else {
    Write-Host "    ERR: Client.exe missing" -ForegroundColor Red
    $Valid = $false
}

if (Test-Path "$TestRoot\Server\Pulsar.exe") {
    $Size = (Get-Item "$TestRoot\Server\Pulsar.exe").Length / 1MB
    Write-Host "    OK: Pulsar.exe ($('{0:F2}' -f $Size) MB)" -ForegroundColor Green
} else {
    Write-Host "    ERR: Pulsar.exe missing" -ForegroundColor Red
    $Valid = $false
}
Write-Host ""

if ($Valid) {
    Write-Host "DEPLOYMENT SUCCESSFUL" -ForegroundColor Green
    Write-Host "=====================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Test Root: $TestRoot" -ForegroundColor Cyan
    Write-Host "Client:    $TestRoot\Client" -ForegroundColor Cyan
    Write-Host "Server:    $TestRoot\Server" -ForegroundColor Cyan
    Write-Host "Logs:      $TestRoot\Logs" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To start testing:" -ForegroundColor Yellow
    Write-Host "  1. & '$TestRoot\Server\Pulsar.exe'" -ForegroundColor Yellow
    Write-Host "  2. & '$TestRoot\Client\Client.exe'" -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "DEPLOYMENT FAILED" -ForegroundColor Red
    Write-Host "=================" -ForegroundColor Red
    exit 1
}
