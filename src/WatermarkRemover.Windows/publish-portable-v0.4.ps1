$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$taskRoot = Resolve-Path (Join-Path $root "..\..")
$publishRoot = Join-Path $root "publish\win-x64"
$heavySource = Resolve-Path (Join-Path $root "..\v0.4-propainter")
$outputRoot = Join-Path $taskRoot "outputs-v0.4"
$outputDir = Join-Path $outputRoot "WatermarkRemoverWindows_v0.4_Portable"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK not found."
}

dotnet publish .\WatermarkRemoverWindows.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o .\publish\win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if (Test-Path $outputDir) {
    $resolved = (Resolve-Path $outputDir).Path
    $allowed = (Resolve-Path $outputRoot).Path
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected output path: $resolved"
    }
    Remove-Item $resolved -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

robocopy $publishRoot $outputDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
$code = $LASTEXITCODE
if ($code -gt 7) {
    throw "Failed to copy publish output (robocopy code $code)."
}

robocopy $heavySource (Join-Path $outputDir "heavy") /E `
    /XD .git __pycache__ smoke-results inputs .pytest_cache /XF *.pyc test_hd.mp4 test_hd_fixed.mp4 worker_heavy_fixed.mp4 test-regions.json worker-status.json /NFL /NDL /NJH /NJS /NP | Out-Null
$code = $LASTEXITCODE
if ($code -gt 7) {
    throw "Failed to copy ProPainter runtime (robocopy code $code)."
}

@"
双击 WatermarkRemoverWindows.exe 即可使用。

v0.4 包含三种处理模式：
1. 快速模式：FFmpeg Delogo
2. 算法修复：OpenCV Telea
3. 强力修复：ProPainter AI（需要 NVIDIA GPU，并会自动利用 GPU）

首次启动强力模式时会加载约 190MB 模型，之后不再需要联网。
请保留本文件夹中的 heavy、ai、tools 三个子目录，程序启动时会自动读取。
"@ | Set-Content -Path (Join-Path $outputDir "如何运行.txt") -Encoding UTF8

Write-Host "Portable v0.4 ready: $outputDir" -ForegroundColor Green
