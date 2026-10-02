# Creates secrets/llm_api_key.txt with a random 256-bit key. Give the same value to the
# backend as Ai__ApiKey (environment variable or secret store), never to the mobile app.
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$secretsDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'secrets'
$keyFile = Join-Path $secretsDir 'llm_api_key.txt'

if ((Test-Path $keyFile) -and -not $Force) {
    throw "$keyFile already exists. Re-run with -Force to rotate (then update the backend too)."
}

New-Item -ItemType Directory -Force $secretsDir | Out-Null
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
[System.IO.File]::WriteAllText($keyFile, $key)
Write-Output "Wrote $keyFile. Restrict access to it (only the deploy account needs it)."
