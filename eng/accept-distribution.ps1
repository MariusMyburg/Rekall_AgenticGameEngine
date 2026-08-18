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
$moduleTrustTamperRoot = Join-Path $tempRoot ('rekall-age-installed-module-tamper-' + [Guid]::NewGuid().ToString('N'))
$gauntletRoot = Join-Path $tempRoot ('rekall-age-installed-gauntlet-' + [Guid]::NewGuid().ToString('N'))
$relocationRoot = Join-Path $tempRoot ('rekall-age-relocated-package-' + [Guid]::NewGuid().ToString('N'))
$audioRoot = Join-Path $tempRoot ('rekall-age-installed-audio-' + [Guid]::NewGuid().ToString('N'))
$diagnosticsRoot = Join-Path $tempRoot ('rekall-age-installed-diagnostics-' + [Guid]::NewGuid().ToString('N'))
$compatibilityRoot = Join-Path $tempRoot ('rekall-age-installed-compatibility-' + [Guid]::NewGuid().ToString('N'))
$succeeded = $false
$previousSdlAudioDriver = $env:SDL_AUDIODRIVER
$previousDiagnosticsRoot = $env:REKALL_AGE_DIAGNOSTICS_DIR

function Invoke-Rekall {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & $cli @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Distributed Rekall AGE command failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function Invoke-RekallOutput {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $lines = @(& $cli @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Distributed Rekall AGE command failed ($exitCode): $($Arguments -join ' ')`n$($lines -join "`n")"
    }
    return $lines -join "`n"
}

function Invoke-RekallFailureOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int]$ExpectedExitCode = 1
    )
    $lines = @(& $cli @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExitCode) {
        throw "Distributed Rekall AGE negative command exited $exitCode, expected $ExpectedExitCode`: $($Arguments -join ' ')`n$($lines -join "`n")"
    }
    return $lines -join "`n"
}

function Invoke-WindowsPlayerProof {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode
    )

    New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
    $stdoutPath = Join-Path $diagnosticsRoot ($Name + '.stdout.txt')
    $stderrPath = Join-Path $diagnosticsRoot ($Name + '.stderr.txt')
    $process = Start-Process -FilePath $windowsPlayer -ArgumentList $Arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $process.WaitForExit(45000)) {
        $process.Kill($true)
        throw "Installed Windows player proof '$Name' exceeded 45 seconds."
    }
    $process.WaitForExit()
    $output = ([IO.File]::ReadAllText($stdoutPath) + [IO.File]::ReadAllText($stderrPath))
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "Installed Windows player proof '$Name' exited $($process.ExitCode), expected $ExpectedExitCode.`n$output"
    }

    return $output
}

