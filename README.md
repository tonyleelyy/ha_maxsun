# ha_maxsun

[English](README.en.md)

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#安装前提)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-WebSocket-41BDF5)](#配置-home-assistant)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

把铭瑄主板 RGB 灯接入 Home Assistant 的 Windows 后台桥接服务。它不运行、不调用 `MaxsunSync2.exe` 或 `MaxsunSyncService.exe`，只复用这些软件安装到系统里的 ASUS Aura / ENE / Maxsun 底层 HAL 和驱动。

当前已实机验证的设备：

- 设备名：`MAXSUN MOTHERBOARD LED ENE`
- HAL GUID：`9d590787-6015-445d-9076-30b360cdf24b`
- LED 数量：`264`

第一版只做静态灯光控制：开关、RGB 颜色、亮度。Home Assistant 侧不需要 MQTT，用 WebSocket API + helper/template light 实现。

## 项目状态

这是一个社区项目，不是铭瑄、ASUS、ENE 或 Home Assistant 官方项目。当前版本已经在上面的 `MAXSUN MOTHERBOARD LED ENE` 设备上完成实机验证，但其他铭瑄/ENE 设备可能需要补充 GUID、LED 数量或 HAL 适配。

欢迎提交 issue 或 PR 补充更多主板型号的测试结果。

## 功能

- 在 Home Assistant 里显示为一个灯实体：`light.maxsun_motherboard_rgb`
- 支持开关、RGB 颜色、`0..255` 亮度
- 亮度在写入硬件前按 `rgb * brightness / 255` 缩放
- Windows Service 后台运行，开机自动启动
- 通过独立 64-bit HAL helper 进程调用 ASUS/ENE COM，避免底层 native 崩溃拖垮主服务
- 启动时检测 `MaxsunSync2.exe` / `MaxsunSyncService.exe` 冲突，发现运行则标记不可用并停止写硬件

## 重要说明

这个项目不包含、也不分发任何铭瑄、ASUS、ENE 的驱动或 HAL 文件。你仍然需要先安装铭瑄 RGB 软件或对应驱动包，让下面这些目录和 COM/HAL 注册存在：

- `C:\Program Files\MaxSun\LightControlModule`
- `C:\Program Files\ASUS\AuraSDK`
- `C:\Program Files\ENE`

运行时不依赖铭瑄主程序，但依赖它随软件安装到系统里的底层组件。

## 已安装后怎么启动服务

如果你已经按本项目安装过服务，服务名是 `ha_maxsun`。它会开机自动启动，也可以手动控制。

如果你是从旧版 `MaxsunControlBridge` 服务名升级，先卸载旧服务：

```powershell
.\scripts\uninstall-service.ps1 -ServiceName MaxsunControlBridge
```

用管理员 PowerShell 启动：

```powershell
Start-Service ha_maxsun
```

查看状态：

```powershell
Get-Service ha_maxsun
```

重启：

```powershell
Restart-Service ha_maxsun
```

停止：

```powershell
Stop-Service ha_maxsun
```

也可以打开 Windows 服务管理器：

```powershell
services.msc
```

然后找到 `ha_maxsun`，右键启动、停止或重启。

日志默认在：

```text
publish\logs\bridge-yyyyMMdd.log
```

如果你换了安装目录，日志就在项目目录下的 `publish\logs\`。

## 新用户从零开始

下面的命令默认都在仓库根目录运行，也就是 clone 后的 `ha_maxsun` 目录，不要在 `homeassistant` 子目录里运行。

### 1. 安装前提

需要：

- Windows x64
- 已安装铭瑄 RGB 软件或对应驱动包，用来提供 ASUS/ENE/Maxsun HAL
- .NET 10 SDK，用于正常构建
- Home Assistant long-lived access token
- 管理员权限，用于硬件测试和安装 Windows Service

如果没有 .NET SDK，但有 .NET 10 Runtime + Visual Studio BuildTools，本项目脚本可以走 Roslyn fallback 构建出 framework-dependent DLL。不过开源用户建议直接安装 .NET 10 SDK。

### 2. 停止铭瑄同步程序

本桥接服务会拒绝和铭瑄官方同步程序抢硬件控制。先停止它：

```powershell
Stop-Process -Name MaxsunSync2 -ErrorAction SilentlyContinue
Stop-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
```

可选：把 `MaxsunSyncService` 改为手动启动。

```powershell
Set-Service -Name MaxsunSyncService -StartupType Manual
```

### 3. 配置 Home Assistant

把 [homeassistant/maxsun_motherboard_rgb.yaml](homeassistant/maxsun_motherboard_rgb.yaml) 放进 Home Assistant packages 目录，例如：

```yaml
homeassistant:
  packages: !include_dir_named packages
