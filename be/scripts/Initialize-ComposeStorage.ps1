param()

$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$envPath = Join-Path $repoPath '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example and set the existing infrastructure credentials first.' }

$envText = [IO.File]::ReadAllText($envPath)
$values = @{}
foreach ($line in ($envText -split '\r?\n')) {
    if ($line -match '^\s*(MINIO_[A-Z0-9_]+)\s*=(.*)$') {
        $name = $Matches[1]
        if ($values.ContainsKey($name)) { throw 'Duplicate MinIO keys found in .env; keep one definition per key.' }
        $values[$name] = $Matches[2].Trim().Trim([char]34, [char]39)
    }
}
if ([string]::IsNullOrWhiteSpace($values['MINIO_ROOT_USER']) -or [string]::IsNullOrWhiteSpace($values['MINIO_ROOT_PASSWORD'])) {
    throw 'Set MinIO root credentials in .env before setup.'
}
# Generate once; reruns preserve existing application credentials.
$updates = @{ MINIO_ENABLED = 'true' }
if ([string]::IsNullOrWhiteSpace($values['MINIO_APP_ACCESS_KEY'])) { $updates['MINIO_APP_ACCESS_KEY'] = 'helpdesk-api-' + [Guid]::NewGuid().ToString('N').Substring(0, 12) }
if ([string]::IsNullOrWhiteSpace($values['MINIO_APP_SECRET_KEY'])) {
    $randomBytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $updates['MINIO_APP_SECRET_KEY'] = ([BitConverter]::ToString($randomBytes)).Replace('-', '').ToLowerInvariant()
}
foreach ($name in $updates.Keys) {
    $pattern = '(?m)^\s*' + [regex]::Escape($name) + '\s*=[^\r\n]*'
    if ([regex]::IsMatch($envText, $pattern)) { $envText = [regex]::Replace($envText, $pattern, $name + '=' + $updates[$name]) }
    else { $envText = $envText.TrimEnd([char]13, [char]10) + "`r`n" + $name + '=' + $updates[$name] + "`r`n" }
}
[IO.File]::WriteAllText($envPath, $envText, (New-Object Text.UTF8Encoding($false)))

Push-Location $repoPath
try {
    # Process overrides take precedence over .env in Compose. Isolate these keys so the
    # generated file and the initialized account always describe the same configuration.
    $previousEnvironment = @{}
    foreach ($name in @('MINIO_ENABLED', 'MINIO_APP_ACCESS_KEY', 'MINIO_APP_SECRET_KEY', 'MINIO_ROOT_USER', 'MINIO_ROOT_PASSWORD', 'MINIO_API_PORT', 'MINIO_CONSOLE_PORT')) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    docker compose --env-file .env config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Compose configuration is invalid; no setup was started.' }
    docker compose --env-file .env up -d --build --wait minio
    if ($LASTEXITCODE -ne 0) { throw 'MinIO setup did not become healthy. Check credentials and bucket settings; passwords were not rotated.' }
    Write-Output 'Compose storage configured. Application credentials remain only in the ignored .env file.'
}
finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    Pop-Location
}
