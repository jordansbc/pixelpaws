@echo off
REM One-click build of PixelPaws (Release). Double-click me.
cd /d "%~dp0"
echo Building PixelPaws (Release)...
dotnet build src\DesktopPet\DesktopPet.csproj -c Release
echo.
echo Build finished. The app is at:
echo   src\DesktopPet\bin\Release\net8.0-windows\PixelPaws.exe
echo.
pause
