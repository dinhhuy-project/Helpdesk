param([string]$Model = 'qwen3:1.7b')

$ErrorActionPreference = 'Stop'
if ($Model -notmatch '^[a-z0-9][a-z0-9._-]*:[a-z0-9][a-z0-9._-]*$' -or $Model.Length -gt 120 -or $Model -match 'cloud') {
    throw 'Choose a local model tag, for example qwen3:1.7b. Cloud models are not supported.'
}
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$envPath = Join-Path $repoPath '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example first.' }
$composePath = Join-Path $repoPath 'docker-compose.ai.yml'

docker compose -p helpdesk-ai -f $composePath up -d --wait ollama
if ($LASTEXITCODE -ne 0) { throw 'The local Ollama container could not start. Check port 11434 and Docker resources.' }
# Explicit setup only: the public API never downloads models.
docker compose -p helpdesk-ai -f $composePath exec -T ollama ollama pull $Model
if ($LASTEXITCODE -ne 0) { throw 'Local model download failed. Configuration has not been enabled.' }
docker compose -p helpdesk-ai -f $composePath exec -T ollama ollama show $Model *> $null
if ($LASTEXITCODE -ne 0) { throw 'Local model is not available.' }

$values = @{
    LOCAL_ASSISTANT_ENABLED = 'true'
    LOCAL_ASSISTANT_MODEL = $Model
    LOCAL_ASSISTANT_BASE_URL = 'http://host.docker.internal:11434/'
    LOCAL_ASSISTANT_TIMEOUT_SECONDS = '60'
}
$envText = [IO.File]::ReadAllText($envPath)
foreach ($name in $values.Keys) {
    $pattern = '(?m)^\s*' + [regex]::Escape($name) + '\s*=[^\r\n]*'
    if ([regex]::Matches($envText, $pattern).Count -gt 1) { throw 'Duplicate local assistant settings in .env; review them first.' }
}
foreach ($name in $values.Keys) {
    $pattern = '(?m)^\s*' + [regex]::Escape($name) + '\s*=[^\r\n]*'
    $line = $name + '=' + $values[$name]
    if ([regex]::IsMatch($envText, $pattern)) { $envText = [regex]::Replace($envText, $pattern, $line) }
    else { $envText = $envText.TrimEnd([char]13, [char]10) + "`r`n" + $line + "`r`n" }
}
[IO.File]::WriteAllText($envPath, $envText, (New-Object Text.UTF8Encoding($false)))

$project = Join-Path $repoPath 'be/be.csproj'
$settings = @{
    'LocalAssistant:Enabled' = 'true'
    'LocalAssistant:BaseUrl' = 'http://localhost:11434/'
    'LocalAssistant:Model' = $Model
    'LocalAssistant:TimeoutSeconds' = '60'
}
foreach ($name in $settings.Keys) {
    dotnet user-secrets set $name $settings[$name] --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure backend development settings. Check .NET user-secrets.' }
}
Write-Output 'Local assistant ready. Restart the API. Visual Studio Container uses host.docker.internal; host CLI uses localhost.'
Write-Output 'No knowledge content was published and no database migration was applied.'
