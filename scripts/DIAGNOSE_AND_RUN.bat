@echo off
setlocal enabledelayedexpansion

echo.
echo ========================================================================
echo PULSAR RAT v2.4.5 - DIAGNOSTIC LAUNCHER
echo ========================================================================
echo.

REM Kill any existing processes
taskkill /F /IM Pulsar.exe >nul 2>&1
taskkill /F /IM Client.exe >nul 2>&1
timeout /t 2 /nobreak >nul

set DEPLOY=C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
set DIAG_LOG=C:\Users\chris\Desktop\Workspace\DIAGNOSTICS.txt

echo [INFO] Checking environment...
echo. > "%DIAG_LOG%"
echo PULSAR RAT DIAGNOSTICS >> "%DIAG_LOG%"
echo Timestamp: %date% %time% >> "%DIAG_LOG%"
echo. >> "%DIAG_LOG%"

echo .NET Runtime Check:
dotnet --version >> "%DIAG_LOG%"
dotnet --list-runtimes | findstr /i "WindowsDesktop" >> "%DIAG_LOG%"

echo [INFO] Binaries found:
dir /s "%DEPLOY%\*.exe" >> "%DIAG_LOG%"

echo.
echo [*] Launching SERVER...
cd /d "%DEPLOY%"
echo Starting: Pulsar.exe >> "%DIAG_LOG%"
echo. >> "%DIAG_LOG%"

start "" Pulsar.exe

timeout /t 5 /nobreak >nul

tasklist | findstr /i "Pulsar" >> "%DIAG_LOG%"
if errorlevel 1 (
    echo [ERROR] Server crashed or not running >> "%DIAG_LOG%"
) else (
    echo [OK] Server is running >> "%DIAG_LOG%"
)

echo.
echo [*] Launching CLIENT...
echo. >> "%DIAG_LOG%"
echo Starting: Client.exe >> "%DIAG_LOG%"
echo. >> "%DIAG_LOG%"

start "" Client.exe

timeout /t 4 /nobreak >nul

tasklist | findstr /i "Client" >> "%DIAG_LOG%"
if errorlevel 1 (
    echo [ERROR] Client crashed or not running >> "%DIAG_LOG%"
) else (
    echo [OK] Client is running >> "%DIAG_LOG%"
)

echo.
echo [*] Monitoring for 15 seconds...
echo. >> "%DIAG_LOG%"
echo PROCESS MONITORING: >> "%DIAG_LOG%"

for /L %%i in (1,1,15) do (
    echo [%%i s] Processes: >> "%DIAG_LOG%"
    tasklist | findstr /i "Pulsar\|Client" >> "%DIAG_LOG%"
    timeout /t 1 /nobreak >nul
)

echo.
echo [*] Results written to: %DIAG_LOG%
echo.
type "%DIAG_LOG%"
echo.
pause
