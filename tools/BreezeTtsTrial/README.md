# Breeze TTS listening trial

`corpus.json` is a frozen, synthetic listening corpus. It contains 12 adult fictional voice profiles (six female-presenting and six male-presenting), 24 English game dialogues, exact-text emotion pairs, exact-text whisper comparisons, and repeat stages for backend reload and game save/load. No audio, voice reference, model output, or model call has been produced by preparing these files.

## Separate identity from delivery

Each `profileId` describes a stable voice identity and is tied to a separate synthetic `npcId`. The identity descriptor contains perceived adult age, pitch/register, timbre, accent and baseline pace; it must not change with a scene's emotion. A voice reference, when one is later created, is a separate stable asset with its transcript and hash. Reuse that exact reference for every same-profile comparison and record its hash. Do not treat a profile ID, seed or descriptor as proof that listeners will hear a unique or consistent identity.

Each render request should preserve the coordinator contract:

```json
{
  "utteranceId": "trial/<stage>/<profileId>/<lineId>/<emotion>/<deliveryMode>/<intent>",
  "npcId": "trial-npc-01",
  "profileId": "npcvoice-01",
  "lineId": "dialogue-01",
  "emotion": "neutral",
  "deliveryMode": "normal",
  "text": "Exact text from the referenced corpus line.",
  "textReadyMonotonicTicks": 123456789,
  "timestampFrequency": 10000000,
  "requestId": "request-specific-id",
  "conversationLifetimeToken": "lifetime-specific-token"
}
```

The example values are illustrative only. Generate unique request and lifetime IDs for each actual native request. Reuse a deterministic `utteranceId` only for a retry of the same planned tuple; give backend-reload and save/load repeats distinct stage IDs so bridge deduplication cannot suppress the experiment. Retain the finalized text exactly. Do not let voice metadata or the TTS layer add words.

The current native bridge is a full-WAV pilot. It can carry the native person ID, display name, finalized text, conversation lifetime token, request ID and text-ready monotonic timestamp/frequency. It does not yet transfer corpus `npcId`, `profileId`, line ID, emotion, delivery mode, whisper intent or voice-reference hash into synthesis. Keep those as trial-side metadata until an adapter explicitly carries and validates them. The synthetic `npcId` is not a substitute for the game's native person ID.

`emotion` and `deliveryMode` are independent. Use exactly these emotion conditions: `neutral`, `joy`, `anger`, `concern`, `grief`, and `fear`. Use delivery modes `normal` and `whisper`. Emotion-pair trials hold text, profile, voice reference and `deliveryMode=normal` fixed. Whisper trials hold the same factors and `emotion=neutral` fixed, change only `deliveryMode`, and record the whisper intent (`confidential`, `nervous`, or `conspiratorial`) separately. Whisper is a vocal delivery choice, never permission to disclose private, confidential, quest-protected or otherwise unpermitted information. Corpus lines are fictional test text; future in-game lines still require native disclosure eligibility.

## Listening matrix

The corpus assigns every profile an anchor dialogue and a distinct coverage dialogue, covering all 24 lines. Repeat both lines in neutral/normal delivery at these stages:

| Stage | Identity comparison | What it tests |
| --- | --- | --- |
| `initial-session` | Anchor line, then distinct coverage line | Baseline identity and text-driven variation |
| `after-backend-reload` | Replay both exact lines with the same profile/reference | Identity retention across backend/model reload; record any changed backend revision |
| `after-game-save-load` | Replay both exact lines with the same profile/reference | Identity retention across restored game identity/session; record any changed profile/reference hash |

For each of the 12 profiles, render `dialogue-24` with its exact same text under the six emotion conditions, keeping delivery normal. This creates a within-voice emotion comparison. For whisper testing, use that same line and each profile under normal delivery and whisper delivery; test the three intents separately while keeping neutral emotion and the reference fixed. Score whether emotion direction is audible, whether a whisper is recognizably whispered, whether words remain intelligible, and whether identity still sounds like the same NPC. If the backend cannot independently control a field, mark it unsupported or conflated rather than silently changing another field.

