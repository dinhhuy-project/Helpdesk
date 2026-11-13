[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../.local/uat/mock-files'),
    [ValidateRange(1, 10485760)]
    [long]$MaxFileBytes = 10485760,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($outputPath)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$fixtures = [ordered]@{
    'uat-public.txt' = $utf8.GetBytes('UAT-PUBLIC-FILE: Noi dung gia lap cong khai; khong co du lieu that.')
    'uat-internal.txt' = $utf8.GetBytes('UAT-INTERNAL-ONLY-20261104-A: Chi danh cho nhom ho tro UAT.')
    'uat-image.png' = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7xkAAAAASUVORK5CYII=')
    'uat-empty.txt' = [byte[]]@()
    'uat-invalid-utf8.txt' = [byte[]]@(0xC3, 0x28)
    'uat-fake.pdf' = $utf8.GetBytes('UAT fake PDF: deliberately lacks a PDF signature.')
    'uat-blocked.exe' = $utf8.GetBytes('Inert UAT text only. This is not an executable program.')
}

foreach ($fixture in $fixtures.GetEnumerator()) {
    $targetPath = Join-Path $outputPath $fixture.Key
    if ((Test-Path -LiteralPath $targetPath) -and -not $Force) {
        throw "File already exists: $targetPath. Use a new directory or -Force to replace UAT fixtures."
    }
}
$oversizePath = Join-Path $outputPath 'uat-oversize.txt'
if ((Test-Path -LiteralPath $oversizePath) -and -not $Force) {
    throw "File already exists: $oversizePath. Use a new directory or -Force to replace UAT fixtures."
}
$manifestPath = Join-Path $outputPath 'manifest.json'
if ((Test-Path -LiteralPath $manifestPath) -and -not $Force) {
    throw "File already exists: $manifestPath. Use a new directory or -Force to replace UAT fixtures."
}

foreach ($fixture in $fixtures.GetEnumerator()) {
    [IO.File]::WriteAllBytes((Join-Path $outputPath $fixture.Key), $fixture.Value)
}
$buffer = New-Object byte[] 65536
for ($index = 0; $index -lt $buffer.Length; $index++) { $buffer[$index] = 65 }
$file = [IO.File]::Open($oversizePath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
try {
    $remaining = $MaxFileBytes + 1
    while ($remaining -gt 0) {
        $chunkLength = [int][Math]::Min($buffer.Length, $remaining)
        $file.Write($buffer, 0, $chunkLength)
        $remaining -= $chunkLength
    }
}
finally { $file.Dispose() }

$names = @($fixtures.Keys) + @('uat-oversize.txt')
$manifest = @($names | ForEach-Object {
    $fixturePath = Join-Path $outputPath $_
    [ordered]@{
        name = $_
        bytes = (Get-Item -LiteralPath $fixturePath).Length
        sha256 = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
[IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject $manifest -Depth 4), $utf8)
Write-Host "Created 8 UAT files and manifest: $outputPath"
Write-Host 'No database, application configuration, or credentials were changed.'
