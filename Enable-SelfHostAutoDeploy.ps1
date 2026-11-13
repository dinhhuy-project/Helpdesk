param([ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]*$')][string]$Branch = 'develop', [ValidateRange(120,3600)][int]$PollSeconds = 120)

. (Join-Path $PSScriptRoot 'deploy/SelfHostDemo.Common.ps1')
. (Join-Path $PSScriptRoot 'deploy/SelfHostAutoDeploy.Common.ps1')
Assert-DemoEngine
$configuration = Get-DemoConfiguration
$root = $PSScriptRoot
$autoDirectory = Join-Path $root '.local/self-host-auto-deploy'
$stateDirectory = $demoPrivate
$demoPrivate = $autoDirectory
Set-DemoPrivatePermissions
$demoPrivate = $stateDirectory
$agentDirectory = Join-Path $autoDirectory 'agent'
[IO.Directory]::CreateDirectory($agentDirectory) | Out-Null
$configPath = Join-Path $autoDirectory 'config.json'
if (Test-Path -LiteralPath (Join-Path $autoDirectory 'process.json')) {
    & (Join-Path $root 'Start-SelfHostAutoDeploy.ps1') -Action Stop
    if (Test-Path -LiteralPath (Join-Path $autoDirectory 'process.json')) { throw 'An update is still finishing. Wait for the agent to stop before reinstalling.' }
}
$remote = Invoke-AutoDeployGit $root @('remote','get-url','origin')
if ($remote.ExitCode -ne 0 -or ($remote.Lines -join '') -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?$') { throw 'A GitHub HTTPS or SSH repository origin is required.' }
$repository = $Matches[1]
$metadata = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repository) -Headers @{ 'User-Agent'='Helpdesk-Demo-AutoDeploy' } -TimeoutSec 20
if ($metadata.private -or $metadata.archived) { throw 'This token-free agent supports an active public repository only.' }
$branchInfo = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repository + '/branches/' + [Uri]::EscapeDataString($Branch)) -Headers @{ 'User-Agent'='Helpdesk-Demo-AutoDeploy' } -TimeoutSec 20
$baseline = Invoke-AutoDeployGit $root @('rev-parse','HEAD')
if ($baseline.ExitCode -ne 0 -or ($baseline.Lines -join '').Trim() -cne $branchInfo.commit.sha) {
    throw 'Local HEAD must match the current remote branch when setting the baseline. Fetch/review the branch first; no reset was attempted.'
}
foreach ($name in @('SelfHostAutoDeploy.Common.ps1','Watch-SelfHostDemo.ps1','Update-SelfHostDemoRelease.ps1','Launch-SelfHostDemoAgent.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root ('deploy/' + $name)) -Destination (Join-Path $agentDirectory $name) -Force
}
$config = @{
    Repository=$repository; Branch=$Branch; PollSeconds=$PollSeconds; TargetRepository=$root
    StateDirectory=$stateDirectory; AgentDirectory=$autoDirectory; SourceDirectory=(Join-Path $autoDirectory 'source')
    TrustedComposeHash=(Get-AutoDeployComposeHash (Join-Path $root 'docker-compose.demo.yml'))
}
Write-AutoDeployJson $configPath $config
$statusPath = Join-Path $autoDirectory 'status.json'
if (-not (Test-Path -LiteralPath $statusPath)) {
    $tag = ([string]$configuration.services.api.image) -replace '^helpdesk-api:', ''
    Write-AutoDeployJson $statusPath @{
        LastDeployedCommit=($baseline.Lines -join '').Trim(); ImageTag=$tag; BlockedCommit=''; Outcome='Installed'
        UpdatedAt=[DateTime]::UtcNow.ToString('o')
    }
} else {
    $state = [IO.File]::ReadAllText($statusPath) | ConvertFrom-Json
    $state.BlockedCommit=''; $state.ImageTag=(([string]$configuration.services.api.image) -replace '^helpdesk-api:', '')
    $state.Outcome='Reconfigured'; $state.UpdatedAt=[DateTime]::UtcNow.ToString('o')
    Write-AutoDeployJson $statusPath $state
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Helpdesk Demo Auto Deploy.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Get-Command powershell.exe -ErrorAction Stop).Source
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $root 'Start-SelfHostAutoDeploy.ps1') + '"'
$shortcut.WorkingDirectory = $root
$shortcut.WindowStyle = 7
$shortcut.Save()
& (Join-Path $root 'Start-SelfHostAutoDeploy.ps1')
Write-Output ('Auto deploy enabled for ' + $repository + ' / ' + $Branch + ', after successful push CI, checked every ' + $PollSeconds + ' seconds.')
Write-Output 'Changes in the IDE are untouched. Demo secrets/data and the running tunnel are preserved.'
Write-Output 'Status: Start-SelfHostAutoDeploy.ps1 -Action Status | Disable: -Action Disable'
