$ErrorActionPreference = 'Stop'
$panelExe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin/SS14.Admin.exe'))
$panelUrl = 'https://localhost:27689'
$hash = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($panelExe.ToLowerInvariant()))).Replace('-', '') } finally { $hash.Dispose() }
$mutex = [Threading.Mutex]::new($false, ('Local\GribleStation.Admin.' + $key))
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (!$ownsMutex) {
        Write-Host "Панель GS14 уже работает или запускается. Открой $panelUrl"
        exit 0
    }
    $listeners = @(Get-NetTCPConnection -LocalPort 27689 -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count -gt 0) {
        foreach ($processId in @($listeners.OwningProcess | Select-Object -Unique)) {
            $ownerProcess = Get-Process -Id $processId -ErrorAction Stop
            if ($ownerProcess.Path -ne $panelExe) { throw "Порт 27689 занят другой программой (номер процесса $processId). Работа программы сохранена." }
        }
        Write-Host "Панель GS14 уже работает. Открой $panelUrl"
        exit 0
    }
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'appsettings.Secret.yml'))) { throw 'Сначала запусти Configure-OAuth.bat для настройки входа.' }
    if (!(Test-Path -LiteralPath $panelExe)) { throw 'Не найден файл запуска панели GS14.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Start-Database.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось запустить базу PostgreSQL.' }
    Set-Location -LiteralPath $PSScriptRoot
    Write-Host "Открой $panelUrl в браузере."
    & $panelExe
    if ($LASTEXITCODE -ne 0) { throw "Панель GS14 завершилась с кодом $LASTEXITCODE. Причина указана выше." }
    exit 0
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
