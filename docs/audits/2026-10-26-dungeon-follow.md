# Native dungeon Follow continuation — 26 October 2026

## Status

Source implementation and CPU checks are complete. **Native entrance, movement, return, and save/load acceptance remain open.** This does not complete the dungeon-navigation roadmap item. No Unity editor, Player, build/deploy process, GPU workload, or model service was started by this subtask. An opt-in native acceptance probe is prepared but has not been run.

## Supported flow

An already accepted ordinary player Follow can use a real `PlayerEnterExit` dungeon entrance. A paid Follow retains its existing action, contract, payment, resident identity, native inventory, and vitals. The additional loaded-scene owner is a partial of the existing `RoadTravelActors`, not a new movement engine or party simulation.

`PrepareDungeonFollow` receives the native pre-transition event before the building-only adapter. It accepts a living, non-protected, non-conversing, non-seated, awake follower with an observed source body. Paid followers additionally require their active Travel coverage. It records the actual source point and a bounded route to the activated native doorway. A ready native NavMesh supplies the route; a dungeon exterior without a city NavMesh can use only a collision-checked, supported straight approach of at most 4.5 metres to that same observed doorway. The departing player is excluded from this source-only obstruction test because the synchronous native transition removes that observer from the source. Other bodies and geometry remain blocking. At most four followers prepare a transition.

The existing `NpcFollowTransition` retains its route, walking-time, length, revision, deadline and consent limits. Its canonical world spaces now also permit `dungeon:<mapId>` paired with `settlement:<sameMapId>`. They do not permit arbitrary world-space pairs, foreign dungeon maps, building-to-dungeon jumps, or dungeon-to-dungeon jumps. No schema fields or save migrations are added.

Native `PlayerEnterExit.TransitionDungeonInterior` uses `MovePlayerToMarker(dungeon.StartMarker)` and then raises `OnTransitionDungeonInterior`. The runtime accepts only that matching actual door and dungeon ID. It transfers the same source `ResidentActor` out of its town/paid owner, keeps it hidden while the verified source walking time elapses, and admits it at the actual native start marker only after static floor support, native navigation and a clear body capsule agree. An occupied landing waits; it does not search for another room position. Source failure resumes the original actor and position. A player reversing a completed native scene transition before follower arrival resumes the existing elapsed source-route position.

In the loaded dungeon, `ResidentActor.Place` delegates to the existing `NativeResidentNavigation`. A failed native route receives the current body point as its fallback target, so it cannot become direct movement through a wall. The shared 0.4 m sampling/arrival tolerance remains unchanged. The existing bounded jump fallback may run through the same movement owner; its engine is unchanged. No climbing, throne-platform boarding, moving-platform route, remote combat, or rescue teleport is added. Actual positions are captured in dungeon-local coordinates.

For return, the source route must reach an actual nearby native dungeon exit. After the real exit, the owner waits for exterior readiness and native repositioning to finish. It selects the same lowest `DungeonEntrance` from the actual loaded location as native `StreamingWorld.PositionPlayerToDungeonExit`, verifies its supported clear landing, and hands the same body back to an existing town plan or the existing paid exterior owner. Missing geometry, unavailable ordinary town ownership or an occupied doorway waits within the original consent deadline. It does not choose the current player position as an invented arrival point.

The exterior paid handoff marker is retired when the live body receives the exact doorway owner. The paid action and contract remain unchanged. Paid exterior reconstruction skips dungeon-owned actions, preventing a second body. Settlement now has an explicit-store internal seam, because the town store can be absent at a dungeon-only location. Cancellation and expiry use the same existing native settlement code; repeated settlement remains idempotent. Stop captures the actual body point, cancels the action/task and holds the existing visible body. It does not claim a route home. A stopped body is not restored as an active follower after a reload.

Native Talk already obtains the managed identity directly from `TalkManager.MobileNPC.GetComponent<ResidentActor>()`; it does not require an active town plan. `FindDungeonCompanion` now exposes that actor to owner lookup, while `IsLoadedDungeonCompanion` verifies its exact current dungeon root, map, saved position and non-pending transition. A visible follower is parented to that actual dungeon; the transition owner preserves it outside the unloading hierarchy before return. The conversation snapshot marks this verified actor indoors without outdoor weather. The place context explicitly names the loaded dungeon instead of the old non-building fallback of outdoors. The life-context builder does not query an absent town schedule for that observed dungeon body. Stop uses the loaded road component even when `StreetPopulation.Ready` and its town store are unavailable.

## Verification

