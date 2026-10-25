# Approved framework: integration inventory and regression baseline

22 October update: the focused speech stage is now integrated as Nano classification followed by category-bound Qwen appraisal, with native consequence guards. This supersedes the older proposed/advisory speech-stage status below; historical failures remain valid evidence for those protocols. Household self-knowledge and bounded three-attempt parameter repair are implemented. Use the [current acceptance matrix](2026-10-22-npc-conversation-acceptance.md) for partial features and unverified native/model quality; this inventory is not a blanket completion checklist.

21 October 2026. The user approved the [common NPC framework](superpowers/specs/2026-10-21-npc-motivation-relationships-and-contracts.md). Three read-only workers inspected runtime, persistence and test partitions; parent checked the important findings against source. This inventory identifies work, not completed implementation. Follow the [staged delivery plan](superpowers/plans/2026-10-21-npc-motivation-and-contract-delivery.md).

## Integration map

Paths below are relative to src/PeopleOfTheIliacBay.Core unless explicitly marked runtime (mod/Scripts).

| Area / source | Reuse | Required integration and invariant |
| --- | --- | --- |
| WorldState.cs, copy/restore/version gates | Schema 32; separate attitudes, mood, inventory, memory, tasks, appointments | Versioned directed relationship/evidence records with stable owner and target IDs. Unknown trust, old-save migration, deep copies and branch isolation; no rerolled traits. |
| ConversationAttitudes.cs | Native baseline, affinity offset, gradual capped friendship ledger | Preserve as affinity. Add trust/confidence separately, never derive proven reliability or loyalty from affection points. |
| ConversationMood.cs / ConversationMemory.cs | Atomic mood-memory commit, turn sequence, attributed transcript | Add once-per-event relationship updates and stable evidence references. Turn sequence or matching text is not a general event ledger. |
| SharedConversationArchive.cs | Immutable checkpoint ancestry and per-person recall | Reuse save ownership; speech does not prove promise fulfillment. Do not use a global latest-memory head. |
| ConversationContextSnapshot.cs / Formatter | Immutable person/session/time/save facts and separate consumer projections | One motivation projection for relevant trust, duties and known constraints. Keep speech-act routing narrow; verify every consumer and context budget. |
| Runtime PeopleOfTheIliacBayMod.ContextSnapshot.cs | Captures actor-owned facts, disposition, traits, mood and current commitments | Capture relationship/contract state in the same decision revision; add typed duty scope and refresh before effects. |
| LocalDialogue.cs | Social/action wording and typed mood result | Concrete mismatch: social instruction currently says negative disposition means distrust. Align affinity/trust meaning after preserving baseline; test trusted-but-disliked versus liked-but-unreliable. Wording alone cannot secure relationship effects or action acknowledgments. |
| Runtime LocalDecisionRouting.cs / LocalDecisionWillingness.cs / ConversationKnowledge | Exact speech-act routing, original proposal, binary ACCEPT/DECLINE | Add common factor/scoped-duty context. Preserve the protocol until a versioned successor passes; avoid a second hidden final selector. |
| Runtime Dialogue.cs / ActionWording.cs / DialogueActions.cs | Freshness, receipt-bound wording and one accepted exchange | Common projection/event owner. Snapshotless social fallback exists. Legacy TryActionConversation/sequence use old context, but parent search found no external production caller of TryActionConversation: inventory this path without falsely claiming normal routing bypasses snapshots. |
| Appointments.cs / DailySchedule.cs / NpcTaskStore.cs | Appointments, ordinary schedules and existing admission/consent | Add contract reservations and actual conflict policy. Initial Core TryAcceptNpcTask passes scheduleCompatible=true; that alone does not establish compatibility with a future contract. Inspect runtime admission before altering current behavior. |
| NpcInventory.cs / NpcTasks.cs / NpcActionContracts.cs | Revision-bound inventory updates and real action/task receipts | Separate escrow/earned/refund ledger and contract lifecycle. LastTransactionId and task step IDs are not full wage or relationship-event ledgers. |
| Future companion adapters | Existing follow/travel/recovery seams | Recruitment, melee/ranged/spell combat, scoped duty, termination, settlement and actual home-return acceptance remain future work. Add after contracts, not as prose promises. |

