param(
    [string]$PublishDirectory = "publish",
    [string]$ServiceName = "ha_maxsun",
    [ValidateSet("zh-CN", "en-US")]
    [string]$Language = "zh-CN",
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

$Text = @{
    "zh-CN" = @{
        AdminRequired = "安装需要管理员权限，正在请求 UAC 授权..."
        PreparePublish = "准备程序文件"
        FoundBridgeOutput = "已找到程序文件：{0}"
        BuildScriptMissing = "没有在 {0} 找到程序文件，并且 scripts\build.ps1 不存在。请下载 Release 压缩包，或先从源码构建。"
        TryBuild = "没有找到 publish 输出，正在尝试自动构建..."
        BuildBridge = "构建程序"
        BuildOutputMissing = "构建已结束，但仍未在 {0} 找到 ha_maxsun.exe 或 ha_maxsun.dll。"
        ValueCannotBeEmpty = "输入不能为空。"
        HaAddressEmpty = "Home Assistant 地址不能为空。"
        HaAddressPrompt = "Home Assistant 地址"
        ConfigureHa = "配置 Home Assistant"
        ExampleConfigMissing = "找不到 appsettings.example.json。"
        ConfigCreated = "已创建配置文件：{0}"
        ConfigMissingHa = "{0} 缺少 homeAssistant 配置段。"
        EnterHaAddress = "请输入 Home Assistant 地址，例如："
        HaUrlConfigured = "Home Assistant URL 已配置：{0}"
        KeepOrNewAddress = "按 Enter 保持不变，或输入新的地址"
        HaTokenPrompt = "Home Assistant 长期访问令牌"
        HaTokenConfigured = "Home Assistant 长期访问令牌已配置。"
        ReplaceToken = "输入 y 替换长期访问令牌，或按 Enter 保持不变"
        ConfigSaved = "已保存配置文件：{0}"
        StopMaxsun = "停止铭瑄同步冲突"
        StoppingProcess = "正在停止进程 {0}({1})"
        StoppingService = "正在停止服务 MaxsunSyncService"
        SettingManual = "正在把 MaxsunSyncService 设置为手动启动"
        MaxsunServiceMissing = "未安装 MaxsunSyncService。"
        ExistingServiceStep = "检查已有 {0} 服务"
        ExistingServiceDetected = "检测到系统里已经安装 {0} 服务，当前状态：{1}。"
        ReplaceExistingService = "输入 y 卸载旧服务并继续重新安装，或按 Enter 中止"
        RemoveExistingService = "卸载已有 {0} 服务"
        ExistingServiceKept = "已保留现有 {0} 服务，安装已中止。"
        ExistingServiceRemoveFailed = "已尝试卸载 {0} 服务，但服务仍然存在。请手动卸载后重试。"
        CheckHaEntities = "检查 Home Assistant 实体"
        HaNotLoaded = "通常这表示 homeassistant\maxsun_motherboard_rgb.yaml 还没有被 Home Assistant 加载。"
        ContinueAnyway = "输入 y 继续硬件测试和服务安装，或按 Enter 中止"
        HardwareTest = "硬件测试"
        InstallService = "安装并启动 {0} 服务"
        ServiceStatus = "服务 {0} 当前状态：{1}"
        SetupTitle = "ha_maxsun 安装向导"
        LogPath = "日志：{0}"
        CheckEnvironment = "检查本机环境"
        SetupCompleted = "安装完成。现在可以在 Home Assistant 里控制 light.maxsun_motherboard_rgb。"
        ToolFailed = "{0} 失败，退出码 {1}。"
        ErrorPrefix = "错误：{0}"
        PressEnterClose = "按 Enter 关闭窗口"
    }
    "en-US" = @{
        AdminRequired = "Administrator privileges are required. Requesting UAC elevation..."
        PreparePublish = "Prepare publish output"
        FoundBridgeOutput = "Found bridge output in {0}"
        BuildScriptMissing = "Bridge output was not found in {0}, and scripts\build.ps1 is missing. Download a release zip or build from source first."
        TryBuild = "Bridge output was not found. Trying to build it now..."
        BuildBridge = "Build bridge"
        BuildOutputMissing = "Build finished, but ha_maxsun.exe or ha_maxsun.dll was still not found in {0}."
        ValueCannotBeEmpty = "Value cannot be empty."
        HaAddressEmpty = "Home Assistant address cannot be empty."
        HaAddressPrompt = "Home Assistant address"
        ConfigureHa = "Configure Home Assistant"
        ExampleConfigMissing = "appsettings.example.json was not found."
        ConfigCreated = "Created {0}"
        ConfigMissingHa = "{0} is missing the homeAssistant section."
        EnterHaAddress = "Enter your Home Assistant address. Examples:"
        HaUrlConfigured = "Home Assistant URL is already configured: {0}"
        KeepOrNewAddress = "Press Enter to keep it, or type a new address"
        HaTokenPrompt = "Home Assistant long-lived token"
        HaTokenConfigured = "Home Assistant token is already configured."
        ReplaceToken = "Type y to replace it, or press Enter to keep it"
        ConfigSaved = "Saved {0}"
        StopMaxsun = "Stop Maxsun Sync conflicts"
        StoppingProcess = "Stopping process {0}({1})"
        StoppingService = "Stopping service MaxsunSyncService"
        SettingManual = "Setting MaxsunSyncService startup type to Manual"
        MaxsunServiceMissing = "MaxsunSyncService is not installed."
        ExistingServiceStep = "Check existing {0} service"
        ExistingServiceDetected = "The {0} service is already installed. Current status: {1}."
        ReplaceExistingService = "Type y to uninstall the old service and reinstall, or press Enter to cancel"
        RemoveExistingService = "Uninstall existing {0} service"
        ExistingServiceKept = "The existing {0} service was kept. Setup has been cancelled."
        ExistingServiceRemoveFailed = "Tried to uninstall the {0} service, but it still exists. Uninstall it manually and try again."
        CheckHaEntities = "Check Home Assistant entities"
        HaNotLoaded = "Usually this means homeassistant\maxsun_motherboard_rgb.yaml has not been loaded by Home Assistant yet."
        ContinueAnyway = "Type y to continue hardware test and service installation anyway"
        HardwareTest = "Hardware test"
        InstallService = "Install and start {0} service"
        ServiceStatus = "Service {0} is {1}."
        SetupTitle = "ha_maxsun setup wizard"
        LogPath = "Log: {0}"
        CheckEnvironment = "Check local environment"
        SetupCompleted = "Setup completed. You can now control light.maxsun_motherboard_rgb in Home Assistant."
        ToolFailed = "{0} failed with exit code {1}."
        ErrorPrefix = "ERROR: {0}"
        PressEnterClose = "Press Enter to close this window"
    }
}

function Get-Text {
    param(
        [string]$Key,
        [object[]]$Arguments = @()
    )

    $template = $Text[$Language][$Key]
    if (-not $template) {
        $template = $Text["en-US"][$Key]
    }

    if ($Arguments.Count -gt 0) {
        return [string]::Format($template, $Arguments)
    }

    return $template
}

function Test-YesAnswer {
    param([string]$Value)

    return $Value -in @("y", "Y", "yes", "YES", "是", "好")
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-ElevatedSelf {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Write-Host (Get-Text "AdminRequired")

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-PublishDirectory", "`"$PublishDirectory`"",
        "-ServiceName", "`"$ServiceName`"",
        "-Language", "`"$Language`"",
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
        throw (Get-Text "ToolFailed" @($Name, $LASTEXITCODE))
    }
}

function Test-BridgeBinary {
    $exe = Join-Path $publish "ha_maxsun.exe"
    $dll = Join-Path $publish "ha_maxsun.dll"
    return (Test-Path $exe) -or (Test-Path $dll)
}

function Ensure-PublishOutput {
    Write-Step (Get-Text "PreparePublish")
    New-Item -ItemType Directory -Force -Path $publish | Out-Null

    if (Test-BridgeBinary) {
        Write-Host (Get-Text "FoundBridgeOutput" @($publish))
        return
    }

    $buildScript = Join-Path $root "scripts\build.ps1"
    if (-not (Test-Path $buildScript)) {
        throw (Get-Text "BuildScriptMissing" @($publish))
    }

    Write-Host (Get-Text "TryBuild")
    Invoke-ToolScript (Get-Text "BuildBridge") $buildScript @("-OutputDirectory", $publish)

    if (-not (Test-BridgeBinary)) {
        throw (Get-Text "BuildOutputMissing" @($publish))
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

        Write-Warning (Get-Text "ValueCannotBeEmpty")
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

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw (Get-Text "HaAddressEmpty")
    }

    $text = $Value.Trim().TrimEnd("/")
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw (Get-Text "HaAddressEmpty")
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
        return Read-HaWebSocketUrl (Get-Text "HaAddressPrompt")
    }
}

