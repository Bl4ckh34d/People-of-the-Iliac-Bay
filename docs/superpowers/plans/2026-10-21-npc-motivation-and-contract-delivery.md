# NPC motivation and paid companion delivery plan

Status: staged delivery plan for the user-approved framework, 21 October 2026. Read the [design specification](../specs/2026-10-21-npc-motivation-relationships-and-contracts.md) and [integration inventory](../../2026-10-21-framework-integration-map.md) before implementing any slice. Implementation acceptance, numeric calibration and successor model protocols require their own evidence; framework approval is not a claim these slices are finished.

**Goal:** Give conversations, voluntary tasks, paid duties, information choices and future companions one coherent persisted basis without replacing native authority or existing identity/memory systems.

**Architecture:** Reuse immutable conversation snapshots and the Qwen interpretation / D3 bounded decision / native validation-execution-rendering boundary. Add directed relationship and obligation components, action-specific utility policy and contract ledgers incrementally. Keep ordinary contracted combat execution native.

**Tech stack:** existing C# 7.3 Core, net472 runtime compilation and Unity 2019.4.40f1. Local Qwen and D3 remain shared services; no additional model is required. All gameplay periods use game time. All NPC intent/appraisal is semantic model output, never text regex or keyword matching.

## Slice 1: Relationship and personality data contracts

- [ ] Inspect current WorldState, ConversationAttitudes, ConversationMood, identity seeds, archive checkpoints and schema migration before choosing final new type names.
- [ ] Define directed relationship owner/target IDs, trust value/confidence and evidence references; keep existing disposition as affinity and add other dimensions only with an identified consumer. Define once-created personality extensions without rerolling existing traits.
- [ ] Write failing Core cases for old saves, unknown strangers, asymmetric relationships, stable trait initialization, bounds, deep copies and sibling-save isolation; implement the smallest compatible records and migration.
- [ ] Deliverable: serializable isolated state plus immutable reads, without changing current gameplay decisions. Review and run the Core suite and real save round-trip before enabling consumers.

## Slice 2: Shared motivation snapshots and relationship events

- [ ] Extend ConversationContextSnapshot/Formatter and PeopleOfTheIliacBayMod.ContextSnapshot with separately attributed personality, mood, disposition, trust/confidence, relevant duties and actor-known constraints.
- [ ] Reuse current sequence/epoch/person ownership and memory provenance. Define a versioned typed relationship event/appraisal contract, with a single native reducer and explicit event identity; speech does not prove promise fulfillment.
- [ ] Test once-only event effects across social/action consumers, invalid/stale/replayed events, missing evidence, refusal without automatic penalty, existing +5 gradual friendship limits and indoor/outdoor weather boundaries.
- [ ] Deliverable: same person and decision see the same factors; Qwen voice and D3 willingness consume them without leaking private/quest truth. Native-only responses do not require another model call.

## Slice 3: Decision-specific policy and contrasting evaluation

- [ ] Add focused Core policy units for eligibility, normalized utility contributions, profile/version and evidence traces. Final public signatures must be agreed before parallel workers implement callers.
- [ ] Preserve current LocalDecisionWillingness ACCEPT/DECLINE protocol; add factor context first. Any richer outcomes need a separate versioned bounded-choice contract bound to offered IDs, snapshot and exact proposal.
- [ ] Test paired requests varying only one factor: mood, affinity, trust, personality, duty, resource, danger or competing appointment. Include paid duty despite moderate irritation and zero permission to create capabilities or receipts.
- [ ] Run frozen local-model contrasts without semantic regex grading. Keep failed outputs and separate ranking/model behavior from native safety/effect correctness.
- [ ] Deliverable: auditable individual choices with explicit authority; no global obedience number, unbounded stochastic rerolls or per-frame model decisions.

## Slice 4: Information and disclosure policy

- [ ] Extend existing knowledge/memory readers with permitted disclosure outcomes: known truth, attributed uncertain belief, partial answer, refusal, and later bounded deliberate deception.
- [ ] Keep protected facts outside renderer input. Require speaker-known conflicting belief/evidence before labeling a statement a deliberate lie; journal propositions/intent without overwriting world truth.
- [ ] Test privacy versus warmth, honesty versus mistaken belief, trusted versus merely liked listeners, correction, rumor, private third-party data and quest secrecy. No unsupported deception action becomes executable yet.
- [ ] Deliverable: conversations can express knowledge and privacy coherently using the shared factor model; subjective speech remains separate from native action receipts.

## Slice 5: Companion contract and wage ledger

- [ ] Implement Core-only scoped contract states, risk/services, funded intervals, explicit renewal permission, ready-at start, suspension/expiry and termination reasons. Integrate reservation admission with existing schedules and appointments.
- [ ] Implement atomic debit/escrow/earned-release/refund receipts, cumulative fractional accrual, deterministic rounding and stable estate/corpse ownership. Adopt the design's termination table explicitly or revise it before code; do not leave refund behavior to the model.
- [ ] Test insufficient funding, duplicate acceptance, before/at/after 24 game hours, partial service, agreed rest, suspension, multiple periods, expiry/nonrenewal, dismissal, death, abuse, quest exclusion, crash/reload and divergent branches. Ended contracts leave no ownerless escrow.
- [ ] Deliverable: money-conserving persistent contract simulation without claiming physical combat support.

## Slice 6: Physical hired companions and role adapters

- [ ] Add recruitment only for verified eligible non-quest actors with actual home/return destination and real combat capability. Preserve identity, belongings and existing quest/native controls.
- [ ] Connect the contract reservation to existing follow/travel/task/recovery adapters. Add fighter, archer and mage controllers separately with actual skills, equipment, ammunition, magicka, line of sight and native damage/effect semantics.
- [ ] Add event-driven warnings, tactical retreat, resignation, one-time settlement and resumable escape/return home. An agreed bounded expedition may replace ordinary nightly home return; essential obligations remain explicit admission or interruption rules.
- [ ] Native acceptance for each role: ordinary paid combat despite moderate low mood/trust, no per-attack consent/model calls, a refused out-of-scope order, emergency withdrawal, refund, physical return and schedule recovery through door/dungeon/scene transitions and save/reload. Test route failure without teleport/duplicate actors.
- [ ] Deliverable: an actual paid dungeon companion lifecycle, not just accepted dialogue or a class label.

## Slice 7: Betrayal and complex allegiance, separately enabled

- [ ] Define permitted motives, conflicting allegiances, concrete opportunities and supported effect adapters. Distinguish warning, disagreement, retreat, termination, theft and deliberate hostile betrayal.
- [ ] Test no arbitrary low-mood backstabbing, stable decisions across reload/ticks, real ownership consequences, witnesses/faction attribution, discovered deception and attributed memories.
- [ ] Deliverable: a separately accepted behavior capability. A renderer or low score alone can never initiate damage, theft or disclosure.

## Verification ownership

Each code slice starts with failing meaningful contract tests, then minimal implementation, independent review and appropriate Core/adapter/runtime checks. Parent alone serializes model-service use, Unity builds, installation and owned native game sessions. Capture exact evidence and limits in TESTING/ROADMAP/Obsidian. The full roadmap remains active throughout; planned architecture is not implementation acceptance.
