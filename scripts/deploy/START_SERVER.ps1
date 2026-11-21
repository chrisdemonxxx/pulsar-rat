# Pulsar RAT v2.4.5 - Server Startup Script
Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Pulsar RAT v2.4.5 - SERVER LAUNCHER  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverExe = Join-Path $scriptPath "Pulsar.exe"

if (-not (Test-Path $serverExe)) {
    Write-Host "[ERROR] Pulsar.exe not found at: $serverExe" -ForegroundColor Red
    exit 1
}

Write-Host "[*] Starting Pulsar Server..." -ForegroundColor Yellow
Write-Host "[*] Executable: $serverExe" -ForegroundColor Cyan
Write-Host "[*] Listening on: localhost:4783" -ForegroundColor Cyan
Write-Host "[*] Configuration: net9.0-windows, DEBUG mode, DummyCertificate" -ForegroundColor Cyan
Write-Host ""
Write-Host "═════════════════════════════════════════" -ForegroundColor Green
Write-Host "[+] Server started. Waiting for client connection..." -ForegroundColor Green
Write-Host "═════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

& $serverExe
