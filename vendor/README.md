# Vendor runtime placeholder

This directory documents the expected layout for optional local vendor runtime files.

The public `ha_maxsun` GitHub repository does not include proprietary Maxsun, ASUS, or ENE DLLs. These files are not authored by this project and should not be committed or redistributed unless you have the right to do so.

For normal use, generate a local runtime package from a machine that already has the vendor components installed:

```powershell
.\scripts\build.ps1
.\scripts\collect-vendor-runtime.ps1
```

The generated files are written to `publish\vendor\`, not this source directory. Copy the whole `publish\` directory to the target machine, then run from an elevated PowerShell:

```powershell
.\register-vendor-runtime.ps1
.\install-service.ps1
```

Expected runtime layout:

```text
vendor/
  ASUS/
    AuraSDK/
      AuraSdk_x64.dll
      AuraSdk_x86.dll
  MaxSun/
    LightControlModule/
      Aac_MaxSunEneLight/
        AacHal_x64.dll
        AacHal_x86.dll
        MXHM.dll
        MXHM32.dll
        SK_64.dll
        SK.dll
  ENE/
    Aac_ENE RGB HAL/
      x64/
        AacHal_x64.dll
        MsIo64_ENE.dll
```

Keep vendor binaries out of public source control unless their license explicitly allows redistribution.
