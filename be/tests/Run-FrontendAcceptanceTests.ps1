$ErrorActionPreference = 'Stop'
$previousBrowserAcceptance = $env:HELPDESK_BROWSER_ACCEPTANCE
try {
    $env:HELPDESK_BROWSER_ACCEPTANCE = 'true'
    & (Join-Path $PSScriptRoot 'Run-PersistenceTests.ps1') -Filter 'FullyQualifiedName~Frontend'
}
finally { $env:HELPDESK_BROWSER_ACCEPTANCE = $previousBrowserAcceptance }
