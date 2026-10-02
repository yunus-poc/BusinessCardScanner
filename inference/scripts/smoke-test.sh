#!/usr/bin/env bash
# Waits for the model, sends the production prompt + schema with a synthetic card several
# times, validates the JSON answer and prints client-side and server-side latency.
# Requires: curl, jq.
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"
MODEL="${MODEL:-phi-4-mini-instruct}"
RUNS="${RUNS:-10}"
MAX_TOKENS="${MAX_TOKENS:-128}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API_KEY="$(tr -d '\r\n' < "$ROOT/secrets/llm_api_key.txt")"

echo "Waiting for $BASE_URL/health ..."
for _ in $(seq 1 60); do
  curl -fsS "$BASE_URL/health" >/dev/null 2>&1 && break
  sleep 3
done
curl -fsS "$BASE_URL/health" >/dev/null || { echo "Model did not become ready." >&2; exit 1; }

BODY="$(jq -n \
  --arg model "$MODEL" \
  --argjson max "$MAX_TOKENS" \
  --rawfile system "$ROOT/contract/system-prompt.txt" \
  --rawfile card "$ROOT/contract/sample-card.txt" \
  --slurpfile schema "$ROOT/contract/classification.schema.json" \
  '{model: $model, temperature: 0, max_tokens: $max,
    messages: [{role: "system", content: $system}, {role: "user", content: $card}],
    response_format: {type: "json_schema", schema: $schema[0]}}')"

walls=()
for i in $(seq 1 "$RUNS"); do
  start=$(date +%s%3N)
  response="$(curl -fsS "$BASE_URL/v1/chat/completions" \
    -H "Authorization: Bearer $API_KEY" -H 'Content-Type: application/json' --data "$BODY")"
  end=$(date +%s%3N)
  content="$(jq -r '.choices[0].message.content' <<<"$response")"
  jq -e . >/dev/null <<<"$content" || { echo "run $i: answer is not valid JSON: $content" >&2; exit 1; }
  walls+=($((end - start)))
  jq -r --arg i "$i" --arg wall "$((end - start))" \
    '"run \($i): \($wall) ms wall | prompt \(.timings.prompt_ms|floor) ms (\(.timings.prompt_n) tok) | generate \(.timings.predicted_ms|floor) ms (\(.timings.predicted_n) tok)"' \
    <<<"$response"
done

sorted=($(printf '%s\n' "${walls[@]}" | sort -n))
n=${#sorted[@]}
p50=${sorted[$(( (n - 1) / 2 ))]}
p95=${sorted[$(( ((n - 1) * 95 + 99) / 100 ))]}
echo
echo "Last answer: $content"
echo "Wall clock over $RUNS runs: p50 ${p50} ms, p95 ${p95} ms (run 1 includes prompt-cache warm-up)."
