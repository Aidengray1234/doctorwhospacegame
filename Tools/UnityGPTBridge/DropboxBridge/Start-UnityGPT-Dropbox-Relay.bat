@echo off
setlocal
set "PROJECT=C:\Users\aiden\doctorwhospacegame"
set "INSTALLED=%PROJECT%\Tools\UnityGPTBridge\DropboxBridge"
set "SETUP=%~dp0"

if not exist "%INSTALLED%\config.json" (
  echo The Dropbox bridge is not installed yet.
  echo Run this first:
  echo   %SETUP%Install-UnityGPT-Dropbox-Bridge.ps1
  echo.
  pause
  exit /b 1
)

copy /Y "%SETUP%UnityGPTDropboxRelay.ps1" "%INSTALLED%\UnityGPTDropboxRelay.ps1" >nul

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALLED%\UnityGPTDropboxRelay.ps1" -ConfigPath "%INSTALLED%\config.json"
echo.
echo The relay stopped. Review the error above before restarting it.
pause
