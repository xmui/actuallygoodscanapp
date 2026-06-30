# Publishes a self-contained, portable build of Actually Good Scan.
#
# Output: build/portable/  — copy the whole folder to a USB stick and run ActuallyGoodScan.exe.
# A 'portable.marker' file is dropped in so settings and a default 'Scans' folder live alongside
# the app instead of in %LOCALAPPDATA%.
#
# Usage (from the repo root, on Windows with the .NET 8 SDK):
#   pwsh build/publish-portable.ps1
#   # or: powershell -ExecutionPolicy Bypass -File build/publish-portable.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src/ScanApp.App/ScanApp.App.csproj"
$out  = Join-Path $root "build/portable"

Write-Host "Publishing portable build to $out ..."

dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

# Mark the build as portable so it stores settings next to the exe.
$marker = Join-Path $out "portable.marker"
"This file makes Actually Good Scan run in portable mode." | Set-Content -Path $marker -Encoding UTF8

Write-Host ""
Write-Host "Done. Portable app is in: $out"
Write-Host "NOTE: For TWAIN scanners on 64-bit Windows, ensure 'twaindsm.dll' (x64) is present"
Write-Host "      next to ActuallyGoodScan.exe. NTwain ships it; if missing, copy it from the"
Write-Host "      NTwain package's runtimes/win-x64 folder."
