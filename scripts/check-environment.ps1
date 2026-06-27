param(
    [string]$ConfigPath = "publish\appsettings.json"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$config = if ([IO.Path]::IsPathRooted($ConfigPath)) { $ConfigPath } else { Join-Path $root $ConfigPath }

function Write-Check {
    param(
        [string]$Name,
        [bool]$Ok,
        [string]$Detail
    )

    $status = if ($Ok) { "OK" } else { "WARN" }
    Write-Host ("[{0}] {1}: {2}" -f $status, $Name, $Detail)
}

function Resolve-ProgIdClsid {
    param([string]$ProgId)

    $clsid = Get-Item "Registry::HKEY_CLASSES_ROOT\$ProgId\CLSID" -ErrorAction SilentlyContinue
    if ($clsid) {
        return [string]$clsid.GetValue("")
    }

    $curVer = Get-Item "Registry::HKEY_CLASSES_ROOT\$ProgId\CurVer" -ErrorAction SilentlyContinue
    if ($curVer) {
        $versionedProgId = [string]$curVer.GetValue("")
        if ($versionedProgId) {
            return Resolve-ProgIdClsid $versionedProgId
        }
    }

    return $null
}

$sdks = @(dotnet --list-sdks)
Write-Check ".NET SDK" ($sdks.Count -gt 0) ($(if ($sdks.Count -gt 0) { $sdks -join "; " } else { "No SDK found; build/publish requires .NET 10 SDK." }))

$net10Runtime = @(dotnet --list-runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+10\.' })
Write-Check ".NET 10 runtime" ($net10Runtime.Count -gt 0) ($(if ($net10Runtime.Count -gt 0) { $net10Runtime -join "; " } else { "No Microsoft.NETCore.App 10.x runtime found." }))

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$csc = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -all -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\Roslyn\csc.exe"
        if (Test-Path $candidate) {
            $csc = $candidate
        }
    }
}
Write-Check "Roslyn fallback" ([bool]$csc) ($(if ($csc) { $csc } else { "Not found; scripts\test.ps1 fallback will not work without SDK." }))

$vendorPaths = @(
    "C:\Program Files\MaxSun\LightControlModule",
    "C:\Program Files\ASUS\AuraSDK",
    "C:\Program Files\ENE"
)
foreach ($path in $vendorPaths) {
    Write-Check $path (Test-Path $path) ($(if (Test-Path $path) { "Found" } else { "Missing" }))
}

$asusAura = Resolve-ProgIdClsid "asus.aura"
Write-Check "COM ProgID asus.aura" ($null -ne $asusAura) ($(if ($asusAura) { $asusAura } else { "Not registered" }))

$maxsunHal = Resolve-ProgIdClsid "MaxSunEneLight.Hal"
Write-Check "COM ProgID MaxSunEneLight.Hal" ($null -ne $maxsunHal) ($(if ($maxsunHal) { $maxsunHal } else { "Not registered" }))

$targetClsid = "Registry::HKEY_CLASSES_ROOT\CLSID\{9D590787-6015-445D-9076-30B360CDF24B}\InprocServer32"
$targetHal = Get-Item $targetClsid -ErrorAction SilentlyContinue
Write-Check "Maxsun ENE HAL CLSID" ($null -ne $targetHal) ($(if ($targetHal) { $targetHal.GetValue("") } else { "Not registered" }))

$conflicts = @(Get-Process -Name MaxsunSync2,MaxsunSyncService -ErrorAction SilentlyContinue)
Write-Check "MaxsunSync conflict processes" ($conflicts.Count -eq 0) ($(if ($conflicts.Count -eq 0) { "None running" } else { ($conflicts | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", " }))

$service = Get-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
Write-Check "MaxsunSyncService" ($null -eq $service -or $service.Status -ne "Running") ($(if ($service) { "$($service.Status), StartType=$($service.StartType)" } else { "Not installed" }))

if (Test-Path $config) {
    $json = Get-Content $config -Raw | ConvertFrom-Json
    $token = [string]$json.homeAssistant.longLivedAccessToken
    $tokenReady = $token -and $token -notmatch "REPLACE"
    $detail = if ($tokenReady) {
        "$config; Home Assistant token is configured."
    }
    else {
        "$config exists, but homeAssistant.longLivedAccessToken is missing or still a placeholder."
    }
    Write-Check "Bridge config" $tokenReady $detail
}
else {
    Write-Check "Bridge config" $false "$config not found; run scripts\build.ps1 or copy appsettings.example.json."
}
