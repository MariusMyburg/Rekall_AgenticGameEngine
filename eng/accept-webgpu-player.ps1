[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Finalize', 'Stop', 'Server', 'SelfTest')]
    [string]$Phase = 'Prepare',
    [string]$OutputRoot,
    [string]$SessionPath,
    [string]$EvidencePath,
    [string]$BrowserLogPath,
    [string]$ScreenshotMetadataPath,
    [string]$RunRoot,
    [string]$ContentRoot,
    [int]$Port,
    [string]$ReadyPath,
    [string]$ServerToken
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$MaximumEvidenceBytes = 256KB
$MaximumLogBytes = 256KB
$MaximumScreenshotMetadataBytes = 64KB
$MaximumScreenshotBytes = 32MB
$MaximumLogEntries = 256
$MaximumPublishFiles = 5000
$MaximumPublishBytes = 512MB
$MaximumBrowserUserAgentBytes = 4096
$MaximumBrowserVersionBytes = 256
$WorkspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ExplicitTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$ExpectedBackend = 'WebGPU'
$ExpectedProtocolVersion = 1
$ExpectedWorkloadId = 'proof.webgpu.asset-independent'

function New-SecureToken() {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Assert-HighEntropyToken([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[A-Za-z0-9_-]{43}$') { throw "$Label must be a 32-byte base64url token." }
    return $Value
}

function Assert-ShortText([object]$Value, [string]$Label, [int]$MaximumBytes) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or [Text.Encoding]::UTF8.GetByteCount($Value) -gt $MaximumBytes) { throw "$Label must be non-empty UTF-8 text no longer than $MaximumBytes bytes." }
    return $Value
}

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

function Get-PublishIdentity([string]$OutputRoot) {
    $root = Assert-AllowedPath $OutputRoot 'Publish output root'
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
    if ($files.Count -eq 0 -or $files.Count -gt $MaximumPublishFiles) { throw "Publish output must contain between 1 and $MaximumPublishFiles files." }
    [long]$totalBytes = 0
    $manifest = [Text.StringBuilder]::new()
    foreach ($file in $files) {
        $totalBytes += $file.Length
        if ($totalBytes -gt $MaximumPublishBytes) { throw "Publish output exceeds the bounded $MaximumPublishBytes byte envelope." }
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        [void]$manifest.Append($relative).Append('|').Append($file.Length).Append('|').Append((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash).Append("`n")
    }
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest.ToString())
    $sha = [Security.Cryptography.SHA256]::HashData($manifestBytes)
    return [ordered]@{ fileCount = $files.Count; totalBytes = $totalBytes; manifestSha256 = [Convert]::ToHexString($sha) }
}

function Assert-PublishIdentity([object]$Actual, [object]$Expected, [string]$Label) {
    if ($null -eq $Actual) { throw "$Label publish identity is required." }
    $fileCount = Assert-Integer (Get-RequiredProperty $Actual 'fileCount' "$Label publish") "$Label publish fileCount" 1 $MaximumPublishFiles
    $totalBytes = [long](Get-RequiredProperty $Actual 'totalBytes' "$Label publish")
    $manifestSha256 = [string](Get-RequiredProperty $Actual 'manifestSha256' "$Label publish")
    if ($totalBytes -lt 1 -or $totalBytes -gt $MaximumPublishBytes -or $manifestSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw "$Label publish identity is invalid." }
    if ($fileCount -ne [int]$Expected.fileCount -or $totalBytes -ne [long]$Expected.totalBytes -or $manifestSha256 -cne [string]$Expected.manifestSha256) { throw "$Label publish identity does not match this prepared run." }
}

function Assert-CapturedUtc([object]$Value, [DateTimeOffset]$PreparedUtc, [string]$Label) {
    try {
        $captured = if ($Value -is [DateTimeOffset]) { $Value } elseif ($Value -is [DateTime]) {
            if ($Value.Kind -ne [DateTimeKind]::Utc) { throw 'not UTC' }
            [DateTimeOffset]$Value
        } elseif ($Value -is [string]) {
            [DateTimeOffset]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        } else { throw 'not a date' }
    }
    catch { throw "$Label capturedUtc must be an ISO-8601 UTC string." }
    if ($captured.Offset -ne [TimeSpan]::Zero -or $captured -lt $PreparedUtc) { throw "$Label capturedUtc predates this prepared session or is not UTC." }
    return $captured
}

function Assert-RunArtifactFile([string]$Path, [object]$Session, [string]$Label, [long]$MaximumBytes) {
    $file = Assert-ExistingFile $Path $Label $MaximumBytes
    $controllerRoot = [string](Get-RequiredProperty $Session 'controllerRoot' 'Acceptance session')
    if (-not (Test-PathWithin $file $controllerRoot)) { throw "$Label must resolve under this run's controller artifact root '$controllerRoot'." }
    return $file
}

function Assert-SessionWrapper([object]$Wrapper, [object]$Session, [string]$Label, [string]$PayloadProperty) {
    if ((Get-RequiredProperty $Wrapper 'sessionId' $Label) -cne (Get-RequiredProperty $Session 'sessionId' 'Acceptance session')) { throw "$Label sessionId does not match this prepared run." }
    if ((Get-RequiredProperty $Wrapper 'url' $Label) -cne (Get-RequiredProperty $Session 'url' 'Acceptance session')) { throw "$Label URL does not match this prepared run." }
    Assert-PublishIdentity (Get-RequiredProperty $Wrapper 'publish' $Label) (Get-RequiredProperty $Session 'publish' 'Acceptance session') $Label
    $preparedUtc = Assert-CapturedUtc (Get-RequiredProperty $Session 'preparedUtc' 'Acceptance session') ([DateTimeOffset]::MinValue) 'Acceptance session'
    [void](Assert-CapturedUtc (Get-RequiredProperty $Wrapper 'capturedUtc' $Label) $preparedUtc $Label)
    $browser = Get-RequiredProperty $Wrapper 'browser' $Label
    $userAgent = Assert-ShortText (Get-RequiredProperty $browser 'userAgent' "$Label browser") "$Label browser userAgent" $MaximumBrowserUserAgentBytes
    $version = Assert-ShortText (Get-RequiredProperty $browser 'version' "$Label browser") "$Label browser version" $MaximumBrowserVersionBytes
    if ($Wrapper.PSObject.Properties.Name -cnotcontains $PayloadProperty) { throw "$Label is missing required property '$PayloadProperty'." }
    $payload = $Wrapper.$PayloadProperty
    return [ordered]@{ payload = $payload; userAgent = $userAgent; version = $version }
}

function Read-JsonFile([string]$Path, [string]$Label, [long]$MaximumBytes) {
    $text = Read-BoundedText $Path $Label $MaximumBytes
    try { return $text | ConvertFrom-Json -DateKind String }
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
    $pixels = Get-DecodedPngFacts $path
    return @{ path = $path; sha256 = $actualHash; bytes = $bytes; width = $pixels.width; height = $pixels.height }
}

function Get-DecodedPngFacts([string]$Path) {
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    $decoded = $null
    $normalized = $null
    $graphics = $null
    $locked = $null
    try {
        $decoded = [Drawing.Bitmap]::FromFile($Path, $false)
        if ($decoded.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Screenshot is not a decodable PNG file.' }
        $width = $decoded.Width
        $height = $decoded.Height
        if ($width -le 0 -or $height -le 0 -or $width -gt 4096 -or $height -gt 4096 -or [long]$width * $height -gt 4MB) {
            throw 'Screenshot dimensions exceed the bounded decoded PNG envelope.'
        }
        $normalized = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($normalized)
        $graphics.DrawImageUnscaled($decoded, 0, 0)
        $rectangle = [Drawing.Rectangle]::new(0, 0, $width, $height)
        $locked = $normalized.LockBits($rectangle, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $rowBytes = [Math]::Abs($locked.Stride)
        $buffer = [byte[]]::new($rowBytes * $height)
        [Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $buffer, 0, $buffer.Length)
        $first = $null
        $nonblank = $false
        $varied = $false
        for ($y = 0; $y -lt $height; $y++) {
            $rowOffset = if ($locked.Stride -ge 0) { $y * $rowBytes } else { ($height - 1 - $y) * $rowBytes }
            for ($x = 0; $x -lt $width; $x++) {
                $offset = $rowOffset + ($x * 4)
                $pixel = "$($buffer[$offset + 2]),$($buffer[$offset + 1]),$($buffer[$offset]),$($buffer[$offset + 3])"
                if ($null -eq $first) { $first = $pixel } elseif ($pixel -ne $first) { $varied = $true }
                if ($buffer[$offset + 3] -gt 0 -and ($buffer[$offset] -gt 0 -or $buffer[$offset + 1] -gt 0 -or $buffer[$offset + 2] -gt 0)) { $nonblank = $true }
            }
        }
        if (-not $nonblank -or -not $varied) { throw 'Decoded screenshot pixels must be nonblank and varied.' }
        return @{ width = $width; height = $height }
    }
    catch {
        if ($_.Exception.Message -like 'Screenshot*' -or $_.Exception.Message -like 'Decoded screenshot*') { throw }
        throw "Screenshot PNG decoding failed: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $locked) { $normalized.UnlockBits($locked) }
        if ($null -ne $graphics) { $graphics.Dispose() }
        if ($null -ne $normalized) { $normalized.Dispose() }
        if ($null -ne $decoded) { $decoded.Dispose() }
    }
}

function Write-ValidSelfTestPng([string]$Path) {
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    $bitmap = [Drawing.Bitmap]::new(8, 8, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y = 0; $y -lt 8; $y++) { for ($x = 0; $x -lt 8; $x++) { $bitmap.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, 5, 10, 15)) } }
        $bitmap.SetPixel(1, 1, [Drawing.Color]::Cyan)
        $bitmap.SetPixel(6, 6, [Drawing.Color]::Magenta)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
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
    $token = Assert-HighEntropyToken $ServerToken 'Server token'
    $ready = Assert-AllowedPath $ReadyPath 'Server readiness path'
    $listener = [Net.HttpListener]::new()
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    try {
        $listener.Start()
        Write-BoundedText $ready "ready:$token" 64
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
    $sessionFile = Assert-ExistingFile $Path 'Acceptance session' 64KB
    $session = Read-JsonFile $sessionFile 'Acceptance session' 64KB
    if ((Assert-Integer (Get-RequiredProperty $session 'schemaVersion' 'Acceptance session') 'Acceptance session schemaVersion' 2 2) -ne 2) { throw 'Acceptance session schemaVersion must be 2.' }
    $runRoot = Assert-AllowedPath ([string](Get-RequiredProperty $session 'runRoot' 'Acceptance session')) 'Acceptance run root'
    $outputRoot = Assert-AllowedPath ([string](Get-RequiredProperty $session 'outputRoot' 'Acceptance session')) 'Acceptance output root'
    $controllerRoot = Assert-AllowedPath ([string](Get-RequiredProperty $session 'controllerRoot' 'Acceptance session')) 'Acceptance controller root'
    if ($sessionFile -cne (Join-Path $runRoot 'acceptance-session.json') -or $outputRoot -cne (Join-Path $runRoot 'publish') -or $controllerRoot -cne (Join-Path $runRoot 'controller')) {
        throw 'Acceptance session paths do not describe one exact run root.'
    }
    if (-not (Test-Path -LiteralPath $outputRoot -PathType Container) -or -not (Test-Path -LiteralPath $controllerRoot -PathType Container)) { throw 'Acceptance session run directories are missing.' }
    $sessionId = [string](Get-RequiredProperty $session 'sessionId' 'Acceptance session')
    $parsedSessionId = [Guid]::Empty
    if (-not [Guid]::TryParse($sessionId, [ref]$parsedSessionId)) { throw 'Acceptance sessionId must be a GUID.' }
    $nonce = Assert-HighEntropyToken ([string](Get-RequiredProperty $session 'nonce' 'Acceptance session')) 'Acceptance nonce'
    $port = Assert-Integer (Get-RequiredProperty $session 'port' 'Acceptance session') 'Acceptance port' 1 65535
    $expectedUrl = "http://127.0.0.1:$port/?rekallSession=$sessionId&rekallNonce=$nonce"
    if ((Get-RequiredProperty $session 'url' 'Acceptance session') -cne $expectedUrl) { throw 'Acceptance session URL does not bind its session nonce.' }
    [void](Assert-CapturedUtc (Get-RequiredProperty $session 'preparedUtc' 'Acceptance session') ([DateTimeOffset]::MinValue) 'Acceptance session')
    [void](Assert-PublishIdentity (Get-RequiredProperty $session 'publish' 'Acceptance session') (Get-PublishIdentity $outputRoot) 'Acceptance session')
    if ((Assert-AllowedPath ([string](Get-RequiredProperty $session 'serverContentRoot' 'Acceptance session')) 'Server content root') -cne (Join-Path $outputRoot 'wwwroot') -or
        (Assert-AllowedPath ([string](Get-RequiredProperty $session 'serverReadyPath' 'Acceptance session')) 'Server ready path') -cne (Join-Path $runRoot '.server-ready')) { throw 'Acceptance session server paths do not match this run.' }
    [void](Assert-HighEntropyToken ([string](Get-RequiredProperty $session 'serverToken' 'Acceptance session')) 'Acceptance server token')
    [void](Assert-ExistingFile ([string](Get-RequiredProperty $session 'serverScriptPath' 'Acceptance session')) 'Server script' 1MB)
    $serverExecutable = [IO.Path]::GetFullPath([string](Get-RequiredProperty $session 'serverExecutable' 'Acceptance session'))
    $expectedServerExecutable = [IO.Path]::GetFullPath((Get-Command pwsh -ErrorAction Stop).Source)
    if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf) -or $serverExecutable -cne $expectedServerExecutable) { throw 'Acceptance session server executable is not this harness PowerShell host.' }
    [void](Assert-Integer (Get-RequiredProperty $session 'serverPid' 'Acceptance session') 'Acceptance server PID' 1 ([int]::MaxValue))
    if ([long](Get-RequiredProperty $session 'serverStartTicks' 'Acceptance session') -le 0) { throw 'Acceptance server start ticks must be positive.' }
    return $session
}

