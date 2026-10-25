# All-GPU model fit — experiments 149 and 150

The user's priority is simultaneous GPU residence of dialogue, decisions and TTS
on the RTX4070 SUPER12GB, alongside DFU. No permanent model/device selection was
changed by these experiments. The original CPU Qwen7B and GPU D3-mini services were
restored afterward; temporary GPU dialogue and TTS servers were stopped.

## Tested combinations

| Combination | Observed result |
| --- | --- |
| Qwen2.5-3B-Instruct Q4_K_M + D3-mini Q4_K_M + Breeze Q4 + loaded DFU | About200MiB free under concurrent load; requests timed out. Preserve as failed experiment149, not a working fit. |
| Qwen2.5-3B-Instruct Q4_K_M + D3-Nano Q8_0 + Breeze Q4 + loaded DFU | Concurrent inference completes, followed by the native180-second performance probe. Minimum free memory in the43-sample inference window is1118MiB. This is a measured candidate, not a world-scale memory guarantee. |

Both decision runs keep the projector, context4096, batch/ubatch2048 and GPU layers99.
The dialogue trial uses explicit CUDA0 with GPU KV/operation offload and context4096.
No context truncation, CPU layer fallback, vision removal or model swapping was used
to obtain the successful150 result. CPU Qwen7B remained available on8080 for the
separate comparison; the measured GPU dialogue requests went to3B on8081.

Experiment150 measured one short concurrent text reply in344ms, D3 in109ms,
and first streamed PCM in319ms. The roughly4.72-second speech clip finished synthesis
in2.21seconds. Baseline first PCM was561ms. These are individual local measurements,
not guarantees for long replies, uncached voices or arbitrary cities. Audio was
generated but not played through the game mixer. A loaded game plus external model
requests is not an end-to-end native conversation acceptance test.

Raw evidence: `work/breeze-tts-20261021/all-gpu-149/` and `all-gpu-150/`, including
the preserved timeout, owners, exact commands, model metadata, HTTP requests/replies,
memory samples, native performance log and restoration receipt.

## Dialogue and extraction quality

Eighteen byte-identical requests were sent once to each model: twelve authored
social snapshots and six frozen experimental one-pass extraction requests.
All36 responses returned HTTP200. The social t1/t2 pairs are independently authored
snapshots, not generated roll-forward. Token budgets fit4096 for both tokenizers.
The CPU/GPU placement differs, so latency cannot isolate model-size effects.

The3B model is more grounded in several direct/quoted-threat and unknown-cause
cases, but reverses roles in one quoted-threat follow-up. Both models invent some
unsupported facts. Both fail several complete-chain and non-current-request cases.
The3B outputs especially lose typed durations and ordering; the7B baseline also
omits destinations/steps. This sample establishes no global winner or native task
acceptance. Exact first responses and independent review remain preserved.

## Decision quality

Forty identical frozen D3 requests compare Nano with retained Mini first responses:
sixteen willingness cases, twelve authored social cases and twelve resentment cases.
All Nano responses return HTTP200 once, identify the exact Nano model, and choose
offered options. They agree on34/40 choices; agreement is not accuracy.

All sixteen willingness choices match Mini. Both wrongly ACCEPT three reported,
quoted or hypothetical requests, while declining the negated control. The six
social differences are largely plausible choices from the supplied facts rather
than demonstrated Nano regressions. The resentment WAIT fixtures have no refusal
oracle and show no causal personality effect. See `review-decisions.md`; its
initial five-difference counting error is preserved in `review-decisions-v1.md`.

## Opt-in support and remaining work

`tools/ResolveLocalDecisionModel.ps1` now resolves pinned `mini`/`nano` registries.
`StartLocalDecision.ps1 -Profile nano` validates both artifact hashes/sizes and the
existing service identity. Without explicit selection or `active-decision-model.json`,
Mini remains the default. Pure profile tests pass after the missing-resolver RED;
the launcher reuses the actual Nano service and rejects a requested Mini profile
without replacing the unrelated current listener. No active selection file was written.

The all-GPU candidate meets the measured hardware target. Before making it the
normal gameplay profile, compare stronger small dialogue models or verify the
actual multi-stage extraction path and native conversation/TTS integration. The
failed one-pass diagnostic is not evidence that every supported production task
fails. Keep earlier7B/Mini models and exact rollback metadata.

## Model sources and pins

- [Official Qwen3B GGUF](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF):
  revision `7dabda4d13d513e3e842b20f0d435c732f172cbe`, Q4_K_M2104932768bytes,
  SHA256 `626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d`.
  This3B release uses the Qwen Research License; do not infer Apache-2.0 from other sizes.
- [Official D3-Nano card](https://huggingface.co/vllm-sr/d3-nano) and
  [GGUF conversion](https://huggingface.co/ggml-org/vllm-d3-nano-GGUF): revision
  `50dea2057df10c4b393559a155ab3bf0bc7532c6`, Q8_02552358464bytes,
  SHA256 `4867d306fd22aec8c5ebd1851b7f1074f480702655e8000ceedf0c47c6a6f337`.
  Q8 projector364664448bytes, SHA256
  `b778993a051d604757dda94a554ba870bf6e685909c44e7399d71ffb73e0e58f`.
- D3-Lite was researched but not downloaded or tested. No claim that a smaller
  D3 alone allows the original7B plus TTS and DFU to fit.
