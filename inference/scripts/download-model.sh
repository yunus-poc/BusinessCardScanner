#!/usr/bin/env bash
# Downloads the pinned Phi-4-mini-instruct GGUF and verifies its SHA-256.
# Pinned by repository revision AND file hash: a changed upstream file fails verification.
set -euo pipefail

REPO="bartowski/microsoft_Phi-4-mini-instruct-GGUF"
REVISION="7ff82c2aaa4dde30121698a973765f39be5288c0"
FILE="microsoft_Phi-4-mini-instruct-Q4_K_M.gguf"
SHA256="01999f17c39cc3074afae5e9c539bc82d45f2dd7faa3917c66cbef76fce8c0c2"
SIZE_HUMAN="2.5 GB"

MODELS_DIR="$(cd "$(dirname "$0")/.." && pwd)/models"
TARGET="$MODELS_DIR/$FILE"

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

mkdir -p "$MODELS_DIR"

if [[ -f "$TARGET" ]] && [[ "$(sha256_of "$TARGET")" == "$SHA256" ]]; then
  echo "Model already present and verified: $TARGET"
  exit 0
fi

echo "Downloading $FILE ($SIZE_HUMAN) from huggingface.co/$REPO @ ${REVISION:0:7}"
# -C - resumes an interrupted download; HTTPS_PROXY is honoured for corporate proxies.
curl -fL --retry 3 -C - -o "$TARGET.part" \
  "https://huggingface.co/$REPO/resolve/$REVISION/$FILE"

ACTUAL="$(sha256_of "$TARGET.part")"
if [[ "$ACTUAL" != "$SHA256" ]]; then
  echo "SHA-256 mismatch: expected $SHA256, got $ACTUAL. Keeping $TARGET.part for inspection." >&2
  exit 1
fi

mv "$TARGET.part" "$TARGET"
echo "Verified and saved: $TARGET"
