$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$parts = @(
    (Join-Path $root "WatermarkRemoverWindows_v0.4_Setup.part01.bin"),
    (Join-Path $root "WatermarkRemoverWindows_v0.4_Setup.part02.bin")
)
$output = Join-Path $env:TEMP "WatermarkRemoverWindows_v0.4_Setup.exe"
$expected = "16A4B7821E67E0840D80A200C26E3799C2BB121EAD9EBB95C3056B3C9BBCCE70"

foreach ($part in $parts) {
    if (-not (Test-Path $part)) {
        throw "Missing release part: $part"
    }
}

if (Test-Path $output) {
    Remove-Item $output -Force
}

$buffer = New-Object byte[] (1048576)
$destination = [IO.File]::Create($output)
try {
    foreach ($part in $parts) {
        $input = [IO.File]::OpenRead($part)
        try {
            while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $destination.Write($buffer, 0, $read)
            }
        } finally {
            $input.Dispose()
        }
    }
} finally {
    $destination.Dispose()
}

$actual = (Get-FileHash $output -Algorithm SHA256).Hash
if ($actual -ne $expected) {
    Remove-Item $output -Force
    throw "Installer hash mismatch. Expected $expected, got $actual."
}

Start-Process -FilePath $output -Wait
