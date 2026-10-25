@echo off
setlocal
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
if not exist "%~dp0Install-RinsLittleShelter.ps1" (
  echo Please extract the complete Setup ZIP before running this file.
  pause
  exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-RinsLittleShelter.ps1" %*
set "rin_setup_exit=%errorlevel%"
echo.
if not "%RIN_SETUP_NO_PAUSE%"=="1" pause
exit /b %rin_setup_exit%
