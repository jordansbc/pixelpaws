<#
  Build the release artifact: a single self-contained PixelPaws.exe that runs on any Windows
  x64 machine with no .NET install, no git, and no Assets folder beside it (the art is embedded).

  Usage:
    .\scripts\publish.ps1              # build into .\dist
    .\scripts\publish.ps1 -Run         # ...and launch it to check it works
#>
param(
    [string]$OutDir = "dist",
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\DesktopPet\DesktopPet.csproj"
$out  = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }

Write-Host "Publishing PixelPaws (self-contained, single file)..." -ForegroundColor Cyan

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c Release -o $out --nologo | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Join-Path $out "PixelPaws.exe"
if (-not (Test-Path $exe)) { throw "No PixelPaws.exe in $out" }

# The .pdb is debugging symbols — useful to keep locally, not something to ship.
Get-ChildItem $out -Filter *.pdb | Remove-Item -Force

$version = (Get-Item $exe).VersionInfo.ProductVersion
$sizeMb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$hash    = (Get-FileHash $exe -Algorithm SHA256).Hash

Write-Host ""
Write-Host "  Built  : $exe"      -ForegroundColor Green
Write-Host "  Version: $version"  -ForegroundColor Green
Write-Host "  Size   : $sizeMb MB" -ForegroundColor Green
Write-Host "  SHA256 : $hash"     -ForegroundColor Green
Write-Host ""

# Publish leaves nothing but the exe; if an Assets folder appears here, embedding has regressed
# and users would get a cat with no artwork.
$stray = Get-ChildItem $out | Where-Object { $_.Name -ne "PixelPaws.exe" }
if ($stray) {
    Write-Warning "Unexpected extra files in the publish output (the exe should be self-sufficient):"
    $stray | ForEach-Object { Write-Warning "  $($_.Name)" }
}

Write-Host "To cut a release:" -ForegroundColor Cyan
Write-Host "  1. Bump <Version> in src/DesktopPet/DesktopPet.csproj"
Write-Host "  2. git tag v$($version.Split('+')[0]) && git push --tags"
Write-Host "  3. gh release create v$($version.Split('+')[0]) `"$exe`" --generate-notes"
Write-Host ""
Write-Host "  (Or just push the tag — .github/workflows/release.yml does 1-3 for you.)"

if ($Run) {
    Write-Host "Launching..." -ForegroundColor Cyan
    Start-Process $exe
}
