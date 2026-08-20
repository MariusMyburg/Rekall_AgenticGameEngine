[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][string]$ReadyPath
)

$ErrorActionPreference = 'Stop'
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
                            }
                            break
                        }
                    }
                }
                if ($headerEnd -ge 0 -and $request.Length -ge $headerEnd + $contentLength) {
                    break
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
