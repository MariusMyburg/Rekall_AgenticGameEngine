[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Finalize', 'Stop', 'Server', 'SelfTest')]
    [string]$Phase = 'Prepare',
    [string]$OutputRoot,
    [string]$SessionPath,
    [string]$EvidencePath,
    [string]$BrowserLogPath,
    [string]$ScreenshotMetadataPath,
    [string]$ContentRoot,
    [int]$Port,
    [string]$ReadyPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$MaximumEvidenceBytes = 256KB
$MaximumLogBytes = 256KB
$MaximumScreenshotMetadataBytes = 64KB
$MaximumScreenshotBytes = 32MB
$MaximumLogEntries = 256
$WorkspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ExplicitTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$ExpectedBackend = 'WebGPU'
$ExpectedProtocolVersion = 1
$ExpectedWorkloadId = 'proof.webgpu.asset-independent'

function Resolve-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A non-empty path is required.' }
    return [IO.Path]::GetFullPath($Path)
}

function Test-PathWithin([string]$Path, [string]$Root) {
    $fullPath = Resolve-FullPath $Path
    $fullRoot = Resolve-FullPath $Root
    if ($fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $fullPath.StartsWith($fullRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-AllowedPath([string]$Path, [string]$Label) {
    $fullPath = Resolve-FullPath $Path
    if (-not (Test-PathWithin $fullPath $WorkspaceRoot) -and -not (Test-PathWithin $fullPath $ExplicitTempRoot)) {
        throw "$Label must resolve under the workspace '$WorkspaceRoot' or the explicit temp root '$ExplicitTempRoot': '$fullPath'."
    }
    $existingParent = $fullPath
    while (-not (Test-Path -LiteralPath $existingParent)) {
        $parent = Split-Path -Parent $existingParent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $existingParent) { throw "$Label does not have an existing parent: '$fullPath'." }
        $existingParent = $parent
    }
    $resolvedParent = (Resolve-Path -LiteralPath $existingParent).Path
    $resolvedWorkspace = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
    $resolvedTemp = (Resolve-Path -LiteralPath $ExplicitTempRoot).Path
    if (-not (Test-PathWithin $resolvedParent $resolvedWorkspace) -and -not (Test-PathWithin $resolvedParent $resolvedTemp)) {
        throw "$Label resolves outside the workspace or explicit temp root: '$resolvedParent'."
    }
    return $fullPath
}

function Assert-ExistingFile([string]$Path, [string]$Label, [long]$MaximumBytes) {
    $fullPath = Assert-AllowedPath $Path $Label
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "$Label was not found at '$fullPath'." }
    $resolved = (Resolve-Path -LiteralPath $fullPath).Path
    $info = Get-Item -LiteralPath $resolved
    if ($info.Length -gt $MaximumBytes) { throw "$Label exceeds the bounded size of $MaximumBytes bytes: '$resolved'." }
    return $resolved
}

function Read-BoundedText([string]$Path, [string]$Label, [long]$MaximumBytes) {
    $file = Assert-ExistingFile $Path $Label $MaximumBytes
    return [IO.File]::ReadAllText($file, [Text.UTF8Encoding]::new($false, $true))
}

function Write-BoundedText([string]$Path, [string]$Text, [long]$MaximumBytes) {
    $fullPath = Assert-AllowedPath $Path 'Output path'
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $encoding = [Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($Text)
    if ($bytes.Length -gt $MaximumBytes) {
        $suffix = "`n[truncated by accept-webgpu-player.ps1]"
        $suffixBytes = $encoding.GetBytes($suffix)
        $bytes = $bytes[0..($MaximumBytes - $suffixBytes.Length - 1)] + $suffixBytes
    }
    [IO.File]::WriteAllBytes($fullPath, $bytes)
}

function Read-JsonFile([string]$Path, [string]$Label, [long]$MaximumBytes) {
    $text = Read-BoundedText $Path $Label $MaximumBytes
    try { return $text | ConvertFrom-Json }
    catch { throw "$Label is not valid JSON: $($_.Exception.Message)" }
}

function Get-RequiredProperty([object]$Value, [string]$Name, [string]$Label) {
    if ($null -eq $Value) { throw "$Label is required." }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "$Label is missing required property '$Name'." }
    return $property.Value
}

function Assert-Integer([object]$Value, [string]$Label, [int]$Minimum, [int]$Maximum) {
    if ($Value -isnot [long] -and $Value -isnot [int] -and $Value -isnot [decimal]) { throw "$Label must be an integer." }
    try { $integer = [int]$Value } catch { throw "$Label must be an Int32." }
    if ($integer -lt $Minimum -or $integer -gt $Maximum) { throw "$Label must be between $Minimum and $Maximum." }
    return $integer
}

function Assert-Pixel([object]$Pixel, [string]$Name, [int]$Width, [int]$Height) {
    $x = Assert-Integer (Get-RequiredProperty $Pixel 'x' "pixelProof.samples.$Name") "pixelProof.samples.$Name.x" 0 ($Width - 1)
    $y = Assert-Integer (Get-RequiredProperty $Pixel 'y' "pixelProof.samples.$Name") "pixelProof.samples.$Name.y" 0 ($Height - 1)
    $r = Assert-Integer (Get-RequiredProperty $Pixel 'r' "pixelProof.samples.$Name") "pixelProof.samples.$Name.r" 0 255
    $g = Assert-Integer (Get-RequiredProperty $Pixel 'g' "pixelProof.samples.$Name") "pixelProof.samples.$Name.g" 0 255
    $b = Assert-Integer (Get-RequiredProperty $Pixel 'b' "pixelProof.samples.$Name") "pixelProof.samples.$Name.b" 0 255
    $a = Assert-Integer (Get-RequiredProperty $Pixel 'a' "pixelProof.samples.$Name") "pixelProof.samples.$Name.a" 0 255
    return @{ x = $x; y = $y; r = $r; g = $g; b = $b; a = $a }
}

function Get-PixelDistance([hashtable]$Left, [hashtable]$Right) {
    return [Math]::Abs($Left.r - $Right.r) + [Math]::Abs($Left.g - $Right.g) + [Math]::Abs($Left.b - $Right.b)
}

function Assert-WebGpuEvidence([object]$Evidence) {
    if ((Get-RequiredProperty $Evidence 'backend' 'Evidence') -cne $ExpectedBackend) { throw "Evidence backend must be exactly '$ExpectedBackend'." }
    if ((Assert-Integer (Get-RequiredProperty $Evidence 'protocolVersion' 'Evidence') 'Evidence protocolVersion' 1 1) -ne $ExpectedProtocolVersion) { throw "Evidence protocolVersion must be exactly $ExpectedProtocolVersion." }
    if ((Get-RequiredProperty $Evidence 'workloadId' 'Evidence') -cne $ExpectedWorkloadId) { throw "Evidence workloadId must be exactly '$ExpectedWorkloadId'." }
    [void](Assert-Integer (Get-RequiredProperty $Evidence 'submittedFrames' 'Evidence') 'Evidence submittedFrames' 1 ([int]::MaxValue))

    $diagnosticsProperty = $Evidence.PSObject.Properties['diagnostics']
    if ($null -eq $diagnosticsProperty -or $null -eq $diagnosticsProperty.Value -or $diagnosticsProperty.Value -isnot [Array]) {
        throw 'Evidence diagnostics must be a JSON array.'
    }
    $diagnostics = $diagnosticsProperty.Value
    if ($diagnostics.Count -ne 0) { throw 'Evidence diagnostics must be empty.' }

    $proof = Get-RequiredProperty $Evidence 'pixelProof' 'Evidence'
    if ($null -eq $proof -or -not [bool](Get-RequiredProperty $proof 'passed' 'pixelProof')) { throw 'pixelProof.passed must be true.' }
    $width = Assert-Integer (Get-RequiredProperty $proof 'width' 'pixelProof') 'pixelProof.width' 1 16384
    $height = Assert-Integer (Get-RequiredProperty $proof 'height' 'pixelProof') 'pixelProof.height' 1 16384
    $bytesPerRow = Assert-Integer (Get-RequiredProperty $proof 'bytesPerRow' 'pixelProof') 'pixelProof.bytesPerRow' 256 65536
    if ($bytesPerRow % 256 -ne 0 -or $bytesPerRow -lt ($width * 4)) { throw 'pixelProof.bytesPerRow must be 256-byte aligned and cover every RGBA pixel.' }
    if ([long]$bytesPerRow * $height -gt 64MB) { throw 'pixelProof readback dimensions exceed the bounded 64 MiB envelope.' }

    $samples = Get-RequiredProperty $proof 'samples' 'pixelProof'
    $background = Assert-Pixel (Get-RequiredProperty $samples 'background' 'pixelProof.samples') 'background' $width $height
    $cyan = Assert-Pixel (Get-RequiredProperty $samples 'cyan' 'pixelProof.samples') 'cyan' $width $height
    $blue = Assert-Pixel (Get-RequiredProperty $samples 'blue' 'pixelProof.samples') 'blue' $width $height
    $magenta = Assert-Pixel (Get-RequiredProperty $samples 'magenta' 'pixelProof.samples') 'magenta' $width $height

    $dark = $background.r -lt 40 -and $background.g -lt 40 -and $background.b -lt 40 -and $background.a -ge 240
    $cyanLike = $cyan.r -lt 110 -and $cyan.g -ge 150 -and $cyan.b -ge 170 -and $cyan.a -ge 240
    $blueLike = $blue.r -lt 110 -and $blue.g -lt 140 -and $blue.b -ge 190 -and $blue.a -ge 240
    $magentaLike = $magenta.r -ge 150 -and $magenta.g -lt 120 -and $magenta.b -ge 160 -and $magenta.a -ge 240
    $distinct = (Get-PixelDistance $cyan $blue) -ge 80 -and (Get-PixelDistance $cyan $magenta) -ge 80 -and (Get-PixelDistance $blue $magenta) -ge 80
    $allZero = @($cyan, $blue, $magenta | ForEach-Object { $_.r -eq 0 -and $_.g -eq 0 -and $_.b -eq 0 -and $_.a -eq 0 }) -notcontains $false
    if (-not ($dark -and $cyanLike -and $blueLike -and $magentaLike -and $distinct -and -not $allZero)) {
        throw 'Raw pixel samples do not satisfy the proof workload threshold contract.'
    }
}

function Assert-BrowserLog([object]$Log) {
    $entriesProperty = $Log.PSObject.Properties['entries']
    if ($null -eq $entriesProperty -or $null -eq $entriesProperty.Value -or $entriesProperty.Value -isnot [Array]) {
        throw 'Browser log entries must be a JSON array.'
    }
    $entries = $entriesProperty.Value
    if ($entries.Count -gt $MaximumLogEntries) { throw "Browser log has more than $MaximumLogEntries entries." }
    foreach ($entry in $entries) {
        $level = [string](Get-RequiredProperty $entry 'level' 'Browser log entry')
        if ($level -match '^(warn(ing)?|error|severe|fatal)$') { throw "Browser log contains a disallowed '$level' entry." }
    }
}

function Assert-ScreenshotMetadata([object]$Metadata) {
    $path = Assert-ExistingFile ([string](Get-RequiredProperty $Metadata 'path' 'Screenshot metadata')) 'Screenshot' $MaximumScreenshotBytes
    if (([string](Get-RequiredProperty $Metadata 'mimeType' 'Screenshot metadata')).ToLowerInvariant() -ne 'image/png') { throw 'Screenshot metadata mimeType must be image/png.' }
    $bytes = [long](Get-RequiredProperty $Metadata 'bytes' 'Screenshot metadata')
    if ($bytes -ne (Get-Item -LiteralPath $path).Length -or $bytes -le 100) { throw 'Screenshot metadata bytes do not match a nonblank screenshot.' }
    $expectedHash = [string](Get-RequiredProperty $Metadata 'sha256' 'Screenshot metadata')
    if ($expectedHash -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Screenshot metadata sha256 must be a SHA-256 hex digest.' }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -cne $expectedHash.ToUpperInvariant()) { throw 'Screenshot metadata sha256 does not match the supplied screenshot.' }
    $header = [IO.File]::ReadAllBytes($path) | Select-Object -First 8
    if (($header -join ',') -ne '137,80,78,71,13,10,26,10') { throw 'Screenshot is not a PNG file.' }
    return @{ path = $path; sha256 = $actualHash; bytes = $bytes }
}

function Get-FreeLoopbackPort() {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Get-MimeType([string]$Path) {
    switch ([IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        '.html' { return 'text/html; charset=utf-8' }
        '.js' { return 'text/javascript; charset=utf-8' }
        '.mjs' { return 'text/javascript; charset=utf-8' }
        '.wasm' { return 'application/wasm' }
        '.json' { return 'application/json; charset=utf-8' }
        '.css' { return 'text/css; charset=utf-8' }
        '.svg' { return 'image/svg+xml' }
        '.png' { return 'image/png' }
        '.ico' { return 'image/x-icon' }
        default { return 'application/octet-stream' }
    }
}

function Invoke-StaticServer() {
    $root = Assert-AllowedPath $ContentRoot 'Server content root'
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Server content root was not found at '$root'." }
    if ($Port -lt 1 -or $Port -gt 65535) { throw 'Server port must be between 1 and 65535.' }
    $ready = Assert-AllowedPath $ReadyPath 'Server readiness path'
    $listener = [Net.HttpListener]::new()
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    try {
        $listener.Start()
        Write-BoundedText $ready 'ready' 32
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            try {
                $rawPath = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart('/'))
                if ([string]::IsNullOrWhiteSpace($rawPath)) { $rawPath = 'index.html' }
                $candidate = [IO.Path]::GetFullPath((Join-Path $root $rawPath.Replace('/', [IO.Path]::DirectorySeparatorChar)))
                if (-not (Test-PathWithin $candidate $root) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    $context.Response.StatusCode = 404
                    continue
                }
                $candidate = (Resolve-Path -LiteralPath $candidate).Path
                if (-not (Test-PathWithin $candidate $root)) {
                    $context.Response.StatusCode = 404
                    continue
                }
                $bytes = [IO.File]::ReadAllBytes($candidate)
                $context.Response.StatusCode = 200
                $context.Response.ContentType = Get-MimeType $candidate
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
            catch {
                $context.Response.StatusCode = 500
            }
            finally { $context.Response.Close() }
        }
    }
    finally {
        if ($listener.IsListening) { $listener.Stop() }
        $listener.Close()
        if (Test-Path -LiteralPath $ready -PathType Leaf) { Remove-Item -LiteralPath $ready -Force }
    }
}

function Read-Session([string]$Path) {
    $session = Read-JsonFile $Path 'Acceptance session' 64KB
    [void](Get-RequiredProperty $session 'schemaVersion' 'Acceptance session')
    [void](Get-RequiredProperty $session 'outputRoot' 'Acceptance session')
    [void](Get-RequiredProperty $session 'serverPid' 'Acceptance session')
    [void](Get-RequiredProperty $session 'serverStartTicks' 'Acceptance session')
    return $session
}

function Stop-SessionServer([object]$Session) {
    $serverProcessId = [int](Get-RequiredProperty $Session 'serverPid' 'Acceptance session')
    $ticks = [long](Get-RequiredProperty $Session 'serverStartTicks' 'Acceptance session')
    $process = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    try {
        if ($process.StartTime.ToUniversalTime().Ticks -ne $ticks) { throw "Refusing to stop process $serverProcessId because it no longer matches the harness server start time." }
        Stop-Process -Id $serverProcessId -Force
        [void]$process.WaitForExit(5000)
        return $true
    }
    finally { $process.Dispose() }
}

function Invoke-Prepare() {
    $output = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        Join-Path $WorkspaceRoot (Join-Path 'artifacts\webgpu-player-acceptance' ("run-" + [Guid]::NewGuid().ToString('N') + '\publish'))
    } else { $OutputRoot }
    $output = Assert-AllowedPath $output 'Publish output root'
    if (Test-Path -LiteralPath $output) { throw "Publish output root must be new and clean; refusing to overwrite '$output'." }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $publishLog = Join-Path $output 'publish.log'
    $project = Join-Path $WorkspaceRoot 'src\Rekall.Age.Player.Web\Rekall.Age.Player.Web.csproj'
    $publishText = (& dotnet publish $project -c Release --no-restore -p:PublishTrimmed=true -o $output 2>&1 | Out-String)
    Write-BoundedText $publishLog $publishText $MaximumLogBytes
    if ($LASTEXITCODE -ne 0) { throw "Trimmed Web player publish failed. See '$publishLog'." }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'wwwroot\index.html') -PathType Leaf)) { throw "Trimmed Web player publish omitted wwwroot/index.html from '$output'." }
    if (@(Get-ChildItem -LiteralPath (Join-Path $output 'wwwroot') -Filter 'main*.js' -File).Count -eq 0) { throw "Trimmed Web player publish omitted its main JavaScript module from '$output'." }

    $port = Get-FreeLoopbackPort
    $ready = Join-Path $output '.server-ready'
    $serverOut = Join-Path $output 'server.stdout.log'
    $serverErr = Join-Path $output 'server.stderr.log'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $server = Start-Process -FilePath $pwsh -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-Phase', 'Server', '-ContentRoot', (Join-Path $output 'wwwroot'), '-Port', "$port", '-ReadyPath', $ready) `
        -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $ready -PathType Leaf)) {
            $server.Refresh()
            if ($server.HasExited) { throw "Web player static server exited before readiness. See '$serverErr'." }
            if ([DateTime]::UtcNow -ge $deadline) { throw 'Web player static server did not become ready within 10 seconds.' }
            Start-Sleep -Milliseconds 100
        }
        $sessionPath = Join-Path $output 'acceptance-session.json'
        $session = [ordered]@{
            schemaVersion = 1
            outputRoot = $output
            url = "http://127.0.0.1:$port/"
            serverPid = $server.Id
            serverStartTicks = $server.StartTime.ToUniversalTime().Ticks
            expectedEvidenceSchema = [ordered]@{
                backend = $ExpectedBackend; protocolVersion = $ExpectedProtocolVersion; workloadId = $ExpectedWorkloadId; submittedFramesMinimum = 1
                diagnostics = 'empty'; pixelProof = 'passed with literal raw RGBA threshold samples'; browserLog = 'JSON entries with no warning/error/severe/fatal level'
                screenshotMetadata = 'path, mimeType=image/png, bytes, sha256'
            }
        }
        Write-BoundedText $sessionPath ($session | ConvertTo-Json -Depth 8 -Compress) 64KB
        [ordered]@{ phase = 'prepared'; url = $session.url; outputRoot = $output; serverPid = $server.Id; sessionPath = $sessionPath; expectedEvidenceSchema = $session.expectedEvidenceSchema } | ConvertTo-Json -Depth 8 -Compress
    }
    catch {
        if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
        throw
    }
    finally { $server.Dispose() }
}

