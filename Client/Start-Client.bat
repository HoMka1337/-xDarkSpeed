@echo off
cd /d "%~dp0"
title Grible Station - Client
"%~dp0bin\Content.Client\Content.Client.exe" %*
pause
