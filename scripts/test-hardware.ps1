param(
    [string]$PublishDirectory = "publish",
    [string]$ConfigPath,
    [ValidateSet("zh-CN", "en-US")]
    [string]$Language = "zh-CN",
    [switch]$ConfirmEachStep
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = if ([IO.Path]::IsPathRooted($PublishDirectory)) { $PublishDirectory } else { Join-Path $root $PublishDirectory }
$exe = Join-Path $publish "ha_maxsun.exe"
$dll = Join-Path $publish "ha_maxsun.dll"
$config = if ($ConfigPath) { $ConfigPath } else { Join-Path $publish "appsettings.json" }
$caseNames = @{
    "zh-CN" = @{
        red = "红色"
        green = "绿色"
        blue = "蓝色"
        "low-white" = "低亮度白色"
        off = "关闭"
    }
    "en-US" = @{
        red = "red"
        green = "green"
        blue = "blue"
        "low-white" = "low-brightness white"
        off = "off"
    }
}

function Get-CaseName {
    param([string]$Name)

    return $caseNames[$Language][$Name]
}

function Test-YesAnswer {
    param([string]$Value)

    return $Value -in @("y", "Y", "yes", "YES", "是", "好")
}

if (Test-Path $exe) {
    $command = $exe
    $prefixArgs = @()
}
elseif (Test-Path $dll) {
    $command = "dotnet"
    $prefixArgs = @($dll)
}
else {
    if ($Language -eq "zh-CN") {
        throw "找不到程序文件：$exe 或 $dll。请先运行 install.bat，或执行 scripts\build.ps1 构建。"
    }

    throw "Bridge executable not found: $exe or $dll. Run scripts\build.ps1 first."
}

if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $root "appsettings.example.json") $config
}

$conflicts = @(Get-Process -Name MaxsunSync2,MaxsunSyncService -ErrorAction SilentlyContinue)
if ($conflicts.Count -gt 0) {
    $names = ($conflicts | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
    if ($Language -eq "zh-CN") {
        throw "检测到 MaxsunSync 冲突：$names。硬件测试前请先停止 MaxsunSync2 和 MaxsunSyncService。"
    }

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
    $displayName = Get-CaseName $case.Name
    if ($Language -eq "zh-CN") {
        Write-Host "正在应用 $displayName..."
    }
    else {
        Write-Host "Applying $displayName..."
    }

    & $command @prefixArgs --config $config @($case.Args)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($ConfirmEachStep) {
        if ($Language -eq "zh-CN") {
            $answer = Read-Host "主板灯是否已经明显变为“$displayName”？输入 y 继续"
            if (-not (Test-YesAnswer $answer)) {
                throw "硬件确认失败：$displayName。"
            }
        }
        else {
            $answer = Read-Host "Did the motherboard RGB visibly change to '$displayName'? Type y to continue"
            if (-not (Test-YesAnswer $answer)) {
                throw "Hardware confirmation failed at '$displayName'."
            }
        }
    }

    Start-Sleep -Seconds 2
}