```

重启 Home Assistant，或重新加载 helpers/templates。

应该出现这些 helper：

- `input_boolean.maxsun_motherboard_rgb_power`
- `input_number.maxsun_motherboard_rgb_brightness`
- `input_text.maxsun_motherboard_rgb_color`
- `input_boolean.maxsun_motherboard_rgb_available`

最终 UI 灯实体是：

- `light.maxsun_motherboard_rgb`

### 4. 获取 Home Assistant token

在 Home Assistant 左下角点你的用户名，进入个人资料页，拉到最下面的 Long-lived access tokens，新建一个 token。复制后只保存到本机配置文件里，不要提交到 GitHub。

### 5. 构建

在项目目录执行：

```powershell
.\scripts\build.ps1
```

构建结果会输出到 `publish\`。如果 `publish\appsettings.json` 不存在，脚本会从 [appsettings.example.json](appsettings.example.json) 复制一份。

### 6. 编辑配置

编辑 `publish\appsettings.json`：

```json
{
  "homeAssistant": {
    "webSocketUrl": "ws://homeassistant.local:8123/api/websocket",
    "longLivedAccessToken": "REPLACE_ME"
  }
}
```

如果你的 Home Assistant 使用 HTTPS，请改成：

```text
wss://your-ha-host:8123/api/websocket
```

不要把 `publish\appsettings.json` 提交到 GitHub。仓库的 `.gitignore` 已经忽略了 `publish\` 和 `appsettings.json`。

### 7. 检查环境和 HA 实体

```powershell
.\scripts\check-environment.ps1
.\scripts\check-ha.ps1
```

`check-environment.ps1` 会检查 .NET、HAL 目录、COM 注册、MaxsunSync 冲突和配置文件。`check-ha.ps1` 会检查 HA helper/template light 是否存在。

### 8. 先做一次硬件测试

用管理员 PowerShell 运行：

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

它会依次测试红、绿、蓝、低亮度白、关闭。每一步都可以人工确认主板灯是否变化。

也可以直接跑单次命令：

```powershell
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-probe
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-apply --rgb 255,0,0 --brightness 128
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-apply --off
```

如果你是 Roslyn fallback 构建，没有 `.exe`，把命令替换为：

```powershell
dotnet .\publish\ha_maxsun.dll --config .\publish\appsettings.json --once-probe
```

`--once-probe` 和 `--once-apply` 不需要 HA token 配好，所以也可以用于排查纯硬件链路。

### 9. 安装并启动 Windows Service

普通 PowerShell 也可以执行，脚本会自动弹 UAC 请求管理员权限：

```powershell
.\scripts\install-service.ps1
```

安装完成后：

- 服务名：`ha_maxsun`
- 运行账户：`LocalSystem`
- 启动类型：`Automatic`
- 安装后会立即启动
- 之后开机会自动启动

查看服务状态：

```powershell
Get-Service ha_maxsun
```

查看日志：

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 80
```

### 10. 卸载服务

```powershell
.\scripts\uninstall-service.ps1
```

如果没有管理员权限，请用管理员 PowerShell 运行。

## 更新版本

如果你已经安装过服务，建议这样更新：

```powershell
Stop-Service ha_maxsun
.\scripts\build.ps1
Start-Service ha_maxsun
```

如果服务的启动命令或安装脚本有变化，可以卸载后重装：

```powershell
.\scripts\uninstall-service.ps1
.\scripts\build.ps1
.\scripts\install-service.ps1
```

如果你机器上还留着旧版 `MaxsunControlBridge` 服务，用下面命令移除旧服务：

```powershell
.\scripts\uninstall-service.ps1 -ServiceName MaxsunControlBridge
```

## 项目结构

```text
src/ha_maxsun.Core        通用 RGB、状态、协议模型
src/ha_maxsun.Service     Windows Service 和 HA WebSocket 桥
src/ha_maxsun.HalHelper  64-bit HAL helper，负责实际调用 ASUS/ENE/Maxsun HAL
tests/ha_maxsun.Tests    单元测试和 fake helper 集成测试
homeassistant/               Home Assistant package
scripts/                     构建、测试、检查、安装服务脚本
```

