@echo off
cd /d "%~dp0"
title Grible Station - GS14 Admin
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Admin.ps1"
pause
