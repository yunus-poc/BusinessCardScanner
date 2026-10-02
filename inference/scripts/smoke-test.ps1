# Waits for the model, sends the production prompt + schema with a synthetic card several
# times, validates the JSON answer and prints client-side and server-side latency.
# Uses contract/ files, i.e. exactly what the backend sends.
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080',
    [string]$Model = 'phi-4-mini-instruct',
    [int]$Runs = 10,
    [int]$MaxTokens = 128
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$apiKey = ([System.IO.File]::ReadAllText((Join-Path $root 'secrets/llm_api_key.txt'))).Trim()
$system = [System.IO.File]::ReadAllText((Join-Path $root 'contract/system-prompt.txt'))
$card = [System.IO.File]::ReadAllText((Join-Path $root 'contract/sample-card.txt'))
$schema = [System.IO.File]::ReadAllText((Join-Path $root 'contract/classification.schema.json')) | ConvertFrom-Json

Write-Output "Waiting for $BaseUrl/health ..."
$deadline = (Get-Date).AddMinutes(3)
while ($true) {
    try { Invoke-RestMethod "$BaseUrl/health" -TimeoutSec 3 | Out-Null; break }
    catch { if ((Get-Date) -gt $deadline) { throw 'Model did not become ready within 3 minutes.' }; Start-Sleep -Seconds 3 }
}

$body = @{
    model           = $Model
    temperature     = 0
    max_tokens      = $MaxTokens
    messages        = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $card })
    response_format = @{ type = 'json_schema'; schema = $schema }
} | ConvertTo-Json -Depth 20
# Windows PowerShell would otherwise send the body as ISO-8859-1 and mangle Turkish letters.
$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
$headers = @{ Authorization = "Bearer $apiKey" }

$wall = @()
for ($i = 1; $i -le $Runs; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod "$BaseUrl/v1/chat/completions" -Method Post -Headers $headers `
        -ContentType 'application/json; charset=utf-8' -Body $bytes -TimeoutSec 60
    $sw.Stop()
    $content = $response.choices[0].message.content
    $null = $content | ConvertFrom-Json  # throws if the answer is not valid JSON
    $wall += $sw.ElapsedMilliseconds
    $t = $response.timings
    Write-Output ("run {0,2}: {1,5} ms wall | prompt {2,6:N0} ms ({3} tok) | generate {4,6:N0} ms ({5} tok)" -f `
        $i, $sw.ElapsedMilliseconds, $t.prompt_ms, $t.prompt_n, $t.predicted_ms, $t.predicted_n)
}

$sorted = $wall | Sort-Object
$p50 = $sorted[[int][Math]::Floor(($sorted.Count - 1) * 0.5)]
$p95 = $sorted[[int][Math]::Ceiling(($sorted.Count - 1) * 0.95)]
Write-Output ''
Write-Output "Last answer: $content"
Write-Output "Wall clock over $Runs runs: p50 $p50 ms, p95 $p95 ms (run 1 includes prompt-cache warm-up)."
