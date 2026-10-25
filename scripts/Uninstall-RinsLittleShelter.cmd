@echo off
setlocal
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
if not exist "%~dp0Uninstall-RinsLittleShelter.ps1" (
  echo Please extract the complete package before running this file.
  pause
  exit /b 1
)
rem Release this launcher and use a neutral working directory before removing files.
start "Uninstall Rin" /D "%TEMP%" "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-RinsLittleShelter.ps1" %*
exit /b
