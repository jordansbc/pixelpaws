@echo off
REM One-click update for PixelPaws.
REM Pulls the latest code, closes the running pet, rebuilds, and relaunches.
REM Double-click me on any computer to get the newest version.
cd /d "%~dp0"

echo ============================================
echo   Updating PixelPaws
echo ============================================
echo.

echo [1/3] Pulling latest changes from GitHub...
git pull
if errorlevel 1 (
    echo.
    echo Could not pull from GitHub. Fix the error above, then run me again.
    echo.
    pause
    exit /b 1
)
echo.

echo [2/3] Closing PixelPaws if it is running...
taskkill /IM PixelPaws.exe /F >nul 2>&1
echo.

echo [3/3] Rebuilding and relaunching...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"

echo.
echo Done! PixelPaws is up to date.
echo.
pause
