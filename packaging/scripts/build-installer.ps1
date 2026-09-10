param(
    [string]$Configuration = "Release",
    [string]$PythonExecutable = "python",
    [string]$RuntimeRoot = "",
    [switch]$SkipHeavyBootstrap
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$staging = Join-Path $repoRoot "release\staging"
$appStage = Join-Path $staging "app"
$aiStage = Join-Path $staging "ai"
$portableStage = Join-Path $staging "portable"
$installerOutput = Join-Path $repoRoot "release\installer"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK not found."
}

& (Join-Path $PSScriptRoot "build-opencv-worker.ps1") `
    -PythonExecutable $PythonExecutable `
    -OutputDirectory $aiStage

if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    $RuntimeRoot = Join-Path $repoRoot ".runtime\propainter"
}
if (-not $SkipHeavyBootstrap -and -not (Test-Path (Join-Path $RuntimeRoot ".venv\Scripts\python.exe"))) {
    & (Join-Path $repoRoot "engines\heavy\bootstrap.ps1") `
        -PythonExecutable $PythonExecutable `
        -RuntimeRoot $RuntimeRoot
}

New-Item -ItemType Directory -Force -Path $appStage, $portableStage, $installerOutput | Out-Null

dotnet publish (Join-Path $repoRoot "src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $appStage
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

if (Test-Path $portableStage) {
    Remove-Item $portableStage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $portableStage | Out-Null
Copy-Item (Join-Path $appStage "*") $portableStage -Recurse -Force

$portableAi = Join-Path $portableStage "ai"
New-Item -ItemType Directory -Force -Path $portableAi | Out-Null
Copy-Item (Join-Path $aiStage "ai_runner.exe") $portableAi -Force
Copy-Item (Join-Path $repoRoot "engines\ai\ai_runner.py") $portableAi -Force

$portableTools = Join-Path $portableStage "tools"
New-Item -ItemType Directory -Force -Path $portableTools | Out-Null
foreach ($tool in @("ffmpeg.exe", "ffprobe.exe")) {
    $sourceTool = Join-Path $repoRoot "tools\$tool"
    if (-not (Test-Path $sourceTool)) {
        throw "Missing $tool. Place FFmpeg tools in the repository tools folder before packaging."
    }
    Copy-Item $sourceTool $portableTools -Force
}

$portableHeavy = Join-Path $portableStage "heavy"
New-Item -ItemType Directory -Force -Path $portableHeavy | Out-Null
Copy-Item (Join-Path $RuntimeRoot "heavy_runner.py") $portableHeavy -Force
Copy-Item (Join-Path $RuntimeRoot ".venv") (Join-Path $portableHeavy ".venv") -Recurse -Force
Copy-Item (Join-Path $RuntimeRoot "ProPainter") (Join-Path $portableHeavy "ProPainter") -Recurse -Force

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 compiler not found."
}

$drive = "W:"
if (Test-Path "$drive\") {
    subst $drive /D | Out-Null
}
subst $drive $repoRoot
try {
    $sourceRoot = "$drive\release\staging\portable"
    $outputRoot = "$drive\release\installer"
    & $iscc (Join-Path $repoRoot "packaging\innosetup\WatermarkRemoverWindows.iss") `
        "/DSourceRoot=$sourceRoot" `
        "/DOutputRoot=$outputRoot"
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed."
    }
} finally {
    subst $drive /D | Out-Null
}

Write-Host "Installer output: $installerOutput" -ForegroundColor Green
