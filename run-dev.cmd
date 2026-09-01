@echo off
rem ---------------------------------------------------------------------------
rem  Builds and runs the DEV version - the one with the DEV tab.
rem
rem  Visual Studio builds without -p:DevTools=true, so pressing F5 there gives
rem  the public app and overwrites bin\Debug with it. That is the correct
rem  default, and it is also why this exists: the dev build gets its own output
rem  and intermediate folders so the two cannot overwrite each other, and you
rem  can keep using Visual Studio normally for everything else.
rem
rem  Just double-click this file.
rem ---------------------------------------------------------------------------

cd /d "%~dp0"

echo Building the DEV version...
echo.

dotnet build JinxyClicker.csproj -c Debug -p:DevTools=true ^
  -p:BaseOutputPath=bin\DevBuild\ ^
  -p:BaseIntermediateOutputPath=obj\DevBuild\ ^
  --nologo -v quiet

if errorlevel 1 (
  echo.
  echo Build failed. Nothing was started.
  pause
  exit /b 1
)

echo Starting the DEV build. Look for the DEV tab at the bottom of the sidebar.
echo.

start "" "%~dp0bin\DevBuild\Debug\net10.0-windows\JinxyClicker.exe"
