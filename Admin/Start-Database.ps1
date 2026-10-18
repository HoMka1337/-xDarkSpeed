$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$pgBin = Join-Path $PSScriptRoot 'PostgreSQL/pgsql/bin'
$dataDir = Join-Path $PSScriptRoot 'private/pgdata'
if (!(Test-Path -LiteralPath (Join-Path $dataDir 'PG_VERSION'))) { throw 'База PostgreSQL ещё не подготовлена.' }
$hash = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($dataDir.ToLowerInvariant()))).Replace('-', '') } finally { $hash.Dispose() }
$mutex = [Threading.Mutex]::new($false, ('Local\GribleStation.Database.' + $key))
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (!$ownsMutex) { throw 'База данных ещё запускается. Повтори попытку немного позже.' }
    # Windows PowerShell 5.1 преобразует stderr native-программ в ErrorRecord.
    # Код возврата pg_ctl проверяем отдельно; отрицательный status не является ошибкой скрипта.
    $ErrorActionPreference = 'Continue'
    $statusOutput = & "$pgBin/pg_ctl.exe" status -D $dataDir 2>&1
    $statusCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($statusCode -eq 0) {
        Write-Host 'База PostgreSQL уже работает.'
        exit 0
    }
    # Не запускаем второй экземпляр, если кластер ещё поднимается вне этого скрипта.
    & "$pgBin/pg_isready.exe" -h 127.0.0.1 -p 55432 -q
    $readyCode = $LASTEXITCODE
    if ($readyCode -eq 0 -or $readyCode -eq 1) {
        throw 'Порт 55432 занят PostgreSQL, но работа нужной базы не подтверждена. Второй экземпляр не запущен.'
    }
    & "$pgBin/pg_ctl.exe" start -D $dataDir -l (Join-Path $PSScriptRoot 'private/postgresql.log') -w
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось запустить PostgreSQL. Проверь журнал private/postgresql.log.' }
    exit 0
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
