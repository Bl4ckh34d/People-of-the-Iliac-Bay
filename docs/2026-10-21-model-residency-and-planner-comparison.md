# Model residency and optional planner comparison

## Decision

The user wants GPU dialogue and GPU speech if practical. D3 must remain optional and must earn its cost through a direct comparison of identical scenarios. Do not make a planner mandatory for ordinary conversation. Preserve the current services until an explicitly controlled comparison can switch them safely.

Compare Qwen alone, Qwen plus D3, and, if compatible, Gemma 4 12B IT uncensored alone. Use the same initial game facts, action capabilities, histories, scenarios and semantic acceptance criteria. Record model-specific templates and any arm-specific orchestration. Include ordinary conversation, mixed refusal plus future invitation, threats, unavailable actions, corrections, and three-to-five-step tasks. Measure semantic decisions separately from structural validity, final wording, native effects, latency and peak dedicated GPU memory. Preserve first outputs and failures. Do not tune to individual examples or replace failures through rerolls.

## Read-only evidence, 21 October 2026

The estimates below are the preserved initial assessment. Later controlled trials supersede the statements that concurrent residency and complex planning remain wholly untested: [GPU Qwen/TTS and CPU D3 measurements](2026-10-21-gpu-dialogue-speech-residency.md) and [complex-planning comparison112/114](2026-10-21-complex-planning-comparison.md). Their bounded scope still does not establish a global model winner or integrated native GPU dialogue acceptance.

- GPU: RTX 4070 SUPER, 12,282 MiB reported total. A live snapshot reported 10,069 MiB used and 1,929 MiB free, with CPU Qwen, GPU D3 and GPU Breeze resident and no DFU process. This is not a measurement of GPU Qwen plus TTS.
- Current Qwen: `Qwen2.5-7B-Instruct-abliterated-v2.Q4_K_M.gguf`, 4,683,074,080 bytes, CPU-only launch and 4,096-token context.
- Windows GPU process allocation counters reported Breeze at approximately 4,321 MiB and D3 at approximately 4,347 MiB. These counters are not a synchronized physical-residency ledger and must not be summed with all desktop allocations as exact physical usage.
- Historical `work/d3-mini-trial-20261020/gpu-game-qwen-baseline.jsonl` records approximately 4.61 GiB dedicated allocation for the Qwen process and approximately 0.30 GiB for that particular native fixture. The current CPU/GPU benchmark confirms the same Qwen model, but this does not establish a current simultaneous speech/game peak.
- Planning estimate: GPU Qwen plus warmed Breeze occupies roughly 9 GiB before other applications, DFU, and workload-dependent peaks. Approximately 3 GiB remains before those costs. A real simultaneous workload is still required.
- Publisher listing for `TrevorJS/gemma-4-12B-it-uncensored-GGUF` gives Q4_K_M at 7.38 GB (about 6.87 GiB). Adding current Breeze allocation leaves under 1 GiB before Gemma KV cache, working buffers, Windows and DFU. Therefore full simultaneous GPU residency is not a credible default on this 12 GiB card. Partial CPU offload or alternating residency remains testable.

Gemma may improve decision quality, but that is unmeasured on this NPC corpus. Removing refusal behavior does not establish better planning or appropriate NPC self-protection. A single model also does not automatically eliminate contradictory outputs across multiple calls. The harness must retain one validated action plan as authority for response generation and execution.

Sources: https://huggingface.co/TrevorJS/gemma-4-12B-it-uncensored-GGUF and https://huggingface.co/google/gemma-4-12B-it . No model download, service switch, or inference was performed for this assessment.
