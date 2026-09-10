param(
    [string]$PythonExecutable = "python",
    [string]$RuntimeRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    $RuntimeRoot = Join-Path $repoRoot ".runtime\propainter"
}

$source = Join-Path $RuntimeRoot "ProPainter"
$venv = Join-Path $RuntimeRoot ".venv"
$py = Join-Path $venv "Scripts\python.exe"

New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null

if (-not (Test-Path $source)) {
    git clone --depth 1 https://github.com/sczhou/ProPainter.git $source
}

if (-not (Test-Path $py)) {
    & $PythonExecutable -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw "Unable to create ProPainter virtual environment." }
}

& $py -m pip install --upgrade pip setuptools wheel
if ($LASTEXITCODE -ne 0) { throw "Unable to update Python packaging tools." }

& $py -m pip install torch==2.5.1+cu121 torchvision==0.20.1+cu121 `
    --index-url https://download.pytorch.org/whl/cu121
if ($LASTEXITCODE -ne 0) { throw "Unable to install CUDA-enabled PyTorch." }

& $py -m pip install -r (Join-Path $source "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Unable to install ProPainter dependencies." }

Copy-Item (Join-Path $PSScriptRoot "heavy_runner.py") (Join-Path $RuntimeRoot "heavy_runner.py") -Force

Write-Host "Heavy runtime ready: $RuntimeRoot" -ForegroundColor Green
