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

Invoke-Rekall project create $project 'Installed Atomic JSON Proof' 'world'
Invoke-Rekall scene create $project Main 'world'
$documents = @(
    (Join-Path $project 'rekall.project.json'),
    (Join-Path $project 'Scenes\Main.age.scene.json'),
    (Join-Path $project 'Transactions\transactions.age.json')
)
$reader = Start-Job -ScriptBlock {
    param([string[]]$Paths)
    $parsed = 0
    $unavailable = 0
    $malformed = [Collections.Generic.List[string]]::new()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        foreach ($path in $Paths) {
            if (-not [IO.File]::Exists($path)) { $unavailable++; continue }
            try {
                $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                try {
                    $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false), $true, 4096, $false)
                    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
                } finally { $stream.Dispose() }
                $null = $text | ConvertFrom-Json -Depth 128
                $parsed++
            } catch [System.IO.IOException] {
                $unavailable++
            } catch [System.UnauthorizedAccessException] {
                $unavailable++
            } catch {
                $malformed.Add("$path :: $($_.Exception.Message)")
            }
        }
        Start-Sleep -Milliseconds 1
    }
    [pscustomobject]@{ Parsed=$parsed; Unavailable=$unavailable; Malformed=@($malformed) }
} -ArgumentList (,$documents)

try {
    0..19 | ForEach-Object { Invoke-Rekall capability add $project ("proof-capability-{0:D2}" -f $_) | Out-Null }
    0..39 | ForEach-Object { Invoke-Rekall entity create $project Main ("Proof Entity {0:D2}" -f $_) 'proof' | Out-Null }
    $readerResult = Receive-Job -Job $reader -Wait -AutoRemoveJob
    $reader = $null

    if ($readerResult.Parsed -lt 30) { throw "Independent JSON reader produced only $($readerResult.Parsed) successful parses." }
    if (@($readerResult.Malformed).Count -ne 0) {
        throw "Independent JSON reader observed malformed live documents:`n$(@($readerResult.Malformed) -join "`n")"
    }
    $temporary = @(Get-ChildItem -LiteralPath $project -Recurse -File -Filter '.*.tmp-*')
    if ($temporary.Count -ne 0) { throw "Atomic JSON proof left temporary files: $($temporary.FullName -join ', ')" }

    $validation = Invoke-Rekall scene validate $project Main
    if (-not $validation.Contains('passed validation', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed scene validation failed after atomic mutations.`n$validation"
    }
    $history = Invoke-Rekall transaction history $project 100
    if (-not $history.Contains('entity create', [StringComparison]::OrdinalIgnoreCase) -or
        -not $history.Contains('capability add', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed transaction history omitted mutation records.`n$history"
    }

    Write-Output "Installed atomic JSON acceptance passed: parsed=$($readerResult.Parsed) transientUnavailable=$($readerResult.Unavailable) malformed=0 tempFiles=0"
}
finally {
    if ($null -ne $reader) {
        Stop-Job -Job $reader -ErrorAction SilentlyContinue
        Remove-Job -Job $reader -Force -ErrorAction SilentlyContinue
    }
}
