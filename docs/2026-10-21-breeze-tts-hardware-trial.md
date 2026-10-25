# Breeze TTS trial after framework completion

Status: the user accepted the baseline race timbres and the first accent audition. Both twenty-sample batches completed without synthesis errors. They are controlled voice auditions, not complete native conversation or streaming-playback acceptance. Native initial greetings have been queued and played; earlier gender failures remain preserved. The latest instruction selects CPU Qwen, GPU D3 and GPU Breeze and requests a short loaded-game trial after the revised accent gallery. Keep STT off. After listening, resume overnight personality/framework evaluation and implementation; the earlier pause instruction is superseded.

## Measured audition and CPU-placement checkpoint

### Completed native CPU-Qwen trial

`work/breeze-tts-20261021/native-cpu-pilot-03/native-player.log` records `BREEZE_TTS_NATIVE_PILOT_PASS`: two actual NPCs, four player turns, two Qwen-backed replies and two scripted native offers, plus both initial greetings. Six speech lines reached verified Unity playback start and completion; replay and exact owned-save restoration/ephemeral cleanup passed. CPU Qwen PID 46696, GPU D3 PID 47236 and warm GPU Breeze PID 29016 stayed resident without a swap. The owned game exited after the test. This proves the tested mixed-device pipeline, not broad semantic quality or live portrait animation.

`acceptance-summary.json` preserves measured per-line data. Text-ready to Unity playback: greetings 1.27/1.46 seconds; short native offers 2.09/2.12 seconds; longer Qwen replies 5.24/5.83 seconds. Breeze synthesis itself took 1.11–5.63 seconds for 1.12–12.08 seconds of audio. The first free Qwen reply's server processing was 10.71 seconds (6.04 prompt + 4.66 generation), excluding earlier classifiers. Peak observed aggregate GPU usage was 10,603 MiB of 12,282 MiB. The native bridge still waits for a complete WAV; 0.33–0.59 second first server PCM is not native streaming playback.

The earlier `native-cpu-pilot-01` failed and remains preserved. Its final completion used the old 1500 ms timeout and was cancelled during CPU prompt processing. Source now defaults to 120000 ms, supports a 300000 ms ceiling, and preserves explicit shorter settings; Core 1449/1449 and clean runtime/Unity install (697 assets) pass. A separate pending-narration budget exception was fixed by allowing the whole optional motivation row to be omitted intact. For the successful retry, the coordinator was preverified in watch mode before the guarded game starter created its request directory, removing per-line coordinator/model-hash startup overhead. All seventy TTS Python tests pass. `native-cpu-pilot-02` was preparation only; a direct game launch was rejected and the standard guarded starter was used for pilot 03.

`work/breeze-tts-20261021/race-voices-01` contains 20 baseline samples. Warm-server generation took 46.10 seconds in total, averaging 2.30 seconds per utterance; first server PCM averaged 114 ms. D3 and the owned DFU fixture remained loaded, with Qwen temporarily unloaded for that earlier GPU arrangement.

`race-accents-01` contains 20 reference-conditioned samples using the same matching speakers, exact transcript, seeds and CFG 4. Total generation took 48.56 seconds, averaging 2.42 seconds per utterance; first server PCM averaged 335 ms. The game was closed for this accent batch. First PCM availability is not measured first audible sound, and the native bridge still buffers a complete WAV.

`race-accents-02` contains the twenty stronger-accent follow-ups, rendered once from frozen `accent_voice_corpus_v2.json`, without rerolls. All twenty succeeded in 50.19 seconds while CPU Qwen and GPU D3/Breeze remained resident; the game was closed. The gallery at port 8096 now serves this directory. Its WAVs and twenty manual-play controls verify; listener acceptance is pending. Earlier directories remain intact.

The user accepted the first accent set, then requested stronger accents, grunty Orcs, purring/scratchier Khajiit (especially a stronger Arabic-influenced accent for the female sample; the male accent was accepted), and slower, less quirky Bosmer. Argonians retain their requested strong scratchy, guttural croak. Preserve earlier samples and fixed references.

`work/cpu-gpu-probe-20261021` records a local llama-bench comparison of the same Qwen2.5 7B Q4_K_M model and build on Ryzen 9 9950X / RTX 4070 SUPER. Three repetitions, 512 prompt tokens and a separate 64-token generation test: fully CPU-only (`--device none`, zero GPU layers, no KV/operation offload), 356 prompt tokens/s and 11.69 generated tokens/s; GPU, 5795 prompt tokens/s and 94.99 generated tokens/s. These synthetic timings exclude model load, game load and complete harness round trips. The system reports 61.6 GiB usable RAM. CPU Qwen is now selected persistently in `local-llm/active-model.json`; normal startup uses CPU flags and explicit GPU remains available. CPU Qwen must remain resident while Breeze runs; do not perform the former GPU Qwen stop/restore cycle for CPU mode.

