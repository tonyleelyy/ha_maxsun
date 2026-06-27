param(
    [string]$PublishDirectory = "publish",
    [string]$ConfigPath,
    [switch]$ConfirmEachStep
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = if ([IO.Path]::IsPathRooted($PublishDirectory)) { $PublishDirectory } else { Join-Path $root $PublishDirectory }
$exe = Join-Path $publish "ha_maxsun.exe"
$dll = Join-Path $publish "ha_maxsun.dll"
$config = if ($ConfigPath) { $ConfigPath } else { Join-Path $publish "appsettings.json" }

if (Test-Path $exe) {
    $command = $exe
    $prefixArgs = @()
}
elseif (Test-Path $dll) {
    $command = "dotnet"
    $prefixArgs = @($dll)
}
else {
    throw "Bridge executable not found: $exe or $dll. Run scripts\build.ps1 first."
}

if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $root "appsettings.example.json") $config
}

$conflicts = @(Get-Process -Name MaxsunSync2,MaxsunSyncService -ErrorAction SilentlyContinue)
if ($conflicts.Count -gt 0) {
    $names = ($conflicts | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
    throw "MaxsunSync conflict detected: $names. Stop MaxsunSync2 and MaxsunSyncService before hardware testing."
}

& $command @prefixArgs --config $config --once-probe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($case in @(
    @{ Name = "red"; Args = @("--once-apply", "--rgb", "255,0,0", "--brightness", "255") },
    @{ Name = "green"; Args = @("--once-apply", "--rgb", "0,255,0", "--brightness", "255") },
    @{ Name = "blue"; Args = @("--once-apply", "--rgb", "0,0,255", "--brightness", "255") },
    @{ Name = "low-white"; Args = @("--once-apply", "--rgb", "255,255,255", "--brightness", "32") },
    @{ Name = "off"; Args = @("--once-apply", "--off") }
)) {
    Write-Host "Applying $($case.Name)..."
    & $command @prefixArgs --config $config @($case.Args)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($ConfirmEachStep) {
        $answer = Read-Host "Did the motherboard RGB visibly change to '$($case.Name)'? Type y to continue"
        if ($answer -notin @("y", "Y", "yes", "YES")) {
            throw "Hardware confirmation failed at '$($case.Name)'."
        }
    }

    Start-Sleep -Seconds 2
}
