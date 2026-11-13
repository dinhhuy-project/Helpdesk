param([Parameter(Mandatory)][string]$ConfigPath, [switch]$Once)

. (Join-Path $PSScriptRoot 'SelfHostAutoDeploy.Common.ps1')
$config = [IO.File]::ReadAllText($ConfigPath) | ConvertFrom-Json
if ($config.Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $config.Branch -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$' -or
    $config.PollSeconds -lt 120 -or $config.PollSeconds -gt 3600) { throw 'Invalid auto-deployment configuration.' }
$statePath = Join-Path $config.AgentDirectory 'status.json'
$stopPath = Join-Path $config.AgentDirectory 'stop.requested'
$lease = $null
$oldPrompt = $env:GIT_TERMINAL_PROMPT; $env:GIT_TERMINAL_PROMPT = '0'
try {
    $lease = New-Object IO.FileStream((Join-Path $config.AgentDirectory 'agent.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    Write-AutoDeployJson (Join-Path $config.AgentDirectory 'process.json') @{ ProcessId = $PID; StartedAt = [DateTime]::UtcNow.ToString('o') }
    Write-AutoDeployLog $config.AgentDirectory ('Agent started for ' + $config.Repository + ' branch ' + $config.Branch + '.')
    do {
        if (Test-Path -LiteralPath $stopPath) { break }
        try {
            $state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
            $state | Add-Member -NotePropertyName LastCheckedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
            Write-AutoDeployJson $statePath $state
            if ($state.Outcome -eq 'RollbackFailed') { throw 'Manual recovery is required after a failed rollback.' }
            $remote = 'https://github.com/' + $config.Repository + '.git'
            if (-not (Test-Path -LiteralPath (Join-Path $config.SourceDirectory '.git'))) {
                $clone = Invoke-AutoDeployGit $config.AgentDirectory @('clone','--single-branch','--branch',$config.Branch,$remote,$config.SourceDirectory)
                if ($clone.ExitCode -ne 0) { throw 'Cannot create the isolated public-repository checkout.' }
            }
            $origin = Invoke-AutoDeployGit $config.SourceDirectory @('remote','get-url','origin')
            if ($origin.ExitCode -ne 0 -or ($origin.Lines -join '').Trim() -cne $remote) { throw 'Isolated checkout origin differs from the configured repository.' }
            $dirty = Invoke-AutoDeployGit $config.SourceDirectory @('status','--porcelain')
            if ($dirty.ExitCode -ne 0 -or $dirty.Lines.Count -gt 0) { throw 'Isolated checkout has local edits; no reset/clean was attempted.' }
            $fetch = Invoke-AutoDeployGit $config.SourceDirectory @('fetch','--no-tags','origin', ('+refs/heads/' + $config.Branch + ':refs/remotes/origin/' + $config.Branch))
            $head = Invoke-AutoDeployGit $config.SourceDirectory @('rev-parse', ('refs/remotes/origin/' + $config.Branch))
            if ($fetch.ExitCode -ne 0 -or $head.ExitCode -ne 0) { throw 'Cannot fetch the trusted branch.' }
            $commit = ($head.Lines -join '').Trim()
            if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid fetched commit.' }
            if ($commit -eq $state.LastDeployedCommit -or $commit -eq $state.BlockedCommit) {
                # No API call while up-to-date or awaiting manual resolution.
                if ($commit -eq $state.LastDeployedCommit) { $state.Outcome='UpToDate'; Write-AutoDeployJson $statePath $state }
            } else {
                $run = Get-AutoDeployLatestRun $config $commit
                if (-not (Test-AutoDeployCi $run $config.Repository $config.Branch $commit)) {
                    $state.Outcome = 'WaitingForCi'; Write-AutoDeployJson $statePath $state
                } else {
                    $ancestor = Invoke-AutoDeployGit $config.SourceDirectory @('merge-base','--is-ancestor',$state.LastDeployedCommit,$commit)
                    if ($ancestor.ExitCode -ne 0) { throw 'Branch history was rewritten; review before resetting the deployment baseline.' }
                    # Keep a manually stopped demo stopped. Never open an Internet tunnel from a push.
                    $running = @(& docker compose -p helpdesk-demo --env-file (Join-Path $config.StateDirectory '.env') -f (Join-Path $config.TargetRepository 'docker-compose.demo.yml') ps --status running -q web 2>$null)
                    if ($LASTEXITCODE -ne 0 -or $running.Count -eq 0) {
                        $state.Outcome = 'WaitingForDemo'; Write-AutoDeployJson $statePath $state
                    } else {
                        $checkout = Invoke-AutoDeployGit $config.SourceDirectory @('checkout','--detach',$commit)
                        if ($checkout.ExitCode -ne 0) { throw 'Cannot select the CI-approved commit.' }
                        & (Join-Path $PSScriptRoot 'Update-SelfHostDemoRelease.ps1') -ConfigPath $ConfigPath -Commit $commit
                    }
                }
            }
        } catch { Write-AutoDeployLog $config.AgentDirectory ('Check failed (' + $_.Exception.GetType().Name + '). No automatic schema changes or working-tree reset. See status.json/last-build.log.') }
        if ($Once) { break }
        # Short sleeps allow graceful stop without interrupting an in-progress rollout.
        for ($second = 0; $second -lt $config.PollSeconds; $second++) {
            if (Test-Path -LiteralPath $stopPath) { break }
            Start-Sleep -Seconds 1
        }
    } while (-not (Test-Path -LiteralPath $stopPath))
} finally {
    $env:GIT_TERMINAL_PROMPT = $oldPrompt
    if ($lease) {
        Remove-Item -LiteralPath (Join-Path $config.AgentDirectory 'process.json') -ErrorAction SilentlyContinue
        $lease.Dispose()
        Write-AutoDeployLog $config.AgentDirectory 'Agent stopped.'
    }
}