## Hardware and pipeline

Module scope clarified 21 October: this trial enables LLM+D3 and TTS and explicitly disables/skips STT. STT is planned separately and requires LLM+D3, not TTS. TTS must ultimately work with base-game/base-mod dialogue even when LLM+D3 is disabled; speaking-portrait animation belongs to the TTS module. The hardware trial does not claim those production integrations are complete. See the optional-module contract in [ROADMAP](ROADMAP.md).

Target the user's RTX 4070 SUPER with 12 GB VRAM. Measure on that device; do not substitute RTX 5090 or H100 results. Keep D3-mini and DFU load visible in the measurements. The current approved sequence keeps CPU Qwen resident while D3 and Breeze use the GPU. Text and speech for the same utterance remain causally ordered: finalized text precedes TTS, even though the models are resident concurrently. The earlier GPU-Qwen swap sequence is retained only for explicit GPU configurations. Loaded-game RAM/VRAM and end-to-end latency still require the new native trial.

Evaluate audio.cpp first, pinned to a verified Breeze-capable revision/release with its actual Windows/CUDA flags. The developer reports approximately 6 GB peak VRAM and RTF 0.21–0.24 on an RTX 5090; these are external reports, not measurements on this PC. Verify the release's voice design, reference conditioning, streaming, cancellation and memory-release behavior separately rather than assuming feature parity with the official Python backend. Use Python only as a separately measured fallback if necessary.

## Questions to answer

1. Does the same NPC retain a recognizably identical voice across different text, moods, repeated synthesis, process/model reload, and save/session reconstruction?
2. Which controls actually work: perceived gender, adult age, pitch/timbre, accent, speaking speed, roughness, warmth, emotional delivery and vocal events? Record ignored or inconsistent directions.
3. Can stable randomized per-NPC profiles provide enough perceptually distinct voices without identity drift? Do not claim every NPC is unique from random seeds alone.
4. What are cold and warm model-load/switch times, text-ready-to-first-playable-sample latency, first audible sound, full synthesis time, real-time factor, and Qwen reload time?
5. Does streaming start playback before complete synthesis? Measure buffer depth, underruns, chunk-boundary artifacts and cancellation. Do not call a fully generated WAV streamed playback.
6. Can Qwen reload overlap audio playback without VRAM spikes, synthesis stalls, out-of-memory errors or unacceptable DFU frame-time impact? Reloading while Breeze is still synthesizing is a separate, potentially competing GPU workload.

## Stable voice identity experiment

Compare three strategies without assuming equivalence: repeated natural-language voice description alone; fixed description/configuration/seed where the backend supports it; and one designed voice reference plus exact transcript reused for subsequent synthesis. Reference conditioning is the leading candidate for stable identity, but listener tests must confirm it. Store a proposed voice profile against stable NPC identity, with descriptor, reference audio/transcript, configuration, backend/model revision and content hashes. Keep mood-specific prosody separate from identity. These are trial artifacts, not mod save-schema changes yet.

## Local backend discovery and request-file coordinator

The downloaded Windows x64 CUDA 12.4 archives are `audio-v0.9.2-drama3-base-small-bin-windows-x64-cuda12.4.zip` (SHA-256 `48852da40790c40f3285bca6c53ff7829b3963080f0e95b6742f868fc1d7c757`) and `audio-v0.9.2-drama3-base-small-cudart-windows-x64-cuda12.4.zip` (SHA-256 `4b1186a525c9ac70a4fbd031b47e0dfca2ebaa36cfbc760d5be1a76eb2b12da5`). Required CLI/DLLs and the Breeze model spec are selectively extracted under `C:/Users/Daniel/Desktop/Code_Experiments/DFU/local-tts/runtime/audio-v0.9.2-drama3-base-small-cuda12.4`; no game/mod config is changed. The CLI identifies as `audio.cpp 0.9.2-drama3-base-small`, commit `50749561`, build date 2026-10-18, backends `cpu,cuda`. Extracted CLI SHA-256 is `ee1ae0b3dce0918f33dd9446e55c21f50120bcf4501a98a9a9a1ef4b7eaa91a9`; extracted Breeze spec SHA-256 is `f6e2587131fd2ca3ddc3e60b4450a58c685fcc7c1f6898748a5eab23003ca37e`.

