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

Invoke-Rekall project create $project 'Installed Revision Proof' 'world' | Out-Null
Invoke-Rekall scene create $project Main 'world' | Out-Null
$initial = Invoke-Rekall context scene $project Main
$initialMatch = [regex]::Match($initial, 'Revision: ([0-9a-f]{64})')
if (-not $initialMatch.Success) { throw "Installed scene summary omitted its revision.`n$initial" }
$staleRevision = $initialMatch.Groups[1].Value

Invoke-Rekall entity create $project Main Intervening proof | Out-Null
$staleLines = @(& $cli entity create $project Main Stale proof $staleRevision 2>&1)
$staleExit = $LASTEXITCODE
$staleOutput = $staleLines -join "`n"
if ($staleExit -eq 0 -or -not $staleOutput.Contains('REKALL_DOCUMENT_REVISION_CONFLICT', [StringComparison]::Ordinal)) {
    throw "Installed stale mutation did not fail with the exact revision conflict code.`n$staleOutput"
}

$refreshed = Invoke-Rekall context scene $project Main
$refreshedMatch = [regex]::Match($refreshed, 'Revision: ([0-9a-f]{64})')
if (-not $refreshedMatch.Success -or $refreshedMatch.Groups[1].Value -eq $staleRevision) {
    throw "Installed scene revision did not change after the intervening mutation.`n$refreshed"
}
$currentRevision = $refreshedMatch.Groups[1].Value
Invoke-Rekall entity create $project Main Retried proof $currentRevision | Out-Null

$final = Invoke-Rekall context scene $project Main
if (-not $final.Contains('Intervening', [StringComparison]::Ordinal) -or
    -not $final.Contains('Retried', [StringComparison]::Ordinal) -or
    $final.Contains('- Stale:', [StringComparison]::Ordinal)) {
    throw "Installed conflict recovery did not preserve exactly the valid semantic edits.`n$final"
}
$history = Invoke-Rekall transaction history $project 100
if (-not $history.Contains('Intervening', [StringComparison]::Ordinal) -or
    -not $history.Contains('Retried', [StringComparison]::Ordinal) -or
    $history.Contains(' Stale ', [StringComparison]::Ordinal)) {
    throw "Installed transaction history did not retain exactly the successful mutations.`n$history"
}
$controls = @(Get-ChildItem -LiteralPath $project -Recurse -File | Where-Object {
    $_.Name -like '.*.tmp-*' -or $_.Name -like '.*.lock'
})
if ($controls.Count -ne 0) { throw "Revision acceptance left control files: $($controls.FullName -join ', ')" }

Write-Output "Installed document revision acceptance passed: stale=$staleRevision current=$currentRevision conflict=REKALL_DOCUMENT_REVISION_CONFLICT retained=2 controlFiles=0"
