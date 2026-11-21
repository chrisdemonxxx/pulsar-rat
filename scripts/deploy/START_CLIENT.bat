@echo off
setlocal enabledelayedexpansion

echo.
echo ╔════════════════════════════════════════╗
echo ║  Pulsar RAT v2.4.5 - CLIENT LAUNCHER  ║
echo ╚════════════════════════════════════════╝
echo.

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
set "CLIENT_EXE=%SCRIPT_DIR%Client.exe"

REM Check if Client.exe exists
if not exist "%CLIENT_EXE%" (
    echo [ERROR] Client.exe not found at: %CLIENT_EXE%
    echo.
    pause
    exit /b 1
)

echo [*] Starting Pulsar Client...
echo [*] Executable: %CLIENT_EXE%
echo [*] Connecting to: localhost:4783
echo [*] Configuration: net9.0-windows, DEBUG mode, Hash verification DISABLED
echo.
echo ═════════════════════════════════════════
echo [+] Client started. Attempting connection...
echo ═════════════════════════════════════════
echo.
echo [*] Encryption Handshake:
echo   1. Connecting to server...
echo   2. Receiving ClientIdentificationResult...
echo   3. Sending encrypted SetUserActiveWindowStatus...
echo   4. All subsequent traffic encrypted
echo.

REM Start the client
"%CLIENT_EXE%"

REM If we get here, client has exited
echo.
echo [!] Client stopped
pause
