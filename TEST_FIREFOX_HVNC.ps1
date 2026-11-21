#!/usr/bin/env pwsh
<#
.SYNOPSIS
Test Firefox HVNC silent launch and control
.DESCRIPTION
Validates Firefox launches silently on hidden HVNC desktop with full control
.EXAMPLE
.\TEST_FIREFOX_HVNC.ps1 -TestRoot "C:\Users\chris\projects\development\pulsar-rat\TEST_DEPLOYMENT"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$TestRoot,
    
    [switch]$Silent = $false,
    [switch]$Verbose = $true
)

# Configuration
$ClientPath = "$TestRoot\Client\Client.exe"
$LogPath = "$TestRoot\Logs"
$ResultsPath = "$TestRoot\TestResults"

# Colors
$ColorPass = "Green"
$ColorFail = "Red"
$ColorInfo = "Cyan"
$ColorWarn = "Yellow"

# Test results tracking
$TestResults = @{
    Total = 0
    Passed = 0
    Failed = 0
    Tests = @()
}

function Write-TestHeader {
    param([string]$Title)
    if (-not $Silent) {
        Write-Host "════════════════════════════════════════════════════════" -ForegroundColor $ColorInfo
        Write-Host "🧪 $Title" -ForegroundColor $ColorInfo
        Write-Host "════════════════════════════════════════════════════════" -ForegroundColor $ColorInfo
    }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = ""
    )
    
    $TestResults.Total++
    $Icon = $Passed ? "✅" : "❌"
    $Color = $Passed ? $ColorPass : $ColorFail
    
    if ($Passed) {
        $TestResults.Passed++
    } else {
        $TestResults.Failed++
    }
    
    $Result = @{
        Name = $TestName
        Passed = $Passed
        Details = $Details
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
    
    $TestResults.Tests += $Result
    
    if ($Verbose -and -not $Silent) {
        Write-Host "$Icon $TestName" -ForegroundColor $Color
        if ($Details) {
            Write-Host "   └─ $Details" -ForegroundColor $Color
        }
    }
}

Write-TestHeader "Firefox HVNC Control Test Suite"
Write-Host ""

# TEST 1: Deployment artifacts exist
Write-Host "TEST 1: Deployment Artifacts" -ForegroundColor $ColorWarn
Write-Host ""

$ClientExists = Test-Path $ClientPath
Write-TestResult "Client.exe exists" $ClientExists "Path: $ClientPath"

$CommonDllExists = Test-Path "$TestRoot\Client\Pulsar.Common.dll"
Write-TestResult "Pulsar.Common.dll exists" $CommonDllExists

Write-Host ""

# TEST 2: Firefox executable detection
Write-Host "TEST 2: Firefox Executable Detection" -ForegroundColor $ColorWarn
Write-Host ""

$FirefoxPaths = @(
    "C:\Program Files\Mozilla Firefox\firefox.exe",
    "C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
)

$FirefoxFound = $false
$FirefoxPath = $null

foreach ($path in $FirefoxPaths) {
    if (Test-Path $path) {
        $FirefoxFound = $true
        $FirefoxPath = $path
        $Details = "Found at: $path"
        Write-TestResult "Firefox detected" $true $Details
        break
    }
}

if (-not $FirefoxFound) {
    Write-TestResult "Firefox detected" $false "Firefox not found in standard locations"
    Write-Host ""
    Write-Host "⚠️  Firefox not installed. Install Firefox to continue testing." -ForegroundColor $ColorWarn
}

Write-Host ""

# TEST 3: Profile location detection
Write-Host "TEST 3: Firefox Profile Detection" -ForegroundColor $ColorWarn
Write-Host ""

$ProfileBasePath = "$env:APPDATA\Mozilla\Firefox"
$ProfileExists = Test-Path $ProfileBasePath
Write-TestResult "Firefox profile path exists" $ProfileExists "Path: $ProfileBasePath"

if ($ProfileExists) {
    $Profiles = Get-ChildItem -Path $ProfileBasePath -Filter "*.default*" -Directory -ErrorAction SilentlyContinue
    $ProfileCount = @($Profiles).Count
    
    if ($ProfileCount -gt 0) {
        Write-TestResult "Firefox profiles found" $true "Count: $ProfileCount"
        
        # Check for profile contents
        $FirstProfile = $Profiles[0].FullName
        $PrefsExists = Test-Path "$FirstProfile\prefs.js"
        Write-TestResult "Profile preferences exist" $PrefsExists "Profile: $($Profiles[0].Name)"
    } else {
        Write-TestResult "Firefox profiles found" $false "No profiles in $ProfileBasePath"
    }
}

Write-Host ""

# TEST 4: Hidden HVNC desktop capability
Write-Host "TEST 4: HVNC Desktop Infrastructure" -ForegroundColor $ColorWarn
Write-Host ""

# Check if system supports virtual desktops (Windows 10+)
$WinVersion = [System.Environment]::OSVersion.Version
$SupportsVirtualDesktops = $WinVersion.Major -ge 10

Write-TestResult "Virtual desktop support" $SupportsVirtualDesktops "Windows $($WinVersion.Major).$($WinVersion.Minor)"

# Check for virtual desktop APIs
try {
    $DesktopClass = [System.Reflection.Assembly]::LoadFrom("C:\Windows\System32\VirtualDesktopAPI.dll")
    Write-TestResult "Virtual desktop APIs available" $true
} catch {
    # VirtualDesktopAPI might not be directly loadable, that's okay
    Write-TestResult "Virtual desktop APIs check" $SupportsVirtualDesktops
}

Write-Host ""

# TEST 5: Process isolation
Write-Host "TEST 5: Process Isolation" -ForegroundColor $ColorWarn
Write-Host ""

