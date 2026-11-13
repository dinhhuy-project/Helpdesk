param([Parameter(Mandatory)][string]$ConfigPath)

$ErrorActionPreference = 'Stop'
$directory = Split-Path -Parent $ConfigPath
$stdout = Join-Path $directory 'process.stdout.log'
$stderr = Join-Path $directory 'process.stderr.log'
[IO.File]::WriteAllText($stderr,'')
try {
    & (Join-Path $directory 'agent/Watch-SelfHostDemo.ps1') -ConfigPath $ConfigPath *> $stdout
} catch {
    [IO.File]::WriteAllText($stderr,('Agent exited unexpectedly (' + $_.Exception.GetType().Name + '). Inspect agent.log and configuration privately.'))
    exit 1
}
