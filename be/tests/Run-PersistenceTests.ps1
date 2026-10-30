param([string]$Filter = '')

$ErrorActionPreference = 'Stop'
$frontendRoot = Join-Path $PSScriptRoot '../../fe'
if ((-not $Filter -or $Filter -match 'Frontend(?:Discussion|Account|Catalog|Workflow|Attachment|Sla)Journey|SignalRJavaScript') -and (-not (Get-Command node -ErrorAction SilentlyContinue) -or
    -not (Test-Path -LiteralPath (Join-Path $frontendRoot 'node_modules/@microsoft/signalr/package.json')))) {
    throw 'Node.js and frontend dependencies are required for frontend API/SignalR integration tests. Run npm ci in fe/ first.'
}
$testSuffix = [Guid]::NewGuid().ToString('N')
$testContainer = "helpdesk-mapping-test-$testSuffix"
$testDatabase = "helpdesk_mapping_test_$testSuffix"
$previousPassword = $env:POSTGRES_PASSWORD
$previousDatabase = $env:POSTGRES_DB
$previousConnection = $env:HELPDESK_TEST_CONNECTION
$previousApiConnection = $env:ConnectionStrings__Helpdesk
$testContainerStarted = $false
$storageContainer = "helpdesk-storage-test-$testSuffix"
$storageStarted = $false
$previousStorageEndpoint = $env:HELPDESK_TEST_MINIO_ENDPOINT
$previousStorageAccess = $env:HELPDESK_TEST_MINIO_ACCESS_KEY
$previousStorageSecret = $env:HELPDESK_TEST_MINIO_SECRET_KEY
$previousStorageRoot = $env:MINIO_ROOT_USER
$previousStoragePassword = $env:MINIO_ROOT_PASSWORD

try {
    $env:POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $env:POSTGRES_DB = $testDatabase
    # No host volume: all data belongs only to this disposable test container.
    docker run -d --rm --name $testContainer --env POSTGRES_PASSWORD --env POSTGRES_DB `
        -p '127.0.0.1::5432' postgres:16-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start the isolated PostgreSQL test container.' }
    $testContainerStarted = $true

    $testPortBinding = docker port $testContainer 5432/tcp
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine PostgreSQL test port.' }
    $testPort = ($testPortBinding.Trim() -split ':')[-1]
    $testReady = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        docker exec $testContainer pg_isready -U postgres -d $testDatabase *> $null
        if ($LASTEXITCODE -eq 0) { $testReady = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $testReady) { throw 'PostgreSQL test container did not become ready.' }

    $env:HELPDESK_TEST_CONNECTION = "Host=127.0.0.1;Port=$testPort;Database=$testDatabase;Username=postgres;Password=$env:POSTGRES_PASSWORD"
    $env:MINIO_ROOT_USER = 'helpdesk-test'
    $env:MINIO_ROOT_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $storageImage = 'helpdesk-minio:latest'
    docker image inspect $storageImage *> $null
    if ($LASTEXITCODE -ne 0) {
        docker build -t $storageImage (Join-Path $PSScriptRoot '../../deploy/minio')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot build the isolated MinIO test image.' }
    }
    docker run -d --rm --name $storageContainer --env MINIO_ROOT_USER --env MINIO_ROOT_PASSWORD `
        -p '127.0.0.1::9000' $storageImage server /data | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start isolated MinIO.' }
    $storageStarted = $true
    $storagePort = (docker port $storageContainer 9000/tcp).Trim().Split(':')[-1]
    $env:HELPDESK_TEST_MINIO_ENDPOINT = "127.0.0.1:$storagePort"
    $env:HELPDESK_TEST_MINIO_ACCESS_KEY = $env:MINIO_ROOT_USER
    $env:HELPDESK_TEST_MINIO_SECRET_KEY = $env:MINIO_ROOT_PASSWORD
    $storageReady = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        docker exec $storageContainer curl --fail --silent http://localhost:9000/minio/health/live *> $null
        if ($LASTEXITCODE -eq 0) { $storageReady = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $storageReady) { throw 'MinIO test container did not become ready.' }
    if ($Filter) { dotnet test (Join-Path $PSScriptRoot '../be.sln') --no-restore --filter $Filter }
    else { dotnet test (Join-Path $PSScriptRoot '../be.sln') --no-restore }
    if ($LASTEXITCODE -ne 0) { throw 'Persistence tests failed.' }

    # Exercise the real CLI entry point only against this test container.
    $env:ConnectionStrings__Helpdesk = $env:HELPDESK_TEST_CONNECTION
    dotnet run --project (Join-Path $PSScriptRoot '../be.csproj') --no-build --no-launch-profile -- seed-identity
    if ($LASTEXITCODE -ne 0) { throw 'Identity seed CLI smoke check failed.' }
    dotnet run --project (Join-Path $PSScriptRoot '../be.csproj') --no-build --no-launch-profile -- grant-team-leadership
    if ($LASTEXITCODE -ne 0) { throw 'Team leadership grant CLI smoke check failed.' }
    dotnet run --project (Join-Path $PSScriptRoot '../be.csproj') --no-build --no-launch-profile -- grant-category-administration
    if ($LASTEXITCODE -ne 0) { throw 'Category administration grant CLI smoke check failed.' }
    dotnet run --project (Join-Path $PSScriptRoot '../be.csproj') --no-build --no-launch-profile -- grant-ticket-workflow
    if ($LASTEXITCODE -ne 0) { throw 'Ticket workflow grant CLI smoke check failed.' }
}
finally {
    if ($storageStarted) { docker stop $storageContainer | Out-Null }
    if ($testContainerStarted) {
        docker stop $testContainer | Out-Null
    }
    $env:POSTGRES_PASSWORD = $previousPassword
    $env:POSTGRES_DB = $previousDatabase
    $env:HELPDESK_TEST_CONNECTION = $previousConnection
    $env:ConnectionStrings__Helpdesk = $previousApiConnection
    $env:HELPDESK_TEST_MINIO_ENDPOINT = $previousStorageEndpoint
    $env:HELPDESK_TEST_MINIO_ACCESS_KEY = $previousStorageAccess
    $env:HELPDESK_TEST_MINIO_SECRET_KEY = $previousStorageSecret
    $env:MINIO_ROOT_USER = $previousStorageRoot
    $env:MINIO_ROOT_PASSWORD = $previousStoragePassword
}