The new ordinary round-trip and cancellation tests first failed against the previous Core transition contract. After the implementation, the isolated full Core suite passes **2151/2151**. Four added cases cover canonical dungeon round trips with unchanged inventory and deadlines, mismatched map rejection and cancellation across restore, paid handoff retirement with idempotent suspension, and expiry refusing exterior-owner resumption. Existing building Follow cases remain in the same suite.

The isolated RuntimeCompile project passes with **0 warnings and 0 errors** against the installed native assemblies. Its generated project includes the current complete runtime and Core source set, with private intermediate and output directories. These are compilation/state tests; they do not establish Unity event ordering, capsule physics, actual doorway geometry or game behavior.

Commands and evidence:

```powershell
dotnet run --project outputs/dungeon-follow-check/Check.csproj --no-restore
dotnet build outputs/dungeon-follow-check/Runtime.csproj --nologo -v:q -p:BaseIntermediateOutputPath=obj-runtime/ -p:OutputPath=bin-runtime/
```

- `outputs/dungeon-follow-red.log`: original two expected failing dungeon cases.
- `outputs/dungeon-follow-core.log`: full passing CPU suite.
- `outputs/dungeon-follow-runtime.log`: isolated runtime compilation.

## Opt-in native acceptance probe

Use the existing native trial launcher with:

```text
POTIB_ROADMAP_PROBE=dungeon-follow
POTIB_DUNGEON_FOLLOW_FIXTURE={"mapId":187853213,"residentId":"<existing accepted follower ID>","verifyDungeonStop":true}
```

The map ID must match the actual loaded exterior; `187853213` is Privateer's Hold, not an instruction to teleport there. The fixture character must have the `PeopleOfTheIliacBayTest-` prefix. The player must already be within four metres of a real dungeon entrance, with the existing accepted visible follower and at least 600 game seconds of original consent remaining. Remote fixtures normally require an already paid Travel companion. The probe refuses missing native item inventory and never hires, extends consent, creates a companion, changes its position, adds geometry, builds navigation or starts a model request.

`NativeDungeonFollowProbe.Run` activates the actual native entrance and waits for the shipped dungeon navigation. It discovers a complete, supported, capsule-clear 8–18 metre path from the native entry using actual NavMesh triangles. Only the test observer moves: its enabled native `CharacterController` follows the measured path, with the player motor temporarily disabled to avoid competing input. The normal production Follow owner moves the companion. The probe requires more than five metres of observed companion movement and native-agent movement, with static floor support, collision checks and maximum 0.55 metre observed frame steps. It returns along the same path, activates the native exit, verifies transfer to the existing exterior owner, and measures continued physical following outside.

At each stage, it checks the same resident/action/body/`MobilePersonNPC`, single visible body, original consent bounds, unchanged paid contract, wallet and serialized native payloads, plus reference equality of every native item object and UID. It opens the actual native Talk window, invokes the normal bound snapshot and life-context builders, and asserts the exact speaker, indoors fact and accepted Follow commitment. Local model dialogue is temporarily disabled for this inspection; no semantic Stop claim is made. Its default second native entry then calls the ordinary `StreetPopulation.TryStopConversationAction` seam in the dungeon, verifies a stationary cancelled body and confirms cancellation survives a Core snapshot restore. This final Stop intentionally settles any paid fixture; the unchanged-contract assertion applies to the completed round trip before Stop. `verifyDungeonStop:false` leaves the round-trip follower active outside.

Pass markers: `PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_ROUNDTRIP_PASS`, optional `PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STOP_PASS`, and final `PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS`. The probe restores its player-motor and dialogue settings in `finally`. It does not restore fixture location, contract cancellation or accepted action state; those are intentional disposable-fixture outcomes. It has compiled, but **none of these markers has been observed in a native run**.

### Optional owned-fixture staging

`POTIB_DUNGEON_FOLLOW_PREPARE=1` enables a separate preparation path, explicitly authorized for disposable fixtures. It accepts only Privateer's Hold as the staging destination and an existing actual eligible adventurer or already paid companion. It does not relax the measured probe's entry, action, consent, body or inventory requirements. A JSON `residentId` can be omitted only in this mode; discovery then reads an actual nearby eligible generated traveler.

The existing exact traveler warmup can prepare the known SAVE18 source automatically:

```text
POTIB_NAVMESH_TRIAL=1
POTIB_NAVMESH_INTERIOR=197386
POTIB_TRAVELER_FIXTURE_INN=197386
POTIB_ROADMAP_PROBE=dungeon-follow
POTIB_DUNGEON_FOLLOW_PREPARE=1
POTIB_DUNGEON_FOLLOW_FIXTURE={"mapId":187853213,"verifyDungeonStop":true}
-peopleoftheiliacbay-fixture-character PeopleOfTheIliacBayTest-9a5f197b
-peopleoftheiliacbay-fixture-save "People - Daggerfall day 2"
```

