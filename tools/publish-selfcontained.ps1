$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\SwarmCastStudio\SwarmCastStudio.csproj"
$output = Join-Path $root "src\SwarmCastStudio\publish-selfcontained"

Write-Host "Publishing self-contained single-file Swarm Cast Studio..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output "SwarmCastStudio.exe"
if (-not (Test-Path $exe)) {
    throw "Publish completed but $exe was not produced."
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $exe ($sizeMb MB, no .NET runtime required)" -ForegroundColor Green
