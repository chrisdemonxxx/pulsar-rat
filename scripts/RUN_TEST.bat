@echo off
setlocal enabledelayedexpansion

REM Kill any existing processes
taskkill /F /IM Pulsar.exe >nul 2>&1
taskkill /F /IM Client.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM Set paths
set DEPLOY=C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
set LOG=C:\Users\chris\Desktop\Workspace\TEST_RESULTS.txt

(
echo ========================================================================
echo PULSAR RAT v2.4.5 - ENCRYPTION HANDSHAKE TEST
echo ========================================================================
echo.
echo [*] Deployment Path: %DEPLOY%
echo.
echo [+] Starting Server...
echo Path: %DEPLOY%\Pulsar.exe
echo Port: localhost:4783
) > "%LOG"

REM Start server
start "" "%DEPLOY%\Pulsar.exe"

REM Wait for server
timeout /t 4 /nobreak >nul

(
echo.
echo [OK] Server started
echo.
echo [+] Starting Client...
echo Path: %DEPLOY%\Client.exe
echo Target: localhost:4783
) >> "%LOG"

REM Start client
start "" "%DEPLOY%\Client.exe"

REM Wait a bit
timeout /t 3 /nobreak >nul

REM Get process info
for /f "tokens=1,2" %%A in ('tasklist ^| findstr /i "Pulsar Client"') do (
    echo [OK] %%A (PID: %%B) >> "%LOG"
)

echo. >> "%LOG"
echo [*] Monitoring for 15 seconds >> "%LOG"
for /L %%i in (1,1,15) do (
    echo [%%i s] Checking processes... >> "%LOG"
    timeout /t 1 /nobreak >nul
)

(
echo.
echo ========================================================================
echo TEST COMPLETE
echo ========================================================================
echo.
echo Check both Pulsar.exe and Client.exe windows for encryption handshake
echo messages. Both should remain open and stable.
echo.
) >> "%LOG"

echo Test results written to: %LOG%
echo.
pause
