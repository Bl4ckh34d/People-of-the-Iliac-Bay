# Finite guards

## 0.3.24 paired street patrols

Gothway Garden now starts finite pairs at night on collision-checked native navigation circuits. Both guards physically join separate circuit entries, then share a saved time phase with two metres of path spacing. Formation validation rejects routes that bring them within one metre. Initial survey found two usable circuits of 57.6 and 102.4 metres. Failed surveys leave guards posted; there is no straight-line fallback. Route work yields between searches, with bounded attempts and circuit length.

`NightPatrols` defaults on. `MaximumPatrolPairs` ranges from 0 to 4, default 2; zero disables patrols. Starting a pair preserves at least four available posted guards. Reducing the cap, disabling patrols or reaching dawn sends excess pairs physically back to their posts. Crime responses use actual patrol positions; cached building responses join their existing approaches through physical travel. A fleeing witness targets a stationary posted guard. Existing torch controls remain independent.

Circuit survey is bounded by the pair cap when the town loads. Increasing the cap beyond already surveyed pairs requires reloading the town; a failed survey does not retry every frame. Reducing the cap takes effect during the current session.

Actual-player GuardPatrolProbe checks surveyed pairs, both walking animations, spacing, point light, real save/reload phase, disabling patrols and native crime dispatch from the current returning position without adding guard identities. Evidence: `work/GuardPatrol-0324-pass.log`. SAVE45, `Living Cities - night patrol`, belongs to `LivingCitiesTest-f238d317`. Native JSON timestamps round at this magnitude; reload comparison allows 0.001 game-second while verifying IDs, interruption state and actor positions separately.

Final housing, physical building response and crime-scene regressions also pass with the compiled package. They preserve named keeper families, reserved-bed sleep/services and real reload; delayed guard entry, wounded persistence and return; and native surrender before damage, court, warning and occupation expiry. Evidence: `work/Housing-0324-pass.log`, `work/GuardBuilding-0324-pass.log`, `work/CrimeScene-0324-pass.log`.

The package uses DFU's official precompiled mod assembly support after source-text compilation failed during native navigation. The exact compiler failure is not established. Guards remain classic sprites with attached flame artwork; wall patrols, simultaneous join/response crowd avoidance, additional settlements and large-city frame-time profiling remain unfinished. M4 and the overall goal remain open. Earlier checkpoints below are historical where superseded.

## 0.3.23 night torches

Visible exterior roster guards now carry Daggerfall's animated210/0 torch flat at night, using the game's dawn/dusk boundary. Each carried sprite follows its guard transform. Daytime, interiors, the setting toggle and guard death extinguish the torch. Torch flats have no active colliders and do not change guard identities, paths or save ownership.

The mod menu exposes `NightTorches`, `TorchLightLimit` (0–16, default8) and `TorchRange` (3–12 metres, default8). Only the nearest selected guards within48 metres emit actual warm point light. A zero light budget retains visible flames. All guard lights have shadows disabled; light selection refreshes four times per second using a reused candidate list. The carried flat is a presentation attachment, not new guard hand/weapon animation artwork. Paired patrol routes and wall patrols remain unfinished; existing posted/responding guards benefit immediately.

Live GuardActorProbe verifies point-light settings, flame visibility independent of the lighting budget, toggle/death extinction and classic animation. GuardControllerProbe verifies actual game night/day, a one-light cap, zero budget and the toggle, followed by native response/combat, wounded save/load and return. The controller takeover fixture now clears ownership before creating its transient native guard; because finite guards default on, the previous fixture incorrectly received an owned roster guard and expected its destruction. That correction changes only the test setup.

The following version statements are historical. See GUARD-BUILDING-RESPONSE.md for current ownership, persistent missing-route reports and discovery behavior.

Evidence:198/198 core tests, Unity/native build, `work/GuardTorch-red.log` (missing torch presentation), `work/GuardTorch-0323-actor-pass.log` and `work/GuardTorch-0323-controller-pass.log`. The final actor probe also observes multiple rendered flame frames. Read-only Luna/high review found no confirmed important defect. Base DFU tracked Assets/Scripts remains unchanged. Large-city lighting/frame-time profiling remains open.

