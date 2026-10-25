# Bounded generated social reply repair

Ordinary generated social replies previously bypassed duplicate detection. The
owned native RED151b demonstrates an exact repeated NPC reply changing mood0 to4,
accepted sequence1 to2, inbox revision0 to1 and transcript rows4 to6.

The social path now compares the full canonical previous and proposed replies
before consuming the captured stage or committing mood/history/presentation.
It permits one rephrase under the same request, context and stage. The repair
annotation is social-only, safely quoted and bounded independently of the full
comparison. Old-attempt and completed callbacks are inert. A repeated repair or
unavailable transport settles once with explicit unavailable provenance. The
actually heard player utterance still enters the existing pending appraisal queue
once, without marking a mood effect as already applied.

Streaming holds segments while the cumulative text could equal the prior reply.
A divergence releases the original segments in order; a shorter unique complete
answer releases at completion. An exact duplicate queues no sentence and enters
the buffered repair. Cancellation clears stream ownership before synchronous
playback callbacks can consume the stage.

An additional controlled RED151d exposes silent presentation loss when the atomic
exchange rejects a mismatched observed-speech receipt. That path now settles as
unavailable while preserving the rejected receipt outside the fallback enqueue.
It never retries a stale commit or applies its emotional delta.

## Verification

Core1592/1592, adapter84/84 and runtime/Unity compilation pass. Installed checkpoint
152 retains the151f production repair sources. `native-social-duplicate-green-151f`
passes unique repair, stale callback replay, duplicate-twice terminal handling,
transport-unavailable handling, invalid-receipt fallback and baseline restoration.
Its separate isolated bridge checks exact duplicate holdback, prefix divergence
ordering and shorter-prefix completion through production queue methods.

These are controlled native lifecycle/queue checks, not model-quality, generated
audio, UI playback or semantic near-duplicate guarantees. Explicit requests to
repeat information can receive a fresh paraphrase; no keyword intent exceptions
were introduced. The diagnostic `native-social-duplicate-cas-red-151c/` corrected
terminal assertions to retain valid player evidence; the preceding
`native-social-duplicate-cas-red-151/` falsely counted an ephemeral thinking row.
The original actual mood/sequence-changing RED is in
`native-social-duplicate-red-151b/`; all diagnostic failures remain preserved.
