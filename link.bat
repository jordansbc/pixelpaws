@echo off
REM One-time linker for PixelPaws folders that were downloaded as a ZIP.
REM Converts this folder into a proper GitHub checkout so update.bat works.
REM Double-click me once, then just use update.bat from now on.
cd /d "%~dp0"

echo ============================================
echo   Linking PixelPaws to GitHub
echo ============================================
echo.

git rev-parse --is-inside-work-tree >nul 2>&1
if not errorlevel 1 (
    echo This folder is already linked to GitHub - nothing to do.
    echo Just use update.bat from now on.
    echo.
    pause
    exit /b 0
)

echo Linking this folder to the PixelPaws repo...
git init >nul 2>&1
git remote remove origin >nul 2>&1
git remote add origin https://github.com/jordansbc/pixelpaws.git
git fetch origin
if errorlevel 1 goto fail
git checkout -f -B main origin/main
if errorlevel 1 goto fail
git branch --set-upstream-to=origin/main main >nul 2>&1

echo.
echo Done! This folder is now linked to GitHub.
echo From now on, just double-click update.bat to get the latest version.
echo.
pause
exit /b 0

:fail
echo.
echo Could not link to GitHub. Make sure Git is installed and you have
echo an internet connection, then run me again.
echo.
pause
exit /b 1
