param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$sdks = dotnet --list-sdks
if ($sdks) {
    dotnet run `
        --project (Join-Path $root "tests\ha_maxsun.Tests\ha_maxsun.Tests.csproj") `
        -c $Configuration
    exit $LASTEXITCODE
}

Write-Warning "No .NET SDK is installed; falling back to Roslyn from Visual Studio BuildTools for a local compile/test check."

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

$outDir = Join-Path $env:TEMP "ha_maxsun.ManualTests"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$globalUsings = Join-Path $outDir "ha_maxsun.GlobalUsings.cs"
@'
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
'@ | Set-Content -LiteralPath $globalUsings -Encoding UTF8

$references = Get-ChildItem $runtimeDir -Filter *.dll |
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

$sources = @($globalUsings) + @(
    Get-ChildItem (Join-Path $root "src"), (Join-Path $root "tests") -Filter *.cs -Recurse |
        ForEach-Object { $_.FullName }
)

$testAssembly = Join-Path $outDir "ha_maxsun.Tests.dll"
& $csc `
    /noconfig `
    /nostdlib `
    /langversion:preview `
    /nullable:enable `
    /target:exe `
    /main:HaMaxsun.Tests.Program `
    /out:$testAssembly `
    @references `
    @sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$runtimeConfig = @"
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "$($runtime.Version)"
    }
  }
}
"@
$runtimeConfig | Set-Content -LiteralPath (Join-Path $outDir "ha_maxsun.Tests.runtimeconfig.json") -Encoding UTF8

& dotnet $testAssembly