## Order and tests

1. Directed relationship/personality state and event ownership, preserving affinity/mood.
2. Shared snapshot/formatter, prompt meaning and willingness context; inventory legacy callers.
3. Decision-specific policies and paired contrasts; keep current binary protocol stable.
4. Permitted disclosure and evidence-linked deception.
5. Contract admission, schedule reservation and economic ledger.
6. Physical role adapters, warnings, retreat, termination and home recovery.
7. Deliberate betrayal as a separately validated effect capability.

Do not mark later slices complete from earlier parser/schema tests. This audit changes documentation, not new framework gameplay code.

### Native resident health and status-onset boundary

The current health producer is narrow and already has a safe evidence shape. `NativeNpcVitalsBinding` samples the bound `DaggerfallEntity`'s health and magicka when they change; `PeopleOfTheIliacBayMod.NpcHealthExperiences.TickNpcHealthExperienceEpisodes` flushes quiet episodes, and `WorldStateStore.FlushNpcHealthExperiences` queues an `Injury` or `Healing` `NpcExperienceEvent` with health totals and `ResponsibleActorAttribution.Unknown`. Do not infer poison, disease, attacker, or intent from a health delta.

DFU's `EntityEffectManager` exposes typed `EffectBundles`, `PoisonBundles`/`DiseaseBundles`, `OnAssignBundle`, and `GetInstancedBundlesSaveData`/`RestoreInstancedBundleSaveData`. Its serialized bundle records include `bundleType`, `fromPoison`, effect keys, and live-effect data. The assignment event can also fire for an incumbent that was not actually added, so an adapter must verify the exact bundle remains in that manager's active bundle list before recording onset. DFU's `DiseaseEffect.Start` explicitly ends disease effects on non-player hosts; disease effects are therefore not established as supported for residents. The current `ResidentActor.CaptureState` saves inventory and vitals, not active effect bundles, and the `NpcExperienceEvent` model has no typed status identity field. Do not claim resident disease support or durable active-effect restoration from the generic DFU APIs alone.

Smallest reliable next slice: attach one listener to each eligible live resident's own `EntityEffectManager`; baseline already-active bundles without emitting events, then accept only a newly assigned bundle that remains active. Preserve the exact native bundle category/key as a bounded typed status identity in the event/inbox schema (no text matching); enqueue one `StatusOnset` with an explicit unknown cause. Allocate a per-save/person onset sequence and persist its receipt/dedup identity atomically with the inbox entry; do not use object hashes, current time, or health changes as the identity. Leave disease effects unsupported for residents until DFU host eligibility and a real resident application are proven. Keep status removal/cure as a separate future event unless there is a native removal receipt.

Native acceptance should apply one controlled poison bundle to an isolated ordinary adult resident and verify: only a bundle present in that resident manager emits one `StatusOnset`; its typed identity matches the native bundle, while responsible actor stays unknown; save/reload preserves the inbox row and onset sequence without replay; removing and applying again creates a distinct onset; a rejected/non-player disease assignment emits no event; and actor transfer/recreation does not duplicate an event. Separately retain the existing health episode check to show damage attribution remains unknown. These are proposed acceptance steps, not completed gameplay evidence.

## Fresh parent baseline

Evidence directory: work/framework-integration-20261021.

| Command / evidence | Result | Scope |
| --- | --- | --- |
| dotnet run --project tests/PeopleOfTheIliacBay.Tests -c Release --no-restore / core-baseline.log | 1293/1293 | Existing Core invariants, not new trust/contracts. |
| dotnet run --project tests/DecisionAdapter.Tests --no-restore / adapter-baseline.log | 77/77 | Existing wire/session/routing/willingness, not model quality or Unity lifecycle. |
| dotnet run --project tools/MoodEvaluation -c Release -- self-test / mood-selftest.log | Pass | Twelve-scenario production prompt/parser/transaction mechanics with synthetic results. |
| dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore --nologo / runtime-baseline.log | Zero warnings/errors | Current runtime compilation; documentation alone needs no game rebuild. |
| Local MoodEvaluation run / mood-baseline.log and mood-baseline.jsonl | 36 HTTP completions, 36 parsed/committed results, zero mechanical failures | Twelve synthetic three-turn trajectories; one pass, no retries. Corpus SHA-256 953fc9c48defe091ac112b10b041e200ff80cb5b03f9a3e8baf3b58c054a8988. |

