param([ValidateSet('Start','Stop','Status','Retry','Disable','Once')][string]$Action = 'Start')

$autoDirectory = Join-Path $PSScriptRoot '.local/self-host-auto-deploy'
$configPath = Join-Path $autoDirectory 'config.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Run Enable-SelfHostAutoDeploy.ps1 first.' }
$stopPath = Join-Path $autoDirectory 'stop.requested'
$processPath = Join-Path $autoDirectory 'process.json'
$agentPath = Join-Path $autoDirectory 'agent/Watch-SelfHostDemo.ps1'
$launcherPath = Join-Path $autoDirectory 'agent/Launch-SelfHostDemoAgent.ps1'
$running = $false
if (Test-Path -LiteralPath $processPath) {
    $record = [IO.File]::ReadAllText($processPath) | ConvertFrom-Json
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$record.ProcessId) -ErrorAction SilentlyContinue
    $running = $null -ne $process -and $process.CommandLine -and
        ($process.CommandLine.IndexOf($agentPath,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or
         $process.CommandLine.IndexOf($launcherPath,[StringComparison]::OrdinalIgnoreCase) -ge 0)
    if (-not $running) { Remove-Item -LiteralPath $processPath }
}
if ($Action -eq 'Status') {
    'Agent running: ' + $running
    Get-Content -LiteralPath (Join-Path $autoDirectory 'status.json') -Encoding UTF8
    'Recent activity:'
    Get-Content -LiteralPath (Join-Path $autoDirectory 'agent.log') -Encoding UTF8 -Tail 8 -ErrorAction SilentlyContinue
    return
}
if ($Action -in @('Stop','Disable')) {
    [IO.File]::WriteAllText($stopPath,'Stop after the current release completes.')
    if ($Action -eq 'Disable') {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Helpdesk Demo Auto Deploy.lnk'
        if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
    }
    if ($running) {
        for ($attempt=0; $attempt -lt 15; $attempt++) {
            if (-not (Test-Path -LiteralPath $processPath)) { break }
            Start-Sleep -Seconds 1
        }
    }
    Write-Output 'Stop requested. An active rollout finishes safely before the agent exits; demo containers/data stay running.'
    return
}
if ($Action -eq 'Retry') {
    if ($running) { throw 'Stop the agent before clearing a blocked release, then retry.' }
    . (Join-Path $autoDirectory 'agent/SelfHostAutoDeploy.Common.ps1')
    $statusPath = Join-Path $autoDirectory 'status.json'
    $state = [IO.File]::ReadAllText($statusPath) | ConvertFrom-Json
    $state.BlockedCommit=''; $state.Outcome='RetryRequested'; Write-AutoDeployJson $statusPath $state
}
if ($running) { Write-Output 'The deployment agent is already running.'; return }
if (Test-Path -LiteralPath $stopPath) { Remove-Item -LiteralPath $stopPath }
if ($Action -eq 'Once') { & $agentPath -ConfigPath $configPath -Once; return }
if (-not (Test-Path -LiteralPath $launcherPath)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'deploy/Launch-SelfHostDemoAgent.ps1') -Destination $launcherPath
}
$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcherPath + '" -ConfigPath "' + $configPath + '"'
# Launch outside the caller's process tree and inherited console pipes. A long-lived
# agent must not keep `powershell -File Deploy-SelfHostDemo.ps1` waiting for EOF.
$environment = @([Environment]::GetEnvironmentVariables().GetEnumerator() | ForEach-Object { [string]$_.Key + '=' + [string]$_.Value })
$startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{
    ShowWindow=[uint16]0; EnvironmentVariables=[string[]]$environment
}
$launched = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine=('"' + (Get-Command powershell.exe -ErrorAction Stop).Source + '" ' + $arguments)
    CurrentDirectory=$PSScriptRoot; ProcessStartupInformation=$startup
}
if ($launched.ReturnValue -ne 0) { throw 'Cannot launch the detached deployment agent under the current Windows user.' }
$started = $false
for ($attempt=0; $attempt -lt 30; $attempt++) {
    if (Test-Path -LiteralPath $processPath) {
        $record = [IO.File]::ReadAllText($processPath) | ConvertFrom-Json
        if ([int]$record.ProcessId -eq [int]$launched.ProcessId) { $started=$true; break }
    }
    Start-Sleep -Milliseconds 100
}
if (-not $started) { throw 'The agent did not confirm startup. Inspect agent.log and process.stderr.log privately.' }
Write-Output 'Background deploy agent started. It watches trusted pushes and waits for CI; it does not open or restart a tunnel.'
