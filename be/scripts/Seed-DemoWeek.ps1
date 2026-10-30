param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projectFile = Join-Path $projectRoot 'be/be.csproj'
$privateRoot = Join-Path $projectRoot '.local/demo-week'
$credentialsFile = Join-Path $privateRoot 'credentials.txt'
$previous = @{}
$keys = @('ConnectionStrings__Helpdesk', 'Database__HostOverride', 'DemoWeekSeed__Enabled', 'DemoWeekSeed__Password', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'Logging__LogLevel__Microsoft.EntityFrameworkCore')
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }

function Protect-PrivateDirectory([string]$Path) {
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    if ($env:OS -eq 'Windows_NT') {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        # Read/write only the DACL. Set-Acl may request SACL privileges on an existing private folder.
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $directory = New-Object IO.DirectoryInfo($Path)
            $acl = [IO.FileSystemAclExtensions]::GetAccessControl($directory, [Security.AccessControl.AccessControlSections]::Access)
        }
        else { $acl = [IO.Directory]::GetAccessControl($Path, [Security.AccessControl.AccessControlSections]::Access) }
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($existingRule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
            $acl.RemoveAccessRuleSpecific($existingRule)
        }
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
        if ($PSVersionTable.PSVersion.Major -ge 6) { [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl) }
        else { [IO.Directory]::SetAccessControl($Path, $acl) }
    }
    else {
        & chmod 700 $Path
        if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict the private seed folder.' }
    }
}
function Quote-ConnectionValue([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }

Push-Location $projectRoot
try {
    # Compose resolves .env correctly. Capture configuration in memory; never print credentials.
    $raw = @(docker compose config --format json)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Docker Compose configuration.' }
    $compose = ($raw -join "`n") | ConvertFrom-Json
    $postgres = $compose.services.postgres
    $binding = @($postgres.ports | Where-Object { $_.target -eq 5432 })
    if ($binding.Count -ne 1) { throw 'Expected one published PostgreSQL port.' }
    if ($binding[0].host_ip -notin @('127.0.0.1', '0.0.0.0', '', $null)) { throw 'The seed script supports only local Compose PostgreSQL.' }
    $settings = $postgres.environment
    $env:ConnectionStrings__Helpdesk = "Host=127.0.0.1;Port=$($binding[0].published);Database=$(Quote-ConnectionValue $settings.POSTGRES_DB);Username=$(Quote-ConnectionValue $settings.POSTGRES_USER);Password=$(Quote-ConnectionValue $settings.POSTGRES_PASSWORD);Include Error Detail=false"
    $env:Database__HostOverride = '127.0.0.1'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:DemoWeekSeed__Enabled = 'true'
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore', 'Warning', 'Process')

    dotnet build $projectFile -c Release --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed; database was not changed.' }
    Protect-PrivateDirectory $privateRoot
    $markerSql = "SELECT EXISTS (SELECT 1 FROM identity.identity_audit_logs WHERE action = 'DemoWeekSeeded' AND data->>'Batch' = 'helpdesk-demo-week-v1');"
    $seeded = @($markerSql | docker compose exec -T postgres sh -c 'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tA -v ON_ERROR_STOP=1')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot check seed marker; verify the database and applied migrations.' }
    if (($seeded -join '').Trim() -eq 't' -and -not (Test-Path -LiteralPath $credentialsFile)) {
        throw 'This database already contains the demo week, but the local credential file is missing. Seed does not reset passwords or generate replacement credentials.'
    }
    if (Test-Path -LiteralPath $credentialsFile) {
        $saved = [IO.File]::ReadAllLines($credentialsFile)
        $passwordLine = @($saved | Where-Object { $_.StartsWith('InitialPassword=') })
        if ($passwordLine.Count -ne 1) { throw 'Existing credential file is invalid; it was not overwritten.' }
        $env:DemoWeekSeed__Password = $passwordLine[0].Substring('InitialPassword='.Length)
    }
    else {
        $bytes = New-Object byte[] 32
        $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
        $env:DemoWeekSeed__Password = [Convert]::ToBase64String($bytes)
        $contents = @(
            'Helpdesk synthetic demo week v1 - local credentials. Do not commit or share publicly.',
            'SeedStatus=Prepared; check the command result and database DemoWeekSeeded marker for completion.',
            "InitialPassword=$env:DemoWeekSeed__Password",
            'All 30 NEW demo accounts share this randomly generated initial password.',
            'Email domain: @demo.helpdesk.example',
            'admin | lead.it | lead.hr | lead.office | accounts | reports',
            'agent.it.1 | agent.it.2 | agent.hr.1 | agent.hr.2 | agent.office.1 | agent.office.2',
            'requester.01 through requester.18',
            'Role matrix and usage: docs/backend-demo-week.md',
            'Existing users, passwords, grants and active SLA policies are preserved.'
        ) -join [Environment]::NewLine
        # CreateNew prevents accidental replacement of credentials from a previous run.
        $stream = [IO.File]::Open($credentialsFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $data = [Text.Encoding]::UTF8.GetBytes($contents + [Environment]::NewLine)
            $stream.Write($data, 0, $data.Length)
        }
        finally { $stream.Dispose() }
        if ($env:OS -ne 'Windows_NT') { & chmod 600 $credentialsFile }
    }

    # Binary backup stays in the private folder. Never reset or migrate the target database.
    $backupName = 'helpdesk-before-demo-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.dump'
    $containerPath = '/tmp/' + $backupName
    docker compose exec -T postgres sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f "$1"' sh $containerPath
    if ($LASTEXITCODE -ne 0) { throw 'Database backup failed; seed was not run.' }
    try {
        docker compose cp "postgres:$containerPath" (Join-Path $privateRoot $backupName) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot copy database backup; seed was not run.' }
    }
    finally { docker compose exec -T postgres rm -- $containerPath | Out-Null }

    dotnet run --project $projectFile -c Release --no-build --no-launch-profile -- seed-demo-week
    if ($LASTEXITCODE -ne 0) { throw 'Demo week seed failed. Existing credentials and backup were preserved for inspection/retry.' }
    $credentialText = [IO.File]::ReadAllText($credentialsFile).Replace(
        'SeedStatus=Prepared; check the command result and database DemoWeekSeeded marker for completion.',
        'SeedStatus=Completed; seed-demo-week returned success. Reruns do not reset credentials.')
    [IO.File]::WriteAllText($credentialsFile, $credentialText, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Seed command completed. Private credentials: $credentialsFile"
    Write-Output "Backup: $(Join-Path $privateRoot $backupName)"
    Write-Output 'Open docs/backend-demo-week.md for account roles and reporting filters. Reruns preserve existing data and do not move the original week.'
}
finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    Pop-Location
}
