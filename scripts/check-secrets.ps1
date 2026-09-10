$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$patterns = @(
    "github_pat_",
    "ghp_",
    "AKIA[0-9A-Z]{16}",
    "BEGIN (RSA|OPENSSH|EC) PRIVATE KEY"
)

$matches = foreach ($pattern in $patterns) {
    Get-ChildItem $repoRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\.git\\" -and $_.Name -ne "check-secrets.ps1" } |
        Select-String -Pattern $pattern -ErrorAction SilentlyContinue
}

if ($matches) {
    $matches | Select-Object Path,LineNumber,Line
    throw "Potential secret material found."
}

Write-Host "No obvious secrets found." -ForegroundColor Green
