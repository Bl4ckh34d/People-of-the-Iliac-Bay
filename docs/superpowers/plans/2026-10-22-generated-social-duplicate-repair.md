# Generated social reply repetition

The user requires fresh generated replies rather than repeated NPC speech. The
current optional action-wording path has a bounded repair, but ordinary social
completion can commit an exact duplicate. Streaming can expose that duplicate
before the complete reply reaches the commit guard.

## Bounded design

1. Keep exact canonical text comparison separate from semantic interpretation.
   Compare full replies without the existing800-character truncation collision.
   Add a pure potential-prefix check for streamed text. This is not intent matching.
2. Add a social-only repair annotation distinct from the existing action-only
   annotation. A copied request preserves the exact current turn, authority,
   history, identity, disposition and mood contract. Quote the previous NPC wording
   as data, bounded to800 characters for the prompt; never change action parameters.
3. Before consuming a social stage or committing mood/transcript/observed speech,
   detect a repeated reply. Permit one rephrase request under the same captured
   session and stage. A second duplicate, failed dispatch or unavailable response
   uses existing unavailable provenance without committing an emotional update or
   replaying any action. Stale and duplicate callbacks remain inert.
   The player's actually heard utterance still enters the existing pending
   appraisal queue once, with no mood effect marked as already applied. A failed
   NPC answer must not erase legitimate player evidence. If the atomic exchange
   itself rejects a mismatched/stale receipt, settle presentation without feeding
   that rejected receipt through the fallback enqueue path.
4. For streaming, hold received segments while their cumulative canonical text
   could still be the entire previous reply. On divergence, release unchanged
   original segments in order. At a valid shorter nonduplicate completion, release
   the held segments. An exact duplicate must reach the bounded repair without
   having queued TTS or changed the transcript. Distinct first sentences keep the
   existing low-latency path. Never retract audio already played.
5. Existing canonical base-game/action acknowledgements remain authoritative.
   Do not turn an optional-model-off response into a model request or fabricate
   new speech. Existing generated action wording retains its own receipt-aware
   duplicate protection; accommodate full previous-text comparison safely.

## Work and evidence

Core tests first; parent verifies RED before production edits. Workers own disjoint
files and never run builds, models or game sessions. Parent reviews and runs Core,
adapter and runtime compilation serially. Controlled owned native acceptance must
verify buffered repair/terminal failure/stale callback and streaming holdback with
one final transcript/mood transaction and unchanged action effects. A real model
sample can demonstrate repair quality but cannot replace those lifecycle checks.
Use existing approved audio only; no new voice generation in this work slice.
Preserve failures, build/install, update evidence and recovery copy after acceptance.

The broad roadmap remains active. This slice does not claim semantic near-duplicate
detection, guaranteed model compliance, a new all-GPU default or complete NPC behavior.
