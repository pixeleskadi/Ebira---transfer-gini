@echo off
cd /d "%~dp0"
if exist "%~dp0dist\windows\Ebira.exe" (
  start "" "%~dp0dist\windows\Ebira.exe"
  exit /b 0
)
echo Build the native v1 Windows app first. See BUILD.md, or use the packaged Windows ZIP.
echo The legacy browser prototype is available separately through Start-PoC.cmd.
pause
