@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Configure-OAuth.ps1"
if errorlevel 1 pause