function Ensure-Config {
    Write-Step (Get-Text "ConfigureHa")

    if (-not (Test-Path $config)) {
        $example = Join-Path $root "appsettings.example.json"
        if (-not (Test-Path $example)) {
            throw (Get-Text "ExampleConfigMissing")
        }

        Copy-Item $example $config
        Write-Host (Get-Text "ConfigCreated" @($config))
    }

    $cfg = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    if (-not $cfg.homeAssistant) {
        throw (Get-Text "ConfigMissingHa" @($config))
    }

    $currentUrl = [string]$cfg.homeAssistant.webSocketUrl
    if (Test-PlaceholderUrl $currentUrl) {
        Write-Host (Get-Text "EnterHaAddress")
        Write-Host "  192.168.1.10:8123"
        Write-Host "  http://homeassistant.local:8123"
        Write-Host "  https://ha.example.com"
        $cfg.homeAssistant.webSocketUrl = Read-HaWebSocketUrl (Get-Text "HaAddressPrompt")
    }
    else {
        Write-Host (Get-Text "HaUrlConfigured" @($currentUrl))
        $inputUrl = Read-Host (Get-Text "KeepOrNewAddress")
        if (-not [string]::IsNullOrWhiteSpace($inputUrl)) {
            $cfg.homeAssistant.webSocketUrl = Convert-HaWebSocketUrlInteractive $inputUrl
        }
    }

    $token = [string]$cfg.homeAssistant.longLivedAccessToken
    if ([string]::IsNullOrWhiteSpace($token) -or $token -match "REPLACE") {
        $cfg.homeAssistant.longLivedAccessToken = Read-RequiredSecret (Get-Text "HaTokenPrompt")
    }
    else {
        Write-Host (Get-Text "HaTokenConfigured")
        $replace = Read-Host (Get-Text "ReplaceToken")
        if (Test-YesAnswer $replace) {
            $cfg.homeAssistant.longLivedAccessToken = Read-RequiredSecret (Get-Text "HaTokenPrompt")
        }
    }

    $cfg | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $config -Encoding UTF8
    Write-Host (Get-Text "ConfigSaved" @($config))
}

