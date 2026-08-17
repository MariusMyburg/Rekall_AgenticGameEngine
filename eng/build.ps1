[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repoRoot 'Rekall.AGE.sln'
$artifactRoot = Join-Path $repoRoot 'Artifacts\Distribution'
$stagingRoot = Join-Path $artifactRoot 'Staging'
$outputRoot = Join-Path $artifactRoot "Rekall-AGE-0.1.0-preview.1-$RuntimeIdentifier"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$ArgumentList)
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($ArgumentList -join ' ')"
    }
}

function Reset-Directory {
    param([string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $allowedRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\', '/')
    if (-not $fullPath.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset path outside the distribution artifact root: '$fullPath'."
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

Push-Location $repoRoot
try {
    Invoke-Checked dotnet @('restore', $solution, '--locked-mode', '-r', $RuntimeIdentifier)
    Invoke-Checked dotnet @('build', $solution, '-c', $Configuration, '--no-restore')
    1..2 | ForEach-Object {
        Write-Output "Release test pass $_ of 2"
        Invoke-Checked dotnet @('test', $solution, '-c', $Configuration, '--no-build', '--no-restore', '--verbosity', 'minimal')
    }

    Reset-Directory $stagingRoot
    $publishes = @{
        cli = @('src\Rekall.Age.Cli\Rekall.Age.Cli.csproj', (Join-Path $stagingRoot 'cli'))
        studio = @('src\Rekall.Age.Studio\Rekall.Age.Studio.csproj', (Join-Path $stagingRoot 'studio'))
        headless = @('src\Rekall.Age.Player\Rekall.Age.Player.csproj', (Join-Path $stagingRoot 'headless'))
        windows = @('src\Rekall.Age.Player.Windows\Rekall.Age.Player.Windows.csproj', (Join-Path $stagingRoot 'windows'))
    }
    foreach ($name in @('cli', 'studio', 'headless', 'windows')) {
        $project, $destination = $publishes[$name]
        Invoke-Checked dotnet @(
            'publish', $project, '-c', $Configuration, '-r', $RuntimeIdentifier,
            '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=false', '-o', $destination)
    }

    $cli = Join-Path $stagingRoot 'cli\Rekall.Age.Cli.exe'
    $sdkSeed = Join-Path $stagingRoot 'sdk-seed'
    Invoke-Checked $cli @('module', 'scaffold', $sdkSeed, 'sdk.seed', 'SDK Seed', 'SdkSeed', 'SdkComponent')
    $sdk = Join-Path $sdkSeed '.rekall\sdk\1'
    Invoke-Checked $cli @(
        'distribution', 'assemble', $outputRoot,
        (Join-Path $stagingRoot 'cli'),
        (Join-Path $stagingRoot 'studio'),
        (Join-Path $stagingRoot 'headless'),
        (Join-Path $stagingRoot 'windows'),
        $sdk,
        (Join-Path $repoRoot 'README.md'),
        (Join-Path $repoRoot 'PROPRIETARY-NOTICE.md'),
        (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt'))

    & (Join-Path $PSScriptRoot 'accept-distribution.ps1') -DistributionRoot $outputRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Installed distribution acceptance failed ($LASTEXITCODE)."
    }

    Write-Output "Distribution: $outputRoot"
    Write-Output "Archive: $outputRoot.zip"
}
finally {
    Pop-Location
}
