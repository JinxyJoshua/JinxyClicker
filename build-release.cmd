@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  Builds BOTH installers for a release, in one go.
rem
rem    dist\JinxyAutoClicker-Beta-Setup-<version>.exe   -> the public repo
rem    dist\JinxyAutoClicker-DEV-Setup-<version>.exe    -> the private repo
rem
rem  They are the same app. The DEV one additionally has the DEV tab, and it
rem  updates itself from the private repository rather than from public
rem  releases - which is the only reason a second installer has to exist at all.
rem  The public installer contains no developer code, so a dev build that
rem  installed it would replace itself with the public app.
rem
rem  Before running: bump <Version> in JinxyClicker.csproj and AppVersion in
rem  Installer\JinxyClicker.iss to the same number. The app compares its own
rem  version against the release tag, so a mismatch means the update prompt
rem  measures against a version nobody shipped.
rem ---------------------------------------------------------------------------

cd /d "%~dp0"

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Could not find ISCC.exe ^(the Inno Setup compiler^).
  pause
  exit /b 1
)

echo.
echo ============================================================
echo  1 of 4  Publishing the PUBLIC build
echo ============================================================
dotnet publish JinxyClicker.csproj -c Release -r win-x64 --self-contained true ^
  --nologo -v quiet
if errorlevel 1 goto :failed

echo.
echo ============================================================
echo  2 of 4  Publishing the DEV build
echo ============================================================
rem Its own output and intermediate folders, so the two publishes cannot
rem overwrite each other's files.
dotnet publish JinxyClicker.csproj -c Release -r win-x64 --self-contained true ^
  -p:DevTools=true ^
  -p:BaseOutputPath=bin\DevBuild\ ^
  -p:BaseIntermediateOutputPath=obj\DevBuild\ ^
  --nologo -v quiet
if errorlevel 1 goto :failed

echo.
echo ============================================================
echo  3 of 4  Packaging the PUBLIC installer
echo ============================================================
"%ISCC%" "Installer\JinxyClicker.iss"
if errorlevel 1 goto :failed

echo.
echo ============================================================
echo  4 of 4  Packaging the DEV installer
echo ============================================================
"%ISCC%" /DDEV=1 "Installer\JinxyClicker.iss"
if errorlevel 1 goto :failed

echo.
echo ============================================================
echo  Done. Both installers are in dist\
echo ============================================================
dir /b dist\*.exe
echo.
echo  Upload the Beta one to the public repo, the DEV one to the private repo,
echo  under the same tag.
echo.
pause
exit /b 0

:failed
echo.
echo Something failed above. Nothing was uploaded anywhere.
pause
exit /b 1
