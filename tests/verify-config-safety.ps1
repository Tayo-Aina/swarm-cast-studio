param(
    [string]$Exe = "src\SwarmCastStudio\bin\Release\net9.0-windows\SwarmCastStudio.exe",
    [string]$Artifacts = "tests\artifacts"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null
$exePath = (Resolve-Path $Exe).Path
$exeDirectory = Split-Path -Parent $exePath
$resultPath = Join-Path $exeDirectory "smoke-result.json"

$checks = @()

function Add-Check([string]$Name, [bool]$Pass, [string]$Evidence) {
    $script:checks += [pscustomobject]@{ Check = $Name; Result = $(if ($Pass) { "PASS" } else { "FAIL" }); Evidence = $Evidence }
}

# 1. JSONC config loads, round-trips, preserves unknown keys.
$jsoncSource = Join-Path $root "tests\fixtures\jsonc-config.json"
$jsoncCopy = Join-Path $root "$Artifacts\jsonc-config.json"
Copy-Item $jsoncSource $jsoncCopy -Force

& $exePath --smoke-write $jsoncCopy | Out-Null
$exit = $LASTEXITCODE
$result = Get-Content $resultPath -Raw | ConvertFrom-Json
$saved = Get-Content $jsoncCopy -Raw | ConvertFrom-Json

Add-Check "JSONC smoke-write exit 0" ($exit -eq 0) "exit=$exit"
Add-Check "JSONC smoke result ok" ($result.ok -eq $true) "ok=$($result.ok); error=$($result.error)"
Add-Check "network root key preserved" ($saved.unknown_root_key -eq "preserve-me") "unknown_root_key=$($saved.unknown_root_key)"
Add-Check "unknown agent key preserved" ($saved.agents.coder.unknown_agent_key -eq 42) "unknown_agent_key=$($saved.agents.coder.unknown_agent_key)"
Add-Check "swarms block preserved" ($saved.swarms.demo.name -eq "Demo") "swarms.demo.name=$($saved.swarms.demo.name)"
Add-Check "coder model written" (-not [string]::IsNullOrWhiteSpace($saved.agents.coder.model)) "model=$($saved.agents.coder.model)"

# 2. Malformed config is rejected and never overwritten.
$broken = Join-Path $root "$Artifacts\broken-config.json"
$brokenText = "{ `"agents`": { `"coder`": "
[System.IO.File]::WriteAllText($broken, $brokenText)
$beforeHash = (Get-FileHash $broken -Algorithm SHA256).Hash
$beforeText = Get-Content $broken -Raw

& $exePath --smoke-write $broken | Out-Null
$brokenExit = $LASTEXITCODE
$afterHash = (Get-FileHash $broken -Algorithm SHA256).Hash
$afterText = Get-Content $broken -Raw
$brokenResult = Get-Content $resultPath -Raw | ConvertFrom-Json

Add-Check "malformed config exits non-zero" ($brokenExit -ne 0) "exit=$brokenExit"
Add-Check "malformed config result not ok" ($brokenResult.ok -eq $false) "ok=$($brokenResult.ok)"
Add-Check "malformed config not overwritten" (($beforeHash -eq $afterHash) -and ($beforeText -ceq $afterText)) "hash=$afterHash"

$checks | Format-Table -AutoSize
$failed = $checks | Where-Object { $_.Result -eq "FAIL" }
if ($failed.Count -gt 0) {
    Write-Output "CONFIG SAFETY: FAIL ($($failed.Count) failed)"
    exit 1
}
Write-Output "CONFIG SAFETY: PASS"
exit 0
