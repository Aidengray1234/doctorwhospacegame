@echo off
setlocal
cd /d "%~dp0\..\.."
title Unity GPT Relay
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UnityGPTRelay.ps1"
endlocal
