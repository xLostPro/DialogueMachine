@echo off
setlocal
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /d "%~dp0"
if exist "%~dp0dist\Windows\DialogueMachine.exe" (
  start "" "%~dp0dist\Windows\DialogueMachine.exe"
  exit /b 0
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Build the app with .NET 10 first, or use the packaged Windows executable.
  pause
  exit /b 1
)
dotnet run --project "%~dp0DialogueMachine.csproj" -c Release
if errorlevel 1 pause