`audiocpp_cli.exe --help`, `--version`, and `--list-loaders --json` were inspected without selecting a model. Its Breeze spec reports `tts`, `clone`, `design`, offline/streaming modes, English/Chinese, voice design, style control, and speaker reference. The model-specific request options include `instruct` and `reference_text`; they do not declare a typed emotion or whisper field. The generic CLI lists `--emotion`, but this is not evidence that Breeze consumes it. The coordinator therefore keeps profile identity, emotion (`neutral|joy|anger|concern|grief|fear`), and delivery (`normal|whisper`) separate in the bridge request/manifest, and composes explicit instruction clauses for the current trial. Listener checks must determine whether emotion and whisper actually affect audio or destabilize voice identity.

The coordinator uses the native bridge protocol without replacing game text: it atomically claims `requests/<requestId>.request.json` and publishes `responses/<requestId>.manifest.json`. The request retains native `requestId`, `conversationToken`, actual `personId`, `displayName`, finalized `text`, and timing fields. `personId` maps to a persistent local profile in `voice-bindings.json`; the game request cannot choose a profile, emotion, delivery mode, model path, prompt, reference path, or backend command. The trial starts each NPC at `neutral`/`normal`. Optional coordinator-only direction overrides are accepted only from a local config explicitly marked `controlled_trial`; they are test directions, not inferred game mood. Profile identity, emotional direction, and delivery remain separate inputs. `import_corpus_profiles.py` converts the authored `voiceProfiles[]` registry to the coordinator's trusted `profiles{}` format with deterministic seeds and explicit perceived-gender/adult-age identity instructions. It transfers only references already marked generated and verified by path and SHA-256; the current corpus intentionally has no generated references. Refreshing an existing profile registry preserves its verified voice references and fails if the voice root/hash cannot verify them.

The response manifest has only the bridge contract fields: `schemaVersion`, `requestId`, `conversationToken`, `personId`, `encoding` (`pcm_s16le`), `wavFile` basename, `sampleRate`, `channels`, and `sampleFrames`. The coordinator writes and validates a complete PCM16 WAV, atomically publishes the WAV, then publishes the manifest. Trial details that the bridge does not consume—profile ID, emotion, delivery mode, provenance, model/profile/reference/audio hashes, request timing and execution duration—go to `measurements/<requestId>.evidence.json`. Failures go to `measurements`, never to the ready-response directory. It skips request IDs with a cancellation marker and checks again after claim, before synthesis, after synthesis and around manifest publication. Cancellation removes only that request's exact working copy, partial WAV, final WAV and manifest. Stable profile/reference files, cancellation markers and trial evidence stay untouched. The bridge owns request-to-conversation playback mapping and deletes only its exact ephemeral response files on close; there is no directory-wide cleanup. The coordinator does not terminate an active CLI process.

Example coordinator invocation (run only after the framework gate, with a fully downloaded model and its exact SHA-256):

```powershell
python tools/BreezeTtsTrial/import_corpus_profiles.py `
  --corpus 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\LivingCities\tools\BreezeTtsTrial\corpus.json' `
  --output 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\profiles.json'

python tools/BreezeTtsTrial/coordinator.py `
  --cli 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\runtime\audio-v0.9.2-drama3-base-small-cuda12.4\audiocpp_cli.exe' `
  --model 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\models\Breeze-TTS-2-GGUF\breeze-tts-2-q8_0.gguf' `
  --model-sha256 '<verified-model-sha256>' `
  --spec 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\runtime\audio-v0.9.2-drama3-base-small-cuda12.4\model_specs\breeze_tts.json' `
  --profiles 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\profiles.json' `
  --bindings 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\voice-bindings.json' `
  --voice-root 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\voices' `
  --root 'C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-tts\bridge'
