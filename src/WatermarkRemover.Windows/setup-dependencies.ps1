$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
}

function Write-ProgressLine([int]$Percent, [string]$Text, [ConsoleColor]$Color = "Gray") {
    $p = [Math]::Max(0, [Math]::Min(100, $Percent))
    $filled = [int][Math]::Round($p / 5.0)
    $bar = ("=" * $filled) + ("-" * (20 - $filled))
    Write-Host ("[{0}] {1,3}%  {2}" -f $bar, $p, $Text) -ForegroundColor $Color
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Refresh-Path {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machinePath, $userPath, $env:Path) -join [IO.Path]::PathSeparator
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

function Invoke-SetupScript([string]$ScriptPath) {
    & $ScriptPath
    if (-not $?) {
        throw "$ScriptPath failed."
    }
}

function Ensure-WingetPackage([string]$CommandName, [string]$PackageId, [string]$DisplayName) {
    if (Test-Command $CommandName) {
        Write-Host "$DisplayName detected." -ForegroundColor DarkGray
        return
    }

    if (-not (Test-Command winget)) {
        throw "$DisplayName is required, but winget is unavailable. Please install $DisplayName manually, then run this installer again."
    }

    Write-Host "$DisplayName not found. Installing with winget..." -ForegroundColor Yellow
    Invoke-Native winget install --id $PackageId --exact --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path

    if (-not (Test-Command $CommandName)) {
        Write-Host "$DisplayName was installed, but the current process cannot see it yet. Refreshing PATH once more..." -ForegroundColor Yellow
        Refresh-Path
    }
}

Write-Host "Watermark Remover Windows v0.2 dependency installer" -ForegroundColor Green
Write-Host "Project root: $ProjectRoot" -ForegroundColor DarkGray
Write-ProgressLine 0 "Starting dependency setup" "Green"

Write-Step "[1/4] Checking Windows tools"
Write-ProgressLine 8 "Checking Python" "Cyan"
Ensure-WingetPackage "python" "Python.Python.3.10" "Python"
Write-ProgressLine 20 "Python is ready" "Green"

Write-Step "[2/4] Checking FFmpeg"
Write-ProgressLine 25 "Checking FFmpeg cache and app copy" "Cyan"
Invoke-SetupScript (Join-Path $ProjectRoot "setup-ffmpeg.ps1")
Write-ProgressLine 50 "FFmpeg is ready" "Green"

Write-Step "[3/4] Installing local OpenCV inpaint runtime"
Write-ProgressLine 55 "Preparing local OpenCV runtime" "Cyan"
Invoke-SetupScript (Join-Path $ProjectRoot "setup-ai.ps1")
Write-ProgressLine 90 "Local OpenCV runtime is ready" "Green"

Write-Step "[4/4] Complete"
Write-ProgressLine 100 "All dependencies are ready" "Green"
Write-Host "FFmpeg has been installed to: $(Join-Path $ProjectRoot 'tools')" -ForegroundColor Green
Write-Host "Persistent FFmpeg has been installed to: $env:LOCALAPPDATA\WatermarkRemoverWindows\tools" -ForegroundColor Green
Write-Host "Local inpaint runtime has been installed to: $env:LOCALAPPDATA\WatermarkRemoverWindows\ai" -ForegroundColor Green
