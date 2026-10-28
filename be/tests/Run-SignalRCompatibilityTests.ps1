$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js is required. Use the version in fe/package.json.' }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'fe/node_modules/@microsoft/signalr/package.json'))) {
    throw 'Run npm ci in fe/ before running this integration test.'
}
$suffix = [Guid]::NewGuid().ToString('N')
$container = "helpdesk-signalr-test-$suffix"
$testDatabase = "helpdesk_mapping_test_signalr_$suffix"
$oldPassword = $env:POSTGRES_PASSWORD
$oldDatabase = $env:POSTGRES_DB
$oldConnection = $env:HELPDESK_TEST_CONNECTION
$started = $false
try {
    $env:POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N')
    $env:POSTGRES_DB = $testDatabase
    docker run -d --rm --name $container --env POSTGRES_PASSWORD --env POSTGRES_DB -p '127.0.0.1::5432' postgres:16-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start isolated PostgreSQL.' }
    $started = $true
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        docker exec $container pg_isready -U postgres -d $testDatabase *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Seconds 1
    }
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL is not ready.' }
    $port = (docker port $container 5432/tcp).Trim().Split(':')[-1]
    $env:HELPDESK_TEST_CONNECTION = "Host=127.0.0.1;Port=$port;Database=$testDatabase;Username=postgres;Password=$env:POSTGRES_PASSWORD"
    dotnet test (Join-Path $repoRoot 'be/tests/Helpdesk.Persistence.Tests/Helpdesk.Persistence.Tests.csproj') --filter FullyQualifiedName~SignalRJavaScriptCompatibilityTests --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript SignalR compatibility test failed.' }
}
finally {
    if ($started) { docker stop $container | Out-Null }
    $env:POSTGRES_PASSWORD = $oldPassword
    $env:POSTGRES_DB = $oldDatabase
    $env:HELPDESK_TEST_CONNECTION = $oldConnection
}
