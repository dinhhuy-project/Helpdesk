param([Parameter(Mandatory)][string]$ConfigPath, [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit)

. (Join-Path $PSScriptRoot 'SelfHostAutoDeploy.Common.ps1')
$config = [IO.File]::ReadAllText($ConfigPath) | ConvertFrom-Json
$statePath = Join-Path $config.AgentDirectory 'status.json'
$state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
$oldDirectory = $env:HELPDESK_DEMO_STATE_DIRECTORY
$oldTag = $env:DEMO_IMAGE_TAG
$changedContainers = $false
$oldEnvironment = $null
$previousTag = $state.ImageTag
$previousCommit = $state.LastDeployedCommit
try {
    $selected = Invoke-AutoDeployGit $config.SourceDirectory @('rev-parse','HEAD')
    $dirty = Invoke-AutoDeployGit $config.SourceDirectory @('status','--porcelain')
    if ($selected.ExitCode -ne 0 -or ($selected.Lines -join '').Trim() -cne $Commit -or $dirty.ExitCode -ne 0 -or $dirty.Lines.Count -gt 0) {
        throw 'Release checkout must be clean and match the approved commit.'
    }
    $env:HELPDESK_DEMO_STATE_DIRECTORY = $config.StateDirectory
    $env:DEMO_IMAGE_TAG = 'demo-' + $Commit
    . (Join-Path $config.SourceDirectory 'deploy/SelfHostDemo.Common.ps1')
    Assert-DemoEngine
    $configuration = Get-DemoConfiguration
    # Infra changes need review; automation only replaces API/frontend images.
    $composeHash = Get-AutoDeployComposeHash (Join-Path $config.SourceDirectory 'docker-compose.demo.yml')
    if ($composeHash -ne $config.TrustedComposeHash) { throw 'Compose changed. Review infrastructure changes and re-enable the agent before deploying this commit.' }
    $history = Invoke-DemoDocker -Arguments ($demoCompose + @('exec','-T','postgres','psql','-X','-A','-t','-U','helpdesk_demo','-d','helpdesk_demo','-c','SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;'))
    if ($history.ExitCode -ne 0) { throw 'Cannot read demo migration history.' }
    $sourceIds = @(Get-ChildItem -LiteralPath (Join-Path $config.SourceDirectory 'be/Infrastructure/Persistence/Migrations') -Filter '*.cs' -File |
        Where-Object { $_.BaseName -match '^\d{14}_[A-Za-z0-9_]+$' } | ForEach-Object { $_.BaseName })
    if ($sourceIds.Count -eq 0 -or (Get-AutoDeployPendingMigrations $sourceIds @($history.Lines)).Count -gt 0) {
        throw 'New database migration requires SQL review/application. The running release was preserved; automatic schema changes are disabled.'
    }
    $oldEnvironment = [IO.File]::ReadAllText($demoEnvironment)
    Write-AutoDeployLog $config.AgentDirectory ('Building approved commit ' + $Commit + ' before replacing containers.')
    $build = Invoke-DemoDocker -Arguments ($demoCompose + @('build','api','web'))
    [IO.File]::WriteAllText((Join-Path $config.AgentDirectory 'last-build.log'), ($build.Lines -join "`n"))
    if ($build.ExitCode -ne 0) { throw 'Candidate image build failed. See the local last-build.log; live containers were preserved.' }
    $preflight = Invoke-DemoDocker -Arguments ($demoCompose + @('run','--rm','--no-deps','api','knowledge-index','check','--manifest','/tmp/auto-deploy-manifest.json'))
    if ($preflight.ExitCode -ne 0) { throw 'Candidate database/index preflight failed. Live containers were preserved.' }
    if ([IO.File]::ReadAllText($demoEnvironment) -cne $oldEnvironment) { throw 'Demo configuration changed during build; retry after configuration review.' }
    # A queued release must not overtake a newer push that arrived during a long build.
    $fetch = Invoke-AutoDeployGit $config.SourceDirectory @('fetch','--no-tags','origin', ('+refs/heads/' + $config.Branch + ':refs/remotes/origin/' + $config.Branch))
    $tip = Invoke-AutoDeployGit $config.SourceDirectory @('rev-parse', ('refs/remotes/origin/' + $config.Branch))
    if ($fetch.ExitCode -ne 0 -or $tip.ExitCode -ne 0) { throw 'Cannot recheck the trusted branch after building.' }
    if (($tip.Lines -join '').Trim() -cne $Commit) {
        Write-AutoDeployLog $config.AgentDirectory 'Candidate superseded by a newer push; no containers replaced.'
        return
    }
    $run = Get-AutoDeployLatestRun $config $Commit
    if (-not (Test-AutoDeployCi $run $config.Repository $config.Branch $Commit)) {
        Write-AutoDeployLog $config.AgentDirectory 'CI was rerun or changed during build; candidate skipped before rollout.'
        return
    }
    $changedContainers = $true
    $rollout = Invoke-DemoDocker -Arguments ($demoCompose + @('up','-d','--no-build','--no-deps','--force-recreate','--wait','api','web'))
    if ($rollout.ExitCode -ne 0 -or -not (Test-AutoDeployHttp ([int]$configuration.services.web.ports[0].published))) {
        throw 'Candidate health/API probe failed.'
    }
    $pattern = '(?m)^DEMO_IMAGE_TAG=[^\r\n]*'
    $line = 'DEMO_IMAGE_TAG=demo-' + $Commit
    if ([regex]::Matches($oldEnvironment,$pattern).Count -gt 1) { throw 'Duplicate DEMO_IMAGE_TAG in private configuration.' }
    $nextEnvironment = if ([regex]::IsMatch($oldEnvironment,$pattern)) { [regex]::Replace($oldEnvironment,$pattern,$line) }
        else { $oldEnvironment.TrimEnd([char]13,[char]10) + "`n" + $line + "`n" }
    $temporary = $demoEnvironment + '.release.tmp'
    [IO.File]::WriteAllText($temporary,$nextEnvironment,(New-Object Text.UTF8Encoding($false)))
    [IO.File]::Replace($temporary,$demoEnvironment,[NullString]::Value)
    $state.LastDeployedCommit = $Commit; $state.ImageTag = 'demo-' + $Commit
    $state.BlockedCommit = ''; $state.Outcome = 'Deployed'; $state.UpdatedAt = [DateTime]::UtcNow.ToString('o')
    Write-AutoDeployJson $statePath $state
    Write-AutoDeployLog $config.AgentDirectory ('Deployed ' + $Commit + '. Database, secrets and tunnel were preserved.')
} catch {
    $reason = $_.Exception.Message
    $rollbackFailed = $false
    if ($changedContainers) {
        $env:DEMO_IMAGE_TAG = $previousTag
        $rollback = Invoke-DemoDocker -Arguments ($demoCompose + @('up','-d','--no-build','--no-deps','--force-recreate','--wait','api','web'))
        if ($rollback.ExitCode -eq 0 -and (Test-AutoDeployHttp ([int]$configuration.services.web.ports[0].published))) {
            if ($null -ne $oldEnvironment) { [IO.File]::WriteAllText($demoEnvironment,$oldEnvironment,(New-Object Text.UTF8Encoding($false))) }
            Write-AutoDeployLog $config.AgentDirectory 'Previous images restored after a failed rollout; database schema was not changed.'
        } else { $rollbackFailed=$true; Write-AutoDeployLog $config.AgentDirectory 'ROLLBACK FAILED. Inspect demo API/web containers locally before restarting automatic deployment.' }
    }
    $state.LastDeployedCommit=$previousCommit; $state.ImageTag=$previousTag
    $state.BlockedCommit = $Commit; $state.Outcome = if($rollbackFailed){'RollbackFailed'}else{'Blocked'}; $state.UpdatedAt = [DateTime]::UtcNow.ToString('o')
    Write-AutoDeployJson $statePath $state
    Write-AutoDeployLog $config.AgentDirectory ('Release blocked: ' + $reason)
    throw 'Automatic release blocked. Read the private agent.log; current data and credentials were not reset.'
} finally {
    $env:HELPDESK_DEMO_STATE_DIRECTORY = $oldDirectory
    $env:DEMO_IMAGE_TAG = $oldTag
}
