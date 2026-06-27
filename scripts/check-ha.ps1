param(
    [string]$ConfigPath = "publish\appsettings.json"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$config = if ([IO.Path]::IsPathRooted($ConfigPath)) { $ConfigPath } else { Join-Path $root $ConfigPath }

if (-not (Test-Path $config)) {
    throw "Bridge config not found: $config"
}

$cfg = Get-Content $config -Raw | ConvertFrom-Json
$ws = [string]$cfg.homeAssistant.webSocketUrl
$token = [string]$cfg.homeAssistant.longLivedAccessToken
if (-not $token -or $token -match "REPLACE") {
    throw "$config has no configured Home Assistant long-lived token."
}

$base = $ws -replace '^ws://', 'http://' -replace '^wss://', 'https://' -replace '/api/websocket$', ''
$headers = @{ Authorization = "Bearer $token" }
$entities = @(
    $cfg.entities.power,
    $cfg.entities.brightness,
    $cfg.entities.color,
    $cfg.entities.available,
    "light.maxsun_motherboard_rgb"
)

$missing = @()
foreach ($entity in $entities) {
    try {
        $state = Invoke-RestMethod -Uri "$base/api/states/$entity" -Headers $headers -Method Get -TimeoutSec 10
        Write-Host "[OK] $entity=$($state.state)"
    }
    catch {
        $missing += $entity
        Write-Host "[MISSING] $entity"
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Home Assistant package/helpers are not loaded yet."
    Write-Host "Copy homeassistant\maxsun_motherboard_rgb.yaml into your HA packages directory, ensure packages are enabled, then restart Home Assistant or reload YAML/template entities."
    exit 2
}

Write-Host "Home Assistant bridge entities are ready."
