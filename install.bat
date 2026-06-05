@echo off
REM One-click setup: builds PixelPaws, makes a Desktop shortcut, and enables auto-start at login.
REM Double-click me on any computer after cloning.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"
echo.
pause
