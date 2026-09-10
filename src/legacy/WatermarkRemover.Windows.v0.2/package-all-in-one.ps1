$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "publish\win-x64"
$stage = Join-Path $root "publish\all-in-one-stage"
$payload = Join-Path $root "publish\payload.zip"
$output = Join-Path $root "publish\WatermarkRemoverWindows_OpenCV_AllInOne.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "Windows .NET Framework C# compiler not found."
}

if (-not (Test-Path (Join-Path $publish "WatermarkRemoverWindows.exe"))) {
    throw "Publish output not found. Run dotnet publish first."
}
if (-not (Test-Path (Join-Path $root "InstallDependencies.exe"))) {
    & (Join-Path $root "build-install-dependencies-exe.ps1")
}
if (-not (Test-Path (Join-Path $root "ai\ai_runner.exe"))) {
    throw "Bundled OpenCV worker not found. Build ai\ai_runner.exe first."
}
if (-not (Test-Path (Join-Path $root "tools\ffmpeg.exe")) -or -not (Test-Path (Join-Path $root "tools\ffprobe.exe"))) {
    throw "Bundled FFmpeg files not found in tools."
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item (Join-Path $publish "WatermarkRemoverWindows.exe") (Join-Path $stage "WatermarkRemoverWindows.exe") -Force
Copy-Item (Join-Path $root "InstallDependencies.exe") (Join-Path $stage "InstallDependencies.exe") -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $stage "README.md") -Force
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") (Join-Path $stage "THIRD_PARTY_NOTICES.md") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stage "tools") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage "ai") | Out-Null
Copy-Item (Join-Path $root "tools\ffmpeg.exe") (Join-Path $stage "tools\ffmpeg.exe") -Force
Copy-Item (Join-Path $root "tools\ffprobe.exe") (Join-Path $stage "tools\ffprobe.exe") -Force
Copy-Item (Join-Path $root "ai\ai_runner.exe") (Join-Path $stage "ai\ai_runner.exe") -Force
if (Test-Path $payload) { Remove-Item $payload -Force }
if (Test-Path $output) { Remove-Item $output -Force }

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $payload -Force
if (-not (Test-Path $payload)) {
    throw "Payload zip was not created: $payload"
}

$frameworkDir = Split-Path -Parent $csc
$compression = Join-Path $frameworkDir "System.IO.Compression.dll"
$compressionFs = Join-Path $frameworkDir "System.IO.Compression.FileSystem.dll"
$forms = Join-Path $frameworkDir "System.Windows.Forms.dll"

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/out:$output",
    "/reference:$compression",
    "/reference:$compressionFs",
    "/reference:$forms",
    "/resource:$payload,payload.zip",
    (Join-Path $root "AllInOneLauncher.cs")
)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "All-in-one launcher build failed."
}

Write-Host "Built: $output" -ForegroundColor Green
