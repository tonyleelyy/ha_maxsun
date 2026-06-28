param(
    [string]$Version = "beta",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = if ([IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $root $OutputRoot }
$packageName = "ha_maxsun-$Version-$RuntimeIdentifier"
$stage = Join-Path $artifacts $packageName
$publish = Join-Path $stage "publish"
$zip = Join-Path $artifacts "$packageName.zip"
$hashFile = Join-Path $artifacts "$packageName.sha256.txt"

function Copy-BatchFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    $text = Get-Content -LiteralPath $Source -Raw
    $text = $text -replace "\r?\n", "`r`n"
    [IO.File]::WriteAllText($Destination, $text, [Text.Encoding]::ASCII)
}

$sdks = @(dotnet --list-sdks)
if ($sdks.Count -eq 0) {
    throw ".NET 10 SDK is required to create a self-contained release package."
}

if (Test-Path $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

if (Test-Path $zip) {
    Remove-Item -LiteralPath $zip -Force
}

if (Test-Path $hashFile) {
    Remove-Item -LiteralPath $hashFile -Force
}

New-Item -ItemType Directory -Force -Path $publish | Out-Null

dotnet publish (Join-Path $root "src\ha_maxsun.HalHelper\ha_maxsun.HalHelper.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publish `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $root "src\ha_maxsun.Service\ha_maxsun.Service.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publish `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $root "appsettings.example.json") -Destination (Join-Path $publish "appsettings.json") -Force

Copy-BatchFile (Join-Path $root "install.bat") (Join-Path $stage "install.bat")
Copy-BatchFile (Join-Path $root "install-en.bat") (Join-Path $stage "install-en.bat")
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "README.en.md") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "appsettings.example.json") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "homeassistant") -Destination (Join-Path $stage "homeassistant") -Recurse -Force

$releaseScripts = Join-Path $stage "scripts"
New-Item -ItemType Directory -Force -Path $releaseScripts | Out-Null
foreach ($scriptName in @(
    "check-environment.ps1",
    "check-ha.ps1",
    "install-service.ps1",
    "setup-wizard.ps1",
    "test-hardware.ps1",
    "uninstall-service.ps1"
)) {
    Copy-Item -LiteralPath (Join-Path $root "scripts\$scriptName") -Destination $releaseScripts -Force
}

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
"$($hash.Hash)  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath $hashFile -Encoding ASCII

Write-Host "Created $zip"
Write-Host "SHA256 $($hash.Hash)"
