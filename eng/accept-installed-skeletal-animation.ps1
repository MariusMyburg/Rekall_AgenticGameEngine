[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DistributionRoot,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$distribution = [IO.Path]::GetFullPath($DistributionRoot)
$cli = Join-Path $distribution 'tools\cli\Rekall.Age.Cli.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Distributed CLI was not found at '$cli'."
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot ('Artifacts\InstalledSkeletalProof\' + [Guid]::NewGuid().ToString('N'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$projectRoot = Join-Path $tempRoot ('rekall-age-installed-skeletal-' + [Guid]::NewGuid().ToString('N'))
$succeeded = $false

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

try {
    Invoke-Rekall project create $projectRoot 'Installed Skeletal Animation Proof' 'world,rendering3d,animation'
    Invoke-Rekall scene create $projectRoot Main 'world,rendering3d,animation'

    $modelDirectory = Join-Path $projectRoot 'Assets\model'
    New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
    $modelPath = Join-Path $modelDirectory 'installed-skinned-triangle.glb'
    $fixtureBase64 = 'Z2xURgIAAACsBQAA0AQAAEpTT057ImFzc2V0Ijp7InZlcnNpb24iOiIyLjAifSwiYnVmZmVycyI6W3siYnl0ZUxlbmd0aCI6MTkyfV0sImJ1ZmZlclZpZXdzIjpbeyJidWZmZXIiOjAsImJ5dGVPZmZzZXQiOjAsImJ5dGVMZW5ndGgiOjY0fSx7ImJ1ZmZlciI6MCwiYnl0ZU9mZnNldCI6NjQsImJ5dGVMZW5ndGgiOjh9LHsiYnVmZmVyIjowLCJieXRlT2Zmc2V0Ijo3MiwiYnl0ZUxlbmd0aCI6MjR9LHsiYnVmZmVyIjowLCJieXRlT2Zmc2V0Ijo5NiwiYnl0ZUxlbmd0aCI6MzZ9LHsiYnVmZmVyIjowLCJieXRlT2Zmc2V0IjoxMzIsImJ5dGVMZW5ndGgiOjEyfSx7ImJ1ZmZlciI6MCwiYnl0ZU9mZnNldCI6MTQ0LCJieXRlTGVuZ3RoIjo0OH1dLCJhY2Nlc3NvcnMiOlt7ImJ1ZmZlclZpZXciOjAsImNvbXBvbmVudFR5cGUiOjUxMjYsImNvdW50IjoxLCJ0eXBlIjoiTUFUNCJ9LHsiYnVmZmVyVmlldyI6MSwiY29tcG9uZW50VHlwZSI6NTEyNiwiY291bnQiOjIsInR5cGUiOiJTQ0FMQVIiLCJtaW4iOlswXSwibWF4IjpbMV19LHsiYnVmZmVyVmlldyI6MiwiY29tcG9uZW50VHlwZSI6NTEyNiwiY291bnQiOjIsInR5cGUiOiJWRUMzIn0seyJidWZmZXJWaWV3IjozLCJjb21wb25lbnRUeXBlIjo1MTI2LCJjb3VudCI6MywidHlwZSI6IlZFQzMifSx7ImJ1ZmZlclZpZXciOjQsImNvbXBvbmVudFR5cGUiOjUxMjEsImNvdW50IjozLCJ0eXBlIjoiVkVDNCJ9LHsiYnVmZmVyVmlldyI6NSwiY29tcG9uZW50VHlwZSI6NTEyNiwiY291bnQiOjMsInR5cGUiOiJWRUM0In1dLCJub2RlcyI6W3sibmFtZSI6IlJvb3QiLCJjaGlsZHJlbiI6WzFdLCJtZXNoIjowLCJza2luIjowfSx7Im5hbWUiOiJKb2ludCIsInRyYW5zbGF0aW9uIjpbMCwwLDBdfV0sIm1lc2hlcyI6W3sibmFtZSI6IlNraW5uZWQgVHJpYW5nbGUiLCJwcmltaXRpdmVzIjpbeyJhdHRyaWJ1dGVzIjp7IlBPU0lUSU9OIjozLCJKT0lOVFNfMCI6NCwiV0VJR0hUU18wIjo1fX1dfV0sInNraW5zIjpbeyJuYW1lIjoiUmlnIiwiam9pbnRzIjpbMV0sInNrZWxldG9uIjowLCJpbnZlcnNlQmluZE1hdHJpY2VzIjowfV0sImFuaW1hdGlvbnMiOlt7Im5hbWUiOiJMaWZ0Iiwic2FtcGxlcnMiOlt7ImlucHV0IjoxLCJvdXRwdXQiOjIsImludGVycG9sYXRpb24iOiJMSU5FQVIifV0sImNoYW5uZWxzIjpbeyJzYW1wbGVyIjowLCJ0YXJnZXQiOnsibm9kZSI6MSwicGF0aCI6InRyYW5zbGF0aW9uIn19XX1dLCJzY2VuZXMiOlt7Im5vZGVzIjpbMF19XSwic2NlbmUiOjB9IMAAAABCSU4AAACAPwAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAA'
    [IO.File]::WriteAllBytes($modelPath, [Convert]::FromBase64String($fixtureBase64))

    $catalog = @{
        assets = @(@{
            id = 'installed-skinned-triangle'
            name = 'installed-skinned-triangle'
            displayName = 'Installed Skinned Triangle'
            kind = 'model'
            sourcePath = ''
            importedPath = $modelPath
            contentHash = 'installed-skeletal-proof'
        })
    }
    $catalogJson = $catalog | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText(
        (Join-Path $projectRoot 'Assets\assets.age.catalog.json'),
        $catalogJson,
        [Text.UTF8Encoding]::new($false))

    $scenePath = Join-Path $projectRoot 'Scenes\Main.age.scene.json'
    $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
    $scene.capabilities = @('world', 'rendering3d', 'animation')
    $scene.entities = @(
        @{
            id = 'installed-skeletal-camera'; name = 'Skeletal Camera'; tags = @('camera'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Transform3D'; properties = @{ x = 0; y = 0; z = -2 } },
                @{ type = 'Rekall.Camera3D'; properties = @{ active = $true; clearColor = '#101828' } }
            )
        },
        @{
            id = 'installed-skeletal-actor'; name = 'Rigged Actor'; tags = @('actor', 'animated'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Transform3D'; properties = @{ x = -1.25; y = -2.5; z = 2.5; scaleX = 2.5; scaleY = 2.5; scaleZ = 2.5 } },
                @{ type = 'Rekall.MeshRenderer'; properties = @{ mesh = 'installed-skinned-triangle' } },
                @{ type = 'Rekall.SkeletalAnimator'; properties = @{ model = 'installed-skinned-triangle'; animation = 'Lift'; skinIndex = 0; playing = $true; loopMode = 'clamp' } }
            )
        },
        @{
            id = 'installed-skeletal-light'; name = 'Skeletal Key Light'; tags = @('light'); parentId = $null; prefabSourceId = $null; visible = $true; locked = $false
            components = @(
                @{ type = 'Rekall.Transform3D'; properties = @{ pitch = -35; yaw = -45 } },
                @{ type = 'Rekall.DirectionalLight'; properties = @{ intensity = 1.2 } }
            )
        }
    )
    $sceneJson = $scene | ConvertTo-Json -Depth 16
    [IO.File]::WriteAllText($scenePath, $sceneJson, [Text.UTF8Encoding]::new($false))

    $inspection = Invoke-RekallOutput runtime inspect $projectRoot Main 30
    foreach ($expected in @('kind=SkeletalAnimator', 'animation=Lift', 'joints=1', 'time=0.500')) {
        if (-not $inspection.Contains($expected, [StringComparison]::Ordinal)) {
            throw "Installed skeletal runtime inspection did not contain '$expected'.`n$inspection"
        }
    }
    if ($inspection.Contains('runtime.animation.skeletal_', [StringComparison]::Ordinal)) {
        throw "Installed skeletal runtime inspection reported a structured skeletal-animation error.`n$inspection"
    }

    $frameOneDirectory = Join-Path $projectRoot 'Builds\FrameOne'
    $frameThirtyDirectory = Join-Path $projectRoot 'Builds\FrameThirty'
    $captureOne = Invoke-RekallOutput render viewport capture $projectRoot Main 1 $frameOneDirectory 320 180 vulkan
    $captureThirty = Invoke-RekallOutput render viewport capture $projectRoot Main 30 $frameThirtyDirectory 320 180 vulkan
    foreach ($capture in @($captureOne, $captureThirty)) {
        foreach ($expected in @('Backend: vulkan', 'Hardware accelerated: True', 'Frame analysis: informative=True', 'Fallback: 0', 'Missing assets: 0', 'Unsupported assets: 0', 'Observations: 0')) {
            if (-not $capture.Contains($expected, [StringComparison]::Ordinal)) {
                throw "Installed skeletal Vulkan capture did not contain '$expected'.`n$capture"
            }
        }
    }

    $frameOne = Get-ChildItem -LiteralPath $frameOneDirectory -Filter '*.png' -File | Select-Object -First 1 -ExpandProperty FullName
    $frameThirty = Get-ChildItem -LiteralPath $frameThirtyDirectory -Filter '*.png' -File | Select-Object -First 1 -ExpandProperty FullName
    foreach ($path in @($frameOne, $frameThirty)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 100) {
            throw "Installed skeletal proof frame is missing or blank at '$path'."
        }
    }
    $hashOne = (Get-FileHash -LiteralPath $frameOne -Algorithm SHA256).Hash
    $hashThirty = (Get-FileHash -LiteralPath $frameThirty -Algorithm SHA256).Hash
    if ($hashOne -eq $hashThirty) {
        throw 'Installed skeletal proof frames are identical; no visible animation was demonstrated.'
    }

    Copy-Item -LiteralPath $frameOne -Destination (Join-Path $evidence 'frame-001.png')
    Copy-Item -LiteralPath $frameThirty -Destination (Join-Path $evidence 'frame-030.png')
    $summary = [ordered]@{
        schemaVersion = 1
        distribution = $distribution
        model = 'installed-skinned-triangle'
        animation = 'Lift'
        skin = 'Rig'
        jointCount = 1
        frameOneSha256 = $hashOne.ToLowerInvariant()
        frameThirtySha256 = $hashThirty.ToLowerInvariant()
        visiblyDifferent = $true
        backend = 'vulkan'
        hardwareAccelerated = $true
        inspection = $inspection
        frameOneCapture = $captureOne
        frameThirtyCapture = $captureThirty
    }
    [IO.File]::WriteAllText(
        (Join-Path $evidence 'evidence.json'),
        ($summary | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    $succeeded = $true
    Write-Output "Installed skeletal animation acceptance passed: $evidence"
}
finally {
    if ($succeeded) {
        $resolved = [IO.Path]::GetFullPath($projectRoot)
        if ($resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolved)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    else {
        Write-Warning "Installed skeletal animation acceptance failed. Project preserved at '$projectRoot'; evidence root is '$evidence'."
    }
}
