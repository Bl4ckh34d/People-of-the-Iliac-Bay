# GPU dialogue/speech residency and text-only CPU D3

## Verified hardware trials

Hardware: RTX4070 SUPER12GiB, Ryzen9 9950X. Qwen is the existing Qwen2.5-7B-Instruct-abliterated-v2 Q4_K_M with4096-token context. Speech uses warmed Breeze-TTS-2 Q4, existing approved `npcvoice-01` instruction/seed and hash-pinned reference. No voice design, listening acceptance, portrait work, installed bundle or persistent model placement changed.

| Condition | Qwen complete response time | Qwen generation rate | Concurrent TTS first PCM data | Sampled minimum free VRAM |
| --- | --- | --- | --- | --- |
| DFU closed, D3 unloaded | 0.53–0.81s across three saved prompts | 90.6–91.8 tokens/s | 0.255s | 1233MiB |
| Loaded Gothway Garden fixture, D3 unloaded | 0.44–0.70s across three saved prompts | 82.2–84.0 tokens/s | 0.300s | 915MiB |

During concurrent Qwen/TTS computation, Qwen's separate response took0.73s without DFU and0.99s with the loaded fixture, at44.6 and42.3 tokens/s respectively. The fixed4.8-second speech clip finished synthesis in2.45s without DFU and2.95s with the fixture. First PCM arrival is not first audible mixer output. These concurrent requests used an already known speech line while another saved prompt generated text; they do not measure integrated LLM-first-sentence-to-TTS streaming latency.

The exact saved prompts are hardware replicas of package106 requests; their new wording is not substituted into that earlier semantic comparison. Replies have different lengths and cache state, so raw response time is not a controlled CPU/GPU speed ratio. No automatic quality score is inferred.

Memory was sampled through `nvidia-smi` during inference at approximately100ms intervals:48 samples without DFU and55 with the loaded fixture. Observed peaks were10765 and11083MiB. Samples can miss shorter spikes and do not capture server-loading peaks. The native fixture rendered at1280x720 in windowed mode; larger cities, other mods, higher resolution, longer context and other applications remain untested.

## Native fixture evidence

Owned `PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden` ran `TownTest.ps1 -PerformanceProbe`. After the first loaded city sample, the parent issued the hardware requests on an isolated GPU Qwen port8081 and existing speech port8094. Three60-second city phases completed and emitted `PEOPLE_OF_THE_ILIAC_BAY_PERFORMANCE_PASS` and `PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS`. Median frame times were about13.3ms, with phase maxima154.23/106.87/56.84ms. This establishes bounded coexistence under that fixture, not full-game stability or native dialogue integration: normal game dialogue remained configured for CPU Qwen8080.

Attempt108 was an operator observer failure: an incompatible log sharing mode triggered cleanup before loaded-scene measurement. It is not a GPU or game crash finding. Attempt109 corrected only shared log reading, used new paths, and retained the failed attempt.

## D3 CPU alternative

A text-only D3 replica ran separately on CPU with no projector and explicit device-none, zero GPU layers, no KV/operation offload,4096-token context, one slot, eight threads, and physical/logical batch2048. All12 requests returned HTTP200, all passed the existing production wire/Core gate, and all12 selected IDs matched their earlier GPU D3 counterparts. Mean time was2447.5ms, range2149.5–2805.1ms. This does not test CPU vision, long contexts, complex plans, or the three-model/game combination concurrently.

Attempt110 used the generic dialogue launcher's512-token physical batch. D3 rejected all requests before a selection because the whole non-causal input had to fit that batch. All errors are preserved. Attempt111 matched production D3's2048 batch and contains the actual CPU selections. This is a configuration correction, not a semantic reroll.

## Architecture implication

GPU Qwen plus GPU TTS is feasible in the tested scene and materially faster than the existing CPU dialogue observations. Its roughly0.9GiB sampled margin is limited. Keep D3 optional. Selective CPU D3 is now a measured candidate for retaining decision assistance without keeping another large GPU model resident; its approximately2.45-second selection cost should not be imposed on every ordinary utterance by default. Compare complex planning and then verify the integrated native route before selecting persistent settings. Gemma12B Q4 remains substantially larger than Qwen7B Q4 and does not gain simultaneous-residency headroom.

The test stopped only owned temporary ports8081/8092 and the owned native fixture. Original CPU Qwen8080 and TTS stayed up, GPU D3 was restored on8093 after each GPU trial, and the active-model file hash remained unchanged. No permanent switch is claimed.

## Evidence

Repository directory `work/npc-factor-evaluation/paired-selector-106/` contains:

- `gpu-residency-107/results.json`, `gpu-memory.jsonl`, exact Qwen request/response rows and WAVs;
- `gpu-residency-with-game-109/` with the same measurements plus native `town-test.log`;
- original108 observer logs and `gpu-108-observer-failure.md`;
- `cpu-decision-110/` configuration failures and `cpu-decision-111/` raw responses, Core plans and summary;
- launch-owner records, server logs and restoration JSON files;
- the operator scripts, which refuse reused output directories and restore services in `finally` blocks.
