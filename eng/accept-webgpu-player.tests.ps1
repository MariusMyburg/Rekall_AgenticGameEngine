[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$harness = Join-Path $PSScriptRoot 'accept-webgpu-player.ps1'

if (-not (Test-Path -LiteralPath $harness -PathType Leaf)) {
    throw "Expected WebGPU acceptance harness at '$harness'."
}

& $harness -Phase SelfTest
if (-not $?) {
    throw 'WebGPU acceptance harness self-test failed.'
}

Write-Output 'WebGPU acceptance harness self-test passed.'
