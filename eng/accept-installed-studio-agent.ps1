[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DistributionRoot,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$distribution = [IO.Path]::GetFullPath($DistributionRoot)
$studio = Join-Path $distribution 'tools\studio\Rekall.Age.Studio.exe'
if (-not (Test-Path -LiteralPath $studio -PathType Leaf)) {
    throw "Distributed Studio was not found at '$studio'."
}

$root = [IO.Path]::GetFullPath($EvidenceRoot)
$projectRoot = Join-Path $root 'Project'
$evidencePath = Join-Path $root 'studio-agent-evidence.json'
$readyPath = Join-Path $root 'ollama-ready.txt'
$serverOutput = Join-Path $root 'ollama-server.out.txt'
$serverError = Join-Path $root 'ollama-server.err.txt'
New-Item -ItemType Directory -Path $root -Force | Out-Null

$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
$probe.Stop()

$serverScript = Join-Path $PSScriptRoot 'serve-studio-ollama-fixture.ps1'
$server = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru `
    -ArgumentList @('-NoProfile', '-File', $serverScript, '-Port', "$port", '-ProjectRoot', $projectRoot, '-ReadyPath', $readyPath) `
    -RedirectStandardOutput $serverOutput -RedirectStandardError $serverError
$previousOllamaUrl = $env:REKALL_AGE_OLLAMA_URL
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
        if ($server.HasExited) {
            throw "Studio Ollama fixture exited before readiness.`n$(Get-Content -LiteralPath $serverError -Raw -ErrorAction SilentlyContinue)"
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Studio Ollama fixture did not become ready within 10 seconds.' }
        Start-Sleep -Milliseconds 100
        $server.Refresh()
    }

    $env:REKALL_AGE_OLLAMA_URL = "http://127.0.0.1:$port"
    $studioStart = [Diagnostics.ProcessStartInfo]::new($studio)
    $studioStart.UseShellExecute = $false
    $studioStart.CreateNoWindow = $true
    foreach ($argument in @(
        '--studio-agent-automation',
        '--project', $projectRoot,
        '--project-name', 'Installed Studio Agent Proof',
        '--scene', 'Main',
        '--model', 'rekall-acceptance',
        '--task', 'Use the generic authoring gauntlet to create and prove a complete playable game.',
        '--evidence', $evidencePath)) {
        $studioStart.ArgumentList.Add($argument)
    }
    $studioProcess = [Diagnostics.Process]::Start($studioStart)
    $studioProcess.WaitForExit()
    $studioExitCode = $studioProcess.ExitCode
    $studioProcess.Dispose()
    if ($studioExitCode -ne 0) {
        throw "Installed Studio agent automation failed ($studioExitCode)."
    }
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "Installed Studio did not write evidence at '$evidencePath'."
    }

    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if (-not $evidence.Succeeded -or -not $evidence.NonblankViewport) {
        throw "Installed Studio evidence did not report a successful nonblank authoring run.`n$($evidence | ConvertTo-Json -Depth 8)"
    }
    if (-not (Test-Path -LiteralPath $evidence.PackageArchivePath -PathType Leaf)) {
        throw "Installed Studio evidence package is missing at '$($evidence.PackageArchivePath)'."
    }
    $archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $evidence.PackageArchivePath).Hash
    $toolCalls = @($evidence.AgentTranscript | Where-Object { $_ -like '*tool.completed*' }).Count
    Write-Output "Installed Studio agent acceptance passed: toolCalls=$toolCalls nonblank=$($evidence.NonblankViewport) archiveSha256=$archiveHash"
}
finally {
    $env:REKALL_AGE_OLLAMA_URL = $previousOllamaUrl
    $server.Refresh()
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
        $server.WaitForExit()
    }
    $server.Dispose()
}
