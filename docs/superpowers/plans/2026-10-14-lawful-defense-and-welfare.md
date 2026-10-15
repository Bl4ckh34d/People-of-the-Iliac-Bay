# Lawful defense, corpse rights and situational guard welfare

**Goal:** Killing a genuine hostile attacker grants immediate persistent loot rights without Murder, and witnessed fights receive a short-lived guard welfare exchange followed by existing crime enforcement.

**Spec:** `docs/LEGITIMATE-DEFENSE.md`, reflecting the user's approved indoor/outdoor defense, immediate loot, bounded gifts, situational timing and outstanding-bounty requirements.

**Architecture:** A saved combat-provenance ledger records original hostility, player provocation, participation, actual fight witnesses and death. The native adapter supplies authoritative hit/death/loot observations. Physical guard approaches use the existing finite roster and surveyed routes. Courtesy never clears player crime. Native enemy corpses remain the actual inventory; no duplicate loot is generated.

**Constraints:** Standard DFU1.1.1 mod, Unity2019.4.40f1, no tracked engine changes; one existing WorldState save owner; immutable target identity, finite responders, no teleport paths, no imaginary witnesses. Existing families, quests and guard states survive migration. The five-in-game-minute courtesy window starts at death, expires sooner on leaving the local scene, and is never renewed by loading. Gift offers are decided once with stable seed and saved probability; one successful exchange can award at most one potion. Native stationary/mobile civilians and guards cannot become lawful victims merely because the player provoked them.

## Stage1: persistent provenance and courtesy contract

Files: `src/LivingCities.Core/DefenseIncidents.cs`, `WorldState.cs`, `tests/LivingCities.Tests/DefenseIncidentTests.cs`, existing schema assertions and `tools/Editor/LivingCitiesBuild.cs`.

Types: `CombatOrigin` (HostileAttacker, Civilian, PassiveEnemy), `DefenseCombatant`, `DefenseWitness`, `DefenseDeath`, `WelfareResult`. Add `WorldState.DefenseCombatants` and migrate schema1–6 into schema7 with an empty ledger.

API: `RegisterDefenseCombatant(id, settlementId, origin, now)` preserves first observation; `RecordDefensePlayerHit(id, now, wasHostile)` permanently records provocation when appropriate. `RecordDefenseWitness(id, observerId, isGuard, now)` records actual sight supplied by the adapter and rejects dead/unknown observers. `ConfirmDefenseDeath(id, now, buildingKey, x,y,z, killedByPlayer, welfareSeconds, potionPercent)` confirms location once, grants immediate loot only to an unprovoked hostile attacker killed by the player, and fixes courtesy expiry/offer. `InformDefenseGuard(id, witnessId, guardId, now)` requires an actual recorded witness and a living known finite guard. `ObserveDefenseScene(id,onSite,now)` expires courtesy after departure/time. `CompleteDefenseWelfare(id,guardId,now,acceptPotion)` consumes at most one eligible informed-guard exchange and returns its bounded gift decision.

Test first: genuine attacker loot before guard arrival; provoked passive enemy and civilian hostility cannot qualify; duplicate death cannot move expiry/re-roll gifts; unwitnessed death has no welfare; stale witnesses do not qualify; exact expiry/departure survives reload; prior participation can qualify for witnessed welfare even when a guard finishes the enemy; death of witnesses never revokes earned rights; gift decline/accept cannot be farmed; unknown identity/time/coordinates and mixed state reject restore transactionally; old saves gain no invented rights. Then native WorldState serialization verifies target provenance, immutable deadlines and consumed gifts. This stage alone is not playable welfare.

## Stage2: native combat and corpse binding

Progress,15 October: native weapon/sourced-health and Disintegrate hooks, actual melee/sourced aggression evidence, immutable passive/ally provocation, invisible-witness exclusion, real corpse binding and immediate native inventory pickup pass. A real dedicated game-slot roundtrip retains corpse, remaining inventory, loot entitlement and prior crime. Scope and evidence are in `docs/LEGITIMATE-DEFENSE.md`. Remaining before Stage2 completion: target-owned scene context and transitions, broader outdoor/mod paths and indirect group provocation. Stage3 remains pending.

Scene follow-up: target-owned interior/exterior coordinates and same-context interior corpse parenting now pass controlled relocation tests, including restored rooms without DoorOwner and inactive exterior parents. Cross-world-context corpse tracking, cross-settlement death, unparented mod actors, observer scene ownership and indirect group provocation remain. Extended testing also fixed resident escape planning to use saved alarm position rather than the rendered position.

Files: native combat hooks under `src/LivingCities.QuestHooks`, `StreetPopulation.BeginWeaponHit`, a focused native defense adapter and live probe under `mod/Scripts`.

Register original native enemy hostility before a player hit can change it. Preserve provocation on later hits and reload. Track player weapon and spell sources separately from guard/environment damage. Observe fight witnesses while the hit occurs; body discovery cannot fabricate a hero. Bind native EnemyDeath's actual corpse marker/LoadID and inventory to the saved resolution. Existing native enemy corpse transfer is already free; confirm actual item transfer and no Murder/Theft both before guard arrival and after reload. Never apply this exemption to managed civilian crime corpses or City Watch deaths.

## Stage3: physical scene security and guard courtesy

Partial runtime checkpoint: native controller now approaches, opens the welfare message, awards a bounded native potion and preserves crime handoff. A controlled indoor fixture verifies these paths and assault cancellation; core310/310, native build and citizen-defense regression pass. Full natural encounter, outdoor/mid-approach reload acceptance, civilian witness relay and scene-security policy remain unfinished. See the current checkpoint in `docs/LEGITIMATE-DEFENSE.md` for exact evidence and limits.

User correction, 15 October: gate the entire welfare exchange on current player health strictly below 66% (not 75%) and above zero. Recheck health and the responding guard's specific threat-report membership before dialogue and completion. Loot entitlement and existing bounty are independent of this health gate.

Extend outdoor/indoor guard reports with lawful-scene policy rather than using guilty-suspect investigation behavior. Welfare uses only guards who responded to fight this particular threat, as clarified by the user on 15 October. Do not dispatch separate courtesy responders or choose unrelated nearby guards. Retain the threat-response identity after the enemy dies. Approach the player only within the live local courtesy window, then show the brief welfare/loot dialogue and optionally add one native healing potion. Consume the saved exchange once; restore normal native bounty surrender afterward. Departure or expiry prevents late greetings/gifts while preserving loot rights and continuing legitimate scene security.

## Stage4: live acceptance and release

Test unprovoked hostile and player-provoked civilian/enemy, indoor/outdoor scenes, witnessed/unwitnessed kills, real weapon/spell provenance, immediate native loot transfer, real guard travel, prior bounty after welfare, one gift across reload, expiry/departure and unavailable routes. Re-run civilian murder/theft, native surrender, keeper and patrol regressions. Update milestones, README and Obsidian from verified scope. The overall goal and remaining broader event/quest/settlement requirements stay active.
