param(
    [string]$VendorDirectory = "",
    [switch]$Unregister,
    [switch]$ElevatedChild
)

$ErrorActionPreference = "Stop"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-VendorDirectory {
    if (-not [string]::IsNullOrWhiteSpace($VendorDirectory)) {
        if ([IO.Path]::IsPathRooted($VendorDirectory)) {
            return $VendorDirectory
        }

        return (Resolve-Path -LiteralPath $VendorDirectory).Path
    }

    $candidates = @(
        (Join-Path $PSScriptRoot "vendor"),
        (Join-Path (Split-Path -Parent $PSScriptRoot) "publish\vendor"),
        (Join-Path (Get-Location) "vendor"),
        (Join-Path (Get-Location) "publish\vendor")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Vendor runtime directory was not found. Run scripts\collect-vendor-runtime.ps1 first."
}

if (-not (Test-Administrator)) {
    if ($ElevatedChild) {
        throw "Administrator privileges are required, but the elevated child process is still not elevated."
    }

    $resolvedVendorForElevation = Resolve-VendorDirectory
    Write-Host "Requesting administrator privileges via UAC..."
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-VendorDirectory", "`"$resolvedVendorForElevation`"",
        "-ElevatedChild"
    )
    if ($Unregister) {
        $arguments += "-Unregister"
    }

    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $process.ExitCode
}

$vendor = Resolve-VendorDirectory
$runtimeDirectories = @(
    (Join-Path $vendor "ASUS\AuraSDK"),
    (Join-Path $vendor "MaxSun\LightControlModule\Aac_MaxSunEneLight"),
    (Join-Path $vendor "ENE\Aac_ENE RGB HAL\x64")
)

foreach ($directory in $runtimeDirectories) {
    if (-not (Test-Path $directory)) {
        throw "Required vendor runtime directory is missing: $directory"
    }
}

$currentPath = [string]$env:PATH
$env:PATH = ($runtimeDirectories -join ";") + ";" + $currentPath

$dlls = @(
    @{ Name = "ASUS Aura SDK"; Path = "ASUS\AuraSDK\AuraSdk_x64.dll" },
    @{ Name = "Maxsun ENE motherboard HAL"; Path = "MaxSun\LightControlModule\Aac_MaxSunEneLight\AacHal_x64.dll" },
    @{ Name = "ENE RGB HAL x64"; Path = "ENE\Aac_ENE RGB HAL\x64\AacHal_x64.dll" }
)

$regsvr32 = Join-Path $env:WINDIR "System32\regsvr32.exe"
foreach ($entry in $dlls) {
    $dll = Join-Path $vendor $entry.Path
    if (-not (Test-Path $dll)) {
        throw "$($entry.Name) DLL is missing: $dll"
    }

    $arguments = @("/s")
    if ($Unregister) {
        $arguments += "/u"
    }
    $arguments += $dll

    $process = Start-Process -FilePath $regsvr32 -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        $operation = if ($Unregister) { "unregister" } else { "register" }
        throw "Failed to $operation $($entry.Name): $dll. regsvr32 exit code: $($process.ExitCode)"
    }

    $verb = if ($Unregister) { "Unregistered" } else { "Registered" }
    Write-Host "[OK] $verb $($entry.Name): $dll"
}

Write-Host ""
if ($Unregister) {
    Write-Host "Vendor runtime COM components were unregistered."
}
else {
    Write-Host "Vendor runtime COM components were registered."
    Write-Host "You can now run install-service.ps1 for ha_maxsun."
}
