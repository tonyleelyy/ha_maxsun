# ha_maxsun

[简体中文](README.md)

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](#prerequisites)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-WebSocket-41BDF5)](#configure-home-assistant)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A Windows background bridge that exposes a Maxsun motherboard RGB device to Home Assistant. It does not run or call `MaxsunSync2.exe` or `MaxsunSyncService.exe`; it only reuses the ASUS Aura / ENE / Maxsun HAL and driver components that the vendor software installs on the system.

Hardware verified so far:

- Device name: `MAXSUN MOTHERBOARD LED ENE`
- HAL GUID: `9d590787-6015-445d-9076-30b360cdf24b`
- LED count: `264`

The first version supports static lighting only: power, RGB color, and brightness. Home Assistant integration uses the WebSocket API plus helper/template light entities. No MQTT broker is required.

## Project Status

This is a community project. It is not affiliated with Maxsun, ASUS, ENE, or Home Assistant. The current version has been tested on the `MAXSUN MOTHERBOARD LED ENE` device listed above. Other Maxsun/ENE devices may require additional GUID, LED count, or HAL compatibility work.

Issues and PRs with additional motherboard test results are welcome.

## Features

- Appears in Home Assistant as `light.maxsun_motherboard_rgb`
- Supports power, RGB color, and `0..255` brightness
- Scales hardware color as `rgb * brightness / 255`
- Runs as an auto-starting Windows Service
- Uses a separate 64-bit HAL helper process so ASUS/ENE native COM failures do not take down the bridge service
- Detects `MaxsunSync2.exe` / `MaxsunSyncService.exe` conflicts and marks the HA light unavailable instead of fighting for hardware control

## Important Notes

This repository does not include or redistribute any Maxsun, ASUS, or ENE driver/HAL files. You still need to install the vendor RGB package or equivalent driver stack so these directories and COM/HAL registrations exist:

- `C:\Program Files\MaxSun\LightControlModule`
- `C:\Program Files\ASUS\AuraSDK`
- `C:\Program Files\ENE`

The bridge does not depend on the Maxsun UI/service at runtime, but it does depend on the low-level components installed by that package.

## Starting an Already Installed Service

If you have already installed the service, its name is `ha_maxsun`. It starts automatically on boot, and you can also control it manually.

If you are upgrading from the old `MaxsunControlBridge` service name, remove the old service first:

```powershell
.\scripts\uninstall-service.ps1 -ServiceName MaxsunControlBridge
```

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

You can also open Windows Services:

```powershell
services.msc
```

Then find `ha_maxsun` and start, stop, or restart it from the UI.

Logs are written to:

```text
publish\logs\bridge-yyyyMMdd.log
```

## New User Setup

The commands below are meant to be run from the repository root, usually the cloned `ha_maxsun` directory, not from the `homeassistant` subdirectory.

### 1. Prerequisites

You need:

- Windows x64
- The Maxsun RGB software or equivalent driver package installed, to provide ASUS/ENE/Maxsun HAL files
- .NET 10 SDK for normal builds
- A Home Assistant long-lived access token
- Administrator privileges for hardware testing and Windows Service installation

If you do not have the .NET SDK but do have .NET 10 Runtime plus Visual Studio BuildTools, the scripts can fall back to Roslyn and produce framework-dependent DLLs. For open-source users, installing the .NET 10 SDK is still recommended.

### 2. Stop MaxsunSync

The bridge refuses to compete with the vendor sync process for hardware control:

```powershell
Stop-Process -Name MaxsunSync2 -ErrorAction SilentlyContinue
Stop-Service -Name MaxsunSyncService -ErrorAction SilentlyContinue
```

Optionally set the service to manual startup:

```powershell
Set-Service -Name MaxsunSyncService -StartupType Manual
```

### 3. Configure Home Assistant

Copy [homeassistant/maxsun_motherboard_rgb.yaml](homeassistant/maxsun_motherboard_rgb.yaml) into your Home Assistant packages directory, for example:

```yaml
homeassistant:
  packages: !include_dir_named packages
```

Restart Home Assistant, or reload helpers/templates.

Expected helper entities:

- `input_boolean.maxsun_motherboard_rgb_power`
- `input_number.maxsun_motherboard_rgb_brightness`
- `input_text.maxsun_motherboard_rgb_color`
- `input_boolean.maxsun_motherboard_rgb_available`

The UI light entity is:

- `light.maxsun_motherboard_rgb`

### 4. Create a Home Assistant Token

In Home Assistant, click your user profile in the lower-left corner, scroll to Long-lived access tokens, and create a token. Store it only in the local config file. Do not commit it to GitHub.

### 5. Build

From the repository root:

```powershell
.\scripts\build.ps1
```

Build outputs go to `publish\`. If `publish\appsettings.json` does not exist, the script copies it from [appsettings.example.json](appsettings.example.json).

### 6. Edit Configuration

Edit `publish\appsettings.json`:

```json
{
  "homeAssistant": {
    "webSocketUrl": "ws://homeassistant.local:8123/api/websocket",
    "longLivedAccessToken": "REPLACE_ME"
  }
}
```

For HTTPS Home Assistant endpoints, use:

```text
wss://your-ha-host:8123/api/websocket
```

Do not commit `publish\appsettings.json`. The repository ignores `publish\` and `appsettings.json`.

### 7. Check Environment and HA Entities

```powershell
.\scripts\check-environment.ps1
.\scripts\check-ha.ps1
```

`check-environment.ps1` checks .NET, HAL directories, COM registration, MaxsunSync conflicts, and local config. `check-ha.ps1` verifies the Home Assistant helper/template light entities.

### 8. Run a Hardware Test First

Run from an elevated PowerShell:

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

It probes the HAL, then applies red, green, blue, low-brightness white, and off.

You can also run one-shot commands:

```powershell
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-probe
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-apply --rgb 255,0,0 --brightness 128
.\publish\ha_maxsun.exe --config .\publish\appsettings.json --once-apply --off
```

For Roslyn fallback builds without `.exe` files, use:

```powershell
dotnet .\publish\ha_maxsun.dll --config .\publish\appsettings.json --once-probe
```

`--once-probe` and `--once-apply` do not require a configured Home Assistant token, so they are useful for hardware-only troubleshooting.

### 9. Install and Start the Windows Service

You can run this from a normal PowerShell; the script requests UAC elevation automatically:

```powershell
.\scripts\install-service.ps1
```

After installation:

- Service name: `ha_maxsun`
- Account: `LocalSystem`
- Startup type: `Automatic`
- The service starts immediately after installation
- It starts automatically after reboot

Check status:

```powershell
Get-Service ha_maxsun
```

View logs:

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 80
```

### 10. Uninstall the Service

```powershell
.\scripts\uninstall-service.ps1
```

Run it from an elevated PowerShell if needed.

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

If the old `MaxsunControlBridge` service is still installed on your machine, remove it with:

```powershell
.\scripts\uninstall-service.ps1 -ServiceName MaxsunControlBridge
```

## Project Layout

```text
src/ha_maxsun.Core        Shared RGB, state, and protocol models
src/ha_maxsun.Service     Windows Service and HA WebSocket bridge
src/ha_maxsun.HalHelper  64-bit HAL helper that calls ASUS/ENE/Maxsun HAL
tests/ha_maxsun.Tests    Unit tests and fake-helper integration tests
homeassistant/               Home Assistant package
scripts/                     Build, test, check, and service scripts
```

## Architecture

`ha_maxsun` is the long-running service. It connects to the Home Assistant WebSocket API, subscribes to helper `state_changed` events, reads power/brightness/RGB helper states, calls the HAL helper, updates availability, and reconnects after failures.

`ha_maxsun.HalHelper` is a separate 64-bit child process. It enters Aura control mode, enumerates `MAXSUN MOTHERBOARD LED ENE`, writes the same static color to all 264 LEDs, calls `Apply()`, and communicates with the bridge via stdin/stdout JSON.

This process boundary makes the bridge easier to recover if vendor native COM/HAL code fails.

## HAL Helper JSON Protocol

The helper accepts one JSON request per line and returns one JSON response per line.

Probe:

```json
{"command":"probe"}
```

Apply:

```json
{"command":"apply","on":true,"rgb":[255,64,32],"brightness":128}
```

Successful apply responses include the brightness-scaled hardware color:

```json
{"ok":true,"appliedRgb":[128,32,16]}
```

When the light is off, the bridge writes black to the device.

## Tests

```powershell
.\scripts\test.ps1
```

The test runner covers RGB brightness scaling, off-to-black behavior, HA helper state parsing, HAL helper JSON protocol, a fake-helper integration path, and reconnect behavior after a Home Assistant connection failure.

## Troubleshooting

### The HA Light Is Unavailable

Check logs:

```powershell
Get-Content .\publish\logs\bridge-$(Get-Date -Format yyyyMMdd).log -Tail 120
```

Common causes:

- `MaxsunSync2.exe` or `MaxsunSyncService.exe` is running
- The HA token is wrong or expired
- The HA WebSocket URL is wrong
- The HA package was not loaded
- ASUS/ENE/Maxsun HAL files or COM registrations are missing

### Commands Return OK but LEDs Do Not Change

Confirm the vendor sync process is stopped, then run:

```powershell
.\scripts\test-hardware.ps1 -ConfirmEachStep
```

If `probe` finds the device but `apply` has no visible effect, another RGB program is probably holding Aura/ENE control.

### Service Installation Fails

Service installation requires administrator privileges:

```powershell
.\scripts\install-service.ps1
```

The script requests UAC elevation automatically. Install logs are written to:

```text
publish\logs\install-service.log
```

### Ports or MQTT

There is nothing to configure. This project does not use MQTT and does not expose a local HTTP port. The Windows service connects outbound to the Home Assistant WebSocket API.

## Contributing

Issues and PRs are welcome, especially for:

- Additional Maxsun motherboard or ENE RGB device test results
- New device names, GUIDs, and LED counts
- Home Assistant package improvements
- Service reliability after reboot, sleep, and resume
- Clearer diagnostics and troubleshooting scripts

When reporting hardware compatibility, please include:

- Motherboard model
- Windows version
- HAL device name
- HAL GUID
- LED count
- Output from `scripts\check-environment.ps1`
- Whether `scripts\test-hardware.ps1 -ConfirmEachStep` works

Do not paste Home Assistant long-lived tokens into issues or PRs.

Before submitting changes, run:

```powershell
.\scripts\test.ps1
.\scripts\check-environment.ps1
```

## Security and Privacy

- Do not commit `publish\appsettings.json`
- Do not commit Home Assistant long-lived tokens
- Do not commit `publish\`, `logs\`, or local generated files
- This repository does not include any vendor DLLs, drivers, or HAL files
- Logs are designed not to print tokens, but review and redact logs before posting them publicly

## Disclaimer

This project controls RGB hardware through the installed ASUS/ENE/Maxsun low-level HAL. Behavior may vary between motherboards, HAL versions, and systems running other RGB software. Use it at your own risk. The project is provided "as is" under the MIT License, without warranty of any kind.

## License

This project is licensed under the [MIT License](LICENSE).


