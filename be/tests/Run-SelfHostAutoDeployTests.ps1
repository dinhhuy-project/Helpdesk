$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repoPath 'deploy/SelfHostAutoDeploy.Common.ps1')
function Assert-DeployTest([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$sha = 'a' * 40
$validRun = [pscustomobject]@{ event='push';status='completed';conclusion='success';head_branch='develop';head_sha=$sha;head_repository=@{full_name='owner/project'};path='.github/workflows/ci.yml' }
Assert-DeployTest (Test-AutoDeployCi $validRun 'owner/project' 'develop' $sha) 'A successful matching push should be eligible.'
foreach ($field in @('event','status','conclusion','head_branch','head_sha','path')) {
    $copy = ($validRun | ConvertTo-Json | ConvertFrom-Json)
    $copy.$field = 'untrusted-or-failed'
    Assert-DeployTest (-not (Test-AutoDeployCi $copy 'owner/project' 'develop' $sha)) ('CI guard accepted invalid '+$field)
}
Assert-DeployTest (-not (Test-AutoDeployCi $validRun 'fork/project' 'develop' $sha)) 'Fork workflow must not deploy.'
Assert-DeployTest ((Get-AutoDeployPendingMigrations @('old','new') @('old')).Count -eq 1) 'Missing migrations must be detected.'

$directory = Join-Path $repoPath ('.local/auto-deploy-tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$fakeHelper = @'
$demoEnvironment = Join-Path $config.StateDirectory '.env'
$demoCompose = @('fake-compose')
function Assert-DemoEngine { }
function Get-DemoConfiguration { return [pscustomobject]@{services=@{web=@{ports=@(@{published=8090})}}} }
function Invoke-AutoDeployGit($Directory,$Arguments) {
    $tip = if ($config.Scenario -eq 'Superseded') { 'f' * 40 } else { $Commit }
    return [pscustomobject]@{ExitCode=0;Lines=@($tip)}
}
function Invoke-DemoDocker([string[]]$Arguments) {
    $command = if ($Arguments -contains 'build') {'build'} elseif ($Arguments -contains 'run') {'preflight'} elseif ($Arguments -contains 'up') {'up'} else {'history'}
    Add-Content -LiteralPath (Join-Path $config.AgentDirectory 'calls.jsonl') -Value (@{command=$command;tag=$env:DEMO_IMAGE_TAG}|ConvertTo-Json -Compress)
    $code=0; $lines=@('20260101000000_Existing')
    if ($command -eq 'build' -and $config.Scenario -eq 'BuildFailure') {$code=1}
    if ($command -eq 'preflight' -and $config.Scenario -eq 'PreflightFailure') {$code=1}
    if ($command -eq 'history' -and $config.Scenario -eq 'PendingMigration') {$lines=@()}
    return [pscustomobject]@{ExitCode=$code;Lines=$lines}
}
function Test-AutoDeployHttp($Port) {
    $ups=@(Get-Content -LiteralPath (Join-Path $config.AgentDirectory 'calls.jsonl') | ForEach-Object {$_|ConvertFrom-Json} | Where-Object command -eq 'up')
    return -not ($config.Scenario -eq 'HealthFailure' -and $ups.Count -eq 1)
}
function Get-AutoDeployLatestRun($Configuration,$Commit) {
    return [pscustomobject]@{event='push';status='completed';conclusion=$(if($Configuration.Scenario -eq 'CiChanged'){'failure'}else{'success'});
        head_branch='develop';head_sha=$Commit;head_repository=@{full_name='owner/project'};path='.github/workflows/ci.yml'}
}
'@
$count=9
foreach ($scenario in @('Success','BuildFailure','PreflightFailure','PendingMigration','HealthFailure','ComposeChanged','Superseded','CiChanged')) {
    $casePath=Join-Path $directory $scenario
    $source=Join-Path $casePath 'source';$agent=Join-Path $casePath 'agent';$private=Join-Path $casePath 'private'
    foreach ($path in @((Join-Path $source 'deploy'),(Join-Path $source 'be/Infrastructure/Persistence/Migrations'),$agent,$private)) { [IO.Directory]::CreateDirectory($path)|Out-Null }
    [IO.File]::WriteAllText((Join-Path $source 'deploy/SelfHostDemo.Common.ps1'),$fakeHelper)
    [IO.File]::WriteAllText((Join-Path $source 'docker-compose.demo.yml'),'isolated fixture')
    [IO.File]::WriteAllText((Join-Path $source 'be/Infrastructure/Persistence/Migrations/20260101000000_Existing.cs'),'fixture')
    foreach ($arguments in @(@('init'),@('add','.'),@('-c','user.name=Helpdesk test','-c','user.email=fixture@example.test','commit','-m','Isolated deploy fixture'))) {
        $result=Invoke-AutoDeployGit $source $arguments
        if ($result.ExitCode -ne 0) { throw 'Cannot initialize isolated Git fixture.' }
    }
    $head=Invoke-AutoDeployGit $source @('rev-parse','HEAD');$commit=($head.Lines -join '').Trim()
    $envPath=Join-Path $private '.env';$original='FAKE_SECRET=keep-this-value';[IO.File]::WriteAllText($envPath,$original)
    $configPath=Join-Path $casePath 'config.json'
    Write-AutoDeployJson $configPath @{SourceDirectory=$source;AgentDirectory=$agent;StateDirectory=$private;Branch='develop';Repository='owner/project';Scenario=$scenario;
        TrustedComposeHash=$(if($scenario -eq 'ComposeChanged') {'invalid'} else {Get-AutoDeployComposeHash (Join-Path $source 'docker-compose.demo.yml')})}
    $statusPath=Join-Path $agent 'status.json'
    Write-AutoDeployJson $statusPath @{LastDeployedCommit=('b'*40);ImageTag='self-host-demo';BlockedCommit='';Outcome='Installed';UpdatedAt='fixture'}
    $failed=$false; $failureType=''
    try { & (Join-Path $repoPath 'deploy/Update-SelfHostDemoRelease.ps1') -ConfigPath $configPath -Commit $commit } catch { $failed=$true; $failureType=$_.Exception.GetType().Name }
    $state=Get-Content $statusPath -Raw | ConvertFrom-Json
    $callsPath=Join-Path $agent 'calls.jsonl'
    $calls=if(Test-Path $callsPath) {@(Get-Content $callsPath | ForEach-Object {$_|ConvertFrom-Json})} else {@()}
    $ups=@($calls | Where-Object command -eq 'up')
    if($scenario -eq 'Success') {
        Assert-DeployTest (-not $failed -and $state.LastDeployedCommit -eq $commit -and $ups.Count -eq 1) ('Successful release did not advance exactly once: '+$failureType)
        Assert-DeployTest ((Get-Content $envPath -Raw).Contains($original)) 'Release overwrote existing secrets.'
    } else {
        Assert-DeployTest ($state.LastDeployedCommit -eq ('b'*40)) ('Failure advanced release: '+$scenario)
        Assert-DeployTest ((Get-Content $envPath -Raw) -ceq $original) ('Failure changed private environment: '+$scenario)
        if($scenario -eq 'HealthFailure') {
            Assert-DeployTest ($ups.Count -eq 2 -and $ups[1].tag -eq 'self-host-demo') 'Failed health did not roll back to previous images.'
        } else { Assert-DeployTest ($ups.Count -eq 0) ('Guard failed before rollout: '+$scenario) }
        if($scenario -in @('Superseded','CiChanged')) { Assert-DeployTest (-not $failed -and -not $state.BlockedCommit) 'A superseded or CI-changed release must be skipped, not blocked.' }
        else { Assert-DeployTest ($failed -and $state.BlockedCommit -eq $commit) ('Failed commit should be blocked: '+$scenario) }
    }
    Assert-DeployTest (@($calls | Where-Object command -eq 'tunnel').Count -eq 0) 'Release must not restart the tunnel.'
    $count++
}
Write-Output ($count.ToString() + ' auto-deploy security/release scenarios passed. Git/Docker/GitHub production state was not changed by fixtures.')

# Readiness diagnostics must distinguish a fresh/reset volume from an existing schema.
. (Join-Path $repoPath 'deploy/SelfHostDemo.Common.ps1')
function Test-StartupDiagnostic([string]$DatabaseState, [int]$ExitCode, [string]$Expected) {
    function Invoke-DemoDocker($Arguments,$InputText) { return [pscustomobject]@{ ExitCode=$ExitCode; Lines=@($DatabaseState) } }
    $message=''
    try { Assert-DemoDatabaseInitialized } catch { $message=$_.Exception.Message }
    if ($Expected) { Assert-DeployTest ($message.Contains($Expected)) ('Wrong startup diagnostic for '+$DatabaseState) }
    else { Assert-DeployTest (-not $message) 'An initialized database should pass readiness.' }
}
Test-StartupDiagnostic 'empty' 0 'Initialize-SelfHostDemo.ps1 -ApplyReviewedMigrations'
Test-StartupDiagnostic 'history-missing' 0 'existing tables but no migration history'
Test-StartupDiagnostic 'initialized' 0 ''
Test-StartupDiagnostic '' 1 'Cannot read the demo database'
Write-Output '4 startup database diagnostics passed without changing a database.'
