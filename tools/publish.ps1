$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\SwarmCastStudio\SwarmCastStudio.csproj"
$output = Join-Path $root "src\SwarmCastStudio\publish"

Write-Host "Publishing Swarm Cast Studio..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output "SwarmCastStudio.exe"
if (-not (Test-Path $exe)) {
    throw "Publish completed but $exe was not produced."
}

Write-Host "Published: $exe" -ForegroundColor Green
