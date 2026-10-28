# Immersion simplification — integrated source results

Nachtrag 27. Oktober: [Spielabnahme mit frischen Hakon-Saves](2026-10-27-game-acceptance.md) ergänzt diesen Quellbericht um echte Player-Ergebnisse und behobene Ursachen. Keine pauschale Gesamtfreigabe.

The user approved a mod-wide simplification audit on 26 October 2026, with explicit corrections. The changes below are integrated into the shared checkout through baseline/current/worker three-way merges. Concurrent exact-bed lodging work remains owned by the separate room-audit chat.

## Preserved behavior and removed prerequisites

- Rest and recovery require actual bed arrival and normal health/interruption conditions, but no sleeping sprite. Missing art uses the existing standing position beside the bed.
- Ordinary guests reserve a real origin household and share its resident identity, surname, family relations and bed capacity. Bounded origin reads replace whole-town housing preparation. Existing validated routes are reused instead of requiring an extra future return search. Guest incidents, death and return-home context use the same identity.
- Family Inn outings use one payer who can fund the entire group, with bank preparation when necessary. Actual menu selection and exact payment occur at the counter. Unobserved service does not reserve seats or require interior routes; observation assigns adjacent seats.
- Household days use a stable approximately 50/50 childcare choice. One adult may remain home, or both may leave with the child locked inside. Adult presence unlocks by day; night/all asleep locks the door.
- Daily destinations are selected incrementally. Interrupted ordinary errands are skipped; fixed appointments and joint family commitments remain. Physical home return and late sleep can cross midnight.
- A begun companion service block receives its actual daily fee directly. Failed-start rollback remains; escrow, prorated accrual and duplicate end-of-service payment are removed. Existing health suspension and native transaction ownership remain.
- NPC shelf purchases preserve native shop stock, create a fresh owned item UID and move real money atomically. Player commerce remains native. Guild healing offers use native recipes without synthetic daily stock or reserved item generations. Personally owned market goods still transfer from their actual owner.
- Guard rest requires a reachable quarters entrance. Beds and interior positions are assigned only when those quarters are loaded. Pending indoor guards cannot become exterior conversion candidates or render without a valid position.
- Guards who see an active player pursuit can join without close guard-to-guard contact. An actual player-directed native `Halt!` shout alerts available guards within 200 metres of the shouting guard without requiring line of sight. Heard location does not bypass each guard's later personal sight requirements for combat.
- Murder searches use varied reachable area routes and approach visible people for the existing physical inquiry procedure. Selecting a person does not confer witness knowledge or guilt.

## One LLM plus TTS

The separate D3 routing/consent service and Decision Shadow runtime, settings and launchers are removed. Normal dialogue uses the existing LLM for goal extraction, Two Pass parameter resolution, willingness and receipt-grounded wording. TTS remains speech output. The actual host launcher is synchronized with the tested repository launcher; a stale saved `EnableDecisionPlanner=true` cannot start another model.

Goal Extraction and Two Pass are retained deliberately. The real 22–23 October Gemma E4B study already achieved 6/6 five-step plans and 17/18 total three-to-five-step plans with no false-ready controls. Multiple calls to one model do not imply multiple models. The later 18/18 target-repair result used known fixtures and remained evaluator-only; this cleanup does not claim new live-model accuracy. See [the original study](../2026-10-22-npc-model-study-results.md).

Current scene context, meaningful memory and technical history retention remain unchanged as explicitly requested. Only identity projections needed for real guest households are corrected. Native validation, asynchronous session ownership, cancellation, exact payments and observed effect receipts remain authoritative.

## Persistence

The integrated format is schema 85. Only that exact format is accepted. Old development saves are rejected before state mutation; no conversion or rescue is attempted and no save file is deleted. Current-format identity, memory, route timing and transaction roundtrips remain supported. Obsolete save-only conversion code and tests are removed.

## Verification

Integrated offline evidence is retained under `work/immersion-simplification-20261026/`:

| Check | Result |
| --- | --- |
| Main Core suite | 2147/2147 |
| RuntimeCompile | 0 warnings, 0 errors |
| NPC commerce/rest extracted adapters | 31/31 |
| Stockless guild recipes | 3/3 |
| Current save schema and memory | 5/5 |
| Routines | 7 new + 120 linked cases |
| Same-model willingness | 16/16 |
| Goal extraction, 3–5-step execution contracts and dispatch | 56/56 |
| Mocked game/service launcher | 15 cases |
| Player pursuit, actual Halt handler and scene routes | 14/14 |
| Existing corpse/evidence cases after old-save test removal | 43/43 |
| Origin household and coupled guest lifecycle | 6 groups |
| Standing traveler recovery | positive and interruption/arrival controls pass |
| Payment, health pause and deferred rest | 65 cases across five forced-build entry points |
| Updated historical factor/speech evaluation tools | compile with 0 warnings/errors |

Independent source reviews covered routines, commerce, guard pursuit, single-model dialogue and payment/rest. Review fixes include missing-keeper catalog handling, exclusion of sheltered guards from exterior native conversion, and rejection of late bed assignments that would delay the next shift. Rejected assignments preserve the existing coarse rest state and allow a closer candidate. Focused tests execute production Core and selected actual adapter methods with controlled native hosts; they do not replace native gameplay verification.

## Native acceptance boundary

This task did not launch a game, invoke a model, build/install a Unity package or alter another chat's running Unity process. The shared room-audit chat owns the current native build sequence. Source compilation and mocked/extracted-adapter tests do not prove Unity visibility, actual audio, collision, bank routes, counter purchases, physical interviews or free-running multi-step dialogue. Those native checks must use fresh schema-85 mod data and a package built after this integration.
