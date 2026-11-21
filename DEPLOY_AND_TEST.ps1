#!/usr/bin/env pwsh
<#
.SYNOPSIS
Master orchestrator for Firefox HVNC fix local deployment and testing
.DESCRIPTION
Coordinates all deployment, validation, and testing phases
#>

param(
    [switch]$QuickTest,
    [switch]$FullTest,
    [switch]$StressTest,
    [switch]$SkipDeploy,
    [switch]$SkipValidation,
    [switch]$SkipTests,
    [switch]$Verbose = $true
)

$ErrorActionPreference = "Continue"

# Configuration
$ProjectRoot = "C:\Users\chris\projects\development\pulsar-rat"
$DeployScript = "$ProjectRoot\DEPLOY_LOCAL_TEST.ps1"
$ValidationScript = "$ProjectRoot\TEST_FIREFOX_HVNC.ps1"
$ControlScript = "$ProjectRoot\TEST_HVNC_CONTROL.ps1"

# Colors
$ColorHeader = "Cyan"
$ColorSuccess = "Green"
$ColorError = "Red"
$ColorWarn = "Yellow"
$ColorStep = "Magenta"

# Determine test mode
$TestMode = if ($StressTest) { "stress" } elseif ($FullTest) { "full" } else { "simple" }
$RunTests = -not $SkipTests

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "====================================================" -ForegroundColor $ColorHeader
    Write-Host $Message -ForegroundColor $ColorHeader
    Write-Host "====================================================" -ForegroundColor $ColorHeader
    Write-Host ""
}

function Write-Step {
    param([string]$Message, [int]$Number)
    Write-Host "[$Number] $Message" -ForegroundColor $ColorStep
    Write-Host "----------------------------------------------------" -ForegroundColor $ColorStep
}

function Write-Result {
    param([string]$Message, [bool]$Success)
    $Icon = $Success ? "OK " : "ERR"
    $Color = $Success ? $ColorSuccess : $ColorError
    Write-Host "[$Icon] $Message" -ForegroundColor $Color
}

# Main execution
Write-Header "Firefox HVNC Fix - Deployment and Testing"

Write-Host "Configuration:" -ForegroundColor $ColorWarn
Write-Host "  Test Mode:        $TestMode" -ForegroundColor $ColorWarn
Write-Host "  Skip Deploy:      $SkipDeploy" -ForegroundColor $ColorWarn
Write-Host "  Skip Validation:  $SkipValidation" -ForegroundColor $ColorWarn
Write-Host "  Skip Tests:       $SkipTests" -ForegroundColor $ColorWarn
Write-Host "  Verbose:          $Verbose" -ForegroundColor $ColorWarn
Write-Host ""

# PHASE 1: Deployment
if (-not $SkipDeploy) {
    Write-Step "DEPLOYING BUILD ARTIFACTS" 1
    
    if (Test-Path $DeployScript) {
        try {
            & $DeployScript -Validate -Verbose
            Write-Result "Deployment completed successfully" $true
        } catch {
            Write-Result "Deployment failed: $($_.Exception.Message)" $false
            Write-Host ""
            exit 1
        }
    } else {
        Write-Result "Deployment script not found: $DeployScript" $false
        exit 1
    }
} else {
    Write-Step "SKIPPING DEPLOYMENT" 1
    Write-Host "Using existing deployment at: $ProjectRoot\TEST_DEPLOYMENT"
}

Write-Host ""

# PHASE 2: Validation
if (-not $SkipValidation) {
    Write-Step "VALIDATING TEST ENVIRONMENT" 2
    
    $TestRoot = "$ProjectRoot\TEST_DEPLOYMENT"
    
    if (Test-Path $ValidationScript) {
        try {
            & $ValidationScript -TestRoot $TestRoot -Verbose
            Write-Result "Environment validation completed" $true
        } catch {
            Write-Result "Validation error (non-critical): $($_.Exception.Message)" $false
        }
    } else {
        Write-Result "Validation script not found: $ValidationScript" $false
    }
} else {
    Write-Step "SKIPPING VALIDATION" 2
    Write-Host "Proceeding with existing deployment"
}

Write-Host ""

# PHASE 3: HVNC Control Testing
if ($RunTests) {
    Write-Step "TESTING HVNC BROWSER CONTROL" 3
    
    $TestRoot = "$ProjectRoot\TEST_DEPLOYMENT"
    
    if (Test-Path $ControlScript) {
        try {
            Write-Host "Running $TestMode mode test..." -ForegroundColor $ColorWarn
            Write-Host ""
            
            & $ControlScript -TestRoot $TestRoot -TestMode $TestMode -Verbose
            Write-Result "HVNC control testing completed" $true
        } catch {
            Write-Result "HVNC testing error (non-critical): $($_.Exception.Message)" $false
        }
    } else {
        Write-Result "Control test script not found: $ControlScript" $false
    }
} else {
    Write-Step "SKIPPING HVNC TESTING" 3
    Write-Host "Tests disabled by user"
}

Write-Host ""

# PHASE 4: Summary and next steps
Write-Header "Deployment Complete"

$TestRoot = "$ProjectRoot\TEST_DEPLOYMENT"

Write-Host "DEPLOYMENT SUMMARY:" -ForegroundColor $ColorWarn
Write-Host ""
Write-Host "Location: $TestRoot" -ForegroundColor $ColorWarn
Write-Host ""
Write-Host "Components:" -ForegroundColor $ColorWarn
Write-Host "  Client:  $TestRoot\Client\Client.exe" -ForegroundColor $ColorWarn
Write-Host "  Server:  $TestRoot\Server\Pulsar.exe" -ForegroundColor $ColorWarn
Write-Host "  Logs:    $TestRoot\Logs" -ForegroundColor $ColorWarn
Write-Host "  Results: $TestRoot\TestResults" -ForegroundColor $ColorWarn
Write-Host ""

Write-Host "NEXT STEPS:" -ForegroundColor $ColorStep
Write-Host ""
Write-Host "1. Start Server (PowerShell window 1):" -ForegroundColor $ColorWarn
Write-Host "   & '$TestRoot\Server\Pulsar.exe'" -ForegroundColor $ColorWarn
Write-Host ""
Write-Host "2. Launch Client (PowerShell window 2):" -ForegroundColor $ColorWarn
Write-Host "   & '$TestRoot\Client\Client.exe'" -ForegroundColor $ColorWarn
Write-Host ""
Write-Host "3. Test Firefox Launch:" -ForegroundColor $ColorWarn
Write-Host "   In Pulsar: HVNC Desktop -> Start Process" -ForegroundColor $ColorWarn
Write-Host "   Process Name: Mozilla" -ForegroundColor $ColorWarn
Write-Host ""
Write-Host "4. Verify:" -ForegroundColor $ColorWarn
Write-Host "   - Firefox silent (no visible window)" -ForegroundColor $ColorWarn
Write-Host "   - Profile clone directory created" -ForegroundColor $ColorWarn
Write-Host "   - Logs show successful injection" -ForegroundColor $ColorWarn
Write-Host ""

Write-Host "MONITOR LOGS:" -ForegroundColor $ColorStep
Write-Host "   Get-Content '$TestRoot\Logs\*.log' -Tail 50" -ForegroundColor $ColorWarn
Write-Host ""

Write-Host "====================================================" -ForegroundColor $ColorSuccess
Write-Host "DEPLOYMENT READY FOR TESTING" -ForegroundColor $ColorSuccess
Write-Host "====================================================" -ForegroundColor $ColorSuccess
Write-Host ""
