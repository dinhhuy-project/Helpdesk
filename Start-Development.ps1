param()

$ErrorActionPreference = 'Stop'
$repoPath = $PSScriptRoot
$envPath = Join-Path $repoPath '.env'
$projectPath = Join-Path $repoPath 'be/be.csproj'
$previousEncoding = $OutputEncoding
$OutputEncoding = New-Object Text.UTF8Encoding($false)

function Invoke-DockerCaptured([string[]]$Arguments, [string]$InputText) {
    # Windows PowerShell 5.1 can promote redirected native stderr to terminating errors.
    # Capture it with Continue locally, then check the process exit code ourselves.
    $ErrorActionPreference = 'Continue'
    if ($InputText) { $lines = @($InputText | & docker @Arguments 2>&1) }
    else { $lines = @(& docker @Arguments 2>&1) }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}

function Read-CoreConfiguration {
    # Never print resolved Compose configuration: it contains credentials.
    $resolved = Invoke-DockerCaptured -Arguments ($coreArguments + @('--profile', 'backend', 'config', '--format', 'json'))
    if ($resolved.ExitCode -ne 0) { throw 'Invalid Compose configuration. Check the required .env keys; resolved secrets were not printed.' }
    try { return (($resolved.Lines -join "`n") | ConvertFrom-Json) }
    catch { throw 'Cannot parse Compose configuration. Check Docker Compose v2 and .env.' }
}

function Get-PublishedPort($service, [int]$target) {
    $bindings = @($service.ports | Where-Object { $_.target -eq $target })
    if ($bindings.Count -ne 1 -or [string]$bindings[0].published -notmatch '^\d+$') {
        throw "Expected one published host port for service port $target."
    }
    return [int]$bindings[0].published
}

function Quote-ConnectionValue([string]$value) { return "'" + $value.Replace("'", "''") + "'" }

