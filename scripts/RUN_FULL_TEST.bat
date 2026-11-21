@echo off
setlocal enabledelayedexpansion

set RUNTIME=C:\Users\chris\Desktop\Workspace\PulsarRAT_Runtime
set SERVER=%RUNTIME%\Pulsar.exe
set CLIENT=%RUNTIME%\Client.exe

echo Cleaning up processes...
taskkill /im Pulsar.exe /f 2>nul
taskkill /im Client.exe /f 2>nul
timeout /t 2 /nobreak

echo.
echo ========================================
echo Starting Server with full runtime...
echo ========================================
cd /d %RUNTIME%
start "Pulsar Server" /min "%SERVER%"
timeout /t 6 /nobreak

echo.
echo Starting Client...
start "Pulsar Client" /min "%CLIENT%"
timeout /t 6 /nobreak

echo.
echo ========================================
echo Monitoring for 30 seconds...
echo ========================================

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
