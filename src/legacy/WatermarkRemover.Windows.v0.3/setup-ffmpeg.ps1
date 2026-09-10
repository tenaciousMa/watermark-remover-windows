$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tools = Join-Path $root "tools"
$localTools = Join-Path $env:LOCALAPPDATA "WatermarkRemoverWindows\tools"
$work = Join-Path $env:TEMP ("ffmpeg-watermark-remover-" + [Guid]::NewGuid().ToString("N"))
$zip = Join-Path $work "ffmpeg-release-essentials.zip"
$extract = Join-Path $work "extract"

function Write-ProgressLine([int]$Percent, [string]$Text, [ConsoleColor]$Color = "Gray") {
    $p = [Math]::Max(0, [Math]::Min(100, $Percent))
    $filled = [int][Math]::Round($p / 5.0)
    $bar = ("=" * $filled) + ("-" * (20 - $filled))
    Write-Host ("[{0}] {1,3}%  {2}" -f $bar, $p, $Text) -ForegroundColor $Color
}

function Save-FileWithProgress([string]$Uri, [string]$OutFile, [int]$StartPercent, [int]$EndPercent) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $request = [Net.HttpWebRequest]::Create($Uri)
    $request.UserAgent = "WatermarkRemoverWindows/0.2"
    $response = $request.GetResponse()
    $total = $response.ContentLength
    $input = $response.GetResponseStream()
    $output = [IO.File]::Open($OutFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $buffer = New-Object byte[] (1024 * 1024)
    $readTotal = [int64]0
    $lastShown = $StartPercent - 5

    try {
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $read)
            $readTotal += $read

            if ($total -gt 0) {
                $percent = $StartPercent + [int](($EndPercent - $StartPercent) * ($readTotal / [double]$total))
            } else {
                $percent = [Math]::Min($EndPercent, $StartPercent + [int]($readTotal / 10485760))
            }

            if ($percent -ge ($lastShown + 5) -or $percent -ge $EndPercent) {
                Write-ProgressLine $percent ("Downloading FFmpeg package... {0:n1} MB" -f ($readTotal / 1MB))
                $lastShown = $percent
            }
        }
    } finally {
        if ($output) { $output.Dispose() }
        if ($input) { $input.Dispose() }
        if ($response) { $response.Dispose() }
    }
}

Write-ProgressLine 0 "Preparing FFmpeg folders" "Cyan"
New-Item -ItemType Directory -Force -Path $tools | Out-Null
New-Item -ItemType Directory -Force -Path $localTools | Out-Null
Write-ProgressLine 10 "Checking installed FFmpeg files" "Cyan"

$projectFfmpeg = Join-Path $tools "ffmpeg.exe"
$projectFfprobe = Join-Path $tools "ffprobe.exe"
$localFfmpeg = Join-Path $localTools "ffmpeg.exe"
$localFfprobe = Join-Path $localTools "ffprobe.exe"

if ((Test-Path $localFfmpeg) -and (Test-Path $localFfprobe)) {
    Copy-Item $localFfmpeg $projectFfmpeg -Force
    Copy-Item $localFfprobe $projectFfprobe -Force
    Write-ProgressLine 100 "FFmpeg already installed; app copy refreshed" "Green"
    Write-Host "FFmpeg already installed. Skipping download." -ForegroundColor Green
    Write-Host "Using persistent copy at $localTools" -ForegroundColor DarkGray
    return
}

if ((Test-Path $projectFfmpeg) -and (Test-Path $projectFfprobe)) {
    Copy-Item $projectFfmpeg $localFfmpeg -Force
    Copy-Item $projectFfprobe $localFfprobe -Force
    Write-ProgressLine 100 "Bundled FFmpeg copied into persistent cache" "Green"
    Write-Host "FFmpeg already bundled with this app. Skipping download." -ForegroundColor Green
    Write-Host "Persistent copy installed to $localTools" -ForegroundColor Green
    return
}

Write-Host "Downloading FFmpeg essentials build..."
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    Write-ProgressLine 20 "No FFmpeg cache found; downloading package" "Yellow"
    Save-FileWithProgress "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" $zip 25 65
    Write-ProgressLine 70 "Extracting FFmpeg package" "Cyan"
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    Write-ProgressLine 82 "Locating ffmpeg.exe and ffprobe.exe" "Cyan"
    $ffmpeg = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    $ffprobe = Get-ChildItem $extract -Recurse -Filter ffprobe.exe | Select-Object -First 1
    if (-not $ffmpeg -or -not $ffprobe) { throw "FFmpeg archive structure not recognized." }

    Write-ProgressLine 90 "Installing FFmpeg into app and persistent cache" "Cyan"
    Copy-Item $ffmpeg.FullName $projectFfmpeg -Force
    Copy-Item $ffprobe.FullName $projectFfprobe -Force
    Copy-Item $ffmpeg.FullName $localFfmpeg -Force
    Copy-Item $ffprobe.FullName $localFfprobe -Force
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-ProgressLine 100 "FFmpeg is ready" "Green"
Write-Host "Done. FFmpeg installed to $tools" -ForegroundColor Green
Write-Host "Persistent copy installed to $localTools" -ForegroundColor Green
