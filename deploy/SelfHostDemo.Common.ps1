$ErrorActionPreference = 'Stop'
$demoRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$demoPrivate = Join-Path $demoRepository '.local/self-host-demo'
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
