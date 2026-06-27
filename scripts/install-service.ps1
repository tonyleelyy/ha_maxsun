param(
    [string]$PublishDirectory = "publish",
    [string]$ServiceName = "ha_maxsun",
    [switch]$ElevatedChild
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$scriptIsInPublish = Test-Path (Join-Path $scriptDir "ha_maxsun.dll")
$root = if ($scriptIsInPublish) { $scriptDir } else { Split-Path -Parent $scriptDir }
$publish = if ([IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory
}
elseif ($scriptIsInPublish -and $PublishDirectory -eq "publish") {
    $scriptDir
}
else {
    Join-Path $root $PublishDirectory
}
$logDir = Join-Path $publish "logs"
$installLog = Join-Path $logDir "install-service.log"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Sc {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & sc.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Administrator)) {
    if ($ElevatedChild) {
        throw "Administrator privileges are required, but the elevated child process is still not elevated."
    }

    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Write-Host "Requesting administrator privileges via UAC..."

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-PublishDirectory", "`"$PublishDirectory`"",
        "-ServiceName", "`"$ServiceName`"",
        "-ElevatedChild"
    )

    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    if (Test-Path $installLog) {
        Get-Content $installLog -Tail 80
    }

    if ($process.ExitCode -ne 0) {
        exit $process.ExitCode
    }

    $installed = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $installed) {
        throw "Service '$ServiceName' was not installed. See $installLog for details."
    }

    Write-Host "Service '$ServiceName' is $($installed.Status)."
    exit 0
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if ($ElevatedChild) {
    Start-Transcript -Path $installLog -Force | Out-Null
}

try {
$exe = Join-Path $publish "ha_maxsun.exe"
$dll = Join-Path $publish "ha_maxsun.dll"
$config = Join-Path $publish "appsettings.json"

if (Test-Path $exe) {
    $binPath = "`"$exe`" --service --config `"$config`""
}
elseif (Test-Path $dll) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $binPath = "`"$dotnet`" `"$dll`" --service --config `"$config`""
}
else {
    throw "Bridge executable not found: $exe or $dll. Run scripts\build.ps1 first."
}

if (-not (Test-Path $config)) {
    $exampleConfig = Join-Path $root "appsettings.example.json"
    if (Test-Path $exampleConfig) {
        Copy-Item $exampleConfig $config
        throw "Created $config. Edit it before installing the service."
    }

    throw "Configuration file not found: $config. Copy appsettings.example.json to this directory and edit it before installing the service."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    throw "Service '$ServiceName' already exists. Use scripts\uninstall-service.ps1 first if you want to replace it."
}

New-Service -Name $ServiceName -DisplayName $ServiceName -BinaryPathName $binPath -StartupType Automatic | Out-Host
Invoke-Sc description $ServiceName "Bridge Maxsun motherboard RGB control to Home Assistant."
Start-Service -Name $ServiceName
}
finally {
    if ($ElevatedChild) {
        Stop-Transcript | Out-Null
    }
}
