# Full-response speech preparation latency

The user selected complete-response Breeze synthesis after hearing voice/prosody differences and missing mouth motion with sentence streaming. Normal gameplay keeps `EnableSocialSentenceStreaming=false`. The experimental streaming changes were removed. Audio is generated once for the entire accepted answer; both mouth cues and word alignment remain mandatory for prepared playback.

## Production change

`tools/BreezeTtsTrial/coordinator.py` runs Rhubarb and forced word alignment concurrently against the same immutable WAV. The coordinator joins both jobs before publishing the prepared manifest, handling cancellation, or removing failed preparation artifacts. Alignment uses half the logical CPUs, capped at eight threads and floored at one. Rhubarb remains the same pocketSphinx recognizer with two threads. The TTS model, voice anchor, complete text, audio normalization and native playback behavior are unchanged.

The evidence JSON now records `visemePreparationMilliseconds`, `alignmentPreparationMilliseconds`, `presentationPreparationMilliseconds`, and `alignmentThreads`. Each worker still validates the exact WAV, transcript, identity, model and output timeline. The existing bounded fallback remains available if preparation fails.

## Controlled preparation benchmark

Hardware: AMD Ryzen 9 9950X, 16 physical / 32 logical cores. One complete generated WAV was reused for every run; synthesis was not repeated inside the comparison.

| Step | Threads | Wall seconds |
| --- | ---: | ---: |
| Rhubarb | 2 | 2.646 |
| Rhubarb | 4 | 2.605 |
| Rhubarb | 8 | 2.641 |
| Alignment | 1 | 4.199 |
| Alignment | 4 | 2.423 |
| Alignment | 8 | 2.152 |
| Parallel Rhubarb + alignment | 4 + 8 | 2.847 |

All 46 mouth cues and all 26 word timestamps were identical across the compared thread counts and the parallel run. More Rhubarb threads offered no meaningful benefit, so production retains two. The old sequential preparation sum was 6.844 seconds. The benchmark is reproducible with `tools/BreezeTtsTrial/benchmark_preparation.py` and a newly generated complete WAV plus its evidence JSON. Raw temporary timing files are deleted automatically. Compact results: `work/speech-preparation-benchmark.json`.

## Native observations

The same Gothway Garden fixture, NPC and player prompt were used: `Hi, how are you? Tell me in three short sentences.` Generated answers differed, so these are gameplay observations rather than identical-audio paired measurements.

| Mode | LLM completion | First nonzero audio | Audio duration |
| --- | ---: | ---: | ---: |
| Previous buffered path | 2.111 s | 12.706 s | 6.56 s |
| Previous buffered repeat | 1.979 s | 10.325 s | 4.64 s |
| Parallel preparation | 2.138 s | 8.349 s | 5.36 s |

The optimized request `c0b78a223f1e434fbfc40b700e39f153` spent 2.650 seconds in synthesis, 3.227 seconds preparing mouth cues and 2.534 seconds aligning words. Combined preparation was 3.227 seconds. Native acceptance confirmed no words before audio, partial aligned word reveal during audio, active mouth animation, one social commit, one canonical player turn, and restoration of the test fixture. Log: `work/speech-latency-optimized.log`.

## Prompt-cache comparison

The local llama.cpp server already caches prompt prefixes by default. A captured real dialogue request was replayed with cache disabled, explicitly enabled, and its existing default. Temperature and seed were fixed for this isolated comparison; inference still generated a fresh response each time.

- Disabled, warm repeats: about 318–321 ms prompt processing and 0.740–0.743 s total completion.
- Enabled: 2,278 cached tokens, one evaluated prompt token, 17–20 ms prompt processing and 0.439–0.449 s total completion.
- Existing default: the same 2,278-token reuse and approximately 0.45 s completion.
- The first uncached run was slower (1.260 s total); do not attribute all of that cold-start cost to caching.
- Outputs were stable within each mode but differed between cached and uncached inference. This does not establish identical wording or a quality improvement.

These are best-case identical-prompt replays, not multi-turn gameplay savings. The captured natural gameplay response had zero reused prefix tokens. The native opening greeting of a first encounter is existing game text sent to TTS, not a Gemma context prefill. Returning greetings can use Gemma. Each player turn submits updated context; routing and other requests can limit prefix reuse. No answer cache, stale context, or new background model warmup was introduced.

Reproduction tool: `tools/BenchmarkDialogueCache.py`; compact results: `work/dialogue-cache-benchmark.json`. The opt-in native latency probe accepts `POTIB_SPEECH_LATENCY=buffered`; `POTIB_SPEECH_CAPTURE=<absolute output prefix>` captures one disposable fixture request/response for replay.

## Validation

- Red regression: concurrent preparation test failed on the old sequential implementation.
- 68 coordinator, Rhubarb and alignment tests passed, including overlap, publication barriers, cancellation and cleanup after alignment failure.
- RuntimeCompile: zero warnings and errors. Unity build and installation passed.
- Final Core run: 2156/2156 passed.
- Distribution and installed bundle match: `8836C14E174D74ED1578A3E8DDC344F0CE634B44B0F8BA1ABE15516E5F86CF6E`. The production speed improvement is in the local Python coordinator loaded by the speech launcher.
- Final rendered-portrait acceptance passed with Exit 0: actual nonzero mixer playback, validated mouth cues and forced word alignment at onset, changing mouth shapes on the rendered portrait, word reveal, one mood commit, and restoration of the original fixture. Request `de268f0d3bf343cab925addffa40c33b`; log `work/speech-full-response-portrait-pass.log`. Its in-speech screenshot was also inspected.

This work does not change the 17/22 core-system acceptance count or claim completion of unrelated guard, arrival or companion work. Changes remain local and uncommitted.
