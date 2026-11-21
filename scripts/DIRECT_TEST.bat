@echo off
setlocal enabledelayedexpansion

echo Testing Pulsar RAT v2.4.5 - Direct Launch
echo.

cd /d "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"

echo Step 1: Killing any existing processes
taskkill /F /IM Pulsar.exe /T >nul 2>&1
taskkill /F /IM Client.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo Step 2: Starting server
echo.
echo Starting Pulsar.exe...
start "" Pulsar.exe
timeout /t 5 /nobreak >nul

echo.
echo Step 3: Checking if server is running
tasklist | findstr /i Pulsar
if errorlevel 1 (
    echo ERROR: Server did not start
) else (
    echo OK: Server is running
)

echo.
echo Step 4: Starting client
echo.
echo Starting Client.exe...
start "" Client.exe
timeout /t 3 /nobreak >nul

echo.
echo Step 5: Checking if client is running
tasklist | findstr /i Client
if errorlevel 1 (
    echo ERROR: Client did not start
) else (
    echo OK: Client is running
)

echo.
echo Step 6: Final status (after 30 seconds of monitoring)
echo Waiting...

for /L %%i in (1,1,30) do (
    tasklist | findstr /i Pulsar >nul
    if errorlevel 1 goto :server_crashed
    tasklist | findstr /i Client >nul
    if errorlevel 1 goto :client_crashed
    timeout /t 1 /nobreak >nul
)

echo.
echo SUCCESS: Both processes ran for 30 seconds
echo.
pause
goto :end

:server_crashed
echo.
echo ERROR: Server crashed or stopped
pause
goto :end

:client_crashed
echo.
echo ERROR: Client crashed or stopped
pause
goto :end

:end