```

An optional `--directions` path can point to coordinator-owned test configuration with per-person emotion and `normal|whisper` values tagged `controlled_trial`; absent entries stay `neutral`/`normal`. The coordinator verifies the exact CLI build and model hash, runs one offline CLI process per request, writes a PCM16 WAV to a temporary path, checks the completed WAV, then publishes `responses/<requestId>.wav` followed by the native manifest. The game bridge must wait for that manifest and play the completed file; no post-generated WAV is labeled as streaming. Failed jobs are separated from ready output. The coordinator controls TTS requests only; the parent process controller must keep DFU open and arrange Qwen/D3 coexistence or GPU switching. It does not stop or reconfigure Qwen/D3.

Per-CLI execution loads the TTS model for that process; cold/warm startup and model-load costs are not measured. The separately discovered `audiocpp_server.exe --help` exposes model residency controls (`--max-loaded-models`, `--idle-unload-ms`, and free-memory guard) and a persistent HTTP service. Its `/v1/audio/speech/live` endpoint documents raw PCM with SSE speech-audio deltas. This is a future warm-server/real-streaming adapter candidate, not started or configured here; parent-owned process control must choose and test it later. Chunk delivery is real streaming only when the native bridge receives and schedules those actual deltas.

## Listening corpus and execution

### Conversation-open model scheduling (user requirement, 21 October)

When a finalized native greeting is already available, load Breeze first and speak that greeting before requiring player input. Release its GPU allocation after synthesis and reload Qwen during buffered playback and the player's typing time. D3 stays resident. If the greeting itself requires model generation, Qwen must first produce that text; do not pretend such a greeting exists before generation. Measure whether Qwen is ready before the next submitted player turn, and queue input safely if it is not. The initial per-process full-WAV pilot does not implement persistent Breeze residency or prove this latency-hiding behavior.

Voice selection must use the NPC's authoritative native sex and a compatible typed voice profile, then persist that identity. Never infer gender from names or dialogue. The first audible pilot assigned a male profile to female Gwen Cienne because its identifier hash considered every profile; the user rejected that assignment. Preserve that result as a failed assignment test, not an accepted NPC voice.

Automatic delivery snapshots existing native state at the finalized utterance: stable saved personality, current game-time mood, known disposition, known reliability trust, interpersonal fear, resentment and encounter count. Missing trust remains unknown. Relevant traits modulate delivery; they never change the speaker reference, identity seed or spoken words. Negative mood does not identify anger, and resentment alone is not an anger label. Use controlled same-NPC, same-text, same-reference comparisons to judge whether the backend renders these directions audibly. Controlled contrast speech must be labeled separately from actual Qwen-generated dialogue.

The in-game pilot must use actual `SubmitFreeText` with enabled D3 and Qwen. A controlled framework callback fixture is not a TTS test. For each turn require a new, actor/session-bound finalized request, completed real synthesis, started Unity playback with nonzero mixer output, and completed playback before the next prompt. Also wait for Qwen readiness after a model reload. No audio, unavailable output, missing completion or timeout fails the pilot; do not fast-forward to the next prompt. The initial full-WAV pilot does not establish streaming. Text-click replay reuses the exact existing row's audio without another model call; closing the conversation stops playback and deletes only ephemeral conversation audio/request/manifest assets, while preserving voice profiles/references and non-audio measurement records. Late canceled results must not recreate dialogue audio.

Whisper is a separate delivery condition alongside emotion, preserving the NPC's voice identity. Include confidential, nervous and conspiratorial whisper comparisons. Later quest-specific whisper use (the user suggested Brisienna as one possible scene) is deferred pending review of the actual authored conversation; no quest facts or disclosure permission are changed for this trial.

Start with a small latency/memory pilot, then expand to at least twelve contrasting adult voice profiles and twenty-four English game-style utterances spanning greetings, friendly follow-ups, reserved/refusing speech, anger, concern, grief, directions, transactions, appointments and longer narration. Include proper names and numbers. Repeat selected lines and changed lines with the same NPC profile before/after reload. Keep raw samples, including poor outputs, and provide a clearly labeled local listening gallery. The user judges naturalness, intelligibility, personality fit, consistency and audible defects; no text-only score establishes voice identity.

Separate synthesis-only, model-switch, streaming and loaded-game measurements. Record exact prompts, reference/configuration hashes, seeds if available, backend/model build, device placement, VRAM/RAM peaks, monotonic timing and errors. Report median and slower-percentile latency after enough repetitions; retain cold starts separately. Begin with a small pilot before committing to the full sample matrix. Do not alter the mod's active model configuration permanently merely to run this trial.

## Sources checked on 21 October 2026

- [audio.cpp developer's Breeze report](https://github.com/breezeblue-ai/breeze-tts/issues/3): release/support and RTX 5090 memory/RTF claims.
- [audio.cpp repository](https://github.com/0xShug0/audio.cpp): implementation and release capabilities to inspect at trial time.
- [Official Breeze TTS 2 model card](https://huggingface.co/BreezeBlue/Breeze-TTS-2): reference-based cloning/direction, reference-free voice design and official runtime requirements. Its model/output license is separate from the Apache-licensed source; verify distribution terms before any later Nexus packaging.

Protocol unit tests do not call the CLI, load weights, or synthesize. No local performance, listener preference, emotion controllability, whisper quality, or voice-consistency result is claimed here.