function Invoke-Finalize() {
    $sessionFile = Assert-ExistingFile $SessionPath 'Acceptance session' 64KB
    $session = Read-Session $sessionFile
    $output = Assert-AllowedPath ([string](Get-RequiredProperty $session 'outputRoot' 'Acceptance session')) 'Session output root'
    if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw "Session output root was not found at '$output'." }
    $stopAttempted = $false
    try {
        $evidenceFile = Assert-ExistingFile $EvidencePath 'Browser evidence' $MaximumEvidenceBytes
        $browserLogFile = Assert-ExistingFile $BrowserLogPath 'Browser log' $MaximumLogBytes
        $screenshotMetadataFile = Assert-ExistingFile $ScreenshotMetadataPath 'Screenshot metadata' $MaximumScreenshotMetadataBytes
        $evidence = Read-JsonFile $evidenceFile 'Browser evidence' $MaximumEvidenceBytes
        $log = Read-JsonFile $browserLogFile 'Browser log' $MaximumLogBytes
        $screenshot = Assert-ScreenshotMetadata (Read-JsonFile $screenshotMetadataFile 'Screenshot metadata' $MaximumScreenshotMetadataBytes)
        Assert-WebGpuEvidence $evidence
        Assert-BrowserLog $log
        $serverStopped = Stop-SessionServer $session
        $stopAttempted = $true
        $result = [ordered]@{
            schemaVersion = 1; acceptance = 'validated-browser-supplied-evidence'; url = $session.url; outputRoot = $output; serverStopped = $serverStopped
            evidencePath = $evidenceFile; browserLogPath = $browserLogFile; screenshot = $screenshot
        }
        $resultPath = Join-Path $output 'acceptance-result.json'
        Write-BoundedText $resultPath ($result | ConvertTo-Json -Depth 8 -Compress) 64KB
        $result | ConvertTo-Json -Depth 8 -Compress
    }
    finally {
        if (-not $stopAttempted) { [void](Stop-SessionServer $session) }
    }
}

