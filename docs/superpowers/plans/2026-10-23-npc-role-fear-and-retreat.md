# NPC role, fear and retreat implementation plan

> Implement with explicit file ownership, test-first changes and parent review. The other chat's memory-compaction implementation has stopped; do not modify its snapshot or resume that work.

**Goal:** enforce the user's rank, combat experience and service obligations in state, presentation and native combat behavior.

**Architecture:** native-backed role facts are separate from random personality. Core owns deterministic fear/retreat policy and saved facts; native adapters resolve actual rank/career/duty and execute existing native combat/movement. Unknown evidence remains unknown. Player-directed fear, danger fear and combat action are distinct.

**Tech stack:** C# Core/netstandard, .NET regression runners, DFU Unity 2019.4.40f1 native adapters and controlled game probes.

**Spec:** the central ROADMAP section “rank, combat role and allegiance govern fear and flight”, including the user's later guard/soldier death-duty exception.

**Verified checkpoint (23 October):** implementation and scoped native acceptance are complete for saved roles/rank fear, controlled native combat, managed CityWatch Town23 retreat and healthy haunting duty. See `docs/2026-10-23-role-fear-retreat-native-acceptance.md` for exact runs, retained failures and limits. Palace-stationed static NPC combat-body integration remains open. The final user clarification supersedes the original blanket guard death-duty rule: CityWatch is 98/2; PalaceGuard/Soldier never retreat.

## Constraints

- Keep Gemma and Breeze. No changes to model defaults or unrelated portrait production.
- Preserve native hostility, quest protections, old save evidence, role facts and decisions across reload.
- Do not infer high rank from Individual faction type or service from career. Later explicit user policy: generic combatants with no recognizable quest/service binding default to Independent; proven binding wins. Palace-stationed actors serving the nobility are NobleKingdom, not generic independents. Visiting companions are not palace-stationed.
- Noble native social group and exact known royal bindings are valid rank sources. Explicit guard duty is a guard role; generic faction rank has no verified threshold.
- Combat roles lower acute fear without erasing unrelated mood, resentment or trust.
- Initial balancing: low danger fear is at most 10/100; independent combatant retreat is eligible at health <=25%, with 35% stable per-encounter chance. User clarification: PalaceGuard and Soldier never retreat; city watch Guard fights in 98% of eligible encounters and retreats into verified city-wall quarters in 2%. Missing reachable wall routes preserve combat. Health and independent-retreat thresholds are chosen balancing values, not pre-existing DFU facts; the 98/2 city-watch split is an explicit user requirement.
- Do not send messages to unrelated portrait chat `01a135ac-12fe-7041-9e19-3dbc601bd8cb`.

## Task 1: Core role facts, persistence and policy

Create `NpcRolePolicy.cs` and `NpcRoleStore.cs`; add a versioned role list to `WorldState`. Public contract: `NpcSocialRank { Unknown, Commoner, Nobility, HighAuthority }`, `NpcCombatRole { Unknown, Civilian, Guard, Soldier, Warrior, Rogue, Assassin, Combatant }`, `NpcServiceBinding { Unknown, NobleKingdom, Independent }`. `NpcRoleState` holds PersonId, the three values, separate source IDs and a monotonic Revision. `ReadNpcRole` returns a detached copy; `RecordNpcRoleEvidence` is native-only and merges supplied facts without replacing known facts with Unknown. Migration of old saves starts with no invented role evidence.

Policy APIs: `SuppressPlayerFear(role)`, `IsCombatExperienced(role)`, `FightsToDeath(role)`, `DangerFear(role, rawIntensity)`, and `ShouldRetreat(role, currentHealth, maximumHealth, encounterRoll)` with an explicit 0..99 roll. Validate invalid health and unknown role/service conservatively. Store and policy tests must show positive and negative controls, stable saves, independent changes to rank/service, and no caller mutation of saved facts.

- [ ] Write and observe failing role/persistence tests.
- [ ] Implement and pass targeted tests plus save compatibility checks.

## Task 2: Rank-aware fear writes and projections

Integrate role policy into `NpcRelationshipStore`, `NpcExperienceAppraisal`/`NpcExperienceCommit`, `NpcDirectedRelationshipView`, `ConversationContextFormatter`/snapshot and `NpcPresentationState`. Preserve legacy stored fear/history but expose an effective player fear of zero for verified nobility/high authority. Apply the same rule to contact projection, portraits, Breeze and request grammar/validation; unrelated danger fear remains visible. Current role changes must invalidate or reject stale appraisal snapshots. New positive fear writes for protected rank must fail at the authoritative commit boundary, not merely through prompt wording.

- [ ] Red/green tests for noble versus commoner fear commits, stale role changes, dialogue/portrait/voice projections and retained historical evidence.
- [ ] Integrate and run focused appraisal/context/presentation regressions.

## Task 3: Native role binding and combat behavior

New focused native binding files resolve verified faction social groups, known royal bindings, guard actors and native enemy careers. Explicit service facts remain independent. Bind role facts before dialogue/appraisal/presentation and before danger response. Extend native tests with controlled authoritative role bindings for facts not available on vanilla static NPC data.

Use native combat targeting and attack components for combatants. Do not change `CivilianReaction.Fight` blindly: the existing civilian counterattack path damages PlayerEntity and is unsafe for hostile-enemy targets. Guard/soldier native combat must continue at critical health; independent combatant retreat requires actual low health, established independence and a stable encounter decision. Quest-owned actors and neutral targets retain existing authority protections.

- [ ] Review native career, target and save lifecycle seams before implementation.
- [ ] Red/green tests for guard/soldier no retreat, independent wounded retreat and fight controls, healthy/unknown-service no retreat, and civilian behavior unchanged.
- [ ] Verify actual native movement/attack behavior and reload stability with controlled disposable actors.

## Task 4: Parent verification and handoff

- [ ] Review all subordinate diffs and run affected Core/runtime regression suites.
- [ ] Coordinate GPU/build ownership only with the relevant implementation chat before native work.
- [ ] Build/install; test rank context plus combat/retreat and saved state on the owned fixture; restore settings and baseline.
- [ ] Update ROADMAP, evidence report, Obsidian and the relevant mindmap owner. Record coverage limits without claiming every native NPC has verified service metadata.
