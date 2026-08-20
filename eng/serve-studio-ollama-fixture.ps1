[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][string]$ReadyPath
)

$ErrorActionPreference = 'Stop'

function Test-ChunkedBodyComplete {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][int]$BodyOffset
    )

    $cursor = $BodyOffset
    while ($true) {
        $lineEnd = -1
        for ($index = $cursor; $index -le $Bytes.Length - 2; $index++) {
            if ($Bytes[$index] -eq 13 -and $Bytes[$index + 1] -eq 10) {
                $lineEnd = $index
                break
            }
        }
        if ($lineEnd -lt 0) { return $false }

        $sizeText = [Text.Encoding]::ASCII.GetString($Bytes, $cursor, $lineEnd - $cursor)
        $extensionIndex = $sizeText.IndexOf(';')
        if ($extensionIndex -ge 0) { $sizeText = $sizeText.Substring(0, $extensionIndex) }
        [long]$chunkSize = 0
        if (-not [long]::TryParse(
            $sizeText.Trim(),
            [Globalization.NumberStyles]::HexNumber,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$chunkSize)) {
            throw 'Ollama fixture received a malformed chunk size.'
        }
        if ($chunkSize -lt 0 -or $chunkSize -gt 4MB) {
            throw 'Ollama fixture received an out-of-range chunk size.'
        }

        $cursor = $lineEnd + 2
        if ($chunkSize -eq 0) {
            return $Bytes.Length -ge $cursor + 2 -and
                $Bytes[$cursor] -eq 13 -and $Bytes[$cursor + 1] -eq 10
        }

        $chunkEnd = $cursor + [int]$chunkSize
        if ($Bytes.Length -lt $chunkEnd + 2) { return $false }
        if ($Bytes[$chunkEnd] -ne 13 -or $Bytes[$chunkEnd + 1] -ne 10) {
            throw 'Ollama fixture received malformed chunk framing.'
        }
        $cursor = $chunkEnd + 2
    }
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
try {
    $listener.Start()
    Write-Output "listening:$Port"
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReadyPath), 'ready')
    1..2 | ForEach-Object {
        Write-Output "waiting:$_"
        $client = $listener.AcceptTcpClient()
        try {
            Write-Output "accepted:$_"
            $stream = $client.GetStream()
            $request = [IO.MemoryStream]::new()
            $buffer = [byte[]]::new(8192)
            $headerEnd = -1
            $contentLength = 0
            $chunked = $false
            while ($true) {
                $count = $stream.Read($buffer, 0, $buffer.Length)
                if ($count -le 0) { throw 'Ollama fixture client closed before sending a complete request.' }
                $request.Write($buffer, 0, $count)
                if ($request.Length -gt 4MB) { throw 'Ollama fixture request exceeded 4 MiB.' }
                $bytes = $request.ToArray()
                if ($headerEnd -lt 0 -and $bytes.Length -ge 4) {
                    for ($index = 0; $index -le $bytes.Length - 4; $index++) {
                        if ($bytes[$index] -eq 13 -and $bytes[$index + 1] -eq 10 -and
                            $bytes[$index + 2] -eq 13 -and $bytes[$index + 3] -eq 10) {
                            $headerEnd = $index + 4
                            $headerText = [Text.Encoding]::ASCII.GetString($bytes, 0, $index)
                            foreach ($line in $headerText -split "`r`n") {
                                if ($line.StartsWith('Content-Length:', [StringComparison]::OrdinalIgnoreCase)) {
                                    $contentLength = [int]$line.Substring($line.IndexOf(':') + 1).Trim()
                                }
                                if ($line.StartsWith('Transfer-Encoding:', [StringComparison]::OrdinalIgnoreCase) -and
                                    $line.Contains('chunked', [StringComparison]::OrdinalIgnoreCase)) {
                                    $chunked = $true
                                }
                            }
                            break
                        }
                    }
                }
                if ($headerEnd -ge 0) {
                    if ($chunked -and (Test-ChunkedBodyComplete -Bytes $bytes -BodyOffset $headerEnd)) { break }
                    if (-not $chunked -and $request.Length -ge $headerEnd + $contentLength) { break }
                }
            }
            $request.Dispose()

            $toolCall = if ($_ -eq 1) {
                @{ function = @{ name = 'rekall.context.engine_status'; arguments = @{} } }
            }
            else {
                @{ function = @{ name = 'rekall.workflow.agent_authoring_gauntlet'; arguments = @{
                    projectRoot = [IO.Path]::GetFullPath($ProjectRoot)
                    projectName = 'Installed Studio Agent Proof'
                    sceneName = 'Main'
                } } }
            }
            $responseObject = @{
                model = 'rekall-acceptance'
                message = @{
                    role = 'assistant'
                    content = ''
                    thinking = 'Execute the deterministic installed proof.'
                    tool_calls = @($toolCall)
                }
                done = $true
                done_reason = 'stop'
                prompt_eval_count = 100
                eval_count = 10
            }
            $body = $responseObject | ConvertTo-Json -Depth 12 -Compress
            $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
            $header = "HTTP/1.1 200 OK`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n"
            $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            $stream.Write($bodyBytes, 0, $bodyBytes.Length)
            $stream.Flush()
            Write-Output "responded:$_"
        }
        finally {
            $client.Dispose()
        }
    }
}
finally {
    $listener.Stop()
}
