param(
    [string]$PythonExecutable = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

dotnet build (Join-Path $repoRoot "src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj") `
    -c Release `
    --configfile (Join-Path $repoRoot "src\WatermarkRemover.Windows\NuGet.Config")
if ($LASTEXITCODE -ne 0) {
    throw ".NET build failed."
}

if ([string]::IsNullOrWhiteSpace($PythonExecutable)) {
    $command = Get-Command python -ErrorAction SilentlyContinue
    if (-not $command) { $command = Get-Command py -ErrorAction SilentlyContinue }
    if (-not $command) { throw "Python 3 executable not found. Pass -PythonExecutable." }
    $PythonExecutable = $command.Source
}

& $PythonExecutable -m py_compile `
    (Join-Path $repoRoot "engines\ai\ai_runner.py") `
    (Join-Path $repoRoot "engines\heavy\heavy_runner.py")
if ($LASTEXITCODE -ne 0) {
    throw "Python syntax validation failed."
}

Write-Host "Verification passed." -ForegroundColor Green
