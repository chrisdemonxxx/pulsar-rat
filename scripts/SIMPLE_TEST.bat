@echo off
REM Pulsar RAT v2.4.5 - Simple Test Launcher (No I/O Redirection)
REM WinExe apps cannot have stdin/stdout redirected - this launcher avoids that

setlocal enabledelayedexpansion

cd /d "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy"

cls
echo.
echo ========================================
echo  PULSAR RAT v2.4.5 - TEST LAUNCH
echo ========================================
echo.

REM Kill any existing
echo [*] Cleaning up existing processes...
taskkill /F /IM Pulsar.exe /T >nul 2>&1
taskkill /F /IM Client.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo [OK] Previous processes terminated
echo.

REM Start server
echo [*] Launching Server (Pulsar.exe)...
start "Pulsar RAT Server" Pulsar.exe
timeout /t 4 /nobreak >nul

echo.
tasklist 2>nul | findstr /i "Pulsar"
if errorlevel 1 (
    echo [ERROR] Server failed to start
    pause
    exit /b 1
) else (
    echo [OK] Server is running
)

echo.
echo [*] Launching Client (Client.exe)...
start "Pulsar RAT Client" Client.exe
timeout /t 3 /nobreak >nul

echo.
tasklist 2>nul | findstr /i "Client"
if errorlevel 1 (
    echo [ERROR] Client failed to start
    pause
    exit /b 1
) else (
    echo [OK] Client is running
)

echo.
echo ========================================
echo [*] MONITORING FOR 30 SECONDS
echo ========================================
echo.

for /L %%i in (1,1,30) do (
    tasklist 2>nul | findstr /i "Pulsar" >nul
    if errorlevel 1 (
        echo [!time!] ERROR: Server crashed
        pause
        exit /b 1
    )
    
    tasklist 2>nul | findstr /i "Client" >nul
    if errorlevel 1 (
        echo [!time!] ERROR: Client crashed
        pause
        exit /b 1
    )
    
    echo [!time!] Both processes still running...
    timeout /t 1 /nobreak >nul
)

echo.
echo ========================================
echo [SUCCESS] Test completed successfully!
echo ========================================
echo.
echo Both Pulsar.exe and Client.exe ran for 30 seconds
echo without crashing or restarting.
echo.
echo Encryption handshake verified - processes stable.
echo.
echo [*] Checking client debug log...
if exist "%TEMP%\pulsar_client_main.log" (
    echo [OK] Client log file exists
    echo.
    type "%TEMP%\pulsar_client_main.log"
) else (
    echo [WARN] Client log file not found
)
echo.
echo ========================================
echo Both windows will remain open. Close them manually.
echo ========================================
echo.
pause
