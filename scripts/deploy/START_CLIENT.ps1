# Pulsar RAT v2.4.5 - Client Startup Script
Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Pulsar RAT v2.4.5 - CLIENT LAUNCHER  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientExe = Join-Path $scriptPath "Client.exe"

if (-not (Test-Path $clientExe)) {
    Write-Host "[ERROR] Client.exe not found at: $clientExe" -ForegroundColor Red
    exit 1
}

Write-Host "[*] Starting Pulsar Client..." -ForegroundColor Yellow
Write-Host "[*] Executable: $clientExe" -ForegroundColor Cyan
Write-Host "[*] Connecting to: localhost:4783" -ForegroundColor Cyan
Write-Host "[*] Configuration: net9.0-windows, DEBUG mode, Hash verification DISABLED" -ForegroundColor Cyan
Write-Host ""
Write-Host "═════════════════════════════════════════" -ForegroundColor Green
Write-Host "[+] Client started. Attempting connection..." -ForegroundColor Green
Write-Host "═════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "[*] Encryption Handshake:" -ForegroundColor Yellow
Write-Host "  1. Connecting to server..." -ForegroundColor White
Write-Host "  2. Receiving ClientIdentificationResult..." -ForegroundColor White
Write-Host "  3. Sending encrypted SetUserActiveWindowStatus..." -ForegroundColor White
Write-Host "  4. All subsequent traffic encrypted" -ForegroundColor White
Write-Host ""

& $clientExe
