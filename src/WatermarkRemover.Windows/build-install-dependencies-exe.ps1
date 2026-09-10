$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "Windows .NET Framework C# compiler not found."
}

$args = @(
    "/nologo",
    "/target:exe",
    "/out:$root\InstallDependencies.exe",
    "/resource:$root\setup-dependencies.ps1,setup-dependencies.ps1",
    "/resource:$root\setup-ffmpeg.ps1,setup-ffmpeg.ps1",
    "/resource:$root\setup-ai.ps1,setup-ai.ps1",
    "/resource:$root\ai\ai_runner.py,ai_runner.py",
    "$root\InstallDependencies.cs"
)

& $csc @args
if ($LASTEXITCODE -ne 0) {
    throw "InstallDependencies.exe build failed."
}

Write-Host "Built: $root\InstallDependencies.exe" -ForegroundColor Green
