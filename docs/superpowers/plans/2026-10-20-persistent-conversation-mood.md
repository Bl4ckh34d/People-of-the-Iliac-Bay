# Persistent conversation mood implementation plan

**Verified checkpoint:** Social completion, persistence and context integration are installed and native-tested; see [acceptance and remaining limits](../../2026-10-20-persistent-mood-acceptance.md). Action-wording mood and full free-text routing acceptance remain open. Do not repeat completed work units from this plan as if they were still pending.

> For agentic workers: implement the approved IMM-09 roadmap increment using test-driven development and independent review. Preserve the existing dirty integration checkout; do not reset, broadly stage, commit, create a worktree, or run Unity/DFU without parent coordination.

**Goal:** Make accepted NPC conversation update persistent mood and ground subsequent replies without another model inference.

**Architecture:** A single typed reply contains spoken English and a bounded `moodDelta`. Native code validates output and atomically commits mood, a per-person accepted sequence, and the dialogue memory row. A captured sequence plus current session/stage/epoch rejects duplicate and stale callbacks. Mood belongs to the world save branch, separate from personality and relationship disposition. NPC-specific weather remains a temporary contextual modifier.

**Tech stack:** C# 7.3, netstandard2.0 Core, net472 runtime verification, Unity 2019.4.40f1, local Qwen via existing llama.cpp transport.

**Spec:** IMM-09 in `docs/ROADMAP.md`, with reviewed seams in `work/persistent-mood-20261020/{implementation-review,atomic-event-design,social-output-contract}.md`. The runtime weather gap in those historical notes is already fixed. The old model-comparison freeze has been released for the approved roadmap implementation.

## Contracts and boundaries

- Mood `Value` is an integer from -100 to 100; `moodDelta` is an integer from -10 to 10, normally zero for emotionally neutral exchanges. These values describe temporary outlook, not liking or action consent.
- Per-person saved `AcceptedTurnSequence` is a nonnegative `long`. No text dedupe or game timestamp serves as its event key. Mood changes only on an accepted new player/reply exchange.
- `WorldStateStore.CaptureConversationMood(string personId)` returns a detached immutable view with `Value` and `AcceptedTurnSequence`, defaulting to neutral/zero without inserting state.
- `TryCommitAcceptedSocialExchange(string personId, long expectedSequence, int moodDelta, double at, string date, string place, string playerText, string npcText, out long committedSequence)` validates all inputs and commits a `model` exchange and shared exchange count once, atomically with mood/sequence. Invalid/stale calls change nothing.
- Saved schema becomes 32. Schema <=31 restores neutral mood. Preserve existing friendship/archive migrations and branch ancestry.
- `DialogueRequest` gains optional trailing `bool moodEnabled = false, int moodValue = 0`. `SocialDialogueResult.TryParse(string text, out SocialDialogueResult result)` strictly parses exactly `reply` and `moodDelta`, rejects malformed/duplicate/extra fields, invalid Unicode, unsafe/empty/overlength reply and invalid delta. It never infers emotion from prose.
- Mood-enabled `LocalDialogueClient.Request` delivers validated JSON to its explicitly mood-enabled caller, or existing unavailable/context-limit sentinel. It does not cache/replay mood-enabled results. Plain reply callers retain existing behavior. Runtime parses typed output again before commit; JSON never reaches Talk.
- Mood-enabled generation adds grammar and sufficient output-token allowance to the existing inference. No extra classifier/inference for mood.
- Return greetings, fallbacks, pending narration, rejected verification, unavailable output, cancellation and stale callbacks do not mutate mood.
- Initial runtime rollout covers actual social replies, including factual context delivered through that path. Action-specific final wording and native-only answers must be audited explicitly; do not mark all-conversation IMM-09 complete if they remain uncovered.
- Current persistent mood is present in immutable context snapshots and later replies. Known persistent outlook replaces daily random/disposition-derived outlook. Grief/danger and verified NPC-outdoor weather remain contextual facts; weather alone never changes the saved value.

## Work units and ownership

1. **Core persistence/transaction:** `ConversationMood.cs`, `WorldState.cs`, targeted helpers in `ConversationMemory.cs` if needed, `ConversationMoodTests.cs`. Test neutral migration, deep copies, strict invalid states, atomic no-op failure, duplicate/out-of-order sequences, repeated identical words on distinct turns, native memory limits, branch rollback, archive checkpoints and separate disposition.
2. **Typed reply protocol/transport:** `SocialDialogueResult.cs`, `LocalDialogue.cs`, `LocalDialogueCompletion.cs`, `LocalDialogueClient.cs`, `SocialDialogueResultTests.cs`. Test strict parser, escaped strings, duplicate keys, Unicode, grammar/body, unchanged plain callers, fixed context and no mood cache replay.
3. **Context projection:** `ConversationContextSnapshot.cs`, `ConversationContextFormatter.cs`, `ConversationLifeContext.cs`, `PeopleOfTheIliacBayMod.ContextSnapshot.cs`, dedicated context tests. Wire store view to personal snapshot; test next-turn mood, immutable capture, unknown defaults, stable personality and outdoor-only weather.
4. **Runtime commit (parent):** `PeopleOfTheIliacBayMod.Dialogue.cs` and `DialogueMemory.cs`. Capture sequence before dispatch; after existing freshness checks validate typed result and unchanged verified reply, commit once, then append unrecorded UI pair. Returning greetings and fallbacks stay mood-free.
5. **Native acceptance/review:** prepare a focused probe using owned fixture, positive/negative/zero typed updates, invalid/stale/replay controls, real local-model output, next prompt grounding, close/reopen and actual disk save/load/rollback. Parent serializes Unity installation and game runs.

## Execution checklist

- [x] Core tests first fail for the missing transaction, then pass.
- [x] Protocol tests first fail for missing typed contract, then pass.
- [x] Context tests first fail for missing persistent mood projection, then pass.
- [x] Parent integrates runtime and reviews atomic UI/memory ordering.
- [x] Independent review addresses correctness issues.
- [x] Combined Core/adapter/runtime builds pass.
- [x] Unity build/install and focused owned native acceptance establish actual game behavior.
- [x] Record exact coverage, model-quality limitations, installed hash and remaining action/native-only integration; update roadmap and Obsidian.

These completed implementation steps refer to bounded acceptance documented in
`docs/2026-10-20-persistent-mood-acceptance.md` and
`docs/2026-10-21-action-mood-acceptance.md`. Broad IMM-09 emotional quality remains open.

Parent alone runs game/build/model acceptance. Workers may edit only their assigned files and dedicated tests, coordinate test registration, and report concrete evidence. Do not claim native acceptance from source tests.
