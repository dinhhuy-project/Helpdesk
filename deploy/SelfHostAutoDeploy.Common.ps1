$ErrorActionPreference = 'Stop'

function Get-AutoDeployComposeHash([string]$Path) {
    # Git checkouts can differ only by CRLF/LF on Windows; compare reviewed text, not line-ending bytes.
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n","`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','') }
    finally { $sha.Dispose() }
}

function Write-AutoDeployJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Write-AutoDeployLog([string]$Directory, [string]$Message) {
    $path = Join-Path $Directory 'agent.log'
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 2MB) {
        Copy-Item -LiteralPath $path -Destination ($path + '.1') -Force
        [IO.File]::WriteAllText($path, '')
    }
    Add-Content -LiteralPath $path -Value (([DateTime]::UtcNow.ToString('o')) + ' ' + $Message) -Encoding UTF8
}

function Invoke-AutoDeployGit([string]$Directory, [string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $lines = @(& git -c ('safe.directory=' + $Directory.Replace('\','/')) -C $Directory @Arguments 2>&1)
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}

function Test-AutoDeployCi($Run, [string]$Repository, [string]$Branch, [string]$Commit) {
    return $null -ne $Run -and $Run.event -eq 'push' -and $Run.status -eq 'completed' -and $Run.conclusion -eq 'success' -and
        $Run.head_branch -ceq $Branch -and $Run.head_sha -ceq $Commit -and $Run.head_repository.full_name -ieq $Repository -and
        $Run.path -eq '.github/workflows/ci.yml'
}

function Get-AutoDeployLatestRun($Configuration, [string]$Commit) {
    $url = 'https://api.github.com/repos/' + $Configuration.Repository + '/actions/workflows/ci.yml/runs?event=push&branch=' +
        [Uri]::EscapeDataString($Configuration.Branch) + '&head_sha=' + $Commit + '&per_page=5'
    $runs = Invoke-RestMethod -Uri $url -Headers @{ 'User-Agent'='Helpdesk-Demo-AutoDeploy'; Accept='application/vnd.github+json' } -TimeoutSec 20
    return ($runs.workflow_runs | Sort-Object id -Descending | Select-Object -First 1)
}

function Get-AutoDeployPendingMigrations([string[]]$SourceIds, [string[]]$AppliedIds) {
    return @($SourceIds | Where-Object { $_ -notin $AppliedIds })
}

function Test-AutoDeployHttp([int]$Port) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri ('http://127.0.0.1:' + $Port + '/health/live') -TimeoutSec 5
        if ($response.StatusCode -ne 200 -or $response.Content -ne 'Healthy') { return $false }
        $response = Invoke-WebRequest -UseBasicParsing -Uri ('http://127.0.0.1:' + $Port + '/api/v1/public/knowledge?pageSize=1') -TimeoutSec 5
        $page = $response.Content | ConvertFrom-Json
        return $response.StatusCode -eq 200 -and $null -ne $page.items -and $null -ne $page.totalCount
    } catch { return $false }
}
