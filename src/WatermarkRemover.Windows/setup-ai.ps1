$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$AiRoot = Join-Path $env:LOCALAPPDATA "WatermarkRemoverWindows\ai"
$Venv = Join-Path $AiRoot "venv"
$RunnerSource = Join-Path $ProjectRoot "ai\ai_runner.py"
$RunnerTarget = Join-Path $AiRoot "ai_runner.py"
$Py = Join-Path $Venv "Scripts\python.exe"

function Write-ProgressLine([int]$Percent, [string]$Text, [ConsoleColor]$Color = "Gray") {
    $p = [Math]::Max(0, [Math]::Min(100, $Percent))
    $filled = [int][Math]::Round($p / 5.0)
    $bar = ("=" * $filled) + ("-" * (20 - $filled))
    Write-Host ("[{0}] {1,3}%  {2}" -f $bar, $p, $Text) -ForegroundColor $Color
}

function Invoke-Native {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments
    )
    & $FilePath @Arguments
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Local inpaint runtime: $AiRoot" -ForegroundColor DarkGray
Write-ProgressLine 0 "Preparing local OpenCV runtime" "Cyan"
Write-Host "[1/4] Checking Python..." -ForegroundColor Cyan
Write-ProgressLine 10 "Checking bundled runner and Python version" "Cyan"
if (-not (Test-Path $RunnerSource)) { throw "Missing ai\ai_runner.py in this project." }

if (-not (Test-Path $Py)) {
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw "Python is required. Install Python 3.8+ and run this script again." }

    $versionOutput = & python -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"
    if ([version]$versionOutput -lt [version]"3.8") {
        throw "Python 3.8+ is required. Current Python is $versionOutput."
    }
}
Write-ProgressLine 25 "Python is ready" "Green"

New-Item -ItemType Directory -Force -Path $AiRoot | Out-Null
Copy-Item $RunnerSource $RunnerTarget -Force
Write-ProgressLine 35 "Copied local inpaint runner" "Green"

Write-Host "[2/4] Creating virtual environment..." -ForegroundColor Cyan
if (-not (Test-Path $Py)) {
    Write-ProgressLine 45 "Creating Python virtual environment" "Cyan"
    Invoke-Native python -m venv $Venv
} else {
    Write-ProgressLine 55 "Virtual environment already exists" "Green"
    Write-Host "Python virtual environment already exists. Skipping creation." -ForegroundColor DarkGray
}

Write-Host "[3/4] Installing local OpenCV dependencies..." -ForegroundColor Cyan
Write-ProgressLine 62 "Checking OpenCV and NumPy modules" "Cyan"
$moduleCheck = @"
import sys
try:
    import cv2
    import numpy
    sys.exit(0)
except Exception:
    sys.exit(1)
"@
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $Py -c $moduleCheck *> $null
    $modulesReady = ($LASTEXITCODE -eq 0)
} finally {
    $ErrorActionPreference = $previousErrorAction
}

if ($modulesReady) {
    Write-ProgressLine 100 "OpenCV and NumPy already installed" "Green"
    Write-Host "OpenCV and NumPy already installed. Skipping pip install." -ForegroundColor Green
} else {
    Write-ProgressLine 70 "Updating Python package tools" "Cyan"
    Invoke-Native $Py -m pip install --upgrade pip setuptools wheel
    Write-ProgressLine 82 "Installing NumPy and OpenCV" "Cyan"
    Invoke-Native $Py -m pip install numpy opencv-python-headless
    Write-ProgressLine 96 "Verifying OpenCV runtime" "Cyan"
    Invoke-Native $Py -c "import cv2, numpy; print('OpenCV runtime OK')"
}

Write-ProgressLine 100 "Local OpenCV runtime is ready" "Green"
Write-Host "[4/4] Done." -ForegroundColor Green
Write-Host "No ProPainter, PyTorch, or model weights are required for this OpenCV mode." -ForegroundColor Green