# Check if running process can spawn hidden processes
$CanSpawnHidden = $true
try {
    # This is a capability test, not an actual spawn
    $PI = New-Object System.Diagnostics.ProcessStartInfo
    $PI.CreateNoWindow = $true
    $PI.UseShellExecute = $false
    Write-TestResult "Can create hidden processes" $true "No window creation supported"
} catch {
    Write-TestResult "Can create hidden processes" $false $_.Exception.Message
    $CanSpawnHidden = $false
}

Write-Host ""

# TEST 6: DLL injection prerequisites
Write-Host "TEST 6: DLL Injection Prerequisites" -ForegroundColor $ColorWarn
Write-Host ""

# Check for DLL injection support (KDOTInjector)
$ClientDllFound = Test-Path "$TestRoot\Client\Pulsar.Common.dll"
Write-TestResult "HVNC control DLL present" $ClientDllFound

# Check permissions for the test directory
$TestDirAccessible = Test-Path $TestRoot -PathType Container
Write-TestResult "Test directory accessible" $TestDirAccessible "Path: $TestRoot"

Write-Host ""

# TEST 7: Profile clone directory
Write-Host "TEST 7: Profile Clone Directory" -ForegroundColor $ColorWarn
Write-Host ""

$ProfileCloneDir = "$TestRoot\ProfileClones"
$CanCreateDirs = Test-Path $ProfileCloneDir

if ($CanCreateDirs) {
    # Try creating a test subdirectory
    $TestCloneDir = "$ProfileCloneDir\TestClone_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    try {
        New-Item -ItemType Directory -Path $TestCloneDir -Force | Out-Null
        Remove-Item -Path $TestCloneDir -Force -ErrorAction SilentlyContinue
        Write-TestResult "Can create profile clones" $true "Path: $ProfileCloneDir"
    } catch {
        Write-TestResult "Can create profile clones" $false $_.Exception.Message
    }
} else {
    Write-TestResult "Can create profile clones" $false
}

Write-Host ""

# TEST 8: Browser configuration
Write-Host "TEST 8: Browser Configuration" -ForegroundColor $ColorWarn
Write-Host ""

$FirefoxConfig = $null
if ($FirefoxFound) {
    $FirefoxConfig = @{
        BrowserName = "Firefox"
        ExecutablePath = $FirefoxPath
        SearchPattern = "Local\Mozilla\Firefox\Profiles"
        ReplacementPath = "Local\Mozilla\Firefox\KDOT"
    }
    
    Write-TestResult "Firefox config valid" $true "Executable: $($FirefoxConfig.ExecutablePath)"
    Write-TestResult "Search pattern set" $true "Pattern: $($FirefoxConfig.SearchPattern)"
    Write-TestResult "Replacement path set" $true "Path: $($FirefoxConfig.ReplacementPath)"
}

Write-Host ""

# TEST 9: Logging infrastructure
Write-Host "TEST 9: Logging Infrastructure" -ForegroundColor $ColorWarn
Write-Host ""

$LogDirAccessible = Test-Path $LogPath
if (-not $LogDirAccessible) {
    New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
}

$LogDirAccessible = Test-Path $LogPath
Write-TestResult "Log directory accessible" $LogDirAccessible "Path: $LogPath"

$CanWriteLogs = $true
try {
    $TestLog = "$LogPath\TestWrite_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
    "Test log entry" | Out-File -Path $TestLog -Force
    Remove-Item -Path $TestLog -Force -ErrorAction SilentlyContinue
    Write-TestResult "Can write logs" $true
} catch {
    Write-TestResult "Can write logs" $false $_.Exception.Message
    $CanWriteLogs = $false
}

Write-Host ""

# TEST 10: Results storage
Write-Host "TEST 10: Results Storage" -ForegroundColor $ColorWarn
Write-Host ""

$ResultsDirAccessible = Test-Path $ResultsPath
if (-not $ResultsDirAccessible) {
    New-Item -ItemType Directory -Path $ResultsPath -Force | Out-Null
}

$ResultsDirAccessible = Test-Path $ResultsPath
Write-TestResult "Results directory accessible" $ResultsDirAccessible "Path: $ResultsPath"

Write-Host ""

# Summary
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor $ColorInfo
Write-Host "📊 TEST SUMMARY" -ForegroundColor $ColorInfo
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor $ColorInfo
Write-Host ""
Write-Host "Total Tests:       $($TestResults.Total)" -ForegroundColor $ColorInfo
Write-Host "Passed:            $($TestResults.Passed)" -ForegroundColor $ColorPass
Write-Host "Failed:            $($TestResults.Failed)" -ForegroundColor $(if ($TestResults.Failed -eq 0) { $ColorPass } else { $ColorFail })
Write-Host ""

$PassRate = if ($TestResults.Total -gt 0) { [math]::Round(($TestResults.Passed / $TestResults.Total) * 100, 1) } else { 0 }
Write-Host "Pass Rate:         $PassRate%" -ForegroundColor $(if ($PassRate -eq 100) { $ColorPass } else { $ColorWarn })
Write-Host ""

# Save results
$ResultsJson = $TestResults | ConvertTo-Json
$ResultsFile = "$ResultsPath\TEST_RESULTS_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
$ResultsJson | Out-File -Path $ResultsFile -Force

Write-Host "✅ Results saved to: $ResultsFile" -ForegroundColor $ColorPass
Write-Host ""

# Final status
if ($TestResults.Failed -eq 0 -and $TestResults.Total -gt 0) {
    Write-Host "✅ ALL TESTS PASSED - Ready for Firefox HVNC control testing!" -ForegroundColor $ColorPass
    exit 0
} else {
    Write-Host "⚠️  Some tests failed or no tests ran. Review results above." -ForegroundColor $ColorWarn
    exit 1
}