The warmup retains its exact world seed, natural pre-checkout date, Inn, traveler, native admission and actual visible-actor guards. No alternate identity or booking is inserted if those prerequisites fail. It now permits the dungeon mode only when explicit preparation is enabled. Its existing controlled player-purse addition remains separately logged as fixture setup.

Preparation uses the existing native paid quote, command-session admission and `TryHirePaidCompanion` funding path. `DUNGEON_FOLLOW_STAGE_FUNDS` records any additional controlled purse funding separately; `DUNGEON_FOLLOW_STAGE_HIRE` requires the real 120 gold debit and records the resulting contract. The explicit fixture option supplies test consent; no model reply or automated native Yes click is represented as user consent. An existing active paid Travel contract is reused unchanged.

The already-bound body is passed to the ordinary paid exterior owner, followed synchronously by native source exit and `StreamingWorld.TeleportToCoordinates` to the actual location data, as used by the module probe. This preparation is not a walked Inn exit or a journey across the world. The existing native reload/paid-body logic may reconstruct the body during transport; stable resident/action, exact item UIDs/native payloads, wallet and paid contract are checked afterward. The test then stages only the disposable observer and that existing paid body on clear, measured actual floors outside the real dungeon door. No new geometry or NPC is created. Same-body/native-item-reference measurements begin only after this setup.

`PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STAGE_READY` explicitly reports `sourceJourneyMeasured=false` and `setupBodyPlacement=true`. It is not an acceptance pass. Native entry, physical dungeon movement, native Talk context, native exit and exterior continuation must still produce their separate measured pass markers. This optional staging path has compiled but has not run.

## Native acceptance still required

1. Use a genuine accepted Follow body at a real dungeon entrance. For a remote dungeon, bring an already paid traveler through the existing exterior owner. Record the resident/action/contract IDs, body instance, item UIDs, native payloads, wallet and vitals before entry.
2. Activate the native door normally. Observe the exact pre/post door and dungeon ID, the native marker, elapsed source-route time, single body at the verified landing, and unchanged identities/inventory/payment. Keep the player off the landing long enough to admit the follower. Repeat with an occupied landing to establish waiting without overlap or alternate placement.
3. Walk the player along at least one actual complete dungeon path. Verify positive physical follower displacement, no extra mover, bounded per-frame movement, collision handling, pause/conversation and continued repathing. Separately confirm an unreachable destination does not cause direct movement through geometry. Existing jump acceptance remains separate.
4. Return through an actual nearby exit. Verify the source path, native exterior reposition, lowest native entrance selection, clear supported landing, single body and successful transfer to the existing town or paid-road owner. Walk outside afterward to establish continued following and unchanged contract/inventory/vitals.
5. Reverse entry and exit before follower arrival, fail a native entry, stop during pending transit and while visibly following, expire paid service, and save/reload while active in the dungeon. Confirm no duplicate body, stale action resurrection, renewed payment or double settlement. A stopped follower must not resume after restore; this slice does not restore terminal parked bodies as a new population system.
6. Exercise a dungeon without captured navigation and an exterior without a city NavMesh. Missing dungeon movement data must remain unavailable. The exterior exception must be only the measured short supported approach, never a fabricated longer path.

All six checks remain unrun. The native return adapter and source-route reversal are implemented, not accepted gameplay.

## Native prerequisite failure and diagnostic revision

`work/native-dungeon-follow-01-player.log` records a native run against package SHA-256 `FD3392919E6DF1ABA55DC503173E5D3AAA316F063E0C0FDFC8D1705F94833EE0`. It failed in `NativeTravelerFixtureWarmup` before dungeon staging or measured entry. The exact expected `traveler:1291010263:197386:145804:0` remained absent throughout the 90-second exterior wait. Street readiness and entrance readiness became true; the actual Inn 197386 inventory later reported 13 rooms. No dungeon pass marker was reached. This run neither accepts nor disproves dungeon movement/return behavior.

Source inspection separates two prerequisites: `StreetPopulation.EnsureInnTravelerDemand` must first obtain an actual exterior route and generate the ordinary identity/visit; native room inventory then permits actual admission. Missing identity is therefore earlier than room admission alone. Demand selects one sorted Inn per second and increments its round-robin index before asking the shared `BackgroundWork` gate to start a route survey. Busy startup work can defer a selected Inn to a later cycle. Zero actual routes can also defer it. The original log records neither demand state nor routes, so it does not establish which occurred or prove that a longer wait fixes production demand.