## 架构

`ha_maxsun` 是长期运行的主服务，负责：

- 连接 Home Assistant WebSocket API
- 订阅 helper 的 `state_changed` 事件
- 读取开关、亮度和 RGB helper 状态
- 调用 HAL helper 写入硬件
- 更新 HA availability helper
- 掉线后自动重连

`ha_maxsun.HalHelper` 是独立 64-bit 子进程，负责：

- 优先通过 `aura.sdk` / ASUS Aura SDK 进入 Aura 控制模式
- 枚举 `MAXSUN MOTHERBOARD LED ENE`
- 对 264 个 LED 写入同一静态颜色
- 调用 `Apply()`
- 通过 stdin/stdout JSON 和主服务通信

这样即使 ASUS/ENE native COM/HAL 出问题，主桥接服务也更容易恢复。

## HAL helper JSON 协议

helper 每行接收一个 JSON 请求，每行返回一个 JSON 响应。

探测：

```json
{"command":"probe"}
```

写颜色：

```json
{"command":"apply","on":true,"rgb":[255,64,32],"brightness":128}
```

成功响应会包含亮度缩放后的实际硬件颜色：

```json
{"ok":true,"appliedRgb":[128,32,16]}
```

关灯时，桥接服务会向硬件写入黑色。

## 测试

```powershell
.\scripts\test.ps1
```

测试覆盖：

- RGB/brightness 缩放
- 关灯写黑色
- HA helper state 解析
- HAL helper JSON 协议
- fake HAL helper 集成链路
- HA 连接失败后的重连行为

## 故障排查

### HA 里灯显示 unavailable

先看日志：

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 120
```

常见原因：

- `MaxsunSync2.exe` 或 `MaxsunSyncService.exe` 正在运行
- HA token 填错或过期
- HA WebSocket URL 不对
- HA package 没加载，helper 实体不存在
- ASUS/ENE/Maxsun HAL 没安装或 COM 注册缺失

### 命令返回 OK 但灯不变

先确认铭瑄官方同步服务已经停止。然后用管理员 PowerShell 跑：

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

如果 `probe` 能找到设备但 apply 不生效，通常是 Aura/ENE 控制权被其他 RGB 软件占用。

### 服务装不上

安装服务需要管理员权限。直接运行：

```powershell
.\scripts\install-service.ps1
```

脚本会自动弹 UAC。安装日志在：

```text
publish\logs\install-service.log
```

### 端口或 MQTT 怎么配置

不用配置。这个项目不使用 MQTT，也不开放本地 HTTP 端口。桥接方向是 Windows 服务主动连接 Home Assistant WebSocket API。

## 贡献

欢迎提交 issue 或 PR，尤其是：

- 其他铭瑄主板或 ENE RGB 设备的测试结果
- 新设备的设备名、GUID、LED 数量
- Home Assistant package 改进
- 安装、恢复、睡眠唤醒后的稳定性改进
- 更清晰的错误日志和排查脚本

提交设备兼容性信息时，建议包含：

- 主板型号
- Windows 版本
- HAL 设备名
- HAL GUID
- LED 数量
- `scripts\check-environment.ps1` 输出
- 是否能通过 `scripts\test-hardware.ps1 -ConfirmEachStep`

请不要在 issue 或 PR 里粘贴 Home Assistant long-lived token。

提交前建议运行：

```powershell
.\scripts\test.ps1
.\scripts\check-environment.ps1
```

## 安全和隐私

- 不要提交 `publish\appsettings.json`
- 不要提交 Home Assistant long-lived token
- 不要提交 `publish\`、`logs\` 或本机生成文件
- 仓库不包含任何厂商 DLL、驱动或 HAL 文件
- 日志设计上不打印 token；如果你要公开日志，仍建议先检查并脱敏

## 免责声明

本项目通过已安装的 ASUS/ENE/Maxsun 底层 HAL 控制 RGB 硬件。不同主板、不同版本 HAL 或其他 RGB 软件同时运行时，行为可能不同。请自行承担使用风险；项目按 MIT License 以“原样”提供，不提供任何形式的担保。

## License

本项目使用 [MIT License](LICENSE)。


