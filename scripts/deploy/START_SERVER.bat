@echo off
setlocal enabledelayedexpansion

echo.
echo ╔════════════════════════════════════════╗
echo ║  Pulsar RAT v2.4.5 - SERVER LAUNCHER  ║
echo ╚════════════════════════════════════════╝
echo.

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
set "SERVER_EXE=%SCRIPT_DIR%Pulsar.exe"

REM Check if Pulsar.exe exists
if not exist "%SERVER_EXE%" (
    echo [ERROR] Pulsar.exe not found at: %SERVER_EXE%
    echo.
    pause
    exit /b 1
)

echo [*] Starting Pulsar Server...
echo [*] Executable: %SERVER_EXE%
echo [*] Listening on: localhost:4783
echo [*] Configuration: net9.0-windows, DEBUG mode, DummyCertificate
echo.
echo ═════════════════════════════════════════
echo [+] Server started. Waiting for client connection...
echo ═════════════════════════════════════════
echo.

REM Start the server
"%SERVER_EXE%"

REM If we get here, server has exited
echo.
echo [!] Server stopped
pause
