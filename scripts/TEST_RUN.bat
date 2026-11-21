@echo off
setlocal enabledelayedexpansion

set DEPLOY=C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy
set SERVER=%DEPLOY%\Pulsar.exe
set CLIENT=%DEPLOY%\Client.exe

echo Cleaning up processes...
taskkill /im Pulsar.exe /f 2>nul
taskkill /im Client.exe /f 2>nul
timeout /t 2 /nobreak

echo.
echo Starting server...
start "Pulsar Server" /min "%SERVER%"
timeout /t 5 /nobreak

echo Starting client...
start "Pulsar Client" /min "%CLIENT%"
timeout /t 5 /nobreak

echo.
echo ========================================
echo Monitoring for 30 seconds...
echo ========================================

setlocal enabledelayedexpansion
set TESTPASS=1

for /l %%i in (1,1,30) do (
  tasklist /fi "imagename eq Pulsar.exe" 2>nul | find /i "Pulsar.exe" >nul
  if errorlevel 1 (
    echo [%%is] FAIL - Server crashed
    set TESTPASS=0
    goto DONE
  )
  
  tasklist /fi "imagename eq Client.exe" 2>nul | find /i "Client.exe" >nul
  if errorlevel 1 (
    echo [%%is] FAIL - Client crashed  
    set TESTPASS=0
    goto DONE
  )
  
  echo [%%is] OK - Both running
  timeout /t 1 /nobreak >nul
)

:DONE
echo.
if %TESTPASS% equ 1 (
  echo ========================================
  echo SUCCESS - Both processes ran 30+ seconds
  echo ========================================
) else (
  echo ========================================
  echo FAILED - See messages above
  echo ========================================
)

taskkill /im Pulsar.exe /f 2>nul
taskkill /im Client.exe /f 2>nul
