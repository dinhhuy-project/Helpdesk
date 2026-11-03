param([switch]$Public, [switch]$Stop)

. (Join-Path $PSScriptRoot 'deploy/SelfHostDemo.Common.ps1')
Assert-DemoEngine
$configuration = Get-DemoConfiguration
if ($Stop) {
    & docker @demoCompose --profile public stop
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stop the demo stack.' }
    Write-Output 'Demo stopped, Internet URL closed. Persistent volumes and Development containers were preserved.'
    return
}
& docker @demoCompose up -d --wait postgres minio
if ($LASTEXITCODE -ne 0) { throw 'Demo database/storage did not become healthy.' }
$check = Invoke-DemoDocker -Arguments ($demoCompose + @('run', '--rm', '--no-deps', 'api', 'knowledge-index', 'check', '--manifest', '/tmp/demo-knowledge-manifest.json'))
if ($check.ExitCode -ne 0) { throw 'Demo database is not ready or its reviewed index/migrations differ. Initialize the demo first; this command does not migrate.' }
& docker @demoCompose up -d --wait api web
if ($LASTEXITCODE -ne 0) { throw 'Demo API/frontend did not become healthy.' }
$port = [int]$configuration.services.web.ports[0].published
$localUrl = 'http://127.0.0.1:' + $port
$health = Invoke-WebRequest -UseBasicParsing -Uri ($localUrl + '/health/live') -TimeoutSec 10
if ($health.StatusCode -ne 200) { throw 'Demo health probe failed.' }
Write-Output ('Demo frontend + API are ready at ' + $localUrl + ' (loopback preview only).')
Write-Output 'Administrator credentials: .local/self-host-demo/credentials.txt. Do not share this file.'
if (-not $Public) {
    Write-Output 'No Internet tunnel was created by this command. Use -Public after local verification; an already-running tunnel stays running until stopped.'
    return
}
& docker @demoCompose --profile public up -d tunnel
if ($LASTEXITCODE -ne 0) { throw 'Cannot start Cloudflare Quick Tunnel.' }
$tunnelId = Invoke-DemoDocker -Arguments ($demoCompose + @('--profile', 'public', 'ps', '-q', 'tunnel'))
if ($tunnelId.ExitCode -ne 0 -or $tunnelId.Lines.Count -ne 1) { throw 'Cannot identify the running tunnel.' }
$started = Invoke-DemoDocker -Arguments @('inspect', '--format', '{{.State.StartedAt}}', [string]$tunnelId.Lines[0])
if ($started.ExitCode -ne 0) { throw 'Cannot determine the tunnel start time.' }
$url = $null
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $logs = Invoke-DemoDocker -Arguments ($demoCompose + @('logs', '--no-color', '--since', [string]$started.Lines[0], '--tail', '150', 'tunnel'))
    $match = [regex]::Match(($logs.Lines -join "`n"), 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($match.Success) { $url = $match.Value; break }
    Start-Sleep -Seconds 1
}
if (-not $url) { throw 'No temporary URL was found. Inspect the tunnel status; stop it with Start-SelfHostDemo.ps1 -Stop when finished.' }
# A brand-new hostname can briefly return NXDOMAIN before registration propagates.
Start-Sleep -Seconds 5
$reachable = $false
$dnsCacheRefreshed = $false
$lastFailure = 'not-ready'
for ($attempt = 0; $attempt -lt 45; $attempt++) {
    try {
        $publicHealth = Invoke-WebRequest -UseBasicParsing -Uri ($url + '/health/live') -TimeoutSec 5
        if ($publicHealth.StatusCode -eq 200 -and $publicHealth.Content -eq 'Healthy') { $reachable = $true; break }
    } catch {
        $lastFailure = if ($_.Exception.Response) { [string][int]$_.Exception.Response.StatusCode } else { [string]$_.Exception.Status }
        if (-not $dnsCacheRefreshed -and $attempt -ge 5 -and $_.Exception -is [Net.WebException] -and
            $_.Exception.Status -eq [Net.WebExceptionStatus]::NameResolutionFailure -and (Get-Command Clear-DnsClientCache -ErrorAction SilentlyContinue)) {
            $dnsCacheRefreshed = $true
            try {
                Clear-DnsClientCache -ErrorAction Stop
                Write-Output 'Refreshed the transient Windows DNS cache for the newly issued hostname. DNS server settings were not changed.'
            } catch { }
        }
    }
    Start-Sleep -Seconds 2
}
if (-not $reachable) {
    & docker @demoCompose --profile public stop tunnel
    throw "The temporary URL did not pass an HTTPS health check ($lastFailure). Tunnel stopped; check outbound Internet/DNS and rerun -Public."
}
[IO.File]::WriteAllText((Join-Path $demoPrivate 'public-url.txt'), $url, (New-Object Text.UTF8Encoding($false)))
Write-Output ('Internet DEMO URL: ' + $url)
Write-Output 'This is public: share only demo accounts/data. Keep Windows awake, Docker running and the Internet connected.'
Write-Output 'Stop all demo services: powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostDemo.ps1 -Stop'
