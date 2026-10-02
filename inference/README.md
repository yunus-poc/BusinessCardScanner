# Inference service

Self-hosted **Phi-4-mini-instruct** (Q4_K_M GGUF) behind **llama.cpp `llama-server`**, an OpenAI-compatible
HTTP API with JSON-schema-constrained output. Only `BusinessCard.Api` calls it; the mobile app never does.

```
BusinessCard.Api ──HTTPS/private network, Bearer key──▶ llama-server :8080 ──▶ Phi-4-mini (resident in RAM/VRAM)
```

| Path | Purpose |
|---|---|
| `docker-compose.yml` | CPU service (default) |
| `docker-compose.gpu.yml` | NVIDIA override: CUDA image, all layers on GPU, flash attention |
| `contract/system-prompt.txt` | **Production** system prompt (embedded by the backend at build time) |
| `contract/classification.schema.json` | **Production** output schema: line ids per field, short keys |
| `contract/sample-card.txt` | Synthetic card used by the smoke test |
| `scripts/download-model.*` | Downloads the pinned GGUF and verifies SHA-256 |
| `scripts/new-api-key.*` | Generates the shared API key |
| `scripts/smoke-test.*` | Readiness wait, schema-constrained requests, latency report |

The `contract/` files are the single source of truth: the smoke test and the
backend both send exactly these, so a prompt change is one reviewed edit.

## Quick start (local, CPU)

> **Manual:** these steps download **2.5 GB** and start a container. Nothing here has been run for you.

```bash
cd inference
bash scripts/download-model.sh        # Windows: .\scripts\download-model.ps1
bash scripts/new-api-key.sh           # Windows: .\scripts\new-api-key.ps1
cp .env.example .env                  # set LLM_THREADS to the host's physical core count
docker compose up -d
bash scripts/smoke-test.sh            # Windows: .\scripts\smoke-test.ps1   (bash version needs jq)
```

Expected smoke-test output: one valid JSON answer per run, e.g.
`{"person":[2],"company":[1],"title":[3],"dept":[4],"address":[8,9],"city":"İstanbul","state":null,"country":null}`,
and p50/p95 wall-clock latency. Run 1 is slower: it fills the prompt cache.

Then give the backend the same key (read as `Ai:ApiKey`): `Ai__ApiKey=<contents of secrets/llm_api_key.txt>`.

## Why these settings

| Setting | Reason |
|---|---|
| llama.cpp server | Single binary/container, model loaded once and kept resident, OpenAI-compatible API, GBNF/JSON-schema constrained decoding, CPU or GPU. vLLM would need a GPU and more operations work for ~60 output tokens per request |
| Q4_K_M quantization | ~2.5 GB, a good accuracy/speed balance for 3.8B models; not yet benchmarked; confirm on real cards |
| `--load-mode mmap+mlock`, `memlock: -1` | Model can never be swapped out, so no cold starts |
| `--parallel 4`, `--ctx-size 6144` | 4 concurrent scans with continuous batching, 1,536 tokens each (a card prompt is ~300–700) |
| Prompt order: fixed system prompt first, card last | llama-server's prompt cache (`cache_prompt`, on by default) reuses the identical prefix, so only the card lines are processed per request. On CPU, prompt processing dominates latency |
| `--no-webui`, `--no-slots` | No browser UI; `/slots` can expose in-flight prompts, i.e. card PII |
| `--metrics` | Prometheus endpoint (`/metrics`, needs the API key) for token rates and queue depth |
| `--api-key-file` + Docker secret | Key not visible in `docker inspect` or process listings |
| `--timeout 30` | Ceiling for stuck connections; the backend gives up much earlier (~2.5 s) and falls back |
| Default log verbosity | Logs requests and timings, **not** prompt text. Never enable `-lv`/verbose logging in production; it would log card contents |

## Sizing and expected latency

**Estimates to be replaced by measurements** (run the smoke test on the target host). Per request:
~150 system-prompt tokens (cached after the first request), ~100–300 card tokens, ~40–60 output tokens.

