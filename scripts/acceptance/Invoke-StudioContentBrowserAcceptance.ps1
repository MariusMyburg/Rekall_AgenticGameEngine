[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$acceptanceRoot = Join-Path ([IO.Path]::GetTempPath()) "rekall-content-browser-$runId"
$projectRoot = Join-Path $acceptanceRoot 'project'
$fixtureRoot = Join-Path $acceptanceRoot 'fixtures'
$evidencePath = Join-Path ([IO.Path]::GetTempPath()) "rekall-content-browser-evidence-$runId.json"

function Write-MinimalGlb([string]$Path) {
    $jsonObject = '{"asset":{"version":"2.0"},"buffers":[{"byteLength":44}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}'
    $json = [Text.Encoding]::UTF8.GetBytes($jsonObject)
    $jsonPadded = New-Object byte[] ([Math]::Ceiling($json.Length / 4.0) * 4)
    for ($i = 0; $i -lt $jsonPadded.Length; $i++) { $jsonPadded[$i] = 0x20 }
    [Array]::Copy($json, $jsonPadded, $json.Length)
    $bin = New-Object byte[] 44
    $positions = [single[]](0,0,0, 1,0,0, 0,1,0)
    for ($i = 0; $i -lt $positions.Length; $i++) {
        [BitConverter]::GetBytes($positions[$i]).CopyTo($bin, $i * 4)
    }
    [BitConverter]::GetBytes([uint16]0).CopyTo($bin, 36)
    [BitConverter]::GetBytes([uint16]1).CopyTo($bin, 38)
    [BitConverter]::GetBytes([uint16]2).CopyTo($bin, 40)
    $length = 12 + 8 + $jsonPadded.Length + 8 + $bin.Length
    $stream = [IO.MemoryStream]::new()
    try {
        foreach ($value in @([uint32]0x46546C67, [uint32]2, [uint32]$length, [uint32]$jsonPadded.Length, [uint32]0x4E4F534A)) {
            $bytes = [BitConverter]::GetBytes($value); $stream.Write($bytes, 0, $bytes.Length)
        }
        $stream.Write($jsonPadded, 0, $jsonPadded.Length)
        foreach ($value in @([uint32]$bin.Length, [uint32]0x004E4942)) {
            $bytes = [BitConverter]::GetBytes($value); $stream.Write($bytes, 0, $bytes.Length)
        }
        $stream.Write($bin, 0, $bin.Length)
        [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    } finally { $stream.Dispose() }
}

function Write-MinimalWav([string]$Path) {
    $bytes = [Convert]::FromBase64String('UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=')
    [IO.File]::WriteAllBytes($Path, $bytes)
}

try {
    New-Item -ItemType Directory -Path $projectRoot, $fixtureRoot -Force | Out-Null
    $glb = Join-Path $fixtureRoot 'acceptance.glb'; Write-MinimalGlb $glb
    $png = Join-Path $fixtureRoot 'acceptance.png'
    [IO.File]::WriteAllBytes($png, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+K3s9WQAAAABJRU5ErkJggg=='))
    $wav = Join-Path $fixtureRoot 'acceptance.wav'; Write-MinimalWav $wav
    $mp3 = Join-Path $fixtureRoot 'acceptance.mp3'
    [IO.File]::WriteAllBytes($mp3, [byte[]](0x49,0x44,0x33,0x04,0x00,0x00,0x00,0x00,0x00,0x00))
    $unsupported = Join-Path $fixtureRoot 'acceptance.xyz'
    [IO.File]::WriteAllText($unsupported, 'unsupported acceptance fixture')

    & dotnet build (Join-Path $repoRoot 'src\Rekall.Age.Studio\Rekall.Age.Studio.csproj') --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Studio build failed.' }
    $studio = Join-Path $repoRoot 'src\Rekall.Age.Studio\bin\Debug\net10.0-windows\Rekall.Age.Studio.exe'
    $fixtures = @($glb, $png, $wav, $mp3, $unsupported) -join '|'
    $process = Start-Process -FilePath $studio -ArgumentList @(
        '--studio-content-browser-acceptance', '--project', $projectRoot,
        '--fixtures', $fixtures, '--evidence', $evidencePath) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Studio acceptance exited with code $($process.ExitCode)." }

    $evidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
    foreach ($requiredKind in @('model', 'texture', 'audio')) {
        if ($requiredKind -notin $evidence.ImportedKinds) { throw "Missing imported kind: $requiredKind" }
    }
    if ($evidence.UnsupportedCode -ne 'REKALL_CONTENT_IMPORT_UNSUPPORTED') { throw 'Unsupported-file result was not preserved.' }
    if ([string]::IsNullOrWhiteSpace($evidence.PersistedModelAssetId)) { throw 'Model placement did not persist.' }
    if ([string]::IsNullOrWhiteSpace($evidence.PersistedTextureAssetId)) { throw 'Texture assignment did not persist.' }
    if (-not $evidence.RestartedIndexContainedImports) { throw 'Imported content did not survive the restart boundary.' }
    $evidence | ConvertTo-Json -Depth 8
} finally {
    if (Test-Path -LiteralPath $acceptanceRoot) {
        $resolved = [IO.Path]::GetFullPath($acceptanceRoot)
        $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolved)).StartsWith('rekall-content-browser-', [StringComparison]::Ordinal)) {
            throw "Refusing unsafe acceptance cleanup target: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
