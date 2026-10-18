@echo off
chcp 65001 >nul
cd /d "%~dp0"
title Grible Station — Сервер
if not exist "%~dp0bin\Content.Server\Content.Client.zip" (
    echo Не найден пакет для загрузки клиента: bin\Content.Server\Content.Client.zip
    echo Восстанови этот пакет перед запуском сервера.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\Admin\Start-Database.ps1"
if errorlevel 1 (
    pause
    exit /b 1
)
"%~dp0bin\Content.Server\Content.Server.exe" --config-file "%~dp0grible-server.toml" --data-dir "%~dp0data" %*
pause
