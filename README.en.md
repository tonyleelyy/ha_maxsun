# ha_maxsun

[简体中文](README.md)

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#one-click-install)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-WebSocket-41BDF5)](#1-configure-home-assistant)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A Windows background bridge that connects Maxsun motherboard RGB lighting to Home Assistant. It uses the ASUS Aura / ENE / Maxsun low-level HAL and driver stack.

```
Currently tested only on MS-Terminator B760M D4 WIFI/WIFI6/WIFI6E motherboards.
```

## Features

- Exposes one Home Assistant light entity: `light.maxsun_motherboard_rgb`
- Supports power, RGB color, and brightness control

## One-Click Install

Download the GitHub Release zip and extract it to a stable directory, for example `D:\ha_maxsun`. Release packages are Windows x64 self-contained builds, so normal users do not need to install the .NET Runtime or .NET SDK. If you cloned the source tree instead, the setup wizard will try to build `publish\` automatically when it is missing.

You need:

- Windows x64
- [Maxsun RGB software](https://www.maxsun.com.cn/2024/1024/6320.html) or an equivalent driver package installed, without requiring it to start with Windows, to provide ASUS/ENE/Maxsun HAL components
- Home Assistant long-lived access token
- Administrator privileges
- .NET 10 SDK only when building from source or creating a Release package

### 1. Configure Home Assistant

Copy [homeassistant/maxsun_motherboard_rgb.yaml](homeassistant/maxsun_motherboard_rgb.yaml) into your Home Assistant packages directory. Create the directory if it does not exist. Then add this to `configuration.yaml`:

```yaml
homeassistant:
  packages: !include_dir_named packages
```

Restart Home Assistant.

These helper entities and the final light should appear:

- `input_boolean.maxsun_motherboard_rgb_power`
- `input_number.maxsun_motherboard_rgb_brightness`
- `input_text.maxsun_motherboard_rgb_color`
- `input_boolean.maxsun_motherboard_rgb_available`
- `light.maxsun_motherboard_rgb`

To create a long-lived access token, click your user name in the lower-left corner of Home Assistant, open your profile page, scroll to the Long-lived access tokens section, and create one. Store it only in the local config file. Do not commit it to GitHub.

### 2. Run the Setup Wizard

Double-click this file from the extracted project root:

```text
install-en.bat
```

Chinese users can run:

```text
install.bat
```

Approve the UAC prompt if Windows asks for administrator privileges. The wizard will:

- Try to build the project if `publish\` output is missing
- Create or update `publish\appsettings.json`
- Ask for the Home Assistant address and long-lived access token if they are not configured yet
- Stop `MaxsunSync2` / `MaxsunSyncService`, and set `MaxsunSyncService` to manual startup
- Check the local environment and Home Assistant entities
- Test red, green, blue, low-brightness white, and off, asking you to confirm each visible change
- Install and start the `ha_maxsun` Windows Service

After setup finishes, control `light.maxsun_motherboard_rgb` in Home Assistant.

## Service Management

```powershell
Get-Service ha_maxsun # Check status
Restart-Service ha_maxsun # Restart service
Stop-Service ha_maxsun # Stop service
Start-Service ha_maxsun # Start service
```

View logs:

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 80
```

Uninstall the service:

```powershell
.\scripts\uninstall-service.ps1
```

## Updating

Download the new Release zip, stop the service, overwrite the old directory, and run `install.bat` again. The wizard will remove and reinstall the service.

```powershell
Stop-Service ha_maxsun
```

## Creating Release Packages

Maintainers can run this on a Windows x64 machine with the .NET 10 SDK installed:

```powershell
.\scripts\package-release.ps1 -Version beta
```

The script creates a self-contained zip and SHA256 file:

```text
artifacts\ha_maxsun-beta-win-x64.zip
artifacts\ha_maxsun-beta-win-x64.sha256.txt
```

## Project Layout

```text
src/ha_maxsun.Core        Shared RGB, state, and protocol models
src/ha_maxsun.Service     Windows Service and HA WebSocket bridge
src/ha_maxsun.HalHelper   64-bit HAL helper that calls ASUS/ENE/Maxsun HAL
tests/ha_maxsun.Tests     Unit tests and fake-helper integration tests
homeassistant/            Home Assistant package
scripts/                  Build, test, check, and service scripts
```

## Architecture

`ha_maxsun` is the long-running service. It:

- Connects to the Home Assistant WebSocket API
- Subscribes to helper `state_changed` events
- Reads power, brightness, and RGB helper state
- Calls the HAL helper to write hardware state
- Updates the HA availability helper
- Reconnects after disconnects

`ha_maxsun.HalHelper` is a separate 64-bit child process. It:

- Enters Aura control mode through `aura.sdk` / ASUS Aura SDK
- Enumerates `MAXSUN MOTHERBOARD LED ENE`
- Writes the same static color to all 264 LEDs
- Calls `Apply()`
- Communicates with the service through stdin/stdout JSON

This process boundary makes the main bridge service easier to recover if ASUS/ENE native COM/HAL code fails.

## Troubleshooting

### The HA Light Shows unavailable

Check logs first:

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 120
```

Common causes:

- `MaxsunSync2.exe` or `MaxsunSyncService.exe` is running
- HA long-lived access token is wrong or expired
- HA WebSocket URL is wrong
- HA package was not loaded, so helper entities do not exist
- ASUS/ENE/Maxsun HAL is missing or COM registration is broken

### Commands Return OK but LEDs Do Not Change

First confirm the official Maxsun sync service is stopped. Then run:

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

If `probe` finds the device but `apply` has no visible effect, Aura/ENE control is usually held by another RGB program.

## Contributing

Issues and PRs are welcome, especially for:

- Test results for other Maxsun motherboards or ENE RGB devices
- New device names, GUIDs, and LED counts
- Home Assistant package improvements
- Reliability improvements for installation, resume, and sleep/wake
- Clearer diagnostics and troubleshooting scripts

When reporting hardware compatibility, please include:

- Motherboard model
- Windows version
- HAL device name
- HAL GUID
- Output from `scripts\check-environment.ps1`
- Whether `scripts\test-hardware.ps1 -ConfirmEachStep` works

Before submitting changes, run:

```powershell
.\scripts\test.ps1
.\scripts\check-environment.ps1
```

## Disclaimer

This project controls RGB hardware through the installed ASUS/ENE/Maxsun low-level HAL. Behavior may vary between motherboards, HAL versions, and systems running other RGB software. Use it at your own risk.

## License

This project is licensed under the [MIT License](LICENSE).