Push-Location $repoPath
try {
    foreach ($tool in @('docker', 'dotnet')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Install $tool before starting development." }
    }
    if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example and fill infrastructure credentials first. See docs/installation-and-running.md.' }
    $engine = Invoke-DockerCaptured -Arguments @('info', '--format', '{{.OSType}}')
    if ($engine.ExitCode -ne 0 -or ($engine.Lines -join '').Trim() -ne 'linux') { throw 'Open Docker Desktop and wait for the Linux container engine to be ready, then rerun this command.' }

    $coreArguments = @('compose', '--env-file', $envPath, '-f', (Join-Path $repoPath 'docker-compose.yml'))
    $configuration = Read-CoreConfiguration
    $api = $configuration.services.api.environment
    if ($api.Minio__Enabled -ne 'true' -or [string]::IsNullOrWhiteSpace($api.Minio__AccessKey) -or [string]::IsNullOrWhiteSpace($api.Minio__SecretKey)) {
        & (Join-Path $repoPath 'be/scripts/Initialize-ComposeStorage.ps1')
        $configuration = Read-CoreConfiguration
        $api = $configuration.services.api.environment
    }
    if ($api.Minio__Enabled -ne 'true' -or [string]::IsNullOrWhiteSpace($api.Minio__AccessKey) -or [string]::IsNullOrWhiteSpace($api.Minio__SecretKey)) {
        throw 'MinIO application configuration is incomplete. Review .env and process environment overrides.'
    }
    # Explicit project/files preserve each stack's existing containers and named volumes.
    $coreArguments += @('-p', [string]$configuration.name)
    $composeApi = @(& docker @coreArguments --profile backend ps --status running -q api)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the Compose API service.' }
    if ($composeApi.Count -gt 0) {
        Write-Output 'Stopping the Compose API so Visual Studio can use port 5000.'
        & docker @coreArguments --profile backend stop api
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stop the Compose API. Stop it before pressing F5.' }
    }
    Write-Output 'Starting PostgreSQL, Redis and private MinIO storage...'
    & docker @coreArguments up -d --build --wait postgres redis minio
    if ($LASTEXITCODE -ne 0) { throw 'Core infrastructure did not become healthy. Check Docker resources and service status.' }

    if ($api.KnowledgeSources__BookStack__Enabled -eq 'true') {
        if ([int]$api.KnowledgeSources__BookStack__BookId -le 0 -or
            [string]::IsNullOrWhiteSpace($api.KnowledgeSources__BookStack__TokenId) -or
            [string]::IsNullOrWhiteSpace($api.KnowledgeSources__BookStack__TokenSecret)) {
            throw 'BookStack connector is enabled but not configured. Complete Initialize-KnowledgeWiki.ps1 -Provision once before using daily startup.'
        }
        Write-Output 'Starting the configured BookStack wiki...'
        & docker compose -p ($configuration.name + '-wiki') --env-file $envPath -f (Join-Path $repoPath 'docker-compose.wiki.yml') up -d --wait bookstack
        if ($LASTEXITCODE -ne 0) { throw 'BookStack did not become healthy. Review wiki credentials/configuration; existing data was preserved.' }
    }
    if ($api.LocalAssistant__Enabled -eq 'true') {
        $model = [string]$api.LocalAssistant__Model
        if ($model.Length -gt 120 -or $model -notmatch '^[a-z0-9][a-z0-9._-]*:[a-z0-9][a-z0-9._-]*$' -or $model -match 'cloud') {
            throw 'LOCAL_ASSISTANT_MODEL must be a local model tag such as qwen3:1.7b.'
        }
        Write-Output 'Starting the configured local Ollama runtime...'
        $aiArguments = @('compose', '-p', 'helpdesk-ai', '--env-file', $envPath, '-f', (Join-Path $repoPath 'docker-compose.ai.yml'))
        & docker @aiArguments up -d --wait ollama
        if ($LASTEXITCODE -ne 0) { throw 'Ollama did not become healthy. Check port 11434 and Docker memory/disk.' }
        $modelState = Invoke-DockerCaptured -Arguments ($aiArguments + @('exec', '-T', 'ollama', 'ollama', 'show', $model))
        if ($modelState.ExitCode -ne 0) {
            Write-Output 'The configured model is missing. Downloading it once into the persistent model volume...'
            & docker @aiArguments exec -T ollama ollama pull $model
            if ($LASTEXITCODE -ne 0) { throw 'Cannot download the configured local model. Fix network/disk and rerun; existing models were preserved.' }
        }
    }

    & (Join-Path $repoPath 'be/scripts/Initialize-DevelopmentSecrets.ps1')
    $pg = $configuration.services.postgres.environment
    $pgPort = Get-PublishedPort $configuration.services.postgres 5432
    $minioPort = Get-PublishedPort $configuration.services.minio 9000
    $connection = "Host=localhost;Port=$pgPort;Database=$(Quote-ConnectionValue $pg.POSTGRES_DB);Username=$(Quote-ConnectionValue $pg.POSTGRES_USER);Password=$(Quote-ConnectionValue $pg.POSTGRES_PASSWORD);Include Error Detail=false"
    $settings = @{
        'ConnectionStrings:Helpdesk' = $connection
        'Database:HostOverride' = ''
        'Minio:Enabled' = 'true'
        'Minio:Endpoint' = "localhost:$minioPort"
        'Minio:HostOverride' = ''
        'Minio:AccessKey' = [string]$api.Minio__AccessKey
        'Minio:SecretKey' = [string]$api.Minio__SecretKey
        'Minio:Bucket' = [string]$api.Minio__Bucket
        'Minio:UseSsl' = 'false'
        'KnowledgeSources:WikipediaEnabled' = [string]$api.KnowledgeSources__WikipediaEnabled
        'KnowledgeSources:BookStack:Enabled' = [string]$api.KnowledgeSources__BookStack__Enabled
        'LocalAssistant:Enabled' = [string]$api.LocalAssistant__Enabled
    }
    if ($api.KnowledgeSources__BookStack__Enabled -eq 'true') {
        foreach ($key in @('SourceId', 'BaseUrl', 'AllowHttp', 'BookId', 'TokenId', 'TokenSecret')) {
            $settings['KnowledgeSources:BookStack:' + $key] = [string]$api.('KnowledgeSources__BookStack__' + $key)
        }
    }
    if ($api.LocalAssistant__Enabled -eq 'true') {
        # Host CLI uses localhost; the Dockerfile profile overrides only this URL's host.
        $settings['LocalAssistant:BaseUrl'] = 'http://localhost:11434/'
        $settings['LocalAssistant:Model'] = [string]$api.LocalAssistant__Model
        $settings['LocalAssistant:TimeoutSeconds'] = [string]$api.LocalAssistant__TimeoutSeconds
    }
    # Only these integration settings are updated. Preserve admin password, JWT and other settings.
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $projectPath *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot synchronize backend user-secrets. Check .NET SDK and file permissions.' }

    # Check history against source without building, migrating or starting backend seed/workers.
    $historyResult = Invoke-DockerCaptured -Arguments ($coreArguments + @('exec', '-T', 'postgres', 'sh', '-c', 'exec psql -X -A -t -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1')) `
        -InputText 'SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;'
    if ($historyResult.ExitCode -ne 0) { throw 'Database migration history is unavailable. Review/apply migrations as described in docs/installation-and-running.md, then rerun.' }
    $history = @($historyResult.Lines)
    $migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'be/Infrastructure/Persistence/Migrations') -Filter '*.cs' -File)
    $sourceMigrations = @($migrationFiles | ForEach-Object { $_.BaseName } | Where-Object { $_ -match '^\d{14}_[A-Za-z0-9_]+$' })
    if ($sourceMigrations.Count -eq 0) { throw 'No source migrations were found; refusing to report readiness.' }
    $pending = @($sourceMigrations | Where-Object { $_ -notin $history })
    $unknown = @($history | Where-Object { $_ -and $_ -notin $sourceMigrations })
    if ($pending.Count -gt 0 -or $unknown.Count -gt 0) { throw 'Database migrations differ from source. Review/apply the correct migrations before F5; this startup command never changes the database schema.' }

    Write-Output ''
    Write-Output 'Development infrastructure and Visual Studio user-secrets are ready.'
    Write-Output 'Frontend (another terminal at repository root): npm --prefix fe run dev'
    Write-Output 'Backend: Visual Studio > be > Container (Dockerfile) > F5'
    Write-Output 'Frontend: http://localhost:5173 | API/Swagger: http://localhost:5000/swagger'
    Write-Output 'No migrations, business seeds or wiki publication were run. F5 retains the existing idempotent Development startup seeds.'
}
finally {
    $OutputEncoding = $previousEncoding
    Pop-Location
}
