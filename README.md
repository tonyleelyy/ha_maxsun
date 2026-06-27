# ha_maxsun

[English](README.en.md)

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#一键安装)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-WebSocket-41BDF5)](#1-配置-home-assistant)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

把铭瑄主板 RGB 灯接入 Home Assistant 的 Windows 后台桥接服务。调用 ASUS Aura / ENE / Maxsun 底层 HAL 和驱动。

```
目前仅在MS-Terminator（终结者）B760M D4 WIFI/WIFI6/WIFI6E主板中测试。
```



## 功能

- 在 Home Assistant 里显示为一个灯实体：`light.maxsun_motherboard_rgb`
- 支持开关、RGB 颜色、调整亮度

## 一键安装

推荐下载 GitHub Release 压缩包，解压到固定目录，例如 `D:\ha_maxsun`。如果你是从源码 clone，本向导也会在缺少 `publish\` 输出时尝试自动构建。

安装前需要：

- Windows x64
- 安装[铭瑄 RGB 软件](https://www.maxsun.com.cn/2024/1024/6320.html)或对应驱动包（无需开机自启），用于提供 ASUS/ENE/Maxsun HAL
- Home Assistant long-lived access token
- 管理员权限
- 从源码自动构建时需要 .NET 10 SDK；Release 包用户不需要自己构建

### 1. 配置 Home Assistant

把 [homeassistant/maxsun_motherboard_rgb.yaml](homeassistant/maxsun_motherboard_rgb.yaml) 放进 Home Assistant 的 packages 目录（没有则新建文件夹）。之后在 `configuration.yaml` 里添加：

```yaml
homeassistant:
  packages: !include_dir_named packages
```

重启 Home Assistant。

应该出现这些 helper 和灯实体：

- `input_boolean.maxsun_motherboard_rgb_power`
- `input_number.maxsun_motherboard_rgb_brightness`
- `input_text.maxsun_motherboard_rgb_color`
- `input_boolean.maxsun_motherboard_rgb_available`
- `light.maxsun_motherboard_rgb`

token 获取方式：在 Home Assistant 左下角点你的用户名，进入个人资料页，拉到最下面的 Long-lived access tokens，新建一个 token。复制后只保存到本机配置文件里，不要提交到 GitHub。

### 2. 运行安装向导

在解压后的项目根目录双击：

```text
install.bat
```

如果弹出 UAC，请允许管理员权限。向导会自动完成：

- 没有 `publish\` 输出时尝试自动构建
- 创建或更新 `publish\appsettings.json`
- 如果没有填 Home Assistant 地址和 token，就在窗口里询问并写入配置
- 停止 `MaxsunSync2` / `MaxsunSyncService`，并把 `MaxsunSyncService` 改为手动启动
- 检查本机环境和 Home Assistant 实体
- 依次测试红、绿、蓝、低亮度白、关闭，并让你确认主板灯是否变化
- 安装并启动 `ha_maxsun` Windows Service

安装完成后，Home Assistant 里控制 `light.maxsun_motherboard_rgb` 即可。

## 服务管理

```powershell
Get-Service ha_maxsun #查看状态
Restart-Service ha_maxsun #重启服务
Stop-Service ha_maxsun #停止服务
Start-Service ha_maxsun #启动服务
```

查看日志：

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 80
```

卸载服务：

```powershell
.\scripts\uninstall-service.ps1
```

## 更新版本

下载新版 Release 压缩包，停止服务后覆盖原目录，再运行 `install.bat`。向导会自动卸载旧服务并重新安装。

```powershell
Stop-Service ha_maxsun
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
- `scripts\check-environment.ps1` 输出
- 是否能通过 `scripts\test-hardware.ps1 -ConfirmEachStep`

提交前建议运行：

```powershell
.\scripts\test.ps1
.\scripts\check-environment.ps1
```

## 免责声明

本项目通过已安装的 ASUS/ENE/Maxsun 底层 HAL 控制 RGB 硬件。不同主板、不同版本 HAL 或其他 RGB 软件同时运行时，行为可能不同。请自行承担使用风险。

## License

本项目使用 [MIT License](LICENSE)。

