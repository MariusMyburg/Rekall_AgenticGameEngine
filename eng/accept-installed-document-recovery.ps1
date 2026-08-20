[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DistributionRoot,
    [Parameter(Mandatory = $true)][string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$distribution = [IO.Path]::GetFullPath($DistributionRoot)
$project = [IO.Path]::GetFullPath($ProjectRoot)
$cli = Join-Path $distribution 'tools\cli\Rekall.Age.Cli.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "Distributed CLI was not found at '$cli'." }

function Invoke-Rekall {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $lines = @(& $cli @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Distributed command failed ($LASTEXITCODE): $($Arguments -join ' ')`n$($lines -join "`n")" }
    return $lines -join "`n"
}

function Invoke-RekallFailure {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $lines = @(& $cli @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 1) { throw "Distributed negative command exited $exitCode, expected 1: $($Arguments -join ' ')`n$($lines -join "`n")" }
    return $lines -join "`n"
}

Invoke-Rekall project create $project 'Installed Recovery Proof' 'world' | Out-Null
Invoke-Rekall scene create $project Main 'world' | Out-Null
Invoke-Rekall entity create $project Main KnownGood proof | Out-Null
Invoke-Rekall entity create $project Main Replacement proof | Out-Null

$scenePath = Join-Path $project 'Scenes\Main.age.scene.json'
$damage = '{ installed damage'
[IO.File]::WriteAllText($scenePath, $damage, [Text.UTF8Encoding]::new($false))

$normalFailure = Invoke-RekallFailure context scene $project Main
if (-not $normalFailure.Contains('REKALL_DOCUMENT_JSON_MALFORMED', [StringComparison]::Ordinal)) {
    throw "Installed ordinary read did not fail with the exact malformed-document code.`n$normalFailure"
}

$inspection = Invoke-Rekall recovery inspect scene $project Main
if (-not $inspection.Contains('REKALL_DOCUMENT_JSON_MALFORMED', [StringComparison]::Ordinal) -or
    -not $inspection.Contains('REKALL_DOCUMENT_RECOVERY_VALID', [StringComparison]::Ordinal) -or
    -not $inspection.Contains('recoverable: True', [StringComparison]::Ordinal)) {
    throw "Installed recovery inspection did not expose complete recovery facts.`n$inspection"
}
$revisionMatch = [regex]::Match($inspection, 'Primary: [A-Z_]+ revision=([0-9a-f]{64})')
if (-not $revisionMatch.Success) { throw "Installed recovery inspection omitted the primary revision.`n$inspection" }
$damagedRevision = $revisionMatch.Groups[1].Value

$staleRestore = Invoke-RekallFailure recovery restore scene $project Main ('0' * 64)
if (-not $staleRestore.Contains('REKALL_DOCUMENT_REVISION_CONFLICT', [StringComparison]::Ordinal)) {
    throw "Installed stale restore did not fail with the exact conflict code.`n$staleRestore"
}
if ([IO.File]::ReadAllText($scenePath) -ne $damage) { throw 'A stale installed restore changed the damaged primary.' }

$restore = Invoke-Rekall recovery restore scene $project Main $damagedRevision
$restoredMatch = [regex]::Match($restore, 'Restored revision: ([0-9a-f]{64})')
if (-not $restoredMatch.Success) { throw "Installed restore omitted its restored revision.`n$restore" }
$restoredRevision = $restoredMatch.Groups[1].Value

$summary = Invoke-Rekall context scene $project Main
if (-not $summary.Contains('KnownGood', [StringComparison]::Ordinal) -or
    $summary.Contains('Replacement', [StringComparison]::Ordinal)) {
    throw "Installed restore did not recover exactly the retained previous scene.`n$summary"
}
$validation = Invoke-Rekall validation scene $project Main
if (-not $validation.Contains('Issues: 0 (blocking 0, warnings 0)', [StringComparison]::Ordinal)) {
    throw "Installed restored scene did not pass ordinary validation.`n$validation"
}
Invoke-Rekall entity create $project Main AfterRecovery proof | Out-Null
$mutated = Invoke-Rekall context scene $project Main
if (-not $mutated.Contains('KnownGood', [StringComparison]::Ordinal) -or
    -not $mutated.Contains('AfterRecovery', [StringComparison]::Ordinal) -or
    $mutated.Contains('Replacement', [StringComparison]::Ordinal)) {
    throw "Installed restored scene did not accept the ordinary post-restore mutation.`n$mutated"
}

$quarantineDirectory = Join-Path $project '.rekall\recovery\quarantine\Scenes'
$quarantines = @(Get-ChildItem -LiteralPath $quarantineDirectory -Filter '*.corrupt' -File)
if ($quarantines.Count -ne 1 -or -not $quarantines[0].Name.Contains($damagedRevision, [StringComparison]::Ordinal)) {
    throw "Installed recovery expected one revision-addressed quarantine, found $($quarantines.Count)."
}
if ([IO.File]::ReadAllText($quarantines[0].FullName) -ne $damage) {
    throw 'Installed recovery quarantine did not retain the exact damaged bytes.'
}
$controls = @(Get-ChildItem -LiteralPath $project -Recurse -File | Where-Object {
    $_.Name -like '.*.tmp-*' -or $_.Name -like '.*.lock'
})
if ($controls.Count -ne 0) { throw "Recovery acceptance left control files: $($controls.FullName -join ', ')" }

Write-Output "Installed document recovery acceptance passed: damaged=$damagedRevision restored=$restoredRevision quarantine=1 validationIssues=0 postRestoreMutation=True controlFiles=0"
