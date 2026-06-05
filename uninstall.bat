@echo off
REM Removes the Desktop shortcut and disables auto-start (does not delete the app).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\uninstall.ps1"
echo.
pause