function Test-ServerCommandLineArgument([string]$CommandLine, [string]$Name, [string]$Value) {
    $namePattern = [Regex]::Escape("-$Name")
    $valuePattern = [Regex]::Escape($Value)
    return [Regex]::IsMatch($CommandLine, "(?i)(?:^|\s)$namePattern\s+(?:`"$valuePattern`"|$valuePattern)(?=\s|$)")
}

function Stop-SessionServer([object]$Session) {
    $serverProcessId = [int](Get-RequiredProperty $Session 'serverPid' 'Acceptance session')
    $ticks = [long](Get-RequiredProperty $Session 'serverStartTicks' 'Acceptance session')
    $process = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    try {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $serverProcessId" -ErrorAction Stop
        if ($null -eq $cim -or [string]::IsNullOrWhiteSpace($cim.CommandLine) -or [string]::IsNullOrWhiteSpace($cim.ExecutablePath)) { throw "Refusing to stop process $serverProcessId because its Win32 process identity is incomplete." }
        if ($process.StartTime.ToUniversalTime().Ticks -ne $ticks -or $cim.ExecutablePath -cne (Get-RequiredProperty $Session 'serverExecutable' 'Acceptance session')) { throw "Refusing to stop process $serverProcessId because it no longer matches the harness server executable/start time." }
        $commandLine = [string]$cim.CommandLine
        foreach ($argument in @(
            @{ name = 'File'; value = [string](Get-RequiredProperty $Session 'serverScriptPath' 'Acceptance session') },
            @{ name = 'Phase'; value = 'Server' },
            @{ name = 'ContentRoot'; value = [string](Get-RequiredProperty $Session 'serverContentRoot' 'Acceptance session') },
            @{ name = 'Port'; value = [string](Get-RequiredProperty $Session 'port' 'Acceptance session') },
            @{ name = 'ReadyPath'; value = [string](Get-RequiredProperty $Session 'serverReadyPath' 'Acceptance session') },
            @{ name = 'ServerToken'; value = [string](Get-RequiredProperty $Session 'serverToken' 'Acceptance session') })) {
            if (-not (Test-ServerCommandLineArgument $commandLine $argument.name $argument.value)) { throw "Refusing to stop process $serverProcessId because it is not this exact harness server invocation." }
        }
        Stop-Process -Id $serverProcessId -Force
        [void]$process.WaitForExit(5000)
        return $true
    }
    finally { $process.Dispose() }
}

function Invoke-Prepare() {
    $run = if (-not [string]::IsNullOrWhiteSpace($RunRoot)) {
        $RunRoot
    } elseif (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
        $candidate = Assert-AllowedPath $OutputRoot 'Publish output root'
        if ([IO.Path]::GetFileName($candidate) -cne 'publish') { throw "OutputRoot must end in 'publish' so controller artifacts remain in one exact run root." }
        Split-Path -Parent $candidate
    } else {
        Join-Path $WorkspaceRoot (Join-Path 'artifacts\webgpu-player-acceptance' ('run-' + [Guid]::NewGuid().ToString('N')))
    }
    $run = Assert-AllowedPath $run 'Acceptance run root'
    if (Test-Path -LiteralPath $run) { throw "Acceptance run root must be new and clean; refusing to overwrite '$run'." }
    $output = Join-Path $run 'publish'
    $controller = Join-Path $run 'controller'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    New-Item -ItemType Directory -Path $controller -Force | Out-Null
    $publishLog = Join-Path $output 'publish.log'
    $project = Join-Path $WorkspaceRoot 'src\Rekall.Age.Player.Web\Rekall.Age.Player.Web.csproj'
    $publishText = (& dotnet publish $project -c Release --no-restore -p:PublishTrimmed=true -o $output 2>&1 | Out-String)
    Write-BoundedText $publishLog $publishText $MaximumLogBytes
    if ($LASTEXITCODE -ne 0) { throw "Trimmed Web player publish failed. See '$publishLog'." }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'wwwroot\index.html') -PathType Leaf)) { throw "Trimmed Web player publish omitted wwwroot/index.html from '$output'." }
    if (@(Get-ChildItem -LiteralPath (Join-Path $output 'wwwroot') -Filter 'main*.js' -File).Count -eq 0) { throw "Trimmed Web player publish omitted its main JavaScript module from '$output'." }

    $port = Get-FreeLoopbackPort
    $sessionId = [Guid]::NewGuid().ToString()
    $nonce = New-SecureToken
    $serverToken = New-SecureToken
    $preparedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $ready = Join-Path $run '.server-ready'
    $serverOut = Join-Path $output 'server.stdout.log'
    $serverErr = Join-Path $output 'server.stderr.log'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $serverScript = Assert-ExistingFile $PSCommandPath 'Harness server script' 1MB
    $contentRoot = Join-Path $output 'wwwroot'
    $server = Start-Process -FilePath $pwsh -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $serverScript,
        '-Phase', 'Server', '-ContentRoot', $contentRoot, '-Port', "$port", '-ReadyPath', $ready, '-ServerToken', $serverToken) `
        -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $ready -PathType Leaf) -or (Get-Content -LiteralPath $ready -Raw -ErrorAction SilentlyContinue) -cne "ready:$serverToken") {
            $server.Refresh()
            if ($server.HasExited) { throw "Web player static server exited before readiness. See '$serverErr'." }
            if ([DateTime]::UtcNow -ge $deadline) { throw 'Web player static server did not become ready within 10 seconds.' }
            Start-Sleep -Milliseconds 100
        }
        $publish = Get-PublishIdentity $output
        $url = "http://127.0.0.1:$port/?rekallSession=$sessionId&rekallNonce=$nonce"
        $sessionPath = Join-Path $run 'acceptance-session.json'
        $session = [ordered]@{
            schemaVersion = 2
            sessionId = $sessionId
            nonce = $nonce
            preparedUtc = $preparedUtc
            runRoot = $run
            outputRoot = $output
            controllerRoot = $controller
            publish = $publish
            url = $url
            port = $port
            serverPid = $server.Id
            serverStartTicks = $server.StartTime.ToUniversalTime().Ticks
            serverExecutable = [IO.Path]::GetFullPath($pwsh)
            serverScriptPath = $serverScript
            serverContentRoot = $contentRoot
            serverReadyPath = $ready
            serverToken = $serverToken
            expectedEvidenceSchema = [ordered]@{
                commonWrapper = [ordered]@{ sessionId = 'exact prepared sessionId'; url = 'exact prepared nonce-bearing URL'; publish = [ordered]@{ fileCount = 'exact'; totalBytes = 'exact'; manifestSha256 = 'exact' }; capturedUtc = 'UTC at or after preparedUtc'; browser = [ordered]@{ userAgent = 'nonempty Chromium user agent'; version = 'nonempty Chromium version' } }
                evidenceWrapper = [ordered]@{ evidence = [ordered]@{ backend = 'WebGPU'; protocolVersion = 1; workloadId = 'proof.webgpu.asset-independent'; submittedFrames = '>= 1'; diagnostics = @(); pixelProof = 'validated raw sample object' } }
                browserLogWrapper = [ordered]@{ entries = @([ordered]@{ level = 'info|debug'; text = 'bounded text' }) }
                screenshotMetadataWrapper = [ordered]@{ screenshot = [ordered]@{ path = 'absolute path under controllerRoot'; mimeType = 'image/png'; bytes = 'exact'; sha256 = 'exact SHA-256' } }
            }
        }
        Write-BoundedText $sessionPath ($session | ConvertTo-Json -Depth 8 -Compress) 64KB
        [ordered]@{ phase = 'prepared'; sessionId = $sessionId; url = $url; runRoot = $run; outputRoot = $output; controllerRoot = $controller; serverPid = $server.Id; sessionPath = $sessionPath; publish = $publish; expectedEvidenceSchema = $session.expectedEvidenceSchema } | ConvertTo-Json -Depth 8 -Compress
    }
    catch {
        if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
        throw
    }
    finally { $server.Dispose() }
}