function Invoke-Stop() {
    $session = Read-Session (Assert-ExistingFile $SessionPath 'Acceptance session' 64KB)
    [ordered]@{ phase = 'stopped'; serverStopped = Stop-SessionServer $session } | ConvertTo-Json -Compress
}

function Invoke-SelfTest() {
    $valid = [pscustomobject]@{
        backend = 'WebGPU'; protocolVersion = 1; workloadId = 'proof.webgpu.asset-independent'; submittedFrames = 1; diagnostics = @()
        pixelProof = [pscustomobject]@{
            passed = $true; width = 64; height = 64; bytesPerRow = 256
            samples = [pscustomobject]@{
                background = [pscustomobject]@{ x = 5; y = 5; r = 5; g = 5; b = 5; a = 255 }
                cyan = [pscustomobject]@{ x = 17; y = 48; r = 10; g = 200; b = 220; a = 255 }
                blue = [pscustomobject]@{ x = 32; y = 20; r = 10; g = 30; b = 220; a = 255 }
                magenta = [pscustomobject]@{ x = 46; y = 48; r = 200; g = 20; b = 200; a = 255 }
            }
        }
    }
    Assert-WebGpuEvidence $valid
    $literalEvidenceJson = $valid | ConvertTo-Json -Depth 8 -Compress
    if ($literalEvidenceJson -notmatch '"diagnostics":\[\]') { throw 'Self-test did not produce literal empty diagnostics JSON.' }
    Assert-WebGpuEvidence ($literalEvidenceJson | ConvertFrom-Json)
    Assert-BrowserLog ('{"entries":[]}' | ConvertFrom-Json)

    $selfTestRoot = Join-Path $ExplicitTempRoot ('rekall-webgpu-acceptance-self-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $selfTestRoot -Force | Out-Null
    $previousSessionPath = $script:SessionPath
    $previousEvidencePath = $script:EvidencePath
    $previousBrowserLogPath = $script:BrowserLogPath
    $previousScreenshotMetadataPath = $script:ScreenshotMetadataPath
    try {
        $selfTestEvidencePath = Join-Path $selfTestRoot 'browser-evidence.json'
        $selfTestLogPath = Join-Path $selfTestRoot 'browser-log.json'
        $selfTestScreenshotPath = Join-Path $selfTestRoot 'browser-screenshot.png'
        $selfTestScreenshotMetadataPath = Join-Path $selfTestRoot 'browser-screenshot.json'
        $selfTestSessionPath = Join-Path $selfTestRoot 'acceptance-session.json'
        [IO.File]::WriteAllText($selfTestEvidencePath, $literalEvidenceJson, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($selfTestLogPath, '{"entries":[]}', [Text.UTF8Encoding]::new($false))
        $screenshotBytes = [byte[]]::new(128)
        ([byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)).CopyTo($screenshotBytes, 0)
        [IO.File]::WriteAllBytes($selfTestScreenshotPath, $screenshotBytes)
        $screenshotMetadata = [ordered]@{ path = $selfTestScreenshotPath; mimeType = 'image/png'; bytes = $screenshotBytes.Length; sha256 = (Get-FileHash -LiteralPath $selfTestScreenshotPath -Algorithm SHA256).Hash }
        [IO.File]::WriteAllText($selfTestScreenshotMetadataPath, ($screenshotMetadata | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        $session = [ordered]@{ schemaVersion = 1; outputRoot = $selfTestRoot; url = 'http://127.0.0.1:1/'; serverPid = [int]::MaxValue; serverStartTicks = 0 }
        [IO.File]::WriteAllText($selfTestSessionPath, ($session | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        $script:SessionPath = $selfTestSessionPath
        $script:EvidencePath = $selfTestEvidencePath
        $script:BrowserLogPath = $selfTestLogPath
        $script:ScreenshotMetadataPath = $selfTestScreenshotMetadataPath
        $selfTestFinalize = Invoke-Finalize | ConvertFrom-Json
        if ($selfTestFinalize.acceptance -ne 'validated-browser-supplied-evidence') { throw 'Self-test did not finalize literal empty evidence arrays.' }
    }
    finally {
        $script:SessionPath = $previousSessionPath
        $script:EvidencePath = $previousEvidencePath
        $script:BrowserLogPath = $previousBrowserLogPath
        $script:ScreenshotMetadataPath = $previousScreenshotMetadataPath
        if (Test-Path -LiteralPath $selfTestRoot) { Remove-Item -LiteralPath $selfTestRoot -Recurse -Force }
    }

    $rejected = $false
    try { Assert-WebGpuEvidence (($literalEvidenceJson -replace '"diagnostics":\[\]', '"diagnostics":null') | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected null diagnostics to be rejected.' }
    $rejected = $false
    try { Assert-WebGpuEvidence (($literalEvidenceJson -replace '"diagnostics":\[\]', '"diagnostics":{}') | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected scalar diagnostics to be rejected.' }
    $rejected = $false
    try { Assert-WebGpuEvidence (($literalEvidenceJson -replace '"diagnostics":\[\]', '"diagnostics":[{"code":"REKALL_WEBGPU_TEST","message":"failure"}]') | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected nonempty diagnostics to be rejected.' }
    $rejected = $false
    try { Assert-BrowserLog ('{"entries":null}' | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected null browser log entries to be rejected.' }
    $rejected = $false
    try { Assert-BrowserLog ('{"entries":{}}' | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected scalar browser log entries to be rejected.' }
    $rejected = $false
    try { Assert-BrowserLog ('{"entries":[{"level":"error","text":"failure"}]}' | ConvertFrom-Json) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected error-level literal browser log entries to be rejected.' }

    $invalid = $valid | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.pixelProof.samples.cyan.g = 1
    $rejected = $false
    try { Assert-WebGpuEvidence $invalid } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected an invalid raw pixel sample to be rejected.' }
    Assert-BrowserLog ([pscustomobject]@{ entries = @([pscustomobject]@{ level = 'info'; text = 'browser booted' }) })
    $rejected = $false
    try { Assert-BrowserLog ([pscustomobject]@{ entries = @([pscustomobject]@{ level = 'error'; text = 'failure' }) }) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected an error-level browser log entry to be rejected.' }
    [ordered]@{ selfTest = 'passed'; physicalBrowser = $false } | ConvertTo-Json -Compress
}

switch ($Phase) {
    'Prepare' { Invoke-Prepare }
    'Finalize' { Invoke-Finalize }
    'Stop' { Invoke-Stop }
    'Server' { Invoke-StaticServer }
    'SelfTest' { Invoke-SelfTest }
}
