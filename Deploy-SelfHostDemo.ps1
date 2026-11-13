param([switch]$Local)

. (Join-Path $PSScriptRoot 'deploy/SelfHostDemo.Common.ps1')
Assert-DemoEngine
Set-DemoPrivatePermissions
$root = $PSScriptRoot
$initializer = Join-Path $root 'Initialize-SelfHostDemo.ps1'
$manager = Join-Path $root 'Start-SelfHostAutoDeploy.ps1'
$agentProcess = Join-Path $root '.local/self-host-auto-deploy/process.json'
$resumeAgent = $false
$url = $null
$completed = $false

function Invoke-DeployStep([string]$Name, [string]$Script, [string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $log = Join-Path $demoPrivate ('deploy-' + $Name + '.log')
    # Native stderr is kept in a private log rather than filling the terminal or
    # triggering Windows PowerShell 5.1 NativeCommandError during successful builds.
    & (Get-Command powershell.exe -ErrorAction Stop).Source -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments *> $log
    if ($LASTEXITCODE -ne 0) { throw ('Deployment step failed: ' + $Name + '. Inspect the private log: ' + $log) }
}

try {
    if (Test-Path -LiteralPath $agentProcess) {
        $record = [IO.File]::ReadAllText($agentProcess) | ConvertFrom-Json
        $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$record.ProcessId) -ErrorAction SilentlyContinue
        $expectedAgent = Join-Path $root '.local/self-host-auto-deploy/agent/Watch-SelfHostDemo.ps1'
        $expectedLauncher = Join-Path $root '.local/self-host-auto-deploy/agent/Launch-SelfHostDemoAgent.ps1'
        $resumeAgent = $null -ne $process -and $process.CommandLine -and
            ($process.CommandLine.IndexOf($expectedAgent,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or
             $process.CommandLine.IndexOf($expectedLauncher,[StringComparison]::OrdinalIgnoreCase) -ge 0)
        if ($resumeAgent) {
            Write-Output '[1/5] Waiting for the auto-deploy agent to pause...'
            & $manager -Action Stop *> (Join-Path $demoPrivate 'deploy-agent-pause.log')
            if (Test-Path -LiteralPath $agentProcess) { throw 'An auto-deploy rollout is still finishing. Wait for it to stop, then rerun this command.' }
        }
    }
    Write-Output '[1/5] Preparing dedicated demo configuration...'
    $prepared = $false
    if (-not (Test-Path -LiteralPath $demoEnvironment)) {
        Invoke-DeployStep 'prepare' $initializer @('-PrepareForStartup')
        $prepared = $true
    }
    $configuration = Get-DemoConfiguration
    Write-Output '[2/5] Checking API/frontend images (the first build can take several minutes)...'
    $missingImage = $false
    foreach ($imageName in @($configuration.services.api.image,$configuration.services.web.image)) {
        $inspect = Invoke-DemoDocker -Arguments @('image','inspect',[string]$imageName,'--format','{{.Id}}')
        if ($inspect.ExitCode -ne 0) { $missingImage=$true }
    }
    if ($missingImage) {
        Invoke-DeployStep 'build' $initializer @('-PrepareForStartup')
        $prepared = $true
    }
    Write-Output '[3/5] Starting PostgreSQL and private storage...'
    $infra = Invoke-DemoDocker -Arguments ($demoCompose + @('up','-d','--wait','postgres','minio'))
    [IO.File]::WriteAllText((Join-Path $demoPrivate 'deploy-infrastructure.log'),($infra.Lines -join "`n"))
    if ($infra.ExitCode -ne 0) { throw 'Demo infrastructure did not become healthy. Inspect deploy-infrastructure.log in the private demo directory.' }
    $databaseState = Get-DemoDatabaseState
    if ($databaseState -eq 'empty') {
        Write-Output '[4/5] Initializing the empty demo database and administrator...'
        $closedTunnel = Invoke-DemoDocker -Arguments ($demoCompose + @('--profile','public','stop','tunnel'))
        if ($closedTunnel.ExitCode -ne 0) { throw 'Cannot stop the old tunnel before first-run database setup.' }
        if (-not $prepared) { Invoke-DeployStep 'prepare-sql' $initializer @('-PrepareForStartup','-SkipBuild') }
        # The user-authorized first-run path is restricted to an empty, dedicated
        # helpdesk_demo DB. The initializer checks emptiness again before applying SQL.
        Invoke-DeployStep 'initialize' $initializer @('-EmptyDatabaseOnly')
    } else {
        Assert-DemoDatabaseInitialized
        Write-Output '[4/5] Keeping existing demo data and credentials...'
    }
    Write-Output '[5/5] Starting the application and checking its URL...'
    $arguments = if ($Local) { @() } else { @('-Public') }
    Invoke-DeployStep 'start' (Join-Path $root 'Start-SelfHostDemo.ps1') $arguments
    if ($Local) { $url = 'http://127.0.0.1:' + [int]$configuration.services.web.ports[0].published }
    else {
        $url = ([IO.File]::ReadAllText((Join-Path $demoPrivate 'public-url.txt'))).Trim()
        if ($url -notmatch '^https://[a-z0-9-]+\.trycloudflare\.com$') { throw 'The deployment did not produce a valid verified tunnel URL.' }
    }
    $completed = $true
} finally {
    if ($resumeAgent) {
        # If the pause timed out while a rollout was active, cancel our stop request
        # rather than leaving a previously running agent scheduled to exit later.
        if (Test-Path -LiteralPath $agentProcess) {
            Remove-Item -LiteralPath (Join-Path $root '.local/self-host-auto-deploy/stop.requested') -ErrorAction SilentlyContinue
        }
        try { & $manager *> (Join-Path $demoPrivate 'deploy-agent-resume.log') }
        catch { Write-Warning 'The demo may be running, but the auto-deploy agent could not resume. Inspect deploy-agent-resume.log privately.' }
    }
}

if ($completed) {
    Write-Output ''
    Write-Output 'DEPLOY READY'
    Write-Output ('Administrator credentials (private): ' + (Join-Path $demoPrivate 'credentials.txt'))
    Write-Output 'Keep Docker Desktop running and the computer awake during the demo.'
    Write-Output 'Stop: powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostDemo.ps1 -Stop'
    Write-Output ''
    # Keep this as the last output, after every setup step and agent-resume message.
    Write-Output ('URL: ' + $url)
}
