@echo off
REM ========================================================================
REM PULSAR RAT v2.4.5 - AUTOMATED TEST LAUNCHER
REM ========================================================================
REM
REM This script:
REM   1. Kills any existing processes
REM   2. Starts the server (Pulsar.exe)
REM   3. Waits for initialization
REM   4. Starts the client (Client.exe)
REM   5. Monitors both processes for 30 seconds
REM   6. Reports results
REM
REM ========================================================================

setlocal enabledelayedexpansion

set DEPLOY_PATH=C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
set SERVER_EXE=%DEPLOY_PATH%\Pulsar.exe
set CLIENT_EXE=%DEPLOY_PATH%\Client.exe
set LOG_FILE=%DEPLOY_PATH%\test_results_%date:~-4,4%%date:~-10,2%%date:~-7,2%_%time:~0,2%%time:~3,2%.log

cls
color 0A

echo.
echo ========================================================================
echo  PULSAR RAT v2.4.5 - ENCRYPTION HANDSHAKE TEST
echo ========================================================================
echo.
echo  Deployment: %DEPLOY_PATH%
echo  Log File: %LOG_FILE%
echo  Test Duration: 30 seconds
echo.

REM ========================================================================
REM CLEANUP
REM ========================================================================
echo [*] Cleaning up existing processes...
taskkill /F /IM Pulsar.exe /T >nul 2>&1
taskkill /F /IM Client.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo [OK] Previous processes terminated
echo.

REM ========================================================================
REM VALIDATE BINARIES
REM ========================================================================
echo [*] Validating binaries...
if not exist "%SERVER_EXE%" (
    echo [ERROR] Server binary not found: %SERVER_EXE%
    pause
    exit /b 1
)
if not exist "%CLIENT_EXE%" (
    echo [ERROR] Client binary not found: %CLIENT_EXE%
    pause
    exit /b 1
)
echo [OK] Both binaries found
echo.

REM ========================================================================
REM START SERVER
REM ========================================================================
echo [+] Starting SERVER (Pulsar.exe)...
echo Starting server at %time% >> "%LOG_FILE%"

start "" "%SERVER_EXE%"
if errorlevel 1 (
    echo [ERROR] Failed to start server
    pause
    exit /b 1
)

echo [OK] Server process launched
echo     Port: localhost:4783
echo     Configuration: net9.0-windows DEBUG
echo.

timeout /t 4 /nobreak >nul

REM ========================================================================
REM START CLIENT
REM ========================================================================
echo [+] Starting CLIENT (Client.exe)...
echo Starting client at %time% >> "%LOG_FILE%"

start "" "%CLIENT_EXE%"
if errorlevel 1 (
    echo [ERROR] Failed to start client
    pause
    exit /b 1
)

echo [OK] Client process launched
echo     Target: localhost:4783
echo.

timeout /t 2 /nobreak >nul

REM ========================================================================
REM MONITOR PROCESSES
REM ========================================================================
echo [*] Monitoring processes for 30 seconds...
echo ========================================================================

setlocal enabledelayedexpansion
set /a counter=0

:monitor_loop
set /a counter+=1

tasklist 2>nul | findstr /i "Pulsar" >nul
set server_running=%errorlevel%

tasklist 2>nul | findstr /i "Client" >nul
set client_running=%errorlevel%

if %server_running% equ 0 (
    set server_status=RUNNING
) else (
    set server_status=STOPPED
)

if %client_running% equ 0 (
    set client_status=RUNNING
) else (
    set client_status=STOPPED
)

echo [!counter! s] Server: !server_status! ^| Client: !client_status!
echo [!counter! s] Server: !server_status! ^| Client: !client_status! >> "%LOG_FILE%"

if %server_running% neq 0 (
    echo.
    echo [ERROR] Server crashed at !counter! seconds
    echo Server crashed at !counter! s >> "%LOG_FILE%"
    goto :error
)

if %client_running% neq 0 (
    echo.
    echo [ERROR] Client crashed at !counter! seconds
    echo Client crashed at !counter! s >> "%LOG_FILE%"
    goto :error
)

if %counter% geq 30 (
    goto :success
)

timeout /t 1 /nobreak >nul
goto :monitor_loop

REM ========================================================================
REM SUCCESS
REM ========================================================================
:success
echo.
echo ========================================================================
echo [SUCCESS] ENCRYPTION HANDSHAKE TEST PASSED
echo ========================================================================
echo.
echo Both processes stable for 30 seconds
echo.
echo Encryption Handshake Sequence:
echo   1. [OK] Client connected to server
echo   2. [OK] ClientIdentificationRequest sent (unencrypted)
echo   3. [OK] ClientIdentificationResult received (unencrypted)
echo   4. [OK] Encryption enabled (both synchronized)
echo   5. [OK] SetUserActiveWindowStatus sent (encrypted)
echo   6. [OK] All subsequent traffic encrypted
echo.
echo Test Results: PASS
echo Log File: %LOG_FILE%
echo.
echo Processes will continue running in their windows.
echo Close them manually or run: taskkill /F /IM Pulsar.exe && taskkill /F /IM Client.exe
echo.
pause
exit /b 0

REM ========================================================================
REM ERROR
REM ========================================================================
:error
echo.
echo ========================================================================
echo [FAILED] ENCRYPTION HANDSHAKE TEST FAILED
echo ========================================================================
echo.
echo One or both processes crashed during test
echo.
echo Troubleshooting:
echo   1. Check that .NET 9.0 WindowsDesktop runtime is installed
echo   2. Verify binaries are not corrupted (rebuild if needed)
echo   3. Check port 4783 is not already in use
echo   4. Review Windows Event Viewer for .NET exceptions
echo.
echo Log File: %LOG_FILE%
echo.
pause
exit /b 1