The preserved `work/terra-known-spells-native-player.log` and `work/player-before-town-8739409f26154e41bc0adab22a186422.log` also show the same missing-traveler warmup failure. A literal search of preserved `work` logs found no `TRAVELER_FIXTURE_WARMUP_PASS` or `ordinaryTraveler=True` evidence. The known fixture identity and source-level setup must not be described as a historically successful native warmup without a successful log.

The diagnostic revision changes only the opt-in fixture warmup: explicit `dungeon-follow` preparation gets a bounded 180 real seconds, while existing paid/spell warmups retain 90. The original exact character/save/seed/town/Inn/identity requirements remain. Natural day and pre-checkout time are now checked on every waiting iteration; no time skip, alternative traveler, generated booking or demand/admission bypass is added.

Every ten seconds and at terminal timeout, `TRAVELER_FIXTURE_WAIT` now records elapsed/limit, native playing/paused/night/gate state, store/epoch consistency, demand day/index/Inn count/next Inn/completed count, target completion, route-cache counts, actual target entrance, next-demand delay, ordinary town visits, exact target admission and bed readiness. `targetCachedRoutes=-1` means no successful cached route; it does **not** distinguish an unattempted survey, a shared-gate deferral or an attempted zero-route result. These diagnostics only read existing state; they never call route survey, consume `BackgroundWork`, generate identities or advance admission.

Verification: `dotnet build outputs/dungeon-follow-check/Runtime.csproj --nologo -v:q -p:BaseIntermediateOutputPath=obj-runtime/ -p:OutputPath=bin-runtime/` passed against the current full source with zero warnings/errors; output is `outputs/dungeon-follow-warmup-runtime.log`. The revised diagnostics and 180-second allowance have not yet run natively. Root owns the next build/run and the separate outer trial timeout; actual staging and measured dungeon acceptance remain open.

## Root completion of the interrupted diagnostics

The user stopped further agent work and requested a test build followed by a pause.
Root compiled and ran the remaining diagnostic changes. Native run02 on083EAE140780
waited180 real seconds:19 Inns, no completed demand, no cached routes, no visits,
all four gates open, playing and store/epoch ownership current. That run could not
distinguish a denied work slot from an empty route.

Run03 on the full D3CA68C3D919 package adds counters only to the existing scheduled
work. Its terminal sample records175 selected Inns:46 shared-gate denials and129
actual route attempts, all129 empty. The selected Inn197386 had one denied slot
and eight actual attempts, all empty. Thus the main prerequisite failure is not
simply work-gate starvation or an insufficient wait. The actual gate-to-Inn route
survey yields no usable path. These counters do not establish which geometric
endpoint or path segment fails; that remains the next navigation investigation.
No invented route, forced traveler, booking or admission bypass was added.

Evidence: `work/native-dungeon-follow-02-player.log`,
`work/native-dungeon-follow-03-player.log` and their launch logs. Both runs exit1
at readiness before dungeon entry. Root Core2162/2162, RuntimeCompile and the full
Unity package pass. Dungeon entry/movement/return acceptance remains open.
The current staging probe uses the actual level-based wage; earlier120g setup
descriptions above describe its historical fixed-price version.

## Later retry04 and static navigation repair

The later package `934D6E533BC7FB753F47CA2532A61293F57FE0920C342F48DC4A4DC2A33086B6` includes a corrected loaded-dungeon arrival rule and excludes non-physical triggers from static navigation fingerprints. A fresh forced-module run verifies five actual Privateer's Hold RDB modules and eight links after recapture/bake. A real-floor jump survey finds no disconnected capable pair and does not force a jump. Those source changes and boundaries are documented in [the later audit](2026-10-26-dungeon-follow-arrival-and-jump.md).

`native-dungeon-follow-04` reruns the existing exact SAVE18/Inn197386 preparation. Traveler routes now work: 19/19 Inns have routes, four for the target, with no empty route, and the real Mage exists. Admission nevertheless remains Pending with no bed for 180 seconds. The actual Inn197386 survey fails with `Lodging bed disconnected from its own floor.` No hire, staging, dungeon entry or round-trip pass occurs. Thus the earlier empty-route explanation is superseded for this run by a specific lodging prerequisite. No identity, booking or bed is invented. Inn263174's separately successful admission does not accept this different layout. Evidence: `work/native-dungeon-follow-04-player.log` and launch log. The owned Player exits; full dungeon Follow acceptance remains open.
