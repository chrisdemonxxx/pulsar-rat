@echo off
setlocal

set "DONUT_DIR=%~dp0..\external\donut"
if not exist "%DONUT_DIR%\Makefile.msvc" (
  echo ERROR: Donut submodule is not initialized at "%DONUT_DIR%"
  echo Run: git submodule update --init external/donut
  exit /b 1
)

set "VCVARSALL="
if not defined VCVARSALL if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARSALL=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARSALL if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARSALL=%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARSALL if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARSALL=%ProgramFiles%\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARSALL if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARSALL=%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"

if not defined VCVARSALL (
  echo ERROR: Visual Studio 2022 C++ Build Tools were not found.
  exit /b 1
)

call "%VCVARSALL%" amd64
if errorlevel 1 (
  echo ERROR: Visual Studio C++ environment initialization failed.
  exit /b 1
)

set "DONUT_BACKUP=%~dp0obj\donut-source-backup"
if not exist "%DONUT_BACKUP%" mkdir "%DONUT_BACKUP%"
copy /Y "%DONUT_DIR%\loader_exe_x64.h" "%DONUT_BACKUP%\loader_exe_x64.h" >nul
copy /Y "%DONUT_DIR%\loader_exe_x64.go" "%DONUT_BACKUP%\loader_exe_x64.go" >nul
if errorlevel 1 (
  echo ERROR: Could not preserve Donut generated source files.
  exit /b 1
)

pushd "%DONUT_DIR%"
set "PATH=%CD%;%PATH%"
nmake /NOLOGO -f Makefile.msvc
set "BUILD_EXIT=%ERRORLEVEL%"
popd
copy /Y "%DONUT_BACKUP%\loader_exe_x64.h" "%DONUT_DIR%\loader_exe_x64.h" >nul
copy /Y "%DONUT_BACKUP%\loader_exe_x64.go" "%DONUT_DIR%\loader_exe_x64.go" >nul
rmdir /S /Q "%DONUT_BACKUP%"

if not "%BUILD_EXIT%"=="0" (
  echo ERROR: Donut nmake failed with exit code %BUILD_EXIT%.
  exit /b %BUILD_EXIT%
)

if not exist "%DONUT_DIR%\donut.exe" (
  echo ERROR: nmake completed without producing donut.exe.
  exit /b 1
)

echo Donut build completed successfully.
exit /b 0
