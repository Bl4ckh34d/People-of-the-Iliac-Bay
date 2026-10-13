# Citizen response checkpoint

## Current 0.3.8 public reporting

With civilian reactions and experimental guards enabled, an outdoor weapon kill of an owned generated resident no longer automatically dispatches guards. A posted guard must see the crime within 20 metres, or a fleeing direct witness must reach a posted guard within 20 metres and unobstructed sight. Guards receive the immutable crime location, not the player's current position. Dispatch uses the existing finite roster and collision-validated routes; repeated reports cannot replenish the response. Guard reporting does not consume the witness's two-citizen warning allowance. Secondhand witnesses still cannot relay.

The original crime location and spent report survive native serialization. The weapon interception scope restores correctly after normal hits and exceptions. This applies only to supported outdoor generated residents. Indoor native alarms remain active; unwitnessed indoor murder discovery, actual witness exit traversal, stationary NPCs and autonomous offscreen reporting remain unfinished. Earlier checkpoint paragraphs below are historical.

Validation: 151 core tests and native build pass. `TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -GuardWitnessProbe` exercises an actual native weapon kill away from guard posts, a direct witness, local reporting, bounded dispatch to the original crime location, repeated-report suppression and native serialization. The probe deliberately moves the witness to a guard; it does not claim autonomous refuge navigation coverage. Its warning call waits for the real quarter-second polling cadence.

Final 0.3.8 player regressions also pass `-ResponseProbe` (native hit, retreat, counterattack, identity and SAVE29 reload) and `-GuardBuildingProbe` (native indoor alarm, delayed entry, wounded SAVE31 reload and return). Run these switches separately. Logs: `work/guard-witness-runtime-pass.log`, `work/civilian-response-038-pass.log`, `work/guard-building-038-pass.log`. Installed bundle SHA-256: `F9414866227E4950DA47C8F7C8237A845F40A05063D18D8ED1A357FA015892EB`. Upstream tracked source remains unchanged. Read-only Luna/high review found no confirmed important defect; native mobile-civilian weapon hits are lethal at this interception boundary.

## Current scope

The 0.3.3 implementation starts the approved fight-or-flight behavior with generated residents in Gothway Garden. It does not complete M3 or M4. The user explicitly prioritized behavior over further sprite production on 14 October 2026.

The mod observes the existing native weapon-hit boundary before a mobile civilian is killed. Awake, nearby generated residents with an unobstructed view become direct witnesses. Children flee; an initial stable adult capability policy selects flight or an unarmed counterattack. Existing appearance, name, household and actor identity remain intact. Flight runs at 2.8 metres per real second using existing art. Separate running and battle frames are deferred.

Exterior fleeing residents retrace their own previously validated commute toward their home. Indoors, the initial response retreats through unobstructed space in the current room. Fighters approach only while they can see the player, stop chasing through blocked sight, and counterattack in melee range. This is a basic response, not the final combat, courage, equipment or pursuit system.

Each fleeing direct witness can warn at most two nearby, previously uninformed residents. Secondhand recipients cannot relay. The incident ID, direct/secondhand status, spent warning budget, reaction and local position persist in schema 5. Repeated reports of the same victim do not renew the 15-game-minute local alarm. Actor unload does not erase the incident. Movement while the actor is unloaded still needs catch-up integration.

`General/CivilianReactions` toggles this pilot without deleting its ledger. Existing native crime accounting, racial effects and guard spawning remain untouched in this slice.

## Next gameplay work

1. Implement a saved finite guard roster and replace native instant spawn paths inside supported settlements. Nearby posts and patrols must physically respond; no available guard means no invented squad.
2. Add shared indoor/exterior movement through real doors, witness guard reports and discovery of unwitnessed household murders.
3. Add guard sight loss, last-seen search, escape and return to duty, then patrol pairs, torches and hostile-creature defense.
4. Extend reactions to original stationary citizens while preserving native service and quest identity.
5. Add corpse persistence, six-hour crime-scene occupation, recruitment and household recovery.

Native static NPCs, spells, sleep detection, theft dispatch, guard warning radius, full indoor exit pathfinding, additional safe refuges and offscreen emergency movement are not covered by this first slice. Core and actual-player results are recorded below only after execution.

## Authoritative housing and art rules

Household capacity equals the actual number of beds. One resident per bed. There is no independent five-person cap; bedless or unknown-bed homes receive no new generated residents. Old saves retain their existing population; new test populations enforce the new rule. Additional residents beyond the two-parent/three-child family pattern are adults.

Use complete single-sleeper images, green blanket and white pillow. Separate head/blanket layers and shared-bed images are rejected. Earlier shared-bed kill-all requirements are therefore superseded. Children inherit either parent's race when race modeling expands; existing child sprites remain in use now. No further art generation belongs to this checkpoint.

## Verified evidence, 14 October 2026

- Core: `dotnet run --project tests/LivingCities.Tests` passes 127/127. This includes two unique warnings per witness, no secondhand relay, repeated-report/expiry behavior, persisted budgets, child flight, schema migration, nine-bed occupancy and transactional rejection.
- Native build: `tools/Build.ps1` passes full DFU serialization of incidents and spent warning budgets, settings defaults, save adapter and bundle checks.
- Actual player: `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -ResponseProbe` passes native weapon interception, three eyewitnesses, room retreat, controlled melee counterattack, talk refusal, retained appearance/name and real SAVE29 reconstruction. Both adult reaction branches are forced on the disposable witness for repeatability; stable default selection has core coverage. Melee uses verified floor positions and briefly freezes the test player motor to separate target placement from native physics.
- Actual beds: `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -BedProbe` passes all fresh household capacities, four people in four distinct beds, reconstructed claims, three classic mattress models, ordinary street animation, door continuity, death and save/load. The fresh town has 181 residents including eight children.
- Distribution: built/installed bundle SHA-256 is `CC34F4C41C65088B66DF2A0F04A2E2CE084835087CB7BDFE78AA91BB54E5D06F`. All 598 official player files remain identical to the release archive; upstream tracked source remains unchanged. A subsequent comparison of 309 old save files found no differences.

Evidence logs: `work/civilian-alarm-final.log`, `work/civilian-response-final-pass.log`, `work/single-bed-runtime-pass.log`, `work/player-integrity-033.json`, and sibling `../work/livingcities-build.log`. Luna/high read-only review found no confirmed important issue in the reviewed scope. Exterior emergency arrival, dynamic warning encounters and sight-loss behavior still need dedicated actual-player scenarios; this checkpoint does not imply full guard or city-wide behavior coverage.
