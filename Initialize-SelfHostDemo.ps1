param([switch]$ApplyReviewedMigrations)

. (Join-Path $PSScriptRoot 'deploy/SelfHostDemo.Common.ps1')
Assert-DemoEngine
Set-DemoPrivatePermissions
$sqlPath = Join-Path $demoPrivate 'migrations.sql'
$reviewPath = Join-Path $demoPrivate 'migrations-review.json'
$utf8 = New-Object Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $demoEnvironment)) {
    function New-DemoSecret {
        $bytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    $values = @{
        DEMO_DB_PASSWORD = New-DemoSecret
        DEMO_JWT_KEY = New-DemoSecret
        DEMO_MINIO_ROOT_PASSWORD = New-DemoSecret
        DEMO_MINIO_APP_SECRET = New-DemoSecret
        DEMO_ADMIN_EMAIL = 'admin@helpdesk-demo.local'
        DEMO_ADMIN_PASSWORD = (New-DemoSecret) + 'Aa!'
        DEMO_LOCAL_PORT = '8090'
        DEMO_AI_ENABLED = 'false'
        DEMO_AI_MODEL = 'qwen3:1.7b'
    }
    $lines = @($values.Keys | Sort-Object | ForEach-Object { $_ + '=' + $values[$_] })
    [IO.File]::WriteAllText($demoEnvironment, ($lines -join "`n") + "`n", $utf8)
}
$configuration = Get-DemoConfiguration

if (-not $ApplyReviewedMigrations) {
    Write-Output 'Building a dedicated demo and preparing SQL for review. Development data/secrets are not copied.'
    & docker @demoCompose build api web minio
    if ($LASTEXITCODE -ne 0) { throw 'Demo image build failed.' }
    dotnet ef migrations script 0 --idempotent --configuration Release --project (Join-Path $demoRepository 'be') --startup-project (Join-Path $demoRepository 'be') --output $sqlPath *> (Join-Path $demoPrivate 'migration-generation.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare migration SQL. Check .NET 8 SDK/EF 8 tools and the private migration-generation.log.' }
    $sourceIds = @(Get-ChildItem -LiteralPath (Join-Path $demoRepository 'be/Infrastructure/Persistence/Migrations') -Filter '*.cs' -File |
        Where-Object { $_.BaseName -match '^\d{14}_[A-Za-z0-9_]+$' } | ForEach-Object { $_.BaseName } | Sort-Object)
    [IO.File]::WriteAllText($reviewPath, (@{ sha256 = (Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash; migrations = $sourceIds } | ConvertTo-Json), $utf8)
    Write-Output 'Prepared. Read .local/self-host-demo/migrations.sql and verify the target helpdesk-demo/helpdesk_demo.'
    Write-Output 'After reviewing SQL: powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-SelfHostDemo.ps1 -ApplyReviewedMigrations'
    return
}

if (-not (Test-Path -LiteralPath $sqlPath) -or -not (Test-Path -LiteralPath $reviewPath)) { throw 'Prepare and review SQL first by running this script without switches.' }
$review = [IO.File]::ReadAllText($reviewPath) | ConvertFrom-Json
if ($review.sha256 -ne (Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash) { throw 'SQL changed after preparation. Prepare again and review before applying.' }
$sourceIds = @(Get-ChildItem -LiteralPath (Join-Path $demoRepository 'be/Infrastructure/Persistence/Migrations') -Filter '*.cs' -File |
    Where-Object { $_.BaseName -match '^\d{14}_[A-Za-z0-9_]+$' } | ForEach-Object { $_.BaseName } | Sort-Object)
if (($sourceIds -join ',') -ne ($review.migrations -join ',')) { throw 'Source migrations changed. Prepare and review SQL again.' }
& docker @demoCompose up -d --build --wait postgres minio
if ($LASTEXITCODE -ne 0) { throw 'Demo PostgreSQL or MinIO did not become healthy.' }
$public = Invoke-DemoDocker -Arguments ($demoCompose + @('--profile', 'public', 'ps', '--status', 'running', '-q', 'tunnel'))
if ($public.ExitCode -ne 0 -or $public.Lines.Count -gt 0) { throw 'Stop the demo tunnel before database maintenance.' }
$query = "SELECT COALESCE((SELECT string_agg(table_schema || '.' || table_name, ',') FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')), '');"
$probe = Invoke-DemoDocker -Arguments ($demoCompose + @('exec', '-T', 'postgres', 'psql', '-X', '-A', '-t', '-U', 'helpdesk_demo', '-d', 'helpdesk_demo', '-v', 'ON_ERROR_STOP=1', '-c', $query))
if ($probe.ExitCode -ne 0) { throw 'Cannot inspect dedicated demo database.' }
$tables = ($probe.Lines -join '').Trim()
if ($tables -and $tables -notmatch 'helpdesk\.__ef_migrations_history') { throw 'Existing unrelated schema found. Refusing to apply demo setup.' }
& docker @demoCompose stop api web
if ($LASTEXITCODE -ne 0) { throw 'Cannot stop demo API/web for migration.' }
& docker @demoCompose cp $sqlPath postgres:/tmp/helpdesk-demo-migrations.sql
if ($LASTEXITCODE -ne 0) { throw 'Cannot copy reviewed SQL to the dedicated demo database.' }
$apply = Invoke-DemoDocker -Arguments ($demoCompose + @('exec', '-T', 'postgres', 'psql', '-X', '-U', 'helpdesk_demo', '-d', 'helpdesk_demo', '-v', 'ON_ERROR_STOP=1', '-f', '/tmp/helpdesk-demo-migrations.sql'))
if ($apply.ExitCode -ne 0) { throw 'Migration stopped. Inspect reviewed SQL and database state; no public tunnel was started.' }

$envText = [IO.File]::ReadAllText($demoEnvironment)
$emailMatch = [regex]::Match($envText, '(?m)^DEMO_ADMIN_EMAIL=([^\r\n]+)')
$passwordMatch = [regex]::Match($envText, '(?m)^DEMO_ADMIN_PASSWORD=([^\r\n]+)')
if (-not $emailMatch.Success -or -not $passwordMatch.Success) { throw 'Missing dedicated demo admin settings.' }
$oldEmail = $env:BootstrapAdmin__Email; $oldPassword = $env:BootstrapAdmin__Password; $oldName = $env:BootstrapAdmin__DisplayName
try {
    $env:BootstrapAdmin__Email = $emailMatch.Groups[1].Value
    $env:BootstrapAdmin__Password = $passwordMatch.Groups[1].Value
    $env:BootstrapAdmin__DisplayName = 'Self-host Demo Administrator'
    foreach ($command in @('seed-identity', 'bootstrap-admin')) {
        $result = Invoke-DemoDocker -Arguments ($demoCompose + @('run', '--rm', '--no-deps', '-e', 'BootstrapAdmin__Email', '-e', 'BootstrapAdmin__Password', '-e', 'BootstrapAdmin__DisplayName', 'api', $command))
        if ($result.ExitCode -ne 0) { throw 'Demo Identity/bootstrap CLI failed; credentials were not printed.' }
    }
} finally { $env:BootstrapAdmin__Email = $oldEmail; $env:BootstrapAdmin__Password = $oldPassword; $env:BootstrapAdmin__DisplayName = $oldName }
[IO.File]::WriteAllText((Join-Path $demoPrivate 'credentials.txt'),
    ("Dedicated self-host DEMO administrator (keep private)`nEmail: " + $emailMatch.Groups[1].Value + "`nPassword: " + $passwordMatch.Groups[1].Value + "`n"), $utf8)
Write-Output 'Dedicated demo database and administrator are ready. Credentials: .local/self-host-demo/credentials.txt (private).'
Write-Output 'Run Start-SelfHostDemo.ps1 for local verification, then add -Public to create an Internet URL.'
