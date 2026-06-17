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

REM If this folder was downloaded as a ZIP (no .git), link it to GitHub in place.
git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo This folder is not linked to GitHub yet - linking it now...
    git init >nul 2>&1
    git remote remove origin >nul 2>&1
    git remote add origin https://github.com/jordansbc/pixelpaws.git
    git fetch origin
    if errorlevel 1 goto pullfail
    git checkout -f -B main origin/main
    if errorlevel 1 goto pullfail
    git branch --set-upstream-to=origin/main main >nul 2>&1
) else (
    git pull
    if errorlevel 1 goto pullfail
)
echo.
goto pulldone

:pullfail
echo.
echo Could not pull from GitHub. Fix the error above, then run me again.
echo.
pause
exit /b 1

:pulldone

echo [2/3] Closing PixelPaws if it is running...
taskkill /IM PixelPaws.exe /F >nul 2>&1
echo.

echo [3/3] Rebuilding and relaunching...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"

echo.
echo Done! PixelPaws is up to date.
echo.
pause