Current 0.3.9 adds delayed indoor witness reports when a physical exit-and-post route exists. Guard dispatch retains the crime building while the witness crosses scenes; ordinary native indoor alarms still use the 0.3.7 adapter. Unwitnessed discovery is not implemented. See INDOOR-WITNESSES.md for the route, persistence and fallback boundaries.

Current 0.3.8 adds public witness reporting. Outdoor generated-resident kills require a local guard witness or a fleeing direct witness reaching a posted guard; reports retain the original crime position and cannot replenish their finite responder group. Both visibility checks use 20 metres. See CIVILIAN-RESPONSES.md for tested scope and limitations. Indoor alarms retain the 0.3.7 behavior below.

Current 0.3.7 adds finite indoor responses for surveyed Gothway Garden buildings: delayed doorway entry, native combat, wounded direct-interior reload and path-based exit/return. The same existing ExperimentalExteriorGuards setting now enables both supported contexts. Unsurveyed interiors remain vanilla. See GUARD-BUILDING-RESPONSE.md; historical limitations below are superseded only where that checkpoint provides evidence.

Version 0.3.6 now incrementally surveys real exterior post-to-door approaches and preserves completed building routes through actual save/load. Indoor dispatch and actors remain pending; see GUARD-BUILDING-RESPONSE.md. Older installed-version statements below describe their historical checkpoints.

The next building-response core now persists validated exterior approaches and immutable building-bound reports. Core tests: 146/146; DFU native serializer verification passes. The installed player remains 0.3.5 while the indoor adapter is unfinished. See [building response implementation](GUARD-BUILDING-RESPONSE.md).

## Live controller checkpoint, 14 October 2026 — version 0.3.5

Enable General / ExperimentalExteriorGuards to try the exterior Gothway Garden controller. Indoor guards remain vanilla. This supersedes older statements below that guard bodies were test-only.

A fixed saved roster supplies bounded reinforcements along validated routes. The controller removes old non-quest transient guards and hides pooled guards while it owns the exterior. Native combat activates on sight or direct hostility. Wounded health, actual position and last-seen suspect position persist; reload does not grant fresh player-position knowledge. Search expiry routes guards back from their actual positions.

Guards / MaximumResponders permits 1–8 (default 4); ActorRadius permits 64–256 metres (default 160). Engaged guards remain active beyond that radius. Settings never reroll existing rosters.

Verified: 142/142 core tests, native Unity build/settings/serialization checks, and actual-player GuardControllerProbe plus GuardActorProbe. Tests cover bounded repeated reports, takeover, combat readiness, wounded SAVE30 reload, search expiry, posted self-defense, walking animation and persistent death. Full arrest dialogue and an actual weapon hit on a guard are not covered by these probes.

Next: physical shop reinforcements and door transitions, paired patrols/torches/walls, citizen reports and defense against other enemies. Guards outside the reachable navigation grid can stop and retry their return route indefinitely; there is no teleport fallback. M4 remains incomplete. Evidence: work/guard-controller-runtime-pass.log and work/guard-actor-runtime-pass.log.

This is the foundation for M4 under the existing approved gameplay requirements. The installed 0.3.4 development preview still uses native spawning in ordinary play until the replacement controller is validated.

## Physical actor checkpoint

`GuardActor` now materializes a saved identity as a native City Watch body. It follows saved route positions and native directional walking animation, retains native colliders and death handling, and records deaths in the roster. `LoadID = 0` excludes the view from native enemy persistence, preventing a second native save owner. Creation preserves shared random state. A dead saved guard cannot materialize again.

Native `EnemyEntity.Update` normally deletes City Watch bodies when the player has no active crime. A narrowly scoped `NativeGuardLifetimeHooks` patch skips that destruction only for bodies carrying `GuardActor`, while preserving the rest of the native entity update. Ordinary vanilla guard cleanup remains active.

The actual-player command `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -GuardActorProbe` passes repeated fixture loads, timed route placement, multiple rendered walking frames, crime-free survival, unchanged RNG, unowned vanilla cleanup, native death and serialized no-resurrection checks. Evidence: `work/guard-actor-runtime-pass.log`. Build and native hook/serializer checks pass. No old save slot is rewritten by this focused probe.

