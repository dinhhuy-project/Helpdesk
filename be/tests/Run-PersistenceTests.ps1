param()

$ErrorActionPreference = 'Stop'
$testSuffix = [Guid]::NewGuid().ToString('N')
$testContainer = "helpdesk-mapping-test-$testSuffix"
$testDatabase = "helpdesk_mapping_test_$testSuffix"
$previousPassword = $env:POSTGRES_PASSWORD
$previousDatabase = $env:POSTGRES_DB
$previousConnection = $env:HELPDESK_TEST_CONNECTION
$previousApiConnection = $env:ConnectionStrings__Helpdesk
$testContainerStarted = $false

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
    dotnet test (Join-Path $PSScriptRoot '../be.sln') --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Persistence tests failed.' }

    # Exercise the real CLI entry point only against this test container.
    $env:ConnectionStrings__Helpdesk = $env:HELPDESK_TEST_CONNECTION
    dotnet run --project (Join-Path $PSScriptRoot '../be.csproj') --no-build --no-launch-profile -- seed-identity
    if ($LASTEXITCODE -ne 0) { throw 'Identity seed CLI smoke check failed.' }
}
finally {
    if ($testContainerStarted) {
        docker stop $testContainer | Out-Null
    }
    $env:POSTGRES_PASSWORD = $previousPassword
    $env:POSTGRES_DB = $previousDatabase
    $env:HELPDESK_TEST_CONNECTION = $previousConnection
    $env:ConnectionStrings__Helpdesk = $previousApiConnection
}
