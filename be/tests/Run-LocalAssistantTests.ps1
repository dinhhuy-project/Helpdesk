param([string]$BaseUrl = 'http://localhost:11434/', [string]$Model = 'qwen3:1.7b')

$ErrorActionPreference = 'Stop'
$endpoint = $null
if (-not [Uri]::TryCreate($BaseUrl,[UriKind]::Absolute,[ref]$endpoint) -or $endpoint.Scheme -notin @('http','https') -or
    (-not $endpoint.IsLoopback -and $endpoint.Host -notin @('host.docker.internal','ollama')) -or $endpoint.AbsolutePath -ne '/' -or
    $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment) { throw 'A local Ollama base URL is required.' }
if ($Model -notmatch '^[a-z0-9][a-z0-9._-]*:[a-z0-9][a-z0-9._-]*$' -or $Model -match 'cloud') { throw 'A local model tag is required.' }
$previousUrl = [Environment]::GetEnvironmentVariable('HELPDESK_TEST_OLLAMA_URL')
$previousModel = [Environment]::GetEnvironmentVariable('HELPDESK_TEST_OLLAMA_MODEL')
$previousBrowser = [Environment]::GetEnvironmentVariable('HELPDESK_BROWSER_ACCEPTANCE')
try {
    [Environment]::SetEnvironmentVariable('HELPDESK_TEST_OLLAMA_URL',$BaseUrl)
    [Environment]::SetEnvironmentVariable('HELPDESK_TEST_OLLAMA_MODEL',$Model)
    [Environment]::SetEnvironmentVariable('HELPDESK_BROWSER_ACCEPTANCE','')
    & (Join-Path $PSScriptRoot 'Run-PersistenceTests.ps1') -Configuration Release -Filter 'FullyQualifiedName~ActualLocalModelAnswersWithReviewedSourcesAndAbstainsWithoutEvidence'
} finally {
    [Environment]::SetEnvironmentVariable('HELPDESK_TEST_OLLAMA_URL',$previousUrl)
    [Environment]::SetEnvironmentVariable('HELPDESK_TEST_OLLAMA_MODEL',$previousModel)
    [Environment]::SetEnvironmentVariable('HELPDESK_BROWSER_ACCEPTANCE',$previousBrowser)
}
