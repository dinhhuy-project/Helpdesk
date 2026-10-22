param(
    [string]$AdminEmail = 'admin@helpdesk.local',
    [string]$AdminDisplayName = 'Helpdesk Administrator'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../be.csproj'

function New-RandomSecret([int]$ByteCount) {
    $randomBytes = New-Object byte[] $ByteCount
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($randomBytes)
}

# Capture the output in memory. Never print existing secrets or generated values.
$rawSecrets = @(dotnet user-secrets list --json --project $projectPath)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read project user-secrets.' }
$jsonText = ($rawSecrets | Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
$existing = [pscustomobject]@{}
if ($jsonText.TrimStart().StartsWith('{')) {
    try { $existing = $jsonText | ConvertFrom-Json }
    catch { throw 'Cannot parse project user-secrets. Existing secrets were not changed.' }
}
$changes = @{}
$defaults = @{
    'BootstrapAdmin:Email' = $AdminEmail
    'BootstrapAdmin:DisplayName' = $AdminDisplayName
    'Jwt:Issuer' = 'Helpdesk.Api'
    'Jwt:Audience' = 'Helpdesk.Web'
    'Jwt:AccessTokenMinutes' = 15
    'Jwt:RefreshTokenDays' = 7
}
foreach ($key in $defaults.Keys) {
    if ([string]::IsNullOrWhiteSpace([string]$existing.$key)) { $changes[$key] = $defaults[$key] }
}
if ([string]::IsNullOrWhiteSpace([string]$existing.'BootstrapAdmin:Password')) {
    $changes['BootstrapAdmin:Password'] = New-RandomSecret 24
}
if ([string]::IsNullOrWhiteSpace([string]$existing.'Jwt:SigningKey')) {
    $changes['Jwt:SigningKey'] = New-RandomSecret 64
}
if ($changes.Count -gt 0) {
    $changes | ConvertTo-Json | dotnet user-secrets set --project $projectPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot save development user-secrets.' }
}
Write-Output 'Development admin/JWT configuration is ready. Existing user-secrets were preserved.'
Write-Output 'Open Visual Studio > be > Manage User Secrets to view BootstrapAdmin:Email and BootstrapAdmin:Password.'
