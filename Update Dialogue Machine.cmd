@echo off
setlocal
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /d "%~dp0"

echo ========================================================
echo Updating Dialogue Machine (Windows Package)
echo ========================================================
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Runtime win-x64
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

echo.
echo [SUCCESS] App updated in dist\Windows!
echo Launching Dialogue Machine...
start "" "%~dp0dist\Windows\DialogueMachine.exe"
timeout /t 2 >nul 2>nul
