param(
    [string]$Target = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $Target) {
    $selfContained = Join-Path $root "src\SwarmCastStudio\publish-selfcontained\SwarmCastStudio.exe"
    $framework = Join-Path $root "src\SwarmCastStudio\publish\SwarmCastStudio.exe"
    if (Test-Path $selfContained) {
        $Target = $selfContained
    } elseif (Test-Path $framework) {
        $Target = $framework
    } else {
        throw "No published SwarmCastStudio.exe found. Run tools\publish.ps1 first."
    }
}

$Target = (Resolve-Path $Target).Path
$desktop = [Environment]::GetFolderPath("Desktop")
$linkPath = Join-Path $desktop "Swarm Cast Studio.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($linkPath)
$shortcut.TargetPath = $Target
$shortcut.WorkingDirectory = Split-Path -Parent $Target
$shortcut.IconLocation = "$Target,0"
$shortcut.Description = "Edit the opencode-swarm agent cast"
$shortcut.WindowStyle = 1
$shortcut.Save()

Write-Output "Created: $linkPath"
Write-Output "Target:  $Target"
