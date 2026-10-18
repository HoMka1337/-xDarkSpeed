@echo off
cd /d "%~dp0"
dotnet restore "%~dp0source\SS14.Admin\SS14.Admin.csproj" --source https://api.nuget.org/v3/index.json
if errorlevel 1 goto end
dotnet publish "%~dp0source\SS14.Admin\SS14.Admin.csproj" -c Release --no-restore -o "%~dp0bin"
:end
pause