function Invoke-Finalize() {
    $session = Read-Session $SessionPath
    $output = Assert-AllowedPath ([string](Get-RequiredProperty $session 'outputRoot' 'Acceptance session')) 'Session output root'
    if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw "Session output root was not found at '$output'." }
    $stopAttempted = $false
    try {
        $evidenceFile = Assert-RunArtifactFile $EvidencePath $session 'Browser evidence' $MaximumEvidenceBytes
        $browserLogFile = Assert-RunArtifactFile $BrowserLogPath $session 'Browser log' $MaximumLogBytes
        $screenshotMetadataFile = Assert-RunArtifactFile $ScreenshotMetadataPath $session 'Screenshot metadata' $MaximumScreenshotMetadataBytes
        $evidenceWrapper = Assert-SessionWrapper (Read-JsonFile $evidenceFile 'Browser evidence' $MaximumEvidenceBytes) $session 'Browser evidence' 'evidence'
        $logWrapper = Assert-SessionWrapper (Read-JsonFile $browserLogFile 'Browser log' $MaximumLogBytes) $session 'Browser log' 'entries'
        $screenshotWrapper = Assert-SessionWrapper (Read-JsonFile $screenshotMetadataFile 'Screenshot metadata' $MaximumScreenshotMetadataBytes) $session 'Screenshot metadata' 'screenshot'
        if ($evidenceWrapper.userAgent -cne $logWrapper.userAgent -or $evidenceWrapper.userAgent -cne $screenshotWrapper.userAgent -or $evidenceWrapper.version -cne $logWrapper.version -or $evidenceWrapper.version -cne $screenshotWrapper.version) { throw 'Browser wrappers must report one exact browser user agent and version.' }
        [void](Assert-RunArtifactFile ([string](Get-RequiredProperty $screenshotWrapper.payload 'path' 'Screenshot metadata screenshot')) $session 'Screenshot' $MaximumScreenshotBytes)
        Assert-WebGpuEvidence $evidenceWrapper.payload
        Assert-BrowserLog ([pscustomobject]@{ entries = $logWrapper.payload })
        $screenshot = Assert-ScreenshotMetadata $screenshotWrapper.payload
        $serverStopped = Stop-SessionServer $session
        $stopAttempted = $true
        $result = [ordered]@{
            schemaVersion = 2; acceptance = 'validated-browser-supplied-evidence'; sessionId = $session.sessionId; url = $session.url; outputRoot = $output; serverStopped = $serverStopped
            publish = $session.publish; browser = [ordered]@{ userAgent = $evidenceWrapper.userAgent; version = $evidenceWrapper.version }
            evidencePath = $evidenceFile; browserLogPath = $browserLogFile; screenshot = $screenshot
        }
        $resultPath = Join-Path ([string]$session.controllerRoot) 'acceptance-result.json'
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
        $selfTestOutputRoot = Join-Path $selfTestRoot 'publish'
        $selfTestControllerRoot = Join-Path $selfTestRoot 'controller'
        $selfTestContentRoot = Join-Path $selfTestOutputRoot 'wwwroot'
        New-Item -ItemType Directory -Path $selfTestContentRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $selfTestControllerRoot -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $selfTestContentRoot 'index.html'), '<!doctype html>', [Text.UTF8Encoding]::new($false))
        $selfTestEvidencePath = Join-Path $selfTestControllerRoot 'browser-evidence.json'
        $selfTestLogPath = Join-Path $selfTestControllerRoot 'browser-log.json'
        $selfTestScreenshotPath = Join-Path $selfTestControllerRoot 'browser-screenshot.png'
        $selfTestScreenshotMetadataPath = Join-Path $selfTestControllerRoot 'browser-screenshot.json'
        $selfTestSessionPath = Join-Path $selfTestRoot 'acceptance-session.json'
        $screenshotBytes = [byte[]]::new(128)
        ([byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)).CopyTo($screenshotBytes, 0)
        [IO.File]::WriteAllBytes($selfTestScreenshotPath, $screenshotBytes)
        $rejected = $false
        try { Assert-ScreenshotMetadata ([pscustomobject]@{ path = $selfTestScreenshotPath; mimeType = 'image/png'; bytes = $screenshotBytes.Length; sha256 = (Get-FileHash -LiteralPath $selfTestScreenshotPath -Algorithm SHA256).Hash }) } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected a header-only PNG to be rejected.' }
        Write-ValidSelfTestPng $selfTestScreenshotPath
        $screenshotMetadata = [ordered]@{ path = $selfTestScreenshotPath; mimeType = 'image/png'; bytes = (Get-Item -LiteralPath $selfTestScreenshotPath).Length; sha256 = (Get-FileHash -LiteralPath $selfTestScreenshotPath -Algorithm SHA256).Hash }
        $selfTestSessionId = [Guid]::NewGuid().ToString()
        $selfTestNonce = New-SecureToken
        $selfTestServerToken = New-SecureToken
        $selfTestPreparedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        $selfTestUrl = "http://127.0.0.1:1/?rekallSession=$selfTestSessionId&rekallNonce=$selfTestNonce"
        $selfTestPublish = Get-PublishIdentity $selfTestOutputRoot
        $selfTestBrowser = [ordered]@{ userAgent = 'Rekall acceptance self-test'; version = 'self-test/1.0' }
        $selfTestEvidence = $literalEvidenceJson | ConvertFrom-Json
        $selfTestWrapper = [ordered]@{ sessionId = $selfTestSessionId; url = $selfTestUrl; publish = $selfTestPublish; capturedUtc = $selfTestPreparedUtc; browser = $selfTestBrowser }
        $evidenceWrapper = [ordered]@{ sessionId = $selfTestWrapper.sessionId; url = $selfTestWrapper.url; publish = $selfTestWrapper.publish; capturedUtc = $selfTestWrapper.capturedUtc; browser = $selfTestWrapper.browser; evidence = $selfTestEvidence }
        $logWrapper = [ordered]@{ sessionId = $selfTestWrapper.sessionId; url = $selfTestWrapper.url; publish = $selfTestWrapper.publish; capturedUtc = $selfTestWrapper.capturedUtc; browser = $selfTestWrapper.browser; entries = @() }
        $screenshotWrapper = [ordered]@{ sessionId = $selfTestWrapper.sessionId; url = $selfTestWrapper.url; publish = $selfTestWrapper.publish; capturedUtc = $selfTestWrapper.capturedUtc; browser = $selfTestWrapper.browser; screenshot = $screenshotMetadata }
        [IO.File]::WriteAllText($selfTestEvidencePath, ($evidenceWrapper | ConvertTo-Json -Depth 12 -Compress), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($selfTestLogPath, ($logWrapper | ConvertTo-Json -Depth 12 -Compress), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($selfTestScreenshotMetadataPath, ($screenshotWrapper | ConvertTo-Json -Depth 12 -Compress), [Text.UTF8Encoding]::new($false))
        $session = [ordered]@{ schemaVersion = 2; sessionId = $selfTestSessionId; nonce = $selfTestNonce; preparedUtc = $selfTestPreparedUtc; runRoot = $selfTestRoot; outputRoot = $selfTestOutputRoot; controllerRoot = $selfTestControllerRoot; publish = $selfTestPublish; url = $selfTestUrl; port = 1; serverPid = [int]::MaxValue; serverStartTicks = 1; serverExecutable = (Get-Command pwsh).Source; serverScriptPath = $PSCommandPath; serverContentRoot = $selfTestContentRoot; serverReadyPath = (Join-Path $selfTestRoot '.server-ready'); serverToken = $selfTestServerToken }
        [IO.File]::WriteAllText($selfTestSessionPath, ($session | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        $script:SessionPath = $selfTestSessionPath
        $script:EvidencePath = $selfTestEvidencePath
        $script:BrowserLogPath = $selfTestLogPath
        $script:ScreenshotMetadataPath = $selfTestScreenshotMetadataPath
        $selfTestFinalize = Invoke-Finalize | ConvertFrom-Json
        if ($selfTestFinalize.acceptance -ne 'validated-browser-supplied-evidence') { throw 'Self-test did not finalize literal empty evidence arrays.' }

        $rejected = $false
        $crossSession = $evidenceWrapper | ConvertTo-Json -Depth 12 | ConvertFrom-Json -DateKind String
        $crossSession.sessionId = [Guid]::NewGuid().ToString()
        try { Assert-SessionWrapper $crossSession $session 'Cross-session evidence' 'evidence' } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected cross-session evidence to be rejected.' }
        $rejected = $false
        $stale = $evidenceWrapper | ConvertTo-Json -Depth 12 | ConvertFrom-Json -DateKind String
        $stale.capturedUtc = ([DateTimeOffset]::Parse($selfTestPreparedUtc).AddSeconds(-1)).ToString('O')
        try { Assert-SessionWrapper $stale $session 'Stale evidence' 'evidence' } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected stale evidence to be rejected.' }
        $rejected = $false
        $handAuthored = $evidenceWrapper | ConvertTo-Json -Depth 12 | ConvertFrom-Json -DateKind String
        $handAuthored.publish.manifestSha256 = '0' * 64
        try { Assert-SessionWrapper $handAuthored $session 'Hand-authored evidence' 'evidence' } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected hand-authored evidence to be rejected.' }
        $rejected = $false
        try { Assert-RunArtifactFile $selfTestSessionPath $session 'Out-of-run controller artifact' 64KB } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected artifacts outside controllerRoot to be rejected.' }
        $rejected = $false
        $currentProcess = Get-Process -Id $PID
        $unrelatedProcessSession = $session | ConvertTo-Json -Depth 12 | ConvertFrom-Json -DateKind String
        $unrelatedProcessSession.serverPid = $PID
        $unrelatedProcessSession.serverStartTicks = $currentProcess.StartTime.ToUniversalTime().Ticks
        try { [void](Stop-SessionServer $unrelatedProcessSession) } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test expected an unrelated PowerShell process to be refused.' }

        $serverPort = Get-FreeLoopbackPort
        $serverReady = Join-Path $selfTestRoot '.server-self-test-ready'
        $serverToken = New-SecureToken
        $server = Start-Process -FilePath (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath, '-Phase', 'Server', '-ContentRoot', $selfTestContentRoot, '-Port', "$serverPort", '-ReadyPath', $serverReady, '-ServerToken', $serverToken)
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while (-not (Test-Path -LiteralPath $serverReady -PathType Leaf) -or (Get-Content -LiteralPath $serverReady -Raw -ErrorAction SilentlyContinue) -cne "ready:$serverToken") {
                $server.Refresh()
                if ($server.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw 'Self-test harness server did not become ready.' }
                Start-Sleep -Milliseconds 50
            }
            $exactServerSession = $session | ConvertTo-Json -Depth 12 | ConvertFrom-Json -DateKind String
            $exactServerSession.serverPid = $server.Id
            $exactServerSession.serverStartTicks = $server.StartTime.ToUniversalTime().Ticks
            $exactServerSession.serverContentRoot = $selfTestContentRoot
            $exactServerSession.serverReadyPath = $serverReady
            $exactServerSession.serverToken = $serverToken
            $exactServerSession.serverExecutable = (Get-Command pwsh).Source
            $exactServerSession.serverScriptPath = $PSCommandPath
            $exactServerSession.port = $serverPort
            if (-not (Stop-SessionServer $exactServerSession)) { throw 'Self-test exact harness server was not stopped.' }
        }
        finally {
            $server.Refresh()
            if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
            $server.Dispose()
        }
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