try {
    Invoke-Rekall context doctor

    $compatibilityScenes = Join-Path $compatibilityRoot 'Scenes'
    New-Item -ItemType Directory -Path $compatibilityScenes -Force | Out-Null
    $compatibilityManifest = Join-Path $compatibilityRoot 'rekall.project.json'
    $compatibilityScene = Join-Path $compatibilityScenes 'Main.age.scene.json'
    $legacyManifest = '{"name":"Installed Legacy Compatibility","capabilities":["world"],"agentExtension":{"preserve":true}}'
    $legacyScene = '{"id":"installed_legacy","name":"Main","capabilities":["world"],"entities":[],"agentExtension":{"preserve":true}}'
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($compatibilityManifest, $legacyManifest, $utf8NoBom)
    [IO.File]::WriteAllText($compatibilityScene, $legacyScene, $utf8NoBom)
    $legacyManifestHash = (Get-FileHash -LiteralPath $compatibilityManifest -Algorithm SHA256).Hash
    $legacySceneHash = (Get-FileHash -LiteralPath $compatibilityScene -Algorithm SHA256).Hash

    $legacyInspection = Invoke-RekallOutput compatibility inspect $compatibilityRoot
    if (-not $legacyInspection.Contains('Current: False; migratable: True', [StringComparison]::Ordinal) -or
        ([Text.RegularExpressions.Regex]::Matches($legacyInspection, 'REKALL_DOCUMENT_SCHEMA_LEGACY')).Count -ne 2) {
        throw "Installed compatibility inspection did not report two migratable legacy documents.`n$legacyInspection"
    }

    $migrationDryRun = Invoke-RekallOutput compatibility migrate $compatibilityRoot
    if (-not $migrationDryRun.Contains('dry-run changed no files', [StringComparison]::Ordinal) -or
        (Get-FileHash -LiteralPath $compatibilityManifest -Algorithm SHA256).Hash -ne $legacyManifestHash -or
        (Get-FileHash -LiteralPath $compatibilityScene -Algorithm SHA256).Hash -ne $legacySceneHash) {
        throw "Installed compatibility dry-run changed bytes or omitted dry-run evidence.`n$migrationDryRun"
    }

    $migrationApply = Invoke-RekallOutput compatibility migrate $compatibilityRoot --apply
    if (-not $migrationApply.Contains('Applied: True', [StringComparison]::Ordinal) -or
        -not $migrationApply.Contains('Backup root:', [StringComparison]::Ordinal)) {
        throw "Installed compatibility apply did not report applied backup evidence.`n$migrationApply"
    }
    $migratedManifestText = [IO.File]::ReadAllText($compatibilityManifest)
    $migratedSceneText = [IO.File]::ReadAllText($compatibilityScene)
    if (-not $migratedManifestText.Contains('"schemaVersion": 1', [StringComparison]::Ordinal) -or
        -not $migratedSceneText.Contains('"schemaVersion": 1', [StringComparison]::Ordinal) -or
        -not $migratedManifestText.Contains('"agentExtension"', [StringComparison]::Ordinal) -or
        -not $migratedSceneText.Contains('"agentExtension"', [StringComparison]::Ordinal)) {
        throw 'Installed compatibility migration did not preserve schema and extension facts.'
    }
    $backupSets = @(Get-ChildItem -LiteralPath (Join-Path $compatibilityRoot '.rekall\migrations') -Directory -Filter 'migration-*')
    if ($backupSets.Count -ne 1 -or
        (Get-FileHash -LiteralPath (Join-Path $backupSets[0].FullName 'rekall.project.json') -Algorithm SHA256).Hash -ne $legacyManifestHash -or
        (Get-FileHash -LiteralPath (Join-Path $backupSets[0].FullName 'Scenes\Main.age.scene.json') -Algorithm SHA256).Hash -ne $legacySceneHash) {
        throw 'Installed compatibility migration did not preserve one exact backup set.'
    }

    $currentInspection = Invoke-RekallOutput compatibility inspect $compatibilityRoot
    if (-not $currentInspection.Contains('Current: True; migratable: False', [StringComparison]::Ordinal) -or
        ([Text.RegularExpressions.Regex]::Matches($currentInspection, 'REKALL_DOCUMENT_SCHEMA_CURRENT')).Count -ne 2) {
        throw "Installed compatibility reinspection did not report two current documents.`n$currentInspection"
    }

    $futureManifestText = $migratedManifestText.Replace('"schemaVersion": 1', '"schemaVersion": 2', [StringComparison]::Ordinal)
    [IO.File]::WriteAllText($compatibilityManifest, $futureManifestText, $utf8NoBom)
    $futureHash = (Get-FileHash -LiteralPath $compatibilityManifest -Algorithm SHA256).Hash
    $futureFailure = Invoke-RekallFailureOutput -Arguments @('compatibility', 'migrate', $compatibilityRoot, '--apply')
    if (-not $futureFailure.Contains('REKALL_DOCUMENT_SCHEMA_FUTURE', [StringComparison]::Ordinal) -or
        (Get-FileHash -LiteralPath $compatibilityManifest -Algorithm SHA256).Hash -ne $futureHash) {
        throw "Installed future-schema refusal was incomplete or changed source bytes.`n$futureFailure"
    }
    Write-Output $legacyInspection
    Write-Output $migrationDryRun
    Write-Output $migrationApply
    Write-Output $currentInspection
    Write-Output $futureFailure

    Invoke-Rekall project create $proofRoot 'Installed Product Proof' 'world,rendering3d'
    Invoke-Rekall scene create $proofRoot Main 'world,rendering3d'
    Invoke-Rekall module scaffold-runtime-system $proofRoot proof.motion 'Proof Motion' ProofMotion MotionState MotionSystem
    Invoke-Rekall build modules $proofRoot
    Invoke-Rekall context doctor $proofRoot
    $trustOutput = Invoke-RekallOutput module trust $proofRoot
    if (-not $trustOutput.Contains('Ready: True', [StringComparison]::Ordinal) -or
        -not $trustOutput.Contains('Trust posture: in-process-full-trust', [StringComparison]::Ordinal)) {
        throw "Installed module trust inspection did not report ready full-trust evidence.`n$trustOutput"
    }
    Write-Output $trustOutput

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

    $moduleReceipt = Join-Path $proofRoot 'Modules\ProofMotion\bin\rekall\net10.0\rekall.module.build.json'
    if (-not (Test-Path -LiteralPath $moduleReceipt -PathType Leaf)) {
        throw "Installed module build receipt was not created at '$moduleReceipt'."
    }

    Copy-Item -LiteralPath $proofRoot -Destination $moduleTrustTamperRoot -Recurse
    $tamperedAssembly = Join-Path $moduleTrustTamperRoot 'Modules\ProofMotion\bin\rekall\net10.0\ProofMotion.dll'
    $tamperedBytes = [IO.File]::ReadAllBytes($tamperedAssembly)
    if ($tamperedBytes.Length -lt 1) {
        throw "Installed module assembly is unexpectedly empty at '$tamperedAssembly'."
    }
    $lastByte = $tamperedBytes.Length - 1
    $tamperedBytes[$lastByte] = [byte]($tamperedBytes[$lastByte] -bxor 0xff)
    [IO.File]::WriteAllBytes($tamperedAssembly, $tamperedBytes)

    $tamperedTrustLines = @(& $cli module trust $moduleTrustTamperRoot 2>&1)
    $tamperedTrustExit = $LASTEXITCODE
    $tamperedTrustOutput = $tamperedTrustLines -join "`n"
    if ($tamperedTrustExit -eq 0 -or
        -not $tamperedTrustOutput.Contains('REKALL_MODULE_OUTPUT_HASH_MISMATCH', [StringComparison]::Ordinal)) {
        throw "Installed tampered module trust inspection did not fail with the exact hash code.`n$tamperedTrustOutput"
    }
    Write-Output $tamperedTrustOutput

    $tamperedLoadLines = @(& $cli module schemas project $moduleTrustTamperRoot 2>&1)
    $tamperedLoadExit = $LASTEXITCODE
    $tamperedLoadOutput = $tamperedLoadLines -join "`n"
    if ($tamperedLoadExit -eq 0 -or
        -not $tamperedLoadOutput.Contains('REKALL_MODULE_OUTPUT_HASH_MISMATCH', [StringComparison]::Ordinal)) {
        throw "Installed tampered module load did not fail with the exact hash code.`n$tamperedLoadOutput"
    }
    Write-Output $tamperedLoadOutput

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

    Invoke-Rekall project create $audioRoot 'Installed Runtime Subsystems Proof' 'audio,ui'
    Invoke-Rekall scene create $audioRoot Main 'audio,ui'
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
    $scene.capabilities = @('audio', 'ui')
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
        },
        @{
            id = 'installed-ui-canvas'; name = 'Installed HUD'; tags = @('ui'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.UiCanvas'; properties = @{ ReferenceWidth = 200; ReferenceHeight = 100 } }
            )
        },
        @{
            id = 'installed-ui-secondary'; name = 'Secondary Action'; tags = @('ui'); parentId = 'installed-ui-panel'; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Button'; properties = @{ Width = 60; Height = 25; LayoutOrder = 20; HorizontalAlignment = 'end'; BackgroundColor = '#2080e0'; Interactive = $true; NavigationOrder = 20 } }
            )
        },
        @{
            id = 'installed-ui-panel'; name = 'Actions'; tags = @('ui'); parentId = 'installed-ui-canvas'; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Panel'; properties = @{ X = 10; Y = 10; Width = 180; Height = 80; LayoutDirection = 'vertical'; PaddingLeft = 10; PaddingTop = 5; PaddingRight = 10; PaddingBottom = 5; Gap = 4; BackgroundColor = '#402060' } }
            )
        },
        @{
            id = 'installed-ui-primary'; name = 'Primary Action'; tags = @('ui'); parentId = 'installed-ui-panel'; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Button'; properties = @{ Width = 50; Height = 20; LayoutOrder = 10; HorizontalAlignment = 'center'; BackgroundColor = '#20c060'; Interactive = $true; NavigationOrder = 10 } }
            )
        }
    )
    $scene | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $scenePath -Encoding utf8
    Invoke-Rekall runtime inspect $audioRoot Main 30
    $soakOutput = Invoke-RekallOutput runtime soak $audioRoot Main 600 120 30 67108864 0 32 128
    foreach ($expectedCheck in @(
        'Check complete-execution: PASS',
        'Check frame-continuity: PASS',
        'Check elapsed-continuity: PASS',
        'Check stable-systems: PASS',
        'Check throughput: PASS',
        'Check retained-managed-memory: PASS',
        'Check entity-growth: PASS',
        'Check checkpoint-observations: PASS',
        'Check checkpoint-events: PASS'
    )) {
        if (-not $soakOutput.Contains($expectedCheck, [StringComparison]::Ordinal)) {
            throw "Installed runtime soak did not contain '$expectedCheck'.`n$soakOutput"
        }
    }
    Write-Output $soakOutput

    $uiCaptureDirectory = Join-Path $audioRoot 'Builds\InstalledUiProof'
    Invoke-Rekall render viewport capture $audioRoot Main 1 $uiCaptureDirectory 200 100 software
    $uiProofFrame = Join-Path $uiCaptureDirectory 'Main_runtime_001.png'
    if (-not (Test-Path -LiteralPath $uiProofFrame -PathType Leaf) -or (Get-Item -LiteralPath $uiProofFrame).Length -le 100) {
        throw "Installed runtime UI proof frame is missing or blank at '$uiProofFrame'."
    }

    $env:SDL_AUDIODRIVER = 'dummy'
    $env:REKALL_AGE_DIAGNOSTICS_DIR = $diagnosticsRoot
    $audioProcess = Start-Process -FilePath $windowsPlayer -ArgumentList @($audioRoot, 'Main', '--frames', '10', '--audio-required') -PassThru -WindowStyle Hidden
    $audioProcess.WaitForExit()
    if ($audioProcess.ExitCode -ne 0) {
        throw "Installed Windows player audio device proof failed ($($audioProcess.ExitCode))."
    }

    $recoveredOutput = Invoke-WindowsPlayerProof 'recovered' @(
        $audioRoot, 'Main', '--frames', '5', '--no-vsync', '--simulate-device-loss-frame', '2') 0
    if (-not $recoveredOutput.Contains('REKALL_PLAYER_GRAPHICS_RECOVERED', [StringComparison]::Ordinal) -or
        -not $recoveredOutput.Contains('Outcome: recovered', [StringComparison]::Ordinal) -or
        -not $recoveredOutput.Contains('Recovery mode: cold-session-restart', [StringComparison]::Ordinal) -or
        -not $recoveredOutput.Contains('Attempts: 2', [StringComparison]::Ordinal) -or
        -not $recoveredOutput.Contains('Frames: 5/5', [StringComparison]::Ordinal)) {
        throw "Installed Windows player recovery proof was incomplete.`n$recoveredOutput"
    }
    Write-Output $recoveredOutput

    $fatalOutput = Invoke-WindowsPlayerProof 'fatal' @(
        $audioRoot, 'Main', '--frames', '5', '--no-vsync', '--simulate-fatal-frame', '2') 10
    if (-not $fatalOutput.Contains('REKALL_PLAYER_RUNTIME_FATAL', [StringComparison]::Ordinal) -or
        -not $fatalOutput.Contains('Outcome: fatal', [StringComparison]::Ordinal) -or
        -not $fatalOutput.Contains('Attempts: 1', [StringComparison]::Ordinal)) {
        throw "Installed Windows player fatal proof was incomplete.`n$fatalOutput"
    }
    Write-Output $fatalOutput

    $exhaustedOutput = Invoke-WindowsPlayerProof 'exhausted' @(
        $audioRoot, 'Main', '--frames', '5', '--no-vsync', '--simulate-device-loss-always-frame', '2') 11
    if (-not $exhaustedOutput.Contains('REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED', [StringComparison]::Ordinal) -or
        -not $exhaustedOutput.Contains('Outcome: exhausted', [StringComparison]::Ordinal) -or
        -not $exhaustedOutput.Contains('Attempts: 3', [StringComparison]::Ordinal) -or
        -not $exhaustedOutput.Contains('Frames: 3/5', [StringComparison]::Ordinal)) {
        throw "Installed Windows player exhaustion proof was incomplete.`n$exhaustedOutput"
    }
    Write-Output $exhaustedOutput

    $reportFiles = @(Get-ChildItem -LiteralPath $diagnosticsRoot -Filter 'failure-*.json' -File)
    if ($reportFiles.Count -ne 3) {
        throw "Installed recovery acceptance expected exactly three JSON reports, found $($reportFiles.Count)."
    }
    $reportCodes = @($reportFiles | ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).code })
    foreach ($expectedCode in @(
        'REKALL_PLAYER_GRAPHICS_RECOVERED',
        'REKALL_PLAYER_RUNTIME_FATAL',
        'REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED')) {
        if ($expectedCode -notin $reportCodes) {
            throw "Installed recovery reports did not contain '$expectedCode'."
        }
    }

    $diagnosticsOutput = Invoke-RekallOutput diagnostics failures $diagnosticsRoot
    foreach ($expectedCode in $reportCodes) {
        if (-not $diagnosticsOutput.Contains($expectedCode, [StringComparison]::Ordinal)) {
            throw "Installed CLI diagnostics inspection did not contain '$expectedCode'.`n$diagnosticsOutput"
        }
    }
    Write-Output $diagnosticsOutput

    $succeeded = $true
    Write-Output "Installed distribution acceptance passed: $distribution"
}
finally {
    $env:SDL_AUDIODRIVER = $previousSdlAudioDriver
    $env:REKALL_AGE_DIAGNOSTICS_DIR = $previousDiagnosticsRoot
    if ($succeeded) {
        foreach ($path in @($proofRoot, $moduleTrustTamperRoot, $gauntletRoot, $relocationRoot, $audioRoot, $diagnosticsRoot, $compatibilityRoot)) {
            $resolved = [IO.Path]::GetFullPath($path)
            if ($resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $resolved)) {
                Remove-Item -LiteralPath $resolved -Recurse -Force
            }
        }
    }
    else {
        Write-Error "Installed distribution acceptance failed. Evidence preserved at '$proofRoot', '$moduleTrustTamperRoot', '$gauntletRoot', '$relocationRoot', '$audioRoot', '$diagnosticsRoot', and '$compatibilityRoot'."
    }
}
