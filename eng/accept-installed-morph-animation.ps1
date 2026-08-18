[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DistributionRoot,
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$distribution = [IO.Path]::GetFullPath($DistributionRoot)
$cli = Join-Path $distribution 'tools\cli\Rekall.Age.Cli.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "Distributed CLI was not found at '$cli'." }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot ('Artifacts\InstalledMorphProof\' + [Guid]::NewGuid().ToString('N'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$projectRoot = Join-Path $tempRoot ('rekall-age-installed-morph-' + [Guid]::NewGuid().ToString('N'))
$succeeded = $false

function Invoke-Rekall {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $lines = @(& $cli @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Distributed command failed ($LASTEXITCODE): $($Arguments -join ' ')`n$($lines -join "`n")" }
    return $lines -join "`n"
}

try {
    Invoke-Rekall project create $projectRoot 'Installed Morph Animation Proof' 'world,rendering3d,animation'
    Invoke-Rekall scene create $projectRoot Main 'world,rendering3d,animation'
    $sourceDirectory = Join-Path $projectRoot 'Source'
    New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
    $sourcePath = Join-Path $sourceDirectory 'morph-triangle.glb'
    # Byte-for-byte output of GlbTestMeshFactory.CreateMorphTriangleGlb; keep the
    # installed proof independent from the source/test assemblies it validates.
    $fixtureBase64 = 'Z2xURgIAAAB8BwAAgAYAAEpTT057CiAgImFzc2V0IjogeyAidmVyc2lvbiI6ICIyLjAiIH0sCiAgImJ1ZmZlcnMiOiBbeyAiYnl0ZUxlbmd0aCI6IDIyMiB9XSwKICAiYnVmZmVyVmlld3MiOiBbCiAgICB7ICJidWZmZXIiOiAwLCAiYnl0ZU9mZnNldCI6IDAsICJieXRlTGVuZ3RoIjogMzYgfSwKICAgIHsgImJ1ZmZlciI6IDAsICJieXRlT2Zmc2V0IjogMzYsICJieXRlTGVuZ3RoIjogMzYgfSwKICAgIHsgImJ1ZmZlciI6IDAsICJieXRlT2Zmc2V0IjogNzIsICJieXRlTGVuZ3RoIjogMzYgfSwKICAgIHsgImJ1ZmZlciI6IDAsICJieXRlT2Zmc2V0IjogMTA4LCAiYnl0ZUxlbmd0aCI6IDM2IH0sCiAgICB7ICJidWZmZXIiOiAwLCAiYnl0ZU9mZnNldCI6IDE0NCwgImJ5dGVMZW5ndGgiOiAzNiB9LAogICAgeyAiYnVmZmVyIjogMCwgImJ5dGVPZmZzZXQiOiAxODAsICJieXRlTGVuZ3RoIjogMzYgfSwKICAgIHsgImJ1ZmZlciI6IDAsICJieXRlT2Zmc2V0IjogMjE2LCAiYnl0ZUxlbmd0aCI6IDYgfQogIF0sCiAgImFjY2Vzc29ycyI6IFsKICAgIHsgImJ1ZmZlclZpZXciOiAwLCAiY29tcG9uZW50VHlwZSI6IDUxMjYsICJjb3VudCI6IDMsICJ0eXBlIjogIlZFQzMiIH0sCiAgICB7ICJidWZmZXJWaWV3IjogMSwgImNvbXBvbmVudFR5cGUiOiA1MTI2LCAiY291bnQiOiAzLCAidHlwZSI6ICJWRUMzIiB9LAogICAgeyAiYnVmZmVyVmlldyI6IDIsICJjb21wb25lbnRUeXBlIjogNTEyNiwgImNvdW50IjogMywgInR5cGUiOiAiVkVDMyIgfSwKICAgIHsgImJ1ZmZlclZpZXciOiAzLCAiY29tcG9uZW50VHlwZSI6IDUxMjYsICJjb3VudCI6IDMsICJ0eXBlIjogIlZFQzMiIH0sCiAgICB7ICJidWZmZXJWaWV3IjogNCwgImNvbXBvbmVudFR5cGUiOiA1MTI2LCAiY291bnQiOiAzLCAidHlwZSI6ICJWRUMzIiB9LAogICAgeyAiYnVmZmVyVmlldyI6IDUsICJjb21wb25lbnRUeXBlIjogNTEyNiwgImNvdW50IjogMywgInR5cGUiOiAiVkVDMyIgfSwKICAgIHsgImJ1ZmZlclZpZXciOiA2LCAiY29tcG9uZW50VHlwZSI6IDUxMjMsICJjb3VudCI6IDMsICJ0eXBlIjogIlNDQUxBUiIgfQogIF0sCiAgIm1lc2hlcyI6IFt7CiAgICAibmFtZSI6ICJNb3JwaCBUcmlhbmdsZSIsCiAgICAid2VpZ2h0cyI6IFswLjI1LCAtMC41XSwKICAgICJleHRyYXMiOiB7ICJ0YXJnZXROYW1lcyI6IFsid2lkZSIsICJyYWlzZWQiXSB9LAogICAgInByaW1pdGl2ZXMiOiBbewogICAgICAiYXR0cmlidXRlcyI6IHsgIlBPU0lUSU9OIjogMCwgIk5PUk1BTCI6IDEgfSwKICAgICAgInRhcmdldHMiOiBbCiAgICAgICAgeyAiUE9TSVRJT04iOiAyLCAiTk9STUFMIjogMyB9LAogICAgICAgIHsgIlBPU0lUSU9OIjogNCwgIk5PUk1BTCI6IDUgfQogICAgICBdLAogICAgICAiaW5kaWNlcyI6IDYsCiAgICAgICJtb2RlIjogNAogICAgfV0KICB9XSwKICAibm9kZXMiOiBbewogICAgIm5hbWUiOiAiTW9ycGggTm9kZSIsCiAgICAibWVzaCI6IDAsCiAgICAid2VpZ2h0cyI6IFswLjUsIDAuNzVdLAogICAgInRyYW5zbGF0aW9uIjogWzEwLCAyMCwgMzBdLAogICAgInJvdGF0aW9uIjogWzAsIDAsIDAuNzA3MTA2NzgxMTg2NTQ3NiwgMC43MDcxMDY3ODExODY1NDc2XSwKICAgICJzY2FsZSI6IFsyLCAyLCAyXQogIH1dLAogICJzY2VuZXMiOiBbeyAibm9kZXMiOiBbMF0gfV0sCiAgInNjZW5lIjogMAp9IOAAAABCSU4AAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAAABAAIAAAA='
    $fixtureBytes = [Convert]::FromBase64String($fixtureBase64)
    if ($fixtureBytes.Length -lt 12 -or [BitConverter]::ToUInt32($fixtureBytes, 8) -ne $fixtureBytes.Length) {
        throw 'Embedded installed morph GLB fixture length is inconsistent.'
    }
    [IO.File]::WriteAllBytes($sourcePath, $fixtureBytes)

    Invoke-Rekall asset import-report $projectRoot $sourcePath model 'Installed Morph Triangle'
    $catalogPath = Join-Path $projectRoot 'Assets\assets.age.catalog.json'
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $asset = @($catalog.assets)[0]
    $meshMetadata = @($asset.glbMetadata.meshes)[0]
    if ($meshMetadata.morphTargetCount -ne 2 -or
        (@($meshMetadata.morphTargetNames) -join ',') -ne 'wide,raised' -or
        (@($meshMetadata.defaultMorphWeights) -join ',') -ne '0.25,-0.5') {
        throw "Installed morph import metadata was incomplete.`n$($catalog | ConvertTo-Json -Depth 12)"
    }

    $scenePath = Join-Path $projectRoot 'Scenes\Main.age.scene.json'
    $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
    $scene.entities = @(
        @{ id='morph-camera'; name='Morph Camera'; tags=@('camera'); parentId=$null; prefabSourceId=$null; visible=$true; locked=$false; components=@(
            @{type='Rekall.Transform3D';properties=@{x=9;y=22;z=24}}, @{type='Rekall.Camera3D';properties=@{active=$true;clearColor='#101828'}}) },
        @{ id='morph-actor'; name='Morph Actor'; tags=@('actor'); parentId=$null; prefabSourceId=$null; visible=$true; locked=$false; components=@(
            @{type='Rekall.Transform3D';properties=@{}}, @{type='Rekall.MeshRenderer';properties=@{mesh=$asset.id}},
            @{type='Rekall.MorphWeights';properties=@{weights=@(0,0)}},
            @{type='Rekall.AnimationClip';properties=@{version=1;durationSeconds=1;tracks=@(@{component='Rekall.MorphWeights';property='weights';interpolation='cubic';keys=@(
                @{time=0;value=@(0,0);inTangent=@(0,0);outTangent=@(2,0)}, @{time=1;value=@(1,0);inTangent=@(0,0);outTangent=@(0,0)})})}},
            @{type='Rekall.AnimationPlayer';properties=@{playing=$true;loopMode='clamp'}}) },
        @{ id='morph-light'; name='Morph Light'; tags=@('light'); parentId=$null; prefabSourceId=$null; visible=$true; locked=$false; components=@(
            @{type='Rekall.Transform3D';properties=@{pitch=-30;yaw=-35}}, @{type='Rekall.DirectionalLight';properties=@{intensity=1.4}}) }
    )
    [IO.File]::WriteAllText($scenePath, ($scene | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))

    $runtime = Invoke-Rekall runtime inspect $projectRoot Main 30
    if (-not $runtime.Contains('Morph Morph Actor: count=2 weights=[0.75,0]', [StringComparison]::Ordinal) -or
        $runtime.Contains('morph_weights_invalid', [StringComparison]::Ordinal)) { throw "Installed morph runtime inspection failed.`n$runtime" }
    $geometry = Invoke-Rekall render mesh inspect $projectRoot Main 30
    foreach ($expected in @('min=(8,21.5,30)', 'max=(10,23.5,30)', 'morphTargets=2 morphSource=authored')) {
        if (-not $geometry.Contains($expected, [StringComparison]::Ordinal)) { throw "Installed morph mesh inspection omitted '$expected'.`n$geometry" }
    }

    $oneDirectory = Join-Path $projectRoot 'Builds\FrameOne'
    $thirtyDirectory = Join-Path $projectRoot 'Builds\FrameThirty'
    $captureOne = Invoke-Rekall render viewport capture $projectRoot Main 1 $oneDirectory 320 180 vulkan
    $captureThirty = Invoke-Rekall render viewport capture $projectRoot Main 30 $thirtyDirectory 320 180 vulkan
    foreach ($capture in @($captureOne, $captureThirty)) {
        foreach ($expected in @('Backend: vulkan','Hardware accelerated: True','Frame analysis: informative=True','Fallback: 0','Missing assets: 0','Unsupported assets: 0','Observations: 0')) {
            if (-not $capture.Contains($expected, [StringComparison]::Ordinal)) { throw "Installed morph Vulkan capture omitted '$expected'.`n$capture" }
        }
    }
    $frameOne = Get-ChildItem -LiteralPath $oneDirectory -Filter '*.png' -File | Select-Object -First 1 -ExpandProperty FullName
    $frameThirty = Get-ChildItem -LiteralPath $thirtyDirectory -Filter '*.png' -File | Select-Object -First 1 -ExpandProperty FullName
    $hashOne = (Get-FileHash -LiteralPath $frameOne -Algorithm SHA256).Hash
    $hashThirty = (Get-FileHash -LiteralPath $frameThirty -Algorithm SHA256).Hash
    if ($hashOne -eq $hashThirty) { throw 'Installed morph proof frames are identical.' }
    Copy-Item $frameOne (Join-Path $evidence 'frame-001.png')
    Copy-Item $frameThirty (Join-Path $evidence 'frame-030.png')
    [IO.File]::WriteAllText((Join-Path $evidence 'evidence.json'), ([ordered]@{
        schemaVersion=1; assetId=$asset.id; targets=@('wide','raised'); frame30Weights=@(0.75,0);
        frame30Minimum=@(8,21.5,30); frame30Maximum=@(10,23.5,30); backend='vulkan'; hardwareAccelerated=$true;
        frameOneSha256=$hashOne.ToLowerInvariant(); frameThirtySha256=$hashThirty.ToLowerInvariant(); visiblyDifferent=$true
    } | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $succeeded = $true
    Write-Output "Installed morph animation acceptance passed: $evidence"
}
finally {
    if ($succeeded) {
        $resolved = [IO.Path]::GetFullPath($projectRoot)
        if ($resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path $resolved)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    } else { Write-Warning "Installed morph acceptance failed; project preserved at '$projectRoot'." }
}
