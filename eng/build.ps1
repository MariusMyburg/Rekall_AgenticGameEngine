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
$testResultRoot = Join-Path $repoRoot 'Artifacts\TestResults'
$stagingRoot = Join-Path $artifactRoot 'Staging'
$outputRoot = Join-Path $artifactRoot "Rekall-AGE-0.1.0-preview.1-$RuntimeIdentifier"
$systemTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testTempRoot = [IO.Path]::GetFullPath((Join-Path $systemTempRoot ("rekall-age-build-" + [Guid]::NewGuid().ToString('N'))))
$priorTestTempRoot = [Environment]::GetEnvironmentVariable('REKALL_AGE_TEST_TEMP_ROOT', 'Process')

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
    if (-not $testTempRoot.StartsWith($systemTempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($testTempRoot).StartsWith('rekall-age-build-', [StringComparison]::Ordinal)) {
        throw "Refusing unsafe test temp root '$testTempRoot'."
    }
    New-Item -ItemType Directory -Path $testTempRoot -Force | Out-Null
    [Environment]::SetEnvironmentVariable('REKALL_AGE_TEST_TEMP_ROOT', $testTempRoot, 'Process')
    Invoke-Checked dotnet @('restore', $solution, '--locked-mode', '-r', $RuntimeIdentifier, '/nr:false')
    Invoke-Checked dotnet @('build', $solution, '-c', $Configuration, '--no-restore', '/nr:false')
    if (Test-Path -LiteralPath $testResultRoot) {
        Remove-Item -LiteralPath $testResultRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $testResultRoot -Force | Out-Null
    $testProjects = @{
        engine = 'tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj'
        studio = 'tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj'
    }
    1..2 | ForEach-Object {
        $pass = $_
        Write-Output "Release test pass $pass of 2"
        foreach ($testName in @('engine', 'studio')) {
            Invoke-Checked dotnet @(
                'test', $testProjects[$testName], '-c', $Configuration, '--no-build', '--no-restore', '--verbosity', 'minimal',
                '--logger', "trx;LogFileName=release-pass-$pass-$testName.trx", '--results-directory', $testResultRoot,
                '/nr:false')
        }
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
            '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=false', '-o', $destination, '/nr:false')
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
    [Environment]::SetEnvironmentVariable('REKALL_AGE_TEST_TEMP_ROOT', $priorTestTempRoot, 'Process')
    if ([IO.Directory]::Exists($testTempRoot)) {
        try {
            [IO.Directory]::Delete($testTempRoot, $true)
        }
        catch {
            Write-Warning "Could not completely remove run-scoped test temp root '$testTempRoot': $($_.Exception.Message)"
        }
    }
    Pop-Location
}
