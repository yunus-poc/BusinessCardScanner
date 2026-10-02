#!/usr/bin/env bash
# Creates secrets/llm_api_key.txt with a random 256-bit key. Give the same value to the
# backend as Ai__ApiKey (environment variable or secret store), never to the mobile app.
set -euo pipefail

SECRETS_DIR="$(cd "$(dirname "$0")/.." && pwd)/secrets"
KEY_FILE="$SECRETS_DIR/llm_api_key.txt"

if [[ -f "$KEY_FILE" && "${1:-}" != "--force" ]]; then
  echo "$KEY_FILE already exists. Re-run with --force to rotate (then update the backend too)." >&2
  exit 1
fi

mkdir -p "$SECRETS_DIR"
umask 077
head -c 32 /dev/urandom | base64 | tr '+/' '-_' | tr -d '=\n' > "$KEY_FILE"
chmod 600 "$KEY_FILE"
echo "Wrote $KEY_FILE (permissions 600)."
