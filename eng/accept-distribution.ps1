[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DistributionRoot
)

$ErrorActionPreference = 'Stop'
$distribution = [IO.Path]::GetFullPath($DistributionRoot)
$cli = Join-Path $distribution 'tools\cli\Rekall.Age.Cli.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Distributed CLI was not found at '$cli'."
}
$windowsPlayer = Join-Path $distribution 'players\windows\Rekall.Age.Player.Windows.exe'
if (-not (Test-Path -LiteralPath $windowsPlayer -PathType Leaf)) {
    throw "Distributed Windows player was not found at '$windowsPlayer'."
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$proofRoot = Join-Path $tempRoot ('rekall-age-installed-proof-' + [Guid]::NewGuid().ToString('N'))
$gauntletRoot = Join-Path $tempRoot ('rekall-age-installed-gauntlet-' + [Guid]::NewGuid().ToString('N'))
$relocationRoot = Join-Path $tempRoot ('rekall-age-relocated-package-' + [Guid]::NewGuid().ToString('N'))
$audioRoot = Join-Path $tempRoot ('rekall-age-installed-audio-' + [Guid]::NewGuid().ToString('N'))
$succeeded = $false
$previousSdlAudioDriver = $env:SDL_AUDIODRIVER

function Invoke-Rekall {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & $cli @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Distributed Rekall AGE command failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

try {
    Invoke-Rekall context doctor
    Invoke-Rekall project create $proofRoot 'Installed Product Proof' 'world,rendering3d'
    Invoke-Rekall scene create $proofRoot Main 'world,rendering3d'
    Invoke-Rekall module scaffold-runtime-system $proofRoot proof.motion 'Proof Motion' ProofMotion MotionState MotionSystem
    Invoke-Rekall build modules $proofRoot
    Invoke-Rekall context doctor $proofRoot

    $moduleProject = Join-Path $proofRoot 'Modules\ProofMotion\ProofMotion.csproj'
    $moduleText = [IO.File]::ReadAllText($moduleProject)
    if ($moduleText.Contains('ProjectReference', [StringComparison]::OrdinalIgnoreCase) -or
        $moduleText.Contains('src\Rekall.Age.Modules', [StringComparison]::OrdinalIgnoreCase) -or
        [Text.RegularExpressions.Regex]::IsMatch($moduleText, '[A-Za-z]:\\')) {
        throw "Generated module project contains a source-repository or absolute-path reference: '$moduleProject'."
    }

    $sdkManifest = Join-Path $proofRoot '.rekall\sdk\1\rekall.sdk.json'
    if (-not (Test-Path -LiteralPath $sdkManifest -PathType Leaf)) {
        throw "Project-local SDK manifest was not created at '$sdkManifest'."
    }

    $packageRoot = Join-Path $gauntletRoot 'Package'
    Invoke-Rekall game gauntlet $gauntletRoot 'Installed Distribution Proof' $packageRoot
    $proofFrame = Join-Path $gauntletRoot 'Builds\AgentAuthoringGauntletAudit\package_play_frame_001.png'
    if (-not (Test-Path -LiteralPath $proofFrame -PathType Leaf) -or (Get-Item -LiteralPath $proofFrame).Length -le 100) {
        throw "Installed gauntlet proof frame is missing or blank at '$proofFrame'."
    }

    $manifestPath = Join-Path $packageRoot 'rekall.package.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2 -or [IO.Path]::IsPathRooted($manifest.gameRoot) -or [IO.Path]::IsPathRooted($manifest.launchPath)) {
        throw "Installed gauntlet emitted a legacy or non-relocatable package manifest at '$manifestPath'."
    }
    $forbiddenPayload = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'Game') -Recurse -File | Where-Object {
        $_.Extension -in @('.cs', '.csproj', '.pdb', '.log', '.trx', '.pfx', '.snk') -or
        $_.Name -eq '.env' -or $_.FullName -match '[\\/]\.rekall[\\/]'
    }
    if ($forbiddenPayload) {
        throw "Installed gauntlet package contains authoring-only or secret-bearing payload: '$($forbiddenPayload[0].FullName)'."
    }

    New-Item -ItemType Directory -Path $relocationRoot | Out-Null
    $relocatedArchive = Join-Path $relocationRoot 'renamed-relocated-game.zip'
    Copy-Item -LiteralPath ($packageRoot + '.zip') -Destination $relocatedArchive
    $relocatedProof = Join-Path $relocationRoot 'Proof'
    Invoke-Rekall game audit-package $relocatedArchive $relocatedProof
    $relocatedFrame = Join-Path $relocatedProof 'package_play_frame_001.png'
    if (-not (Test-Path -LiteralPath $relocatedFrame -PathType Leaf) -or (Get-Item -LiteralPath $relocatedFrame).Length -le 100) {
        throw "Relocated package audit did not produce a nonblank proof frame at '$relocatedFrame'."
    }

    Invoke-Rekall project create $audioRoot 'Installed Audio Proof' 'audio'
    Invoke-Rekall scene create $audioRoot Main 'audio'
    $audioDirectory = Join-Path $audioRoot 'Assets\audio'
    New-Item -ItemType Directory -Path $audioDirectory -Force | Out-Null
    $wavePath = Join-Path $audioDirectory 'installed-tone.wav'
    $sampleRate = 48000
    $samples = New-Object Int16[] $sampleRate
    for ($index = 0; $index -lt $samples.Length; $index++) {
        $samples[$index] = [Int16]([Math]::Sin(2 * [Math]::PI * 440 * $index / $sampleRate) * 8000)
    }
    $waveStream = [IO.MemoryStream]::new()
    $waveWriter = [IO.BinaryWriter]::new($waveStream)
    try {
        $dataLength = $samples.Length * 2
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
        $waveWriter.Write(36 + $dataLength)
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
        $waveWriter.Write(16)
        $waveWriter.Write([Int16]1)
        $waveWriter.Write([Int16]1)
        $waveWriter.Write($sampleRate)
        $waveWriter.Write($sampleRate * 2)
        $waveWriter.Write([Int16]2)
        $waveWriter.Write([Int16]16)
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $waveWriter.Write($dataLength)
        foreach ($sample in $samples) { $waveWriter.Write($sample) }
        $waveWriter.Flush()
        [IO.File]::WriteAllBytes($wavePath, $waveStream.ToArray())
    }
    finally {
        $waveWriter.Dispose()
        $waveStream.Dispose()
    }

    $catalog = @{
        assets = @(@{
            id = 'installed-tone'
            name = 'installed-tone'
            displayName = 'Installed Tone'
            kind = 'audio'
            sourcePath = ''
            importedPath = 'Assets/audio/installed-tone.wav'
            contentHash = 'installed-audio-proof'
        })
    }
    $catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $audioRoot 'Assets\assets.age.catalog.json') -Encoding utf8
    $scenePath = Join-Path $audioRoot 'Scenes\Main.age.scene.json'
    $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
    $scene.capabilities = @('audio')
    $scene.entities = @(
        @{
            id = 'installed-audio-listener'; name = 'Audio Listener'; tags = @('audio'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Transform3D'; properties = @{} },
                @{ type = 'Rekall.AudioListener'; properties = @{ Active = $true } }
            )
        },
        @{
            id = 'installed-audio-emitter'; name = 'Installed Tone'; tags = @('audio'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Transform3D'; properties = @{} },
                @{ type = 'Rekall.AudioEmitter'; properties = @{ Clip = 'installed-tone'; PlayOnStart = $true; Loop = $true } }
            )
        }
    )
    $scene | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $scenePath -Encoding utf8
    Invoke-Rekall runtime inspect $audioRoot Main 30

    $env:SDL_AUDIODRIVER = 'dummy'
    $audioProcess = Start-Process -FilePath $windowsPlayer -ArgumentList @($audioRoot, 'Main', '--frames', '10', '--audio-required') -PassThru -WindowStyle Hidden
    $audioProcess.WaitForExit()
    if ($audioProcess.ExitCode -ne 0) {
        throw "Installed Windows player audio device proof failed ($($audioProcess.ExitCode))."
    }

    $succeeded = $true
    Write-Output "Installed distribution acceptance passed: $distribution"
}
finally {
    $env:SDL_AUDIODRIVER = $previousSdlAudioDriver
    if ($succeeded) {
        foreach ($path in @($proofRoot, $gauntletRoot, $relocationRoot, $audioRoot)) {
            $resolved = [IO.Path]::GetFullPath($path)
            if ($resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $resolved)) {
                Remove-Item -LiteralPath $resolved -Recurse -Force
            }
        }
    }
    else {
        Write-Error "Installed distribution acceptance failed. Evidence preserved at '$proofRoot', '$gauntletRoot', '$relocationRoot', and '$audioRoot'."
    }
}
