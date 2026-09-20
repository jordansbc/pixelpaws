<#
  PixelPaws installer.
  Builds the app (Release), creates a Desktop shortcut with the app icon, and enables
  launch-at-login. Re-run any time to update the shortcut after pulling new changes.
#>
$ErrorActionPreference = "Stop"

# Repo root = parent of this script's folder.
$root    = Split-Path -Parent $PSScriptRoot
$proj    = Join-Path $root "src\DesktopPet\DesktopPet.csproj"
$binRoot = Join-Path $root "src\DesktopPet\bin\Release"

Write-Host "Building PixelPaws (Release)..." -ForegroundColor Cyan
dotnet build $proj -c Release -nologo | Select-Object -Last 3

# Locate the built exe rather than assuming its path. The output directory carries the target
# framework and, since the project pins a RuntimeIdentifier for single-file publish, a RID
# subfolder as well. Hardcoding that path leaves the Desktop shortcut and the auto-start entry
# silently pointing at a stale build after any such change.
$exe = Get-ChildItem $binRoot -Recurse -Filter PixelPaws.exe -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw "Build did not produce a PixelPaws.exe under $binRoot" }
Write-Host "Using: $exe" -ForegroundColor DarkGray

# ── Desktop shortcut (with the embedded app icon) ──
$desktop  = [Environment]::GetFolderPath("Desktop")
$lnk      = Join-Path $desktop "PixelPaws.lnk"
$wsh      = New-Object -ComObject WScript.Shell
$sc       = $wsh.CreateShortcut($lnk)
$sc.TargetPath       = $exe
$sc.WorkingDirectory = Split-Path -Parent $exe
$sc.IconLocation     = "$exe,0"
$sc.Description      = "PixelPaws desktop pet"
$sc.Save()
Write-Host "Desktop shortcut created: $lnk" -ForegroundColor Green

# ── Launch at login (HKCU Run; same value the in-app 'Start with Windows' toggle uses) ──
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-ItemProperty -Path $runKey -Name "PixelPaws" -Value "`"$exe`"" -PropertyType String -Force | Out-Null
Write-Host "Auto-start at login: ENABLED" -ForegroundColor Green

Write-Host ""
Write-Host "Done! Launching PixelPaws..." -ForegroundColor Cyan
if (-not (Get-Process PixelPaws -ErrorAction SilentlyContinue)) {
    Start-Process $exe
}
