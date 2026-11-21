@echo off
cd /d "C:\Users\chris\Downloads\c2panel\pulsar"

echo Rebuilding Pulsar.Server with fixes...
echo.

REM Clean
dotnet clean Pulsar.Server/Pulsar.Server.csproj -c Debug --nologo >nul 2>&1

REM Build
echo Building...
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Debug --nologo

REM Check result
if exist "Pulsar.Server\bin\Debug\Pulsar.exe" (
    echo.
    echo SUCCESS: Pulsar.exe built
    for %%F in (Pulsar.Server\bin\Debug\Pulsar.exe) do (
        echo Size: %%~zF bytes
        echo Location: %%~F
    )
    
    echo.
    echo Copying to deploy folder...
    copy /Y "Pulsar.Server\bin\Debug\Pulsar.exe" "C:\Users\chris\Desktop\Workspace\PulsarRAT_Deploy\Pulsar.exe"
    
    if errorlevel 1 (
        echo ERROR: Failed to copy
    ) else (
        echo OK: Deployed
    )
) else (
    echo ERROR: Pulsar.exe not found
)

echo.
pause
