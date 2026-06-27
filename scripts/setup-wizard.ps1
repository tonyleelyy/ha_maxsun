param(
    [string]$PublishDirectory = "publish",
    [string]$ServiceName = "ha_maxsun",
    [switch]$SkipHardwareTest,
    [switch]$SkipHomeAssistantCheck,
    [switch]$ElevatedChild,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = if ([IO.Path]::IsPathRooted($PublishDirectory)) { $PublishDirectory } else { Join-Path $root $PublishDirectory }
$config = Join-Path $publish "appsettings.json"
$logDir = Join-Path $publish "logs"
$setupLog = Join-Path $logDir "setup-wizard.log"

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-ElevatedSelf {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Write-Host "Administrator privileges are required. Requesting UAC elevation..."

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-PublishDirectory", "`"$PublishDirectory`"",
        "-ServiceName", "`"$ServiceName`"",
        "-ElevatedChild"
    )

    if ($SkipHardwareTest) {
        $arguments += "-SkipHardwareTest"
    }

    if ($SkipHomeAssistantCheck) {
        $arguments += "-SkipHomeAssistantCheck"
    }

    if ($NoPause) {
        $arguments += "-NoPause"
    }

    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $process.ExitCode
}

function Write-Step {
    param([string]$Name)

    Write-Host ""
    Write-Host "== $Name =="
}

function Invoke-ToolScript {
    param(
        [string]$Name,
        [string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    Write-Step $Name
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Test-BridgeBinary {
    $exe = Join-Path $publish "ha_maxsun.exe"
    $dll = Join-Path $publish "ha_maxsun.dll"
    return (Test-Path $exe) -or (Test-Path $dll)
}

function Ensure-PublishOutput {
    Write-Step "Prepare publish output"
    New-Item -ItemType Directory -Force -Path $publish | Out-Null

    if (Test-BridgeBinary) {
        Write-Host "Found bridge output in $publish"
        return
    }

    $buildScript = Join-Path $root "scripts\build.ps1"
    if (-not (Test-Path $buildScript)) {
        throw "Bridge output was not found in $publish, and scripts\build.ps1 is missing. Download a release zip or build from source first."
    }

    Write-Host "Bridge output was not found. Trying to build it now..."
    Invoke-ToolScript "Build bridge" $buildScript @("-OutputDirectory", $publish)

    if (-not (Test-BridgeBinary)) {
        throw "Build finished, but ha_maxsun.exe or ha_maxsun.dll was still not found in $publish."
    }
}

function ConvertFrom-SecureText {
    param([Security.SecureString]$SecureText)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureText)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Read-RequiredSecret {
    param([string]$Prompt)

    while ($true) {
        $secure = Read-Host $Prompt -AsSecureString
        $plain = ConvertFrom-SecureText $secure
        if (-not [string]::IsNullOrWhiteSpace($plain) -and $plain -notmatch "REPLACE") {
            return $plain
        }

        Write-Warning "Value cannot be empty."
    }
}

function Test-PlaceholderUrl {
    param([string]$Value)

    return [string]::IsNullOrWhiteSpace($Value) -or
        $Value -match "REPLACE" -or
        $Value -eq "ws://homeassistant.local:8123/api/websocket"
}

function Normalize-HaWebSocketUrl {
    param([string]$Value)

    $text = $Value.Trim().TrimEnd("/")
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Home Assistant address cannot be empty."
    }

    if ($text -match "^http://") {
        $text = "ws://" + $text.Substring(7)
    }
    elseif ($text -match "^https://") {
        $text = "wss://" + $text.Substring(8)
    }
    elseif ($text -notmatch "^wss?://") {
        if ($text -notmatch ":" -and $text -notmatch "/") {
            $text = "${text}:8123"
        }

        $text = "ws://$text"
    }

    if ($text -notmatch "/api/websocket$") {
        $text = $text.TrimEnd("/") + "/api/websocket"
    }

    return $text
}

function Read-HaWebSocketUrl {
    param([string]$Prompt)

    while ($true) {
        $inputUrl = Read-Host $Prompt
        try {
            return Normalize-HaWebSocketUrl $inputUrl
        }
        catch {
            Write-Warning $_.Exception.Message
        }
    }
}

function Convert-HaWebSocketUrlInteractive {
    param([string]$Value)

    try {
        return Normalize-HaWebSocketUrl $Value
    }
    catch {
        Write-Warning $_.Exception.Message
        return Read-HaWebSocketUrl "Home Assistant address"
    }
}

function Ensure-Config {
    Write-Step "Configure Home Assistant"

    if (-not (Test-Path $config)) {
        $example = Join-Path $root "appsettings.example.json"
        if (-not (Test-Path $example)) {
            throw "appsettings.example.json was not found."
        }

        Copy-Item $example $config
        Write-Host "Created $config"
    }

    $cfg = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    if (-not $cfg.homeAssistant) {
        throw "$config is missing the homeAssistant section."
    }

    $currentUrl = [string]$cfg.homeAssistant.webSocketUrl
    if (Test-PlaceholderUrl $currentUrl) {
        Write-Host "Enter your Home Assistant address. Examples:"
        Write-Host "  192.168.1.10:8123"
        Write-Host "  http://homeassistant.local:8123"
        Write-Host "  https://ha.example.com"
        $cfg.homeAssistant.webSocketUrl = Read-HaWebSocketUrl "Home Assistant address"
    }
    else {
        Write-Host "Home Assistant URL is already configured: $currentUrl"
        $inputUrl = Read-Host "Press Enter to keep it, or type a new address"
        if (-not [string]::IsNullOrWhiteSpace($inputUrl)) {
            $cfg.homeAssistant.webSocketUrl = Convert-HaWebSocketUrlInteractive $inputUrl
        }
    }

    $token = [string]$cfg.homeAssistant.longLivedAccessToken
    if ([string]::IsNullOrWhiteSpace($token) -or $token -match "REPLACE") {
        $cfg.homeAssistant.longLivedAccessToken = Read-RequiredSecret "Home Assistant long-lived token"
    }
    else {
        Write-Host "Home Assistant token is already configured."
        $replace = Read-Host "Type y to replace it, or press Enter to keep it"
        if ($replace -in @("y", "Y", "yes", "YES")) {
            $cfg.homeAssistant.longLivedAccessToken = Read-RequiredSecret "Home Assistant long-lived token"
        }
    }

    $cfg | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $config -Encoding UTF8
    Write-Host "Saved $config"
}

function Stop-MaxsunSync {
    Write-Step "Stop Maxsun Sync conflicts"

    $processes = @(Get-Process -Name MaxsunSync2,MaxsunSyncService -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        Write-Host "Stopping process $($process.ProcessName)($($process.Id))"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $service = Get-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne "Stopped") {
            Write-Host "Stopping service MaxsunSyncService"
            Stop-Service -Name MaxsunSyncService -Force -ErrorAction SilentlyContinue
            $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
        }

        Write-Host "Setting MaxsunSyncService startup type to Manual"
        Set-Service -Name MaxsunSyncService -StartupType Manual -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "MaxsunSyncService is not installed."
    }
}

function Remove-ExistingBridgeService {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    Invoke-ToolScript "Remove existing $ServiceName service" (Join-Path $root "scripts\uninstall-service.ps1") @("-ServiceName", $ServiceName)

    for ($i = 0; $i -lt 10; $i++) {
        if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Seconds 1
    }
}

function Invoke-HomeAssistantCheck {
    if ($SkipHomeAssistantCheck) {
        return
    }

    try {
        Invoke-ToolScript "Check Home Assistant entities" (Join-Path $root "scripts\check-ha.ps1") @("-ConfigPath", $config)
    }
    catch {
        Write-Warning $_.Exception.Message
        Write-Warning "Usually this means homeassistant\maxsun_motherboard_rgb.yaml has not been loaded by Home Assistant yet."
        $continue = Read-Host "Type y to continue hardware test and service installation anyway"
        if ($continue -notin @("y", "Y", "yes", "YES")) {
            throw
        }
    }
}

function Invoke-HardwareTest {
    if ($SkipHardwareTest) {
        return
    }

    Invoke-ToolScript "Hardware test" (Join-Path $root "scripts\test-hardware.ps1") @(
        "-PublishDirectory", $publish,
        "-ConfigPath", $config,
        "-ConfirmEachStep"
    )
}

function Install-BridgeService {
    Invoke-ToolScript "Install and start $ServiceName service" (Join-Path $root "scripts\install-service.ps1") @(
        "-PublishDirectory", $publish,
        "-ServiceName", $ServiceName
    )

    $service = Get-Service -Name $ServiceName -ErrorAction Stop
    Write-Host "Service $ServiceName is $($service.Status)."
}

if (-not (Test-Administrator)) {
    Invoke-ElevatedSelf
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path $setupLog -Force | Out-Null

$exitCode = 0
try {
    Write-Host "ha_maxsun setup wizard"
    Write-Host "Log: $setupLog"

    Ensure-PublishOutput
    Ensure-Config
    Stop-MaxsunSync
    Remove-ExistingBridgeService
    Invoke-ToolScript "Check local environment" (Join-Path $root "scripts\check-environment.ps1") @("-ConfigPath", $config)
    Invoke-HomeAssistantCheck
    Invoke-HardwareTest
    Install-BridgeService

    Write-Host ""
    Write-Host "Setup completed. You can now control light.maxsun_motherboard_rgb in Home Assistant."
}
catch {
    $exitCode = 1
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
    if ($ElevatedChild -and -not $NoPause) {
        Write-Host ""
        Read-Host "Press Enter to close this window" | Out-Null
    }
}

exit $exitCode
