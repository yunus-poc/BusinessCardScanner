# Downloads the pinned Phi-4-mini-instruct GGUF and verifies its SHA-256.
# Pinned by repository revision AND file hash: a changed upstream file fails verification.
$ErrorActionPreference = 'Stop'

$Repo = 'bartowski/microsoft_Phi-4-mini-instruct-GGUF'
$Revision = '7ff82c2aaa4dde30121698a973765f39be5288c0'
$File = 'microsoft_Phi-4-mini-instruct-Q4_K_M.gguf'
$Sha256 = '01999f17c39cc3074afae5e9c539bc82d45f2dd7faa3917c66cbef76fce8c0c2'

$modelsDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'models'
$target = Join-Path $modelsDir $File
New-Item -ItemType Directory -Force $modelsDir | Out-Null

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 $Path).Hash.ToLowerInvariant() }

if ((Test-Path $target) -and ((Get-Sha256 $target) -eq $Sha256)) {
    Write-Output "Model already present and verified: $target"
    return
}

Write-Output "Downloading $File (2.5 GB) from huggingface.co/$Repo @ $($Revision.Substring(0, 7))"
# curl.exe ships with Windows 10+; -C - resumes an interrupted download.
& curl.exe -fL --retry 3 -C - -o "$target.part" "https://huggingface.co/$Repo/resolve/$Revision/$File"
if ($LASTEXITCODE -ne 0) { throw "Download failed (curl exit code $LASTEXITCODE)." }

$actual = Get-Sha256 "$target.part"
if ($actual -ne $Sha256) {
    throw "SHA-256 mismatch: expected $Sha256, got $actual. Keeping $target.part for inspection."
}

Move-Item -Force "$target.part" $target
Write-Output "Verified and saved: $target"