| Host | Memory | Expected per-request latency |
|---|---|---|
| NVIDIA T4 / L4 / A10 / RTX 4000-class, ≥ 8 GB VRAM | ~3.5 GB VRAM | **~0.3–0.8 s**: meets the 1–2 s preview target |
| 8–16 modern server cores (AVX2/AVX-512), CPU only | ~4 GB RAM | **~2–5 s**: prompt processing ~50–150 tok/s, generation ~10–25 tok/s |

**A CPU-only host will usually miss the 1–2 s target.** With the default backend timeout (2.5 s) many
requests would fall back to deterministic parsing. Either provision a GPU, or raise the timeout after measuring
and accept slower previews. The model has no per-request fee, but the host runs 24/7 to stay warm: that
compute (GPU VM or a large CPU VM) is a real, recurring cost.

## Network placement (production)

> **Manual (infrastructure team):**
> - Run on a VM/host in a **private subnet**; no public IP, no internet ingress.
> - Allow inbound `8080/tcp` **only from the API's hosts/subnet** (NSG / security group / firewall).
> - If API and LLM run on the same Docker host, put both on one user-defined network and remove the `ports:`
>   mapping entirely.
> - If traffic crosses networks you do not fully control, terminate TLS in front of llama-server (reverse proxy
>   or `--ssl-key-file` / `--ssl-cert-file`) and use `https://` in the backend's `Ai:BaseUrl`.
> - Store the API key in your secret store (Key Vault or equivalent) for both sides; rotate with
>   `new-api-key --force`, then update the backend, then restart the container.

## Pinning the image

`:server` / `:server-cuda` are moving tags. For production, pin a tested build:

```bash
docker pull ghcr.io/ggml-org/llama.cpp:server
docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/ggml-org/llama.cpp:server
# then in .env:  LLAMA_IMAGE=ghcr.io/ggml-org/llama.cpp@sha256:<digest>
```

Re-run the smoke test (and check real-card accuracy) before moving the pin.

## Model provenance

- **Base model:** `microsoft/Phi-4-mini-instruct`, MIT licence.
- **GGUF used:** `bartowski/microsoft_Phi-4-mini-instruct-GGUF`, file `microsoft_Phi-4-mini-instruct-Q4_K_M.gguf`,
  repo revision `7ff82c2aaa4dde30121698a973765f39be5288c0`, SHA-256
  `01999f17c39cc3074afae5e9c539bc82d45f2dd7faa3917c66cbef76fce8c0c2`, 2,491,874,688 bytes.
- This is a **community conversion**. If your security policy requires first-party artefacts, convert the
  official weights yourself with llama.cpp (`convert_hf_to_gguf.py`, then `llama-quantize … Q4_K_M`), record your
  own hash, and update `scripts/download-model.*` / `LLM_MODEL_FILE`.
- GGUF files are data, not code, but llama.cpp parses them, which is another reason to pin by hash and keep the
  image up to date.

## GPU

```bash
docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d
```

Requires the NVIDIA driver and NVIDIA Container Toolkit on the host. Check with `docker compose logs llm` that all
layers are offloaded to the GPU.

## Operations

| Task | Command |
|---|---|
| Status / readiness | `docker compose ps` (healthcheck on `/health`: 503 while loading, 200 ready) |
| Logs | `docker compose logs -f llm` (rotated: 3 × 10 MB) |
| Metrics | `curl -H "Authorization: Bearer $KEY" http://127.0.0.1:8080/metrics` |
| Restart | `docker compose restart llm` (model reload takes seconds to a minute) |
| Swap model | download new GGUF to `models/`, set `LLM_MODEL_FILE` / `LLM_MODEL_ALIAS`, `docker compose up -d` |

## Troubleshooting

| Symptom | Fix |
|---|---|
| Container restarts, `failed to mlock` | Missing `ulimits: memlock: -1`, or the host forbids it; as a last resort set `--load-mode mmap` |
| `401` from the API | Key mismatch between `secrets/llm_api_key.txt` and the backend's `Ai__ApiKey` |
| `/health` stays 503 | Still loading, or the model path is wrong: `docker compose logs llm` |
| Very slow on CPU | `LLM_THREADS` should equal physical cores; check no other heavy process; consider GPU |
| Answers not valid JSON | Should not happen with schema-constrained decoding; check the request sends `response_format` |
