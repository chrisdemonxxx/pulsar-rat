@echo off
setlocal

set RUNTIME=C:\Users\chris\Desktop\Workspace\PulsarRAT_Runtime

echo Killing old processes...
taskkill /im Pulsar.exe /f 2>nul
taskkill /im Client.exe /f 2>nul
timeout /t 2 /nobreak

echo.
echo ========================================
echo Starting Pulsar SERVER on 4783...
echo ========================================
cd /d %RUNTIME%
start "Pulsar Server" Pulsar.exe
timeout /t 7 /nobreak

echo.
echo Checking port 4783...
netstat -ano | findstr "4783"

echo.
echo ========================================
echo Starting Pulsar CLIENT...
echo ========================================
start "Pulsar Client" Client.exe
timeout /t 5 /nobreak

echo.
echo Checking connections on 4783...
netstat -ano | findstr "4783"

echo.
echo Done. Both windows should be open.
