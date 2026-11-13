$ErrorActionPreference = 'Stop'
$demoRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$demoPrivate = Join-Path $demoRepository '.local/self-host-demo'
if ($env:HELPDESK_DEMO_STATE_DIRECTORY) {
    if (-not [IO.Path]::IsPathRooted($env:HELPDESK_DEMO_STATE_DIRECTORY)) { throw 'HELPDESK_DEMO_STATE_DIRECTORY must be an absolute private state path.' }
    $demoPrivate = [IO.Path]::GetFullPath($env:HELPDESK_DEMO_STATE_DIRECTORY)
}
$demoEnvironment = Join-Path $demoPrivate '.env'
$demoCompose = @('compose', '-p', 'helpdesk-demo', '--env-file', $demoEnvironment, '-f', (Join-Path $demoRepository 'docker-compose.demo.yml'))

function Invoke-DemoDocker([string[]]$Arguments, [string]$InputText) {
    # Capture native errors so PS 5.1 does not print credentials in provider output.
    $ErrorActionPreference = 'Continue'
    if ($InputText) { $lines = @($InputText | & docker @Arguments 2>&1) }
    else { $lines = @(& docker @Arguments 2>&1) }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}

function Get-DemoConfiguration {
    if (-not (Test-Path -LiteralPath $demoEnvironment)) { throw 'Run Initialize-SelfHostDemo.ps1 first.' }
    $resolved = Invoke-DemoDocker -Arguments ($demoCompose + @('config', '--format', 'json'))
    if ($resolved.ExitCode -ne 0) { throw 'Invalid dedicated demo configuration. Inspect .local/self-host-demo/.env privately.' }
    try { $configuration = ($resolved.Lines -join "`n") | ConvertFrom-Json }
    catch { throw 'Cannot parse demo configuration; resolved secrets were not printed.' }
    # Fixed project, database and private ports prevent accidental use of the Development stack.
    if ($configuration.name -ne 'helpdesk-demo' -or $configuration.services.postgres.environment.POSTGRES_DB -ne 'helpdesk_demo' -or
        $configuration.services.api.environment.ASPNETCORE_ENVIRONMENT -ne 'Production' -or
        $configuration.services.postgres.ports -or $configuration.services.minio.ports -or $configuration.services.api.ports) {
        throw 'Demo isolation checks failed. Review docker-compose.demo.yml.'
    }
    return $configuration
}

function Assert-DemoEngine {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install and start Docker Desktop with Linux containers first.' }
    $engine = Invoke-DemoDocker -Arguments @('info', '--format', '{{.OSType}}')
    if ($engine.ExitCode -ne 0 -or ($engine.Lines -join '').Trim() -ne 'linux') { throw 'Start Docker Desktop and wait for its Linux engine.' }
}

function Get-DemoDatabaseState {
    $sql = @'
SELECT CASE
    WHEN NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')) THEN 'empty'
    WHEN to_regclass('helpdesk.__ef_migrations_history') IS NULL THEN 'history-missing'
    ELSE 'initialized'
END;
'@
    $result = Invoke-DemoDocker -Arguments ($demoCompose + @('exec','-T','postgres','psql','-X','-A','-t','-U','helpdesk_demo','-d','helpdesk_demo','-v','ON_ERROR_STOP=1')) -InputText $sql
    if ($result.ExitCode -ne 0) { throw 'Cannot read the demo database. Check PostgreSQL health, database credentials and permissions; no schema was changed.' }
    $state = ($result.Lines -join '').Trim()
    if ($state -notin @('empty','history-missing','initialized')) { throw 'Unexpected demo database readiness response. Inspect PostgreSQL locally; no schema was changed.' }
    return $state
}

function Assert-DemoDatabaseInitialized {
    switch (Get-DemoDatabaseState) {
        'empty' {
            throw 'Demo database helpdesk_demo is empty (possibly after deleting its volume). Apply the previously reviewed SQL with: powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-SelfHostDemo.ps1 -ApplyReviewedMigrations. Existing secrets are preserved; deleted business data needs a backup to restore.'
        }
        'history-missing' { throw 'Demo database has existing tables but no migration history. Review/restore the correct schema before continuing; do not reset the volume or blindly apply initial migrations.' }
        'initialized' { return }
        default { throw 'Unexpected demo database readiness response. Inspect PostgreSQL locally; no schema was changed.' }
    }
}

function Set-DemoPrivatePermissions {
    [IO.Directory]::CreateDirectory($demoPrivate) | Out-Null
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $directory = New-Object IO.DirectoryInfo($demoPrivate)
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $acl = [IO.FileSystemAclExtensions]::GetAccessControl($directory, [Security.AccessControl.AccessControlSections]::Access)
    } else { $acl = [IO.Directory]::GetAccessControl($demoPrivate, [Security.AccessControl.AccessControlSections]::Access) }
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleSpecific($rule) }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    if ($PSVersionTable.PSVersion.Major -ge 6) { [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl) }
    else { [IO.Directory]::SetAccessControl($demoPrivate, $acl) }
}
