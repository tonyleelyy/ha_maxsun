param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }

function Get-Net10Runtime {
    $runtime = dotnet --list-runtimes |
        ForEach-Object {
            if ($_ -match '^Microsoft\.NETCore\.App\s+([^\s]+)\s+\[(.+)\]$') {
                [pscustomobject]@{
                    Version = [version]$matches[1]
                    Path = $matches[2]
                }
            }
        } |
        Where-Object { $_.Version.Major -eq 10 } |
        Sort-Object Version -Descending |
        Select-Object -First 1

    if (-not $runtime) {
        throw "No Microsoft.NETCore.App 10.x runtime was found. Install the .NET 10 SDK."
    }

    $runtimeDir = Join-Path $runtime.Path $runtime.Version.ToString()
    if (-not (Test-Path $runtimeDir)) {
        throw "Could not find runtime directory $runtimeDir."
    }

    return [pscustomobject]@{
        Version = $runtime.Version
        Directory = $runtimeDir
    }
}

function Get-RoslynCompiler {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "No .NET SDK is installed and vswhere.exe was not found. Install the .NET 10 SDK."
    }

    $vsPath = & $vswhere -all -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
    if (-not $vsPath) {
        throw "No .NET SDK is installed and Visual Studio BuildTools with MSBuild was not found. Install the .NET 10 SDK."
    }

    $csc = Join-Path $vsPath "MSBuild\Current\Bin\Roslyn\csc.exe"
    if (-not (Test-Path $csc)) {
        throw "Roslyn csc.exe was not found at $csc. Install the .NET 10 SDK."
    }

    return $csc
}

function Get-ManagedReferences {
    param([string]$RuntimeDirectory)

    Get-ChildItem $RuntimeDirectory -Filter *.dll |
        Where-Object {
            try {
                [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
                $true
            }
            catch {
                $false
            }
        } |
        ForEach-Object { "/r:$($_.FullName)" }
}

function Write-RuntimeConfig {
    param(
        [string]$AssemblyName,
        [version]$RuntimeVersion
    )

    $json = @"
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "$RuntimeVersion"
    }
  }
}
"@
    $json | Set-Content -LiteralPath (Join-Path $out "$AssemblyName.runtimeconfig.json") -Encoding UTF8
}

function Invoke-RoslynPublish {
    Write-Warning "No .NET SDK is installed; falling back to Roslyn from Visual Studio BuildTools. This creates framework-dependent DLLs. Install the .NET 10 SDK for normal publish/apphost EXE output."

    $runtime = Get-Net10Runtime
    $csc = Get-RoslynCompiler
    $references = @(Get-ManagedReferences $runtime.Directory)

    $globalUsings = Join-Path $out "ha_maxsun.GlobalUsings.g.cs"
    @'
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
'@ | Set-Content -LiteralPath $globalUsings -Encoding UTF8

    $coreSources = @(Get-ChildItem (Join-Path $root "src\ha_maxsun.Core") -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
    $bridgeSources = @(Get-ChildItem (Join-Path $root "src\ha_maxsun.Service") -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
    $helperSources = @(Get-ChildItem (Join-Path $root "src\ha_maxsun.HalHelper") -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
    $helperOut = Join-Path $out "ha_maxsun.HalHelper.dll"
    $bridgeOut = Join-Path $out "ha_maxsun.dll"

    & $csc `
        /noconfig `
        /nostdlib `
        /langversion:preview `
        /nullable:enable `
        /target:exe `
        /main:HaMaxsun.HalHelper.Program `
        /out:$helperOut `
        @references `
        $globalUsings `
        @coreSources `
        @helperSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-RuntimeConfig "ha_maxsun.HalHelper" $runtime.Version

    & $csc `
        /noconfig `
        /nostdlib `
        /langversion:preview `
        /nullable:enable `
        /target:exe `
        /main:HaMaxsun.Service.Program `
        /out:$bridgeOut `
        @references `
        $globalUsings `
        @coreSources `
        @bridgeSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-RuntimeConfig "ha_maxsun" $runtime.Version

    Remove-Item -LiteralPath $globalUsings -Force
}

New-Item -ItemType Directory -Force -Path $out | Out-Null

$sdks = @(dotnet --list-sdks)
if ($sdks.Count -gt 0) {
    dotnet publish (Join-Path $root "src\ha_maxsun.HalHelper\ha_maxsun.HalHelper.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained false `
        -o $out

    dotnet publish (Join-Path $root "src\ha_maxsun.Service\ha_maxsun.Service.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained false `
        -o $out
}
else {
    Invoke-RoslynPublish
}

$config = Join-Path $out "appsettings.json"
if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $root "appsettings.example.json") $config
}
else {
    $configText = Get-Content -LiteralPath $config -Raw
    $updatedConfigText = $configText.Replace('"helperPath": "MaxsunControl.HalHelper.dll"', '"helperPath": "ha_maxsun.HalHelper.exe"')
    $updatedConfigText = $updatedConfigText.Replace('"helperPath": "HaMaxsun.HalHelper.dll"', '"helperPath": "ha_maxsun.HalHelper.exe"')
    $updatedConfigText = $updatedConfigText.Replace('"helperPath": "ha_maxsun.HalHelper.dll"', '"helperPath": "ha_maxsun.HalHelper.exe"')

    if ($updatedConfigText -ne $configText) {
        Set-Content -LiteralPath $config -Value $updatedConfigText -Encoding UTF8
        Write-Host "Updated HAL helper path in $config"
    }
}

Write-Host "Published to $out"
Write-Host "Edit $config before starting the service."

