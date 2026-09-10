$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK not found. Install .NET 8 SDK first."
}

if (-not (Test-Path "$root\tools\ffmpeg.exe")) {
    Write-Warning "tools\ffmpeg.exe not found. Run setup-ffmpeg.ps1 first if you want it copied into the publish folder."
}

& "$root\build-install-dependencies-exe.ps1"

dotnet publish .\WatermarkRemoverWindows.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o .\publish\win-x64

Write-Host "Publish complete: $root\publish\win-x64" -ForegroundColor Green