Model: existing local Qwen2.5-7B-Instruct-abliterated-v2 Q4_K_M, context 4096. Identity originates in an owned fixture; dates, voice and scene variants are explicitly synthetic. This is a Qwen mood/wording baseline, not D3 evaluation or new-framework native gameplay. Installed gameplay remains earlier accepted bundle 364B459F.

### Semantic findings: 36/36 is not behavioral success

- Actual model-generated deltas occur: gratitude reaches +10, an insult yields -5 and a reported apology yields +5. This proves capability to output nonzero affect, not complete emotional appropriateness or an actual prior offense.
- Explicit instruction to set moodDelta to +10 succeeds; subsequent turns add +10 and +5. This is semantic manipulation despite valid bounded JSON. Keep an adversarial relationship-appraisal gate; no keyword workaround.
- A typed rejected Wait receipt is contradicted by a promise to wait, then repeated agreement. Native effects are not committed, but wording is misleading: IMM-18 receipt fidelity remains a blocker.
- Responses invent market hours, directions and a spare room absent from supplied facts. Knowledge/disclosure and native authority need stronger integration.
- Insult replies remain apologetic/weather-focused. Low/high mood cases are not paired same-input contrasts and cannot establish reliable tone differences. A clipped sentence and repetitive questions remain in raw evidence.
- Seven deltas fall outside broad review bands. These bands are diagnostic, not an accuracy denominator; do not claim 29/36 correct emotional decisions.

No prompt tuning, grammar relaxation or model replacement was performed in this pass. Preserve raw baseline and readable review copy mood-readable.json.

### Speech attribution: next bounded architecture test

The current D3-label advisory A/B is not a reliable quote fix. On the 12 fixed synthetic pairs, the request changed only by the advisory sentence; for quoted threats it corrected two labels, but one corrected proposal was rejected by the existing impact bound and two accepted responses still blamed the player. Numeric response deltas also varied. The accepted corrected quotation retained an unjustified -5 player-trust proposal, so a corrected label alone did not fix the effect attribution. See `work/npc-factor-evaluation/speech-candidate-ab-current-20261021/run-01/manual-paired-review.md` and preserve its raw pairs.

Smallest useful follow-up is an evaluation-only two-stage request using the existing receipt-bound speech cases: D3 emits a typed `NpcExperienceSpeechActKind` candidate bound to the exact observed-speech receipt; Qwen receives that candidate and proposes effect magnitudes and only an already eligible speech target. The versioned Qwen response must not choose a replacement speech-act class. Keep D3's raw response and typed label, Qwen's raw response and parsed proposal, and the blind oracle in separate records. The D3 label is fallible model output, never native speaker/culprit proof; a misclassification stays visible in the evaluation. Keep Core bounds strict: out-of-range or incompatible proposals reject with raw output preserved, with no clamping and no state commit in this test. Reuse the 12 pairs only as an exact reproduction set; score attribution on the existing wider speech-choice cases as a separate check, then test a pre-registered held-out paraphrase set before generalizing.

This needs a versioned appraisal request/parser seam: production currently lets Qwen emit `speechActKind` alongside numeric effects, and Core derives conditional bounds from that same Qwen label. The runtime appraisal queue calls Qwen directly; it has no D3 classification stage, no typed classification bound to the exact speech receipt, and no stage-ticket freshness/cancellation path. D3's current production route vocabulary is only `SOCIAL`, `ACTION_REQUEST`, `OFFER_RESPONSE` and `COMMITMENT_UPDATE`; it is not a speech-attribution classifier. Do not add an advisory label or this experimental stage to live commits until label quality, numeric bounds and stale-stage rejection pass their own gates.

