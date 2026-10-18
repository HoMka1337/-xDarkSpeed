$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
Write-Host 'Grible Station / GS14 Admin'
Write-Host 'Главная страница: https://localhost:27689'
Write-Host 'Адрес возврата после входа: https://localhost:27689/signin-oidc'
$clientId = Read-Host 'Идентификатор приложения OAuth (Client ID)'
$parsedClient = [Guid]::Empty
if (![Guid]::TryParse($clientId, [ref]$parsedClient)) { throw 'Идентификатор приложения должен иметь формат UUID: буквы, цифры и дефисы.' }
$secretValue = Read-Host 'Секрет приложения OAuth (ввод скрыт)' -AsSecureString
$ownerId = Read-Host 'Идентификатор аккаунта владельца (UUID из игры, не имя персонажа)'
$parsedOwner = [Guid]::Empty
if (![Guid]::TryParse($ownerId, [ref]$parsedOwner) -or $parsedOwner -eq [Guid]::Empty) { throw 'Неверный идентификатор аккаунта владельца.' }
$secret = [System.Net.NetworkCredential]::new('', $secretValue).Password
if ([string]::IsNullOrWhiteSpace($secret)) { throw 'Секрет приложения не может быть пустым.' }
$database = Get-Content -LiteralPath 'private/database.json' -Raw | ConvertFrom-Json
$quote = { param($value) ConvertTo-Json -InputObject ([string]$value) -Compress }
$settings = @(
    'ConnectionStrings:'
    ('  DefaultConnection: ' + (& $quote $database.AdminConnectionString))
    'Auth:'
    '  Authority: "https://account.spacestation14.com/"'
    ('  ClientId: ' + (& $quote $clientId))
    ('  ClientSecret: ' + (& $quote $secret))
    'Grible:'
    ('  OwnerUserId: ' + (& $quote $parsedOwner.ToString()))
)
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'appsettings.Secret.yml'), $settings, [Text.UTF8Encoding]::new($false))
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe (Join-Path $PSScriptRoot 'appsettings.Secret.yml') /inheritance:r /grant:r "${identity}:F" '*S-1-5-18:F' /Q | Out-Null
$secret = $null
Write-Host 'Настройки сохранены. Запусти Start-Admin.bat и войди с указанным аккаунтом владельца.'
Read-Host 'Нажми Enter, чтобы закрыть'
