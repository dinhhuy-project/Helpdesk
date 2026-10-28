param()

$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projectName = 'helpdesk-bootstrap-test-' + [Guid]::NewGuid().ToString('N')
$composeArgs = @('--project-name', $projectName, '--env-file', (Join-Path $repoPath '.env.example'), '-f', (Join-Path $repoPath 'docker-compose.yml'))
# This test does not read .env, development secrets or existing application volumes.
$testEnvironment = @{
    COMPOSE_PROJECT_NAME = $projectName
    POSTGRES_DB = 'helpdesk_bootstrap_test'
    POSTGRES_USER = 'postgres'
    POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N')
    POSTGRES_PORT = '0'
    REDIS_PASSWORD = [Guid]::NewGuid().ToString('N')
    REDIS_PORT = '0'
    MINIO_ROOT_USER = 'bootstrap-test'
    MINIO_ROOT_PASSWORD = [Guid]::NewGuid().ToString('N')
    MINIO_API_PORT = '0'
    MINIO_CONSOLE_PORT = '0'
    MINIO_ENABLED = 'true'
    MINIO_APP_ACCESS_KEY = 'bootstrap-app-test'
    MINIO_APP_SECRET_KEY = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    JWT_SIGNING_KEY = 'bootstrap-test-only-not-a-runtime-signing-key'
}
$previousEnvironment = @{}
$started = $false

function Remove-TestStack {
    if ($projectName -notmatch '^helpdesk-bootstrap-test-[0-9a-f]{32}$') { throw 'Refusing cleanup outside the generated test project.' }
    $volumes = @(docker volume ls --filter "label=com.docker.compose.project=$projectName" --format '{{.Name}}')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect isolated test volumes.' }
    foreach ($volumeName in $volumes) {
        if ($volumeName -notin @(($projectName + '_postgres-data'), ($projectName + '_minio-data'))) { throw 'Unexpected test volume; refusing cleanup.' }
        $volumeDetails = (docker volume inspect $volumeName) | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $volumeDetails[0].Labels.'com.docker.compose.project' -cne $projectName) {
            throw 'Refusing to remove a volume owned by another project.'
        }
    }
    docker compose @composeArgs down --volumes --remove-orphans
    if ($LASTEXITCODE -ne 0) { throw 'Cannot clean up the isolated bootstrap test project.' }
}

function Assert-StorageReady {
    $services = @(docker compose @composeArgs ps --services)
    if ($LASTEXITCODE -ne 0 -or $services.Count -ne 3 -or $services -contains 'minio-init') {
        throw 'Expected only postgres, redis and minio; bootstrap must not require an extra service.'
    }
    docker compose @composeArgs exec -T minio /bin/sh /opt/helpdesk/verify.sh
    if ($LASTEXITCODE -ne 0) { throw 'Application storage verification failed after Compose reported ready.' }
}

try {
    foreach ($name in $testEnvironment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $testEnvironment[$name])
    }
    $started = $true
    docker compose @composeArgs up -d --build --wait postgres redis minio
    if ($LASTEXITCODE -ne 0) { throw 'Fresh Compose bootstrap failed.' }
    Assert-StorageReady

    # The writable layer and volume survive this restart. Stale readiness cannot skip setup.
    docker compose @composeArgs restart minio
    if ($LASTEXITCODE -ne 0) { throw 'MinIO restart failed.' }
    docker compose @composeArgs up -d --wait postgres redis minio
    if ($LASTEXITCODE -ne 0) { throw 'Restarted Compose bootstrap failed.' }
    Assert-StorageReady

    # Remove only the volumes created by this GUID-named test, then recreate with the same credentials.
    Remove-TestStack
    docker compose @composeArgs up -d --build --wait postgres redis minio
    if ($LASTEXITCODE -ne 0) { throw 'Compose bootstrap after a test volume reset failed.' }
    Assert-StorageReady
    Write-Output 'Compose bootstrap passed: fresh volumes, restart, volume reset, app I/O and denied out-of-scope/admin access.'
}
finally {
    try { if ($started) { Remove-TestStack } }
    finally {
        foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    }
}
