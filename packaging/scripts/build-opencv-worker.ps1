param(
    [Parameter(Mandatory=$true)][string]$PythonExecutable,
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$buildRoot = Join-Path $repoRoot "artifacts\opencv-worker"
$venv = Join-Path $buildRoot "venv"
$py = Join-Path $venv "Scripts\python.exe"
$runner = Join-Path $repoRoot "engines\ai\ai_runner.py"
$requirements = Join-Path $repoRoot "engines\ai\requirements.txt"
$dist = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repoRoot "release\staging\ai"
} else {
    $OutputDirectory
}
$work = Join-Path $buildRoot "pyinstaller-work"
$spec = Join-Path $buildRoot "spec"

if (-not (Test-Path $runner)) {
    throw "Missing engines\ai\ai_runner.py."
}
if (-not (Test-Path $requirements)) {
    throw "Missing engines\ai\requirements.txt."
}

New-Item -ItemType Directory -Force -Path $buildRoot, $dist, $work, $spec | Out-Null

if (-not (Test-Path $py)) {
    Write-Host "[1/4] Creating isolated worker build environment..." -ForegroundColor Cyan
    & $PythonExecutable -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw "Unable to create worker build environment." }
}

Write-Host "[2/4] Installing OpenCV worker build packages..." -ForegroundColor Cyan
& $py -m pip install --upgrade pip setuptools wheel
if ($LASTEXITCODE -ne 0) { throw "Unable to update Python build packages." }
& $py -m pip install -r $requirements
if ($LASTEXITCODE -ne 0) { throw "Unable to install OpenCV worker build packages." }

Write-Host "[3/4] Building standalone OpenCV worker..." -ForegroundColor Cyan
& $py -m PyInstaller --noconfirm --clean --onefile --name ai_runner `
    --distpath $dist --workpath $work --specpath $spec $runner
if ($LASTEXITCODE -ne 0) { throw "OpenCV worker build failed." }

$worker = Join-Path $dist "ai_runner.exe"
if (-not (Test-Path $worker)) { throw "OpenCV worker output not found: $worker" }

Write-Host "[4/4] Built: $worker" -ForegroundColor Green
