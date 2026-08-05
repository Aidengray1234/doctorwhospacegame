@echo off
setlocal
cd /d "%~dp0\..\.."
title Unity GPT Bridge Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-UnityGPT-GitHub.ps1"
endlocal
