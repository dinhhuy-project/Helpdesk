param([switch]$BootstrapBucket, [switch]$UseComposeConfiguration)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../be.csproj'
$envPath = Join-Path $PSScriptRoot '../../.env'
$storageValues = @{}
if (Test-Path -LiteralPath $envPath) {
    foreach ($line in Get-Content -LiteralPath $envPath) {
        if ($line -match '^\s*(MINIO_ENABLED|MINIO_APP_ACCESS_KEY|MINIO_APP_SECRET_KEY|MINIO_ROOT_USER|MINIO_ROOT_PASSWORD|MINIO_API_PORT)\s*=(.*)$') {
            $storageValue = $Matches[2].Trim()
            if ($storageValue.Length -ge 2 -and (($storageValue.StartsWith('"') -and $storageValue.EndsWith('"')) -or
                ($storageValue.StartsWith("'") -and $storageValue.EndsWith("'")))) { $storageValue = $storageValue.Substring(1, $storageValue.Length - 2) }
            $storageValues[$Matches[1]] = $storageValue
        }
    }
}
foreach ($key in @('MINIO_APP_ACCESS_KEY','MINIO_APP_SECRET_KEY','MINIO_ROOT_USER','MINIO_ROOT_PASSWORD','MINIO_API_PORT')) {
    $processValue = [Environment]::GetEnvironmentVariable($key)
    if (-not $UseComposeConfiguration -and -not [string]::IsNullOrWhiteSpace($processValue)) { $storageValues[$key] = $processValue }
}
$storagePort = if ($storageValues['MINIO_API_PORT']) { $storageValues['MINIO_API_PORT'] } else { '9000' }
if ($storagePort -notmatch '^\d+$' -or [int]$storagePort -lt 1 -or [int]$storagePort -gt 65535) { throw 'Invalid MINIO_API_PORT.' }
if ($BootstrapBucket) {
    if (-not $storageValues['MINIO_ROOT_USER'] -or -not $storageValues['MINIO_ROOT_PASSWORD']) { throw 'Set local MinIO root credentials in .env before bucket bootstrap.' }
    $overrides = @{
        'Minio__Enabled' = 'true'; 'Minio__Endpoint' = "localhost:$storagePort"; 'Minio__HostOverride' = '';
        'Minio__Bucket' = 'helpdesk-attachments'; 'Minio__UseSsl' = 'false';
        'Minio__AccessKey' = $storageValues['MINIO_ROOT_USER']; 'Minio__SecretKey' = $storageValues['MINIO_ROOT_PASSWORD']
    }
    $previousStorageEnvironment = @{}
    try {
        foreach ($key in $overrides.Keys) {
            $previousStorageEnvironment[$key] = [Environment]::GetEnvironmentVariable($key)
            [Environment]::SetEnvironmentVariable($key, $overrides[$key])
        }
        dotnet run --project $projectPath --no-launch-profile -- initialize-storage | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Bucket bootstrap failed. Check MinIO readiness and private/unversioned policy; existing bucket settings were not changed.' }
    }
    finally {
        foreach ($key in $previousStorageEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $previousStorageEnvironment[$key]) }
    }
    Write-Output 'Private bucket bootstrap completed. Existing policy and versioning were preserved.'
    return
}
# Do not persist root credentials. The application must use a scoped account/service account.
$rawSecrets = @(dotnet user-secrets list --json --project $projectPath)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read project user-secrets.' }
$jsonText = ($rawSecrets | Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
$existing = [pscustomobject]@{}
if ($jsonText.TrimStart().StartsWith('{')) { $existing = $jsonText | ConvertFrom-Json }
$changes = @{}
$defaults = @{
    'Minio:Enabled' = $true; 'Minio:Endpoint' = "localhost:$storagePort"; 'Minio:UseSsl' = $false;
    'Minio:Bucket' = 'helpdesk-attachments'; 'Minio:CleanupEnabled' = $true
}
if ($UseComposeConfiguration) {
    if ($storageValues['MINIO_ENABLED'] -notin @('true', 'false')) { throw 'Set MINIO_ENABLED=true or false in .env before syncing Compose configuration.' }
    $defaults['Minio:Enabled'] = [bool]::Parse($storageValues['MINIO_ENABLED'])
    $defaults['Minio:HostOverride'] = ''
}
foreach ($key in $defaults.Keys) {
    if ($UseComposeConfiguration -or $null -eq $existing.$key -or [string]::IsNullOrWhiteSpace([string]$existing.$key)) { $changes[$key] = $defaults[$key] }
}
foreach ($pair in @(@('Minio:AccessKey','MINIO_APP_ACCESS_KEY'), @('Minio:SecretKey','MINIO_APP_SECRET_KEY'))) {
    if ($UseComposeConfiguration -or [string]::IsNullOrWhiteSpace([string]$existing.($pair[0]))) {
        if ([string]::IsNullOrWhiteSpace([string]$storageValues[$pair[1]])) {
            throw 'Create a scoped MinIO application account using deploy/minio/attachments-policy.json, then set MINIO_APP_ACCESS_KEY and MINIO_APP_SECRET_KEY in .env and rerun. No root credential is stored as an application secret.'
        }
        $changes[$pair[0]] = $storageValues[$pair[1]]
    }
}
if ($changes.Count -gt 0) {
    $changes | ConvertTo-Json | dotnet user-secrets set --project $projectPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot save development storage user-secrets.' }
}
if ($UseComposeConfiguration) { Write-Output 'MinIO development settings synchronized from Compose .env. Other user-secrets were preserved.' }
else { Write-Output 'Storage defaults saved; existing user-secrets were preserved. Verify Minio:Enabled=true if it was previously disabled.' }
