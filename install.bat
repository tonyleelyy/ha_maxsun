@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-wizard.ps1" -Language zh-CN
set "code=%ERRORLEVEL%"

echo.
if not "%code%"=="0" (
  echo ha_maxsun 安装失败，退出码：%code%
) else (
  echo ha_maxsun 安装完成。
)
pause
exit /b %code%
