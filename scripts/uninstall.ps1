<#
  Removes the PixelPaws Desktop shortcut and disables launch-at-login.
  Does not delete the repo or built files.
#>
$ErrorActionPreference = "SilentlyContinue"

# Stop a running instance.
Get-Process PixelPaws -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove desktop shortcut.
$lnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "PixelPaws.lnk"
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "Removed desktop shortcut." -ForegroundColor Green }

# Remove auto-start entry.
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "PixelPaws" -ErrorAction SilentlyContinue
Write-Host "Auto-start at login: DISABLED" -ForegroundColor Green
