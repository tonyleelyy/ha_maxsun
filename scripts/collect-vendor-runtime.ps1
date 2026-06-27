param(
    [string]$OutputDirectory = "publish\vendor",
    [string]$AuraSdkSource = "C:\Program Files\ASUS\AuraSDK",
    [string]$MaxsunHalSource = "C:\Program Files\MaxSun\LightControlModule\Aac_MaxSunEneLight",
    [string]$EneHalSource = "C:\Program Files\ENE\Aac_ENE RGB HAL\x64",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }

function Copy-VendorDirectory {
    param(
        [string]$Name,
        [string]$Source,
        [string]$RelativeDestination
    )

    if (-not (Test-Path $Source)) {
        throw "$Name source directory was not found: $Source"
    }

    $destination = Join-Path $output $RelativeDestination
    if ((Test-Path $destination) -and $Force) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $destination -Recurse -Force
    Write-Host "[OK] $Name -> $destination"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

Copy-VendorDirectory "ASUS Aura SDK" $AuraSdkSource "ASUS\AuraSDK"
Copy-VendorDirectory "Maxsun ENE motherboard HAL" $MaxsunHalSource "MaxSun\LightControlModule\Aac_MaxSunEneLight"
Copy-VendorDirectory "ENE RGB HAL x64" $EneHalSource "ENE\Aac_ENE RGB HAL\x64"

$publishDirectory = Split-Path -Parent $output
foreach ($scriptName in @("register-vendor-runtime.ps1", "install-service.ps1", "uninstall-service.ps1")) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $publishDirectory $scriptName) -Force
}

$manifest = @(
    "ha_maxsun local vendor runtime",
    "Generated: $((Get-Date).ToString("u"))",
    "",
    "This directory contains proprietary vendor runtime files copied from this machine.",
    "Do not commit or redistribute it unless you have the right to do so.",
    "",
    "Sources:",
    "ASUS Aura SDK: $AuraSdkSource",
    "Maxsun ENE motherboard HAL: $MaxsunHalSource",
    "ENE RGB HAL x64: $EneHalSource"
)
$manifest | Set-Content -LiteralPath (Join-Path $output "README-vendor-runtime.txt") -Encoding UTF8

Write-Host ""
Write-Host "Collected vendor runtime to $output"
Write-Host "Copied portable service scripts to $publishDirectory"
Write-Host "On a target machine, run register-vendor-runtime.ps1 from an elevated PowerShell before installing the service."