This actor view is currently exercised by the explicit test mode only. Native motor, senses and attack are disabled while the view owns its position. The production controller still must enforce one view per ID, handle scene/door transitions and distance visibility, preserve wounded health, dispatch crime reports and connect pursuit/combat/arrest. Do not claim that normal play already has finite guards.

## State and behavior

A settlement owns a saved roster. Each guard has a permanent identity, a post, current duty, location/route and life state. Creating a settlement twice returns the same roster. Population changes and menu changes never silently reroll existing guards.

The initial policy uses five percent of residents, at least four guards in towns and eight in cities. Very small hamlets (fewer than forty residents) have no garrison. Larger hamlets have a small contingent. These defaults are a starting policy, not a claim that settlement-size tuning is final. Later settings must expose supported controls.

Reports dispatch available living guards by actual route length, with up to four responders. A guard cannot answer two incidents simultaneously. Repeated reports reuse the same dispatch rather than replenishing it. Travel uses game time and measured route length; actor visibility and player door transitions do not control elapsed travel. No reachable route or no available guard means no responder.

A report with no assigned responders remains pending and can retry when a route or guard becomes available. Once any responders are assigned, the same report never recruits additional responders. Invalid route requests leave the entire roster unchanged, including other guards whose travel would otherwise advance.

Arrival starts a bounded search at the reported location. Fresh sightings may update pursuit; loss of sight leaves only a last-seen position. Search expiry returns guards to their posts. Recruitment preserves dead identities and introduces a new identity only after a delay, with at most one replacement per day. It never happens while loading a save simply because an actor is absent.

## Implementation sequence

The production `NativeGuardHooks` bridge now intercepts both native spawn entry points and suppresses automatic pooled-guard conversion while the roster owns a town. Its owner supplies a supported-town predicate, a report callback and a real roster-actor resolver. Native Unity tests exercise immediate/deferred calls and actor returns, unsupported-town fallthrough, conversion gating, disposal and rejection of null actors. The hook is deliberately not installed by `LivingCitiesMod` yet: there is no physical roster controller to satisfy its actor contract.

The controller must also take ownership of native mobile guards. `WeaponManager` immediately dereferences a guard returned while attacking a mobile guard, and `PlayerEntity` can convert additional pooled guards after a response. Leaving native pooled guards active alongside the roster would duplicate the population. Entry from an interior must retain exterior route/door information; current saved citizen routes do not provide a complete navigation cache. These are integration requirements before activation, not reasons to keep instant native reinforcements in the final mod.

1. Test and implement roster identities, settlement counts, finite dispatch, distance-based arrival, search/return, death and bounded recruitment in the deterministic core.
2. Persist and validate the roster in schema 6, including native DFU serialization and migration from schemas 1–5.
3. Replace supported-town native batch/single spawn paths with roster ownership, preserving the return contract when vanilla hits an already visible guard.
4. Materialize patrol/post/response actors at their real locations. Preserve native combat/arrest integration while controlling sight/search and door arrival.
5. Test repeated reports, exhausted/dead roster, nearby/far reinforcements, escape, shop entry, save/load mid-response and old-save compatibility in the actual player.

Paired torch patrols, walls/headquarters, creature defense and wider settlements remain part of M4 after the first dispatch adapter. They are not optional replacements for the above requirements.

## Night patrol lighting requirement

Night street patrol guards carry visible torches that emit moving local light. This is part of the planned paired-patrol behavior, not yet implemented. Limit active lights by distance and expose sensible lighting controls in mod settings so large cities remain practical.

## Surrender and arrest contract

Guards must preserve vanilla surrender before damaging the player for a crime. Native EnemyAttack.ApplyDamageToPlayer opens the Yes/No surrender message on the first successful City Watch hit when a crime is active and the dialogue has not already been shown. That branch does not call SendDamageToPlayer. Yes calls SurrenderToCityGuards(true) and opens court; No applies the attack damage. The adapter must not bypass this flow. Actual-player verification now passes: a real roster guard runs the native attack handler with god mode disabled, opens surrender before damage, and accepting Yes enters the native court window. The probe calls the native button handler directly rather than clicking the UI. Evidence: work/SurrenderProbe-pass.log. Full sentencing and imprisonment outcomes remain unverified.