### AI-on hostile-history evidence boundary

`WorldStateStore.LatestDirectFamilyThreat` currently scans `NpcConversationMemory.PlayerText` with `ConversationKnowledge.IsDirectFamilyThreat`; `PeopleOfTheIliacBayMod.DialogueMemory.CurrentConversationMemory` then calls that result “Authoritative hostile history.” This converts raw historical text into an authoritative family-threat fact with semantic regex. Remove this lookup from AI-on context; raw legacy/offline memory remains unclassified and must not be backfilled. Keep any offline exact-fallback policy separate from AI-on context.

Available typed evidence is partial. `NpcExperienceInboxEntry` saves the exact attributed-speech text, observer, speaker, receipt and time, but its consumed `NpcExperienceAppliedAppraisal` currently saves deltas only, not `SpeechActKind` or speech target. `NpcAcceptedRelationshipOutcome` does persist `Origin`, receipt/canonical IDs, `SpeechActKind` and `SpeechTargetPersonId`, but only when a relationship appraisal creates an accepted outcome; it does not preserve the capture-time known-family set. `ConversationAttitude.Offset`/`Insults` preserve hostility severity/counts, not its cause or target. `ConversationKnowledge` and `LocalDecisionDialogueRoute` produce a current `family_threat` route value, but that classification is transient; `ApplyFamilyThreatDisposition` persists only the offset.

Safe history source: persist the route's `family_threat` classification directly on the exact observed-speech receipt, with classifier provenance and no fabricated family-member identity. Phrase it as “this receipt was classified as a direct threat to your family,” not as proof that the speaker intended or could carry out harm. The existing accepted-relationship record is a secondary source only when its `SpeechTargetPersonId` is proven to be in the capture-time family set; it is fallible Qwen appraisal, not proof of intent, and the family set is not currently stored there. The minimal persistence gap is the consumed inbox/appraisal (or sibling receipt DTO): retain the validated speech class, classifier source, optional target ID, and capture-time target relation alongside the existing speech receipt. Do not infer family targeting from raw words, the hostility offset, an ordinary `DirectThreat` label without a family-target fact, or a current family roster that may differ from the capture-time roster. If there is no such typed receipt, provide no family-threat history; legacy rows get no inference.

Bounded regressions: raw threat-shaped text in a legacy/action memory row plus hostility floor yields no AI-on claim; quoted, reported and hypothetical accepted classes yield none; a receipt-bound persisted `family_threat` route label yields only a source-qualified statement and no specific family-member identity; an accepted appraisal's `DirectThreat` yields a family statement only when its captured target relation says known family; direct threat to self/other target, unknown target relation, or hostility floor alone yields no family claim. Verify save/load, dedupe, and branch restore preserve the typed receipt binding. These are source/API recommendations, not implemented fixes or tested semantics.

## Rerun gates after implementation

- Data/events: full Core plus migration, relationship direction, unknown defaults, event evidence/deduplication, friendship limits and divergent-save tests.
- Snapshot/prompt/decision context: Core, DecisionAdapter, runtime compile, frozen Qwen/D3 paired contrasts and mood corpus. Preserve before/after and held-out paraphrases; no language rule fitting.
- Social/action runtime: build/install after source gates, then serial owned ConversationMoodProbe, ConversationActionMoodProbe, DialogueAvailabilityProbe and FriendshipProgressionProbe, using PeopleOfTheIliacBayTest-9a5f197b / People - Gothway Garden. The friendship probe's injected polite tone does not prove natural-language classification.
- Task admission: add relevant goal binding, sequence/cancellation, receipt/feedback and schedule-recovery cases; a scheduleCompatible flag is not evidence by itself.
- Contracts/economics: new funding, 24-hour boundaries, suspension/renewal, partial settlement, death/estate, quest exclusion and branch/reload idempotency before native companions.
- Companion/deception/betrayal adapters: new owned physical acceptance per role/outcome. Existing tests cannot certify absent features.

Parent owns shared model calls and serial build/game sessions. Native probes were not rerun solely for documentation; earlier bounded acceptance remains historical evidence. Next source integration starts from this preserved baseline.