function Stop-MaxsunSync {
    Write-Step (Get-Text "StopMaxsun")

    $processes = @(Get-Process -Name MaxsunSync2,MaxsunSyncService -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        Write-Host (Get-Text "StoppingProcess" @($process.ProcessName, $process.Id))
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $service = Get-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne "Stopped") {
            Write-Host (Get-Text "StoppingService")
            Stop-Service -Name MaxsunSyncService -Force -ErrorAction SilentlyContinue
            $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
        }

        Write-Host (Get-Text "SettingManual")
        Set-Service -Name MaxsunSyncService -StartupType Manual -ErrorAction SilentlyContinue
    }
    else {
        Write-Host (Get-Text "MaxsunServiceMissing")
    }
}

function Remove-ExistingBridgeService {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    Write-Step (Get-Text "ExistingServiceStep" @($ServiceName))
    Write-Host (Get-Text "ExistingServiceDetected" @($ServiceName, $service.Status))
    $replace = Read-Host (Get-Text "ReplaceExistingService")
    if (-not (Test-YesAnswer $replace)) {
        throw (Get-Text "ExistingServiceKept" @($ServiceName))
    }

    Invoke-ToolScript (Get-Text "RemoveExistingService" @($ServiceName)) (Join-Path $root "scripts\uninstall-service.ps1") @("-ServiceName", $ServiceName)

    for ($i = 0; $i -lt 10; $i++) {
        if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw (Get-Text "ExistingServiceRemoveFailed" @($ServiceName))
}

function Invoke-HomeAssistantCheck {
    if ($SkipHomeAssistantCheck) {
        return
    }

    try {
        Invoke-ToolScript (Get-Text "CheckHaEntities") (Join-Path $root "scripts\check-ha.ps1") @("-ConfigPath", $config)
    }
    catch {
        Write-Warning $_.Exception.Message
        Write-Warning (Get-Text "HaNotLoaded")
        $continue = Read-Host (Get-Text "ContinueAnyway")
        if (-not (Test-YesAnswer $continue)) {
            throw
        }
    }
}

function Invoke-HardwareTest {
    if ($SkipHardwareTest) {
        return
    }

    Invoke-ToolScript (Get-Text "HardwareTest") (Join-Path $root "scripts\test-hardware.ps1") @(
        "-PublishDirectory", $publish,
        "-ConfigPath", $config,
        "-Language", $Language,
        "-ConfirmEachStep"
    )
}

function Install-BridgeService {
    Invoke-ToolScript (Get-Text "InstallService" @($ServiceName)) (Join-Path $root "scripts\install-service.ps1") @(
        "-PublishDirectory", $publish,
        "-ServiceName", $ServiceName
    )

    $service = Get-Service -Name $ServiceName -ErrorAction Stop
    Write-Host (Get-Text "ServiceStatus" @($ServiceName, $service.Status))
}

if (-not (Test-Administrator)) {
    Invoke-ElevatedSelf
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path $setupLog -Force | Out-Null

$exitCode = 0
try {
    Write-Host (Get-Text "SetupTitle")
    Write-Host (Get-Text "LogPath" @($setupLog))

    Ensure-PublishOutput
    Ensure-Config
    Stop-MaxsunSync
    Remove-ExistingBridgeService
    Invoke-ToolScript (Get-Text "CheckEnvironment") (Join-Path $root "scripts\check-environment.ps1") @("-ConfigPath", $config)
    Invoke-HomeAssistantCheck
    Invoke-HardwareTest
    Install-BridgeService

    Write-Host ""
    Write-Host (Get-Text "SetupCompleted")
}
catch {
    $exitCode = 1
    Write-Host ""
    Write-Host (Get-Text "ErrorPrefix" @($_.Exception.Message)) -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
    if ($ElevatedChild -and -not $NoPause) {
        Write-Host ""
        Read-Host (Get-Text "PressEnterClose") | Out-Null
    }
}

exit $exitCode
