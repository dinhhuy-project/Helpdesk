param([switch]$Provision, [switch]$ConfigureBackend, [int]$BookId = 0, [string]$TokenId = '', [Security.SecureString]$TokenSecret)

$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$envPath = Join-Path $repoPath '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example first.' }

function New-RandomHex {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}

$envText = [IO.File]::ReadAllText($envPath)
$baseProject = 'helpdesk'
if ($envText -match '(?m)^COMPOSE_PROJECT_NAME=([a-z0-9][a-z0-9_-]*)\s*$') { $baseProject = $Matches[1] }
$wikiProject = $baseProject + '-wiki'
$values = @{}
foreach ($line in ($envText -split '\r?\n')) {
    if ($line -match '^\s*(BOOKSTACK_[A-Z0-9_]+)\s*=(.*)$') {
        $name = $Matches[1]
        if ($values.ContainsKey($name)) { throw 'Duplicate BookStack keys in .env.' }
        $values[$name] = $Matches[2].Trim().Trim([char]34, [char]39)
    }
}
$defaults = @{
    BOOKSTACK_PORT = '6875'
    BOOKSTACK_APP_URL = 'http://localhost:6875'
    BOOKSTACK_APP_KEY = ''
    BOOKSTACK_DB_PASSWORD = New-RandomHex
    BOOKSTACK_DB_ROOT_PASSWORD = New-RandomHex
}
# App keys require 32 bytes; generate the bytes directly rather than reusing a database password.
$keyBytes = New-Object byte[] 32
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
$defaults['BOOKSTACK_APP_KEY'] = 'base64:' + [Convert]::ToBase64String($keyBytes)
foreach ($name in $defaults.Keys) {
    if (-not [string]::IsNullOrWhiteSpace($values[$name])) { continue }
    $pattern = '(?m)^\s*' + [regex]::Escape($name) + '\s*=[^\r\n]*'
    $newLine = $name + '=' + $defaults[$name]
    if ([regex]::IsMatch($envText, $pattern)) { $envText = [regex]::Replace($envText, $pattern, $newLine) }
    else { $envText = $envText.TrimEnd([char]13, [char]10) + "`r`n" + $newLine + "`r`n" }
    $values[$name] = $defaults[$name]
}
[IO.File]::WriteAllText($envPath, $envText, (New-Object Text.UTF8Encoding($false)))

Push-Location $repoPath
$previousEnvironment = @{}
try {
    foreach ($name in $defaults.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    # -p takes precedence over COMPOSE_PROJECT_NAME in .env, keeping this stack isolated.
    docker compose -p $wikiProject --env-file .env -f docker-compose.wiki.yml config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Invalid wiki Compose configuration.' }
    docker compose -p $wikiProject --env-file .env -f docker-compose.wiki.yml up -d --wait bookstack
    if ($LASTEXITCODE -ne 0) { throw 'Wiki did not become healthy; existing credentials were preserved.' }

    if ($Provision) {
        & (Join-Path $PSScriptRoot 'Provision-LocalKnowledgeWiki.ps1')
    }

    if ($ConfigureBackend) {
        if ($BookId -le 0 -or $TokenId -notmatch '^[A-Za-z0-9]{8,200}$') { throw 'Provide a positive BookId and the read-only account TokenId.' }
        if (-not $TokenSecret) { $TokenSecret = Read-Host 'BookStack read-only Token Secret (hidden)' -AsSecureString }
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($TokenSecret)
        try { $plainSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        if ($plainSecret -notmatch '^[A-Za-z0-9]{8,200}$') { throw 'Invalid BookStack token format.' }
        $settings = @{
            'KnowledgeSources:BookStack:Enabled' = 'true'
            'KnowledgeSources:BookStack:BaseUrl' = 'http://host.docker.internal:' + $values['BOOKSTACK_PORT'] + '/'
            'KnowledgeSources:BookStack:AllowHttp' = 'true'
            'KnowledgeSources:BookStack:SourceId' = 'support-wiki'
            'KnowledgeSources:BookStack:BookId' = [string]$BookId
            'KnowledgeSources:BookStack:TokenId' = $TokenId
            'KnowledgeSources:BookStack:TokenSecret' = $plainSecret
        }
        # JSON is piped on stdin; the secret is not placed in command arguments or output.
        $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project be | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot configure backend user-secrets.' }
        $plainSecret = $null
        Write-Output 'BookStack connector configured for Visual Studio Container (Dockerfile). Restart that profile.'
    }
    Write-Output ('Wiki is ready at ' + $values['BOOKSTACK_APP_URL'] + '. See docs/knowledge-integrations.md for editor access and connector settings.')
}
finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    Pop-Location
}
