# Rank, fear and combat retreat acceptance

The user distinguishes palace guards and soldiers (never flee) from city watch (98% remain in combat, 2% may retreat into the city wall). Generic unbound combatants default to independent service; verified palace/service evidence takes precedence. The chosen injury threshold is at most 25% health. Independent combatants initially have a 35% stable retreat chance at that threshold.

## Implementation

`WorldState` schema 43 stores separate rank, combat-role and service evidence with source IDs and revision. Unknown observations do not erase known evidence. Noble/high-authority player-directed fear projects to zero and rejects new positive fear writes; historical raw evidence and fear of other actors remain available. Appraisal and conversation snapshots reject changed role evidence. Combat experience reduces acute danger fear independently of mood and interpersonal fear.

Native combat binds to DFU's own `EnemyMotor.TakeActionHandler`, preserving foreign callback ownership, native hostility and quest protection. Stable world seed and actor/target identities determine the roll, without frame-by-frame rerolls. PalaceGuard and Soldier always reject custom retreat. Independent severe-injury fighters use bounded, collision-checked movement away from their admitted target.

Managed city watch bind through `GuardPopulation.View`. An eligible guard must have an active engagement, at most 25% health and a verified Town23 entrance/interior route. The saved roster owns the refuge journey, shelter release and return to post; the callback does not also move the controller. Route search considers the nearest eight wall entrances and fails closed to native combat if none passes. Release is at dawn after at least one sheltered hour. This is an initial release policy, not a healing system.

Old haunting-specific blanket guard flight, patrol cancellation and gate-duty cancellation have been removed. Existing saved refuges retain their release handling. Civilian haunting responses are unchanged.

## CPU and build evidence

Evidence directory: `work/npc-role-native-20261023/`.

- Full Core runner: 1670/1670 pass (`core-final.log`).
- Role policy/persistence: 11/11 pass, including exactly two eligible city-watch rolls out of 100 and zero PalaceGuard/Soldier rolls.
- Role-directed fear: 5/5 pass.
- Role conversation context: 14 assertions pass.
- Native combat decision regression: 8/8 pass.
- RuntimeCompile: zero warnings/errors (`runtime-compile-final.log`).
- Independent final review found no additional P1/P2 after parent review. The nearest-eight search is an explicit bounded coverage limit.

## Native evidence

Native runs use the owned character `PeopleOfTheIliacBayTest-9a5f197b`, save `People - Gothway Garden`. Every run records the installed bundle SHA-256, retains its Player log and restores settings. No extra LLM request or model-quality retry is part of this acceptance. Gemma and Breeze remain unchanged.

Build 05: `9968a6b6f72fea5c83081f0d182f27b7c56329f9b2caba3df0490bec7f8372b2`. Final installed build 06: `f0eafae93e115a5fcc301a4ff70a2bf43b86d93bd907de7092578ec06d16c6d2`, built by Unity 2019.4.40f1. Deployed C# manifests prove the only difference is `NpcResentmentRecoveryProbe.cs`: three stale schema-42 assertions now use `WorldState.CurrentVersion`. Gameplay sources are identical. Combat/wall/haunting acceptance below used build 05; identity acceptance used final build 06. These runs are not claimed to use one identical bundle.

| Run | Result | Scope |
| --- | --- | --- |
| combat-role-06 | Pass | Actual independent Rogue retreat 0.874 m away; healthy, unknown-service and noble-service controls preserve native actions. PalaceGuard at exactly 1 HP preserves 38 naturally scheduled native callbacks; all 100 policy rolls reject retreat. City watch without an owned route preserves 37 native callbacks. Quest-owned actors/protected targets preserve native action. Only the passive-player control calls the wrapper explicitly, because native CanAct correctly declines combat. |
| identity-role-03, final build 06 | Pass | Native faction 852 noble projection retains raw fear 60 with effective player fear 0; underworld faction 108 keeps fear 17; NPC-directed fear unchanged; FullSerializer schema 43 and detached static-context evidence pass. Full identity integration, three traits, stale action rejection and baseline rollback pass. |
| citywatch-wall-04 | Pass | Real host-managed Daggerfall guard `g:1291010263:n:0`, stable roll 1, verified Town23 building 770; native callback accepts saved refuge, physical GuardActor approach and return placement follow the route, at least one sheltered hour is retained, FullSerializer round-trip passes, completion returns to Posted and releases combat suppression. Runtime 122.9 seconds, mostly sliced municipal setup. No world-clock change or game-save write. |
| haunting-role-02 | Pass | Active curse/night Ghost and Wraith threat registration; healthy posted guard remains outside refuge; an existing native combat target and motor/attack controls remain intact; both members of a moving patrol remain attached and survive FullSerializer round-trip. Controlled world-clock advancement occurs only in the disposable process. |

## Failures retained and corrections

Earlier attempts are retained, not overwritten. Native severe-Rogue retreat exposed a real runtime error: ground steering was passed the controller/body center instead of the lower controller plane. The corrected bounds-minimum origin produced physical retreat in subsequent runs. The old framework probe asserted schema 42; it now checks `WorldState.CurrentVersion`.

Combat fixtures initially omitted native pursuit evidence and assumed every passive actor schedules a callback. Later failures exposed DFU's automatic destruction of unowned CityWatch when player crime is None. The isolated combat runner disables the municipal controller during disposable adapter cases, and the two native CityWatch fixtures temporarily hold Assault, restore it afterward and require naturally scheduled callbacks. PalaceGuard health is explicitly 1 HP. Production behavior is not changed to satisfy these fixture prerequisites.

The Daggerfall wall test initially assumed guard setup completed after two seconds, then after 90 seconds. Real sliced navigation/post setup required roughly two minutes. The probe now waits up to 240 seconds with readiness diagnostics. Binding startup similarly waits for the native motor and LateUpdate ownership rather than assuming one yielded frame is sufficient.

Old haunting probes hardcoded building key 256 and required the now-obsolete automatic guard flight. The updated probes select a real Town23 route, verify `LiftedCurse=false` at night, and assert retained duty instead. Controlled small rosters are isolated from unrelated municipal gate recruitment. These are specification/fixture corrections, not evidence that all legacy refuge and regional-population tests pass.

## Coverage limits

- Managed wall retreat requires the finite guard module. Its current bundled default is **enabled** (`ExperimentalExteriorGuards=true`); missing user overrides inherit that default. Vanilla guards without an owned wall resolver continue native combat.
- Static palace identity/service evidence is resolved where native metadata proves stationing. Creating combat-capable bodies for all static palace NPCs is not implemented. The PalaceGuard native test uses a controlled CityWatch body with that authoritative role.
- The wall test checks a controlled eligible encounter, native route placement and roster serialization. It is not a statistical simulation of 100 live battles, nor an exhaustive survey of every city.
- No new all-usecase Gemma claim is made. The prior V3 gift-answer quality failure remains historical evidence in the V3 acceptance report.

## Final cleanup

`verify_final.py` passed and wrote `verification.json`: all four selected native runs pass; 740 baseline save/archive files and the settings file match their pre-test SHA-256 values; owned DFU/Unity processes are closed; final build 06 remains installed. Additional files are preserved, not deleted. Build/source manifests and every failed attempt remain in the evidence directory. The full legacy refuge/regional population suite and the separate resentment probe were not rerun; their acceptance is not implied.
