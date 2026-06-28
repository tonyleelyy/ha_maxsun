@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-wizard.ps1" -Language en-US
set "code=%ERRORLEVEL%"

echo.
if not "%code%"=="0" (
  echo ha_maxsun setup failed. Exit code: %code%
) else (
  echo ha_maxsun setup finished.
)
pause
exit /b %code%