This is a listening plan, not a requirement to generate the entire Cartesian product before a small pilot. First validate one profile and one line through each supported path, then schedule the full matrix if the pipeline works. Preserve all failed, clipped, mispronounced, identity-drifting and otherwise poor samples; do not reroll them away. An error with no audio is still a trial result and should have a manifest row.

## Blind review and raw records

Randomize opaque sample IDs independently for each listener. Hide profile, descriptor, emotion, delivery mode, stage and backend metadata until ratings are saved. Present exact-text pairs in randomized order; listeners should not know which clip is the anchor or which requested emotion/whisper condition produced each clip. Keep the key and randomization seed with the trial manifest, not in the listening view. Do not normalize away audible differences by post-processing one arm only.

For each sample, retain the original audio bytes and SHA-256, request payload, exact line text, `utteranceId`, `requestId`, native person/lifetime binding, text-ready ticks and frequency, backend/model build, profile/reference/configuration hashes, random seed if supported, device placement, cold/warm/reload state, load start/end, first playable sample, first audible sound, synthesis end, audio duration, RTF, peak VRAM/RAM, buffering/underruns, cancellation result, errors, and any resampling/post-processing. Record timings with a monotonic clock. Keep cold and warm runs separate.

Each blind listener form should include:

- Intelligibility and exact-word fidelity (1–5), plus any misheard word.
- Naturalness (1–5) and audible defect tags (clipping, artifacts, breath/noise, pacing, pronunciation, other).
- Perceived voice age band, gender presentation and accent as listener judgments, with “unclear” available.
- Pairwise same-NPC identity similarity across changed lines and across reload/save-load (1–5); do not substitute a seed or descriptor match.
- Emotion direction success (target emotion audible: yes / partly / no / unclear) and free-text impression.
- Whisper recognizability (yes / partly / no / unclear), intelligibility, and identity similarity for whisper comparisons.
- Raw listener notes and sample IDs; preserve disagreement and weak ratings rather than averaging them away.

Review at least two listeners for the identity and emotion comparisons where practical. Keep raw labels, order, notes and individual ratings; report agreement and disagreement alongside aggregates. A panel's “same voice” judgment is evidence about this corpus and setup, not proof of universal uniqueness or identity stability.

## Stop and compare

Use the hardware and sequencing requirements in [the Breeze hardware-trial plan](../../docs/2026-10-21-breeze-tts-hardware-trial.md), including the target RTX 4070 SUPER with 12 GB VRAM, D3/Qwen transitions, and keeping STT out of this trial. Record measurements rather than importing reported performance claims. Report which controls were actually supported, which were ignored or conflated, and whether identity/emotion/whisper judgments held across all repeated stages. Keep full-WAV playback labeled as such; it is not streaming.

## Passive resource telemetry

Start `telemetry.py` beside the real-game trial. It appends one JSON object per line and samples NVIDIA aggregate GPU memory/utilization/temperature plus Windows CIM working-set (RSS) for explicitly named PIDs. Example:

```powershell
python tools/BreezeTtsTrial/telemetry.py `
  --game-pid 1234 `
  --process-pid qwen=2345 `
  --process-pid d3=3456 `
  --process-pid tts=4567 `
  --output 'work/breeze-trial/telemetry.jsonl' `
  --interval 1
```

Omit `--process-pid` for any unavailable process. The collector only queries counters; it does not start, stop, signal, or reconfigure any PID. Stop sampling with Ctrl+C. `nvidia-smi` per-process VRAM may show `N/A` under Windows WDDM; those rows retain `memoryUsedMiB: null` and an `unavailable` or `partial` status. No per-process VRAM is estimated. PowerShell/CIM RSS is also marked unavailable if the process exits, permissions block the query, or the platform lacks CIM.

The backend should append markers at the real event sites by importing `append_event` from `telemetry.py`. Supported names are `text_ready`, `synthesis_started`, `synthesis_finished`, and `model_reloaded`. Include native request IDs. For `text_ready`, preserve native `textReadyMonotonicTicks` and `timestampFrequency` as fields when available; do not substitute backend receipt time for that source timestamp. Example:

```python
from telemetry import append_event

append_event(output_path, "synthesis_started", requestId=request_id)
```

Each marker includes UTC wall time and Python monotonic time from its call site. This lets the actual backend events align with resource samples without guessing phase boundaries. The script itself emits no synthesis or model-reload markers.
