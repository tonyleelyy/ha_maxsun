# ha_maxsun

[简体中文](README.md)

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#prerequisites)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-WebSocket-41BDF5)](#configure-home-assistant)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A Windows background bridge that connects Maxsun motherboard RGB lighting to Home Assistant. It uses the ASUS Aura / ENE / Maxsun low-level HAL and driver stack.

## Features

- Exposes one Home Assistant light entity: `light.maxsun_motherboard_rgb`
- Supports power, RGB color, and brightness control

## Starting an Installed Service

If you have already installed this project as a service, the service name is `ha_maxsun`. It starts automatically on boot, and you can also control it manually.

Start from an elevated PowerShell:

```powershell
Start-Service ha_maxsun
```

Check status:

```powershell
Get-Service ha_maxsun
```

Restart:

```powershell
Restart-Service ha_maxsun
```

Stop:

```powershell
Stop-Service ha_maxsun
```

You can also manage it from Windows Services.

## Installation

Run the commands below from the repository root, usually the cloned `ha_maxsun` directory. Do not run them from the `homeassistant` subdirectory.

### 1. Prerequisites

You need:

- Windows x64
- Maxsun RGB software or an equivalent driver package installed, to provide ASUS/ENE/Maxsun HAL components
- .NET 10 SDK for normal builds
- Home Assistant long-lived access token
- Administrator privileges for hardware testing and Windows Service installation

### 2. Stop Maxsun Sync

This bridge refuses to compete with the official Maxsun sync process for hardware control. Stop it first:

```powershell
Stop-Process -Name MaxsunSync2 -ErrorAction SilentlyContinue
Stop-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
```

Optionally set `MaxsunSyncService` to manual startup:

```powershell
Set-Service -Name MaxsunSyncService -StartupType Manual
```

### 3. Configure Home Assistant

Copy [homeassistant/maxsun_motherboard_rgb.yaml](homeassistant/maxsun_motherboard_rgb.yaml) into your Home Assistant packages directory. Create the directory if it does not exist. Then add this to `configuration.yaml`:

```yaml
homeassistant:
  packages: !include_dir_named packages
```

Restart Home Assistant.

These helper entities should appear:

- `input_boolean.maxsun_motherboard_rgb_power`
- `input_number.maxsun_motherboard_rgb_brightness`
- `input_text.maxsun_motherboard_rgb_color`
- `input_boolean.maxsun_motherboard_rgb_available`

The final UI light entity is:

- `light.maxsun_motherboard_rgb`

### 4. Create a Home Assistant Token

In Home Assistant, click your user name in the lower-left corner, open your profile page, scroll to Long-lived access tokens, and create a token. Store it only in the local config file. Do not commit it to GitHub.

### 5. Build

Run this from the repository root:

```powershell
.\scripts\build.ps1
```

Build output is written to `publish\`. If `publish\appsettings.json` does not exist, the script copies it from [appsettings.example.json](appsettings.example.json).

### 6. Optional: Create a Local Vendor Runtime

This GitHub repository does not include proprietary Maxsun, ASUS, or ENE DLLs. If you want a target machine to run without installing MaxsunSync2 first, collect the local runtime on a machine that already has those components:

```powershell
.\scripts\collect-vendor-runtime.ps1
```

The script copies the required components to `publish\vendor\` and copies portable install scripts to `publish\`. `publish\vendor\` is ignored by `.gitignore`; do not commit or publicly redistribute those vendor files unless you have the right to do so.

After copying the whole `publish\` directory to the target machine, run this from an elevated PowerShell inside `publish\`:

```powershell
.\register-vendor-runtime.ps1
.\install-service.ps1
```

The program prefers HAL files from `publish\vendor\`. COM components still need to be registered once on the target machine, so run `register-vendor-runtime.ps1` before installing the service.

The repository includes [vendor/README.md](vendor/README.md) as a directory layout note. If you do have redistribution rights for the vendor DLLs, you can use that layout for a private package or release; the public GitHub repository does not include those proprietary DLLs by default.

### 7. Edit Configuration

Edit `publish\appsettings.json`:

```json
{
  "homeAssistant": {
    "webSocketUrl": "ws://homeassistant.local:8123/api/websocket",
    "longLivedAccessToken": "REPLACE_ME"
  }
}
```

If your Home Assistant endpoint uses HTTPS, use:

```text
wss://your-ha-host:8123/api/websocket
```

### 8. Check Environment and HA Entities

```powershell
.\scripts\check-environment.ps1
.\scripts\check-ha.ps1
```

`check-environment.ps1` checks .NET, HAL directories, COM registration, MaxsunSync conflicts, and the config file. `check-ha.ps1` checks whether the HA helper/template light entities exist.

### 9. Hardware Test

Run from an elevated PowerShell:

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

The script tests red, green, blue, low-brightness white, and off. You can visually confirm each step.

### 10. Install and Start the Windows Service

Run from the repository root:

```powershell
.\scripts\install-service.ps1
```

After installation:

- Service name: `ha_maxsun`
- Account: `LocalSystem`
- Startup type: `Automatic`
- The service starts immediately after installation
- The service starts automatically after reboot

Check service status:

```powershell
Get-Service ha_maxsun
```

View logs:

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 80
```

### 11. Uninstall the Service

```powershell
.\scripts\uninstall-service.ps1
```

If you do not have administrator privileges, run it from an elevated PowerShell.

## Updating

If the service is already installed, update with:

```powershell
Stop-Service ha_maxsun
.\scripts\build.ps1
Start-Service ha_maxsun
```

If the service command line or install script changed, uninstall and reinstall:

```powershell
.\scripts\uninstall-service.ps1
.\scripts\build.ps1
.\scripts\install-service.ps1
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
- HA token is wrong or expired
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
