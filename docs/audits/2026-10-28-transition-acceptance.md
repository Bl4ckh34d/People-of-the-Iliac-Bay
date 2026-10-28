# Transition acceptance — 28 October 2026

The active goal remains working systems across houses, dungeons, overland travel
and native fast travel. The earlier 15/22 checklist is not a completion claim for
this goal. Current-source validation and remaining transition failures follow.

## Native item identity after loading

The actual portable `SAVE15` (Hakon Ironhand / People - Daggerfall hired Allan ready)
contains native `currentUID=33555768`, but its mod inventories contain 693 items
across 175 inventories, with maximum UID 33555810. Forty-three saved items have IDs
at or above the native counter. This is current-schema data, not an old-save migration.

DFU `SaveLoadManager.BuildSaveData` reads its allocator before mod save callbacks.
Native restore then precedes mod restore. New NPC items could therefore collide
with items captured later by the mod; actual native runs reported `Unknown inventory
owner or conflicting native UID`.

After validated world restore, `PeopleOfTheIliacBayMod.RestoreSaveData` now reserves
all existing NPC item IDs by advancing the native next-ID counter. Existing item
IDs, payloads, ownership, revisions and money are unchanged. An already higher
counter remains higher. Integer arithmetic retains unsigned precision.

Three regression cases first fail, then the full Core suite passes 2155/2155.
RuntimeCompile passes with zero warnings/errors. Unity integration additionally
uses the real native ItemBuilder after a deliberately stale allocator and confirms
a distinct new item and unchanged saved inventory:
`PEOPLE_OF_THE_ILIAC_BAY_NPC_UID_RESTORE_PASS`.

## Required navigation build inputs

The first new package build fails because `work/navigation-geometry` no longer
contains its capture manifest. The Unity project still has all 120 referenced
baked assets for 122 module records, approximately 5.86 MiB. They are preserved
unchanged under `mod/Navigation` with their manifest. Deployment copies them and
the baker uses this durable catalogue when no explicit capture manifest exists.
The build succeeds with the temporary restored work manifest removed:
`modules=120; newlyBaked=0; reused=120`.

Raw `.navsource` geometry was not recovered. Reuse preserves the existing runtime
geometry/provider checks; it is not a new capture or a gameplay acceptance.

## Paid companion exit: fixture error separated from production error

The original checkpoint-resume test invokes native exit beside the guest, away
from the actual doorway. This two-door Inn then selects the other exterior door,
and the bounded follower adapter correctly refuses an unsurveyed exit. Moving the
observer along the older test helper's route instead times out; that is not an NPC
movement result.

The focused fixture now places only the observer on the existing surveyed exit
floor, verifies native exit selection, and clears the exterior landing through
the existing supported native controller movement. NPC position, agreement,
payment and route are not injected. Logs explicitly mark this controlled setup.

On bundle `D47895573557B72C84D50BE7711EBD0F9E2C49371EB35E0A46E3051908F5F91A`,
the actual saved contract resumes, the native exit creates an Approaching action
with a real FollowTransition, and the observer clears three metres. The exterior
owner subsequently treats the same traveler as away (`observedVisit=none`, no
materialized actor, pending FollowTransition) and fails the action. This is the
remaining production failure; fast travel has not yet been reached.

Relevant bounded logs: `work/inn-exit-20261028-red.log`,
`work/inn-exit-observer-20261028.log`, and
`work/inn-exit-transit-20261028-red.log`. Additional diagnostic fields identify
whether the exterior owner has the correct visit/plan/scene binding. Do not rerun
unrelated completed systems or claim the fast-travel fare/arrival checks passed.

### Exact follow-owner diagnosis and correction

The diagnostic native replay proves that the paid door owner and the normal
traveler owner refer to the same resident, traveler, visit and settlement, but have
different Plan/Routine instances. The FollowTransition destination matches the
current scene. Requiring the temporary door plan to be the normal visit plan
therefore rejects an otherwise valid accepted travel action.

`PaidDoorOwnsTravel` now checks the binding's actual action ID, current store,
nonterminal action and live travel authority. Only that owner bypasses origin
absence in the shared overlay and subsequent native door transitions. Death,
quest protection, alarm/native movement ownership and contract checks remain.
The next native replay preserves Approaching/FollowTransition without the earlier
failure. It does not yet establish a completed exterior arrival.

The remaining waiting diagnostic proves that the native arrival time has elapsed
and the game is unpaused, but the player collider occupies the exact landing.
The fixture had moved the observer before asynchronous native repositioning;
that movement was subsequently undone. The fixture now waits for the existing
StreetPopulation exterior-scene synchronization before clearing the landing.
Departure uses the actual exit door normal, not the observer's retained indoor
view direction. These are controlled observer changes, not relaxed collision
checks or NPC placement.

### Native exit and single-companion fast travel passed

Final single-companion run exits0 on bundle
`445B95BE7D85E845D40BFB8EB71EB0E0915D8450D912EB02A36F1CE5F68B9072`,
identical in distribution and Player. Log:
`work/companion-fast-travel-20261028-pass.log`, SHA256
`EA7BF95F6DCC11FDCFD70F130D6B0DBB5B5D18CCBD1C6F0A16FF5645B8EFC0BD`;
receipt: `work/companion-fast-travel-20261028-receipt.json`.

The existing genuine hire resumes without a new hire, wage or model request.
The actual follower crosses the Inn boundary and materializes outside after its
existing source-route time. Native UI pricing displays20Gold from the independently
recomputed10Gold solo fare. Native cancellation preserves wallet/time/map/action;
controlled zero coins produce the native insufficient-funds rejection without
travel or debit. Coins are restored before the successful journey.

Native Begin charges exactly20Gold once (99990 to99970), fires one departure and
one arrival, and advances the quoted2548game minutes to Gothway Garden206728581.
The same body, person, inventory owner, native item objects/UIDs/equipment and
relationship arrive; exactly one visible companion exists. Original action and
contract dates are unchanged. The one-day agreement expires during this42h28m
journey; it is terminal at arrival and is not renewed.

This proves one companion and the expired-contract arrival branch. Multiple
companions, unexpired follow continuation at destination, dungeon traversal,
overland movement and the remaining roadmap systems are separate requirements.

### Dungeon replay remains failing

The replay after the asynchronous exterior-floor observer correction exits1.
It fails earlier, during outward movement, with `Dungeon Follow crossed collision`
against `DaggerfallEnemy [Imp]` (CharacterController, not a trigger). The log
records repeated native hostile encounters and clear events before this failure.
Consequently this replay does not verify the corrected exterior-floor wait or a
completed roundtrip. Whether the failure is production movement or the probe's
handling of a moving native enemy remains to be diagnosed; collision checks have
not been relaxed. Bounded evidence:
`work/dungeon-follow-20261028-imp-collision-red.log`.
The owned game process has exited. No commit or push was performed.

### Native dungeon roundtrip passed; second entry remains open

The next native run on bundle
`A8100492E4A9FC4C54410AE40F88140E4C4813551897B10CEE2BA1E323DA8745`
passes the actual entry, outward movement, native talk context, reverse movement,
exit and subsequent exterior following. Measured distances are 12.97699 m outward,
8.638844 m back and 4.87476 m outside; maximum observed step is 0.05383661 m.
The same body, seven native item objects, inventory payload, action and contract
are preserved. Native hostile encounters occurred and were not suppressed.
The earlier Imp-contact failure was not reproduced in this run and remains
unresolved; this successful traversal does not prove every hostile contact safe.

Evidence: `work/dungeon-follow-20261028-roundtrip-pass-reentry-red.log`, SHA256
`698D2569BC5CB76D63A3B401B9BCB6BF0E3BA9C84D8CBA56FADFB568AA576B3A`.
The process exits1 later because the second entry for the separate Stop check
rejects its exterior approach. The exterior has no matching CUSTAA30.RMB navmesh;
the supported direct approach currently has a 4.5 m cutoff. A focused staged
5 m approach is being tested separately. No Stop, obstacle recovery, loot or
overland completion is inferred from the completed roundtrip.

### Door approach red/green and full roundtrip/Stop pass

The focused staged 5 m entry fails with `distance=5.000151`, height difference
0.03808594 m, `directApproachClear=True`, `navigationReady=False`. This establishes
the 4.5 m cutoff as the cause, not missing floor or obstructed geometry. Evidence:
`work/dungeon-door-5m-20261028-red.log`. The fallback now uses the same existing
32 m bound as the mesh doorway route, with unchanged full floor samples, endpoint
capsules, swept collision checks, ownership and contract validation.

The identical 5 m setup then passes the complete native test with exit0 on bundle
`904A04507FA190B79D77135948C82ED88AF7CE7BB2A2596C6C6CE6AE7BB4F34D`, byte-identical
in distribution and Player. Native roundtrip distances: 11.08339 m outward,
8.739424 m back, 4.850266 m exterior; maximum step 0.04678527 m. Same body, seven
native item objects and unchanged inventory/contract. Second native entry and
Stop pass: the same action becomes Cancelled, body remains held and a Core
snapshot restore does not resurrect it. No free semantic Stop or disk reload
is claimed. Controlled setup transport and starting-body placement remain
explicitly excluded from measured travel.

Log: `work/dungeon-follow-20261028-pass.log`, SHA256
`552FEEB132EFCC6210D04EE7896D78D4AFA856724B143A2EA67E61BD061A4BA7`.
Receipt: `work/dungeon-follow-20261028-receipt.json`.
Recovery/obstacles, loot, overland and party fast travel retain their own open
requirements. The earlier intermittent Imp contact has not been diagnosed or
silently exempted by these changes.

### Recovery entry initialization: correction awaiting native acceptance

The separate recovery run fails before its controlled obstacle wait because the
expected paired entrance is missing (`work/dungeon-recovery-20261028-portal-red.log`).
Source inspection finds its capture in the synchronous native entry callback,
while `VerifiedDungeonFloor` requires a NavMesh that `NativeNavigationWorld.Update`
loads later. Capture has been moved into the ready-scene arrival path before
materialization, retaining the actual recruitment origin and paired doorway.
Failed portal capture now postpones arrival instead of being silently ignored.

Runtime compile and native package builds pass. The focused
`tests/CompanionRecovery.Regression` executable passes. Native acceptance of the
new capture and recovery chain remains incomplete: an initial retry stopped on
an unrelated minimum NavMesh-distance assertion during native flight. The focused
recovery branch now uses its own measured return criterion rather than requiring
another already-accepted NavMesh Follow distance first. The subsequent run still
cannot begin recovery: the follower flees from an actual Imp and fails to catch
the observer during the bounded setup. The diagnostic reports 24.34973 m observed
movement, recovery phase Following, actor (28.3,38.5,17.7), observer (26.5,38.4,24.9).
Evidence: `work/dungeon-recovery-20261028-preface-red.log` and
`work/dungeon-recovery-20261028-hostile-setup-red.log`.

No enemy has been suppressed and no collision check has been relaxed. The next
recovery attempt needs a suitable real setup or a focused observer approach;
blindly repeating the same hostile setup does not establish the wait/return
requirements. All owned native processes have exited. No commit or push was made.

### Native controlled recovery passed

The focused recovery run now exits0 on bundle
`363A0F9101A73C3706C2E287AE384BB599BFF55B6B844864A90F5A933404C9FB`, identical in
distribution and Player. The source fix captures the actual paired portal after
dungeon navigation becomes ready and before follower materialization. The test
observer approaches the actual moving body through the native controller and
existing supported, collision-checked steps; it does not relocate the NPC or
suppress enemies. Setup approach is bounded to20seconds.

Native legacy FullSerializer prints15significant digits. The former exact
comparison rejected an18microsecond deadline drift and7.54e-15metre origin drift,
despite preserved notice and exact source space. The probe now permits only
less than1millisecond and1micrometre numeric drift, retaining exact source-space
identity and notice assertions. No production serialization was changed.

The native run proves the real nearby Talk/message, paused game clock/body,
native serializer roundtrip, selected3/6game-hour waits,9.110322metres of physical
return (maximum step0.02462022metres), actual portal crossing after original
service expiry, exterior reconstruction/waiting without resetting the deadline,
and preserved inventory. Visible exterior departure measures0.3314565metres.
The native return plan retains actual recruitment map1291010263/building263177,
26 measured final-approach points and711480seconds of native estimated travel.
No renewal of the paid service is performed.

Log `work/dungeon-recovery-20261028-pass.log`, SHA256
`5F23D3B83C329F7E639D7C78DD3BFB5B180444419E92B9B3245A9D4E903BC92C`;
receipt `work/dungeon-recovery-20261028-receipt.json`.

Limits remain explicit: obstacle wait was started through the controlled probe,
wait time was skipped to the real deadlines, serialization was not a disk reload,
and destination arrival was not observed. An earlier physical-return timeout in
`work/dungeon-recovery-20261028-return-red.log` remains unclassified; the next
diagnostic run passed without any production movement change. No collision,
floor or movement-distance assertion was relaxed to obtain this result.
Automatic obstacle detection, actual origin arrival, loot, overland companion
travel and multiple fast-travel companions still require their own evidence.

### Recruitment Inn arrival and actual disk reload

The native recovery run saved SAVE17, `People - Daggerfall recovery returning`,
inside the actual recruitment Inn while Allan's existing return was still in
progress. This checkpoint is an active approximately27MB test fixture, created
through native SaveLoadManager. No action, booking or NPC identity was injected.

Reload exposed a second ownership failure: `OwnsDungeonFollow` consulted only
the runtime dictionary, which is not populated inside an ordinary building.
The town adapter then cleared `ReturningToRoutine` and spawned the traveler
before his saved arrival: now12597703658.8479, arrival12598415113.5892. Ownership
now also recognizes the persisted active recovery before a runtime binding
exists. Red evidence: `work/dungeon-recovery-reload-20261028-red.log`.

The next run preserves absence but exposes the arrival handoff: Core reaches
`ResumedRoutine`, yet the interior receiver accepts only resident plans and
rejects this traveler. Its expired visit then loses its last owner. Red evidence:
`work/dungeon-recovery-arrival-20261028-owner-red.log`. The receiver now accepts
the existing action plan, including travelers. A watched destination retains
the measured pending journey until the entry and destination plan can receive
it. After the actual portal landing, the existing routine-return path owns
onward movement; an expired booking does not grant a new room or paid service.

Focused native SAVE17 reload passes with exit0 on bundle
`0AFD7C0C39B4E891D0287CE1BAC4EC7ED16A4D52987D0A3355C2E72003AEE808`, identical
in distribution and Player. It proves absence before arrival, one actual body
in map1291010263/building263177, preserved inventory/UIDs/wallet and original
expired action deadlines. The log also records the ordinary routine's actual
Inn exit, `door:1291010263:263177:15:9:0`.

Log: `work/dungeon-recovery-arrival-20261028-pass.log`, SHA256
`766720E2915D5AD15E6A0EEA83B7ACE971DA0651525F82C034555C7C68ABCD37`.
Runtime compilation has0warnings/errors; focused CompanionRecovery.Regression
passes. The arrival run skips the native estimated travel clock
and its531.378687436203m measured final approach; it does not prove observed
walking over that route, automatic obstacle detection or the entire goal.

The stronger blocked-entry case also passes with exit0. The real player walks
onto the actual native entry before the clock advances. Eight frames after
the measured arrival deadline, the journey must remain pending and no body
may overlap the player. Walking clear releases the same journey; the probe
observes the actual unique interior body, unchanged inventory, then the
existing routine's native Inn-exit receipt. The original booking expired;
the terminal action remains an ordinary routine-return owner until onward
movement finishes, without renewed consent, wages or a new bed.

Final verified bundle/Player SHA256:
`7E8E0083C56F1AD127E085853D3FB3FEA3D63394285A434A070D02C6CE06F218`.
Log `work/dungeon-recovery-arrival-blocked-20261028-pass.log`, SHA256
`1DD0960EB8E06B51CA9C83C9E660D1862F7E734EDF3969FBEB23D7F64F7E36CE`.
Receipt `work/dungeon-recovery-arrival-20261028-receipt.json` records explicit
scope and limitations. Unity and game processes exited; no commit or push.

Repeat this focused case with `TownTest.ps1 -OutdoorNavigationProbe
-FixtureCharacter 'Hakon Ironhand' -FixtureSaveName
'People - Daggerfall recovery returning'`, environment `POTIB_NAVMESH_TRIAL=1`,
`POTIB_ROADMAP_PROBE=dungeon-follow`, `POTIB_DUNGEON_RECOVERY_RESUME=1` and
`POTIB_DUNGEON_FOLLOW_FIXTURE={"residentId":"s:1242772764:h:131074:r:0"}`.
Do not set the separate PREPARE/ARRIVAL switches for this reload case.

### Fast travel before consent expiry and destination reload

The continuation probe selects an actual nearby settlement using native map
metadata and TravelTimeCalculator with the player's existing transport. From
SAVE15 this is Ripwych Commons, map1276329198,26 native minutes. No consent,
wages, NPC position or route is injected. Cancellation and insufficient funds
remain side-effect free; the actual native trip charges10Gold, solo5times two.

The first run passes with exit0 on bundle
`373A70FC126B1365994A0CB5943F90D739FC6BCAB107C1ECFA5DEA5225F9FAB7`.
The same body/person/native inventory items/equipment, relationship and original
action/contract survive. Actual post-arrival Follow covers7.199395m while the
player walks7.606598m; maximum NPC step0.5000129m. Floor and collision checks
remain enabled. Log `work/companion-fast-travel-continue-20261028-pass.log`, SHA256
`E30AD41576E2E507F5776965EB57885E58AB26579F7EA5A8C0626AD573658618`.
This closes the one-companion continuation case; multiple companions remain open.

The added native disk-save/load case then exposes a real defect: SAVE18,
`People - Ripwych fast travel follow`, restores Allan2.413639m away from the
saved point. The restoration branch calls the new-arrival placement around
the player instead of using its saved action position. Red evidence:
`work/companion-fast-travel-reload-20261028-red.log`. The corrected source
restores only the actual saved point in the matching settlement/space, using
the same native floor and collision checks. A blocked or distant saved point
remains pending instead of relocating the person beside the player.

Runtime compilation passes0warnings/errors. The already-running Unity build
finished successfully and installed bundle
`CCFD01AB2D9C6F3D40483BFF95A7B7AA98935DF283F90C2AAC2E299AADB2923C`.
The user then paused automated testing to play personally and authorized
continued source work. No native green reload result is claimed. Do not start
automated tests or additional builds until the user releases that pause.
The overall implementation goal remains active; only testing/builds are paused.

When released, rerun the existing companion-fast-travel case from SAVE15 with
`POTIB_GOAL_PAID_TRAVEL=1`, `POTIB_GOAL_RESUME_HIRED=1`,
`POTIB_FAST_TRAVEL_CONTINUE=1` and `POTIB_FAST_TRAVEL_RELOAD=1`, in addition to
the established NAVMESH_TRIAL/ROADMAP_PROBE switches. The native SAVE18 checkpoint
is reused on retries, not duplicated. It is an active fixture for this defect.

### Source-only work during the user's testing pause: completed indoor arrival

Static tracing finds another concrete ownership gap. `RestoreSaveData` clears
the street and road owners. `RestorePaidDoorPlans` then recreates temporary
plans only when `FollowTransition` still exists. After an actual completed
building entry, that field is null and `LastPosition` is the interior. A
fast-travel companion visiting a foreign building has neither a local resident
plan nor a booked traveler visit there; the road owner also does not materialize
bodies while the player is inside. No owner reconstructs this saved body.

The source now accepts an active persisted `FastTravelFollowOwner` saved inside
this exact current building after its transition completed. Existing alive,
store, action and consent checks remain. This creates only the existing temporary
action plan, not a booking, home, identity, route or renewed agreement.

`GoalSequenceProbe.FastTravelInterior.cs` adds an explicit future native
`companion-interior-reload` acceptance case. It requires an actual test save
where a real fast-travel follower has already entered the current building,
then saves/loads natively and requires one interior body, the same action,
inventory/money/relationship and original consent. It does not create that
starting scene or accept a replacement hire. Its one named checkpoint is reused.

These source edits and the small shared checkpoint-helper extraction have not
been compiled, installed or executed because testing/builds remain paused.
The interior fixture still needs to be reached through an actual native door
after release of the pause. Do not report this source correction as a native
pass. The installed user-test build remains `CCFD01AB2D9C...`; the source tree
is newer. Mindmap LIFE-68 now separates verified continuation from these open
reload checks without changing the15/22 system count.

### Source-only dungeon handoff from fast travel

Continued static tracing identifies two related handoff omissions:

- `CompleteNativeDungeonEntry` removes the paid/exterior dictionaries after
  successful entry but previously retained `fastTravelFollowers`. The same body
  therefore stayed registered with both owners. On returning outside,
  `UpdateFastTravelFollowers` runs before `UpdateDungeonFollowers`; the former
  rejects a pending door transition and can terminate the valid Follow action.
  Successful native entry now removes only the obsolete fast-travel runtime
  binding as well. Failed-entry recovery still retains its source binding.
- The dungeon exterior-landing gate previously admitted a paid contract or a
  local resident plan only. An actual unexpired unpaid fast-travel Follow has
  neither at a foreign dungeon. The gate now also accepts the existing
  `FastTravelFollowOwner` through `NpcCompanionFastTravel.CanFollow(...,true)`.
  After the real landing, the existing `TryDeferPaidCompanion` branch already
  restores unpaid fast-travel ownership. No new owner, wage, identity or consent
  period is created; measured route, clear landing and expiry checks remain.

The existing native dungeon probe now requires exclusive dungeon ownership
while the body is inside. Optional fixture fields `requireFastTravelOwner` and
`requireUnpaidFastTravelOwner` reject setups that do not exercise the requested
case; they never set those flags or extend consent. After the testing pause,
use an actual fast-travel follower at a native dungeon entrance and retain the
roundtrip/action/body/item assertions. The unpaid variant requires genuinely
unexpired unpaid consent and may need a different suitable native fixture.

These are source findings and uncompiled source corrections, not observed
native failures or passes. No tests/builds/game operations ran during this
turn. Installed bundle remains unchanged; the user testing pause still applies.

### Source-only expiry inside the destination building

Static tracing finds an earlier failure before retirement itself: both population
loops call `Scheduled` before `ApplyNpcActionOverlay`. The temporary travel door
plan has no `Schedule` or `Routine`. Previously `TryScheduledPaidDoorPlan` rejected
an expired agreement, allowing the fallback to reach `ObservedSchedule` and its
`schedule.EvaluateSeconds` call with no local schedule. The bound job's saved
position is now still used for that provisional snapshot until the action overlay
retires the plan. This does not authorize continued Follow or create a routine.

The unpaid branch also could not finish: `RetirePaidDoorPlan` handed every visible
body to `TryRetirePaidCompanionFromStreet`, which explicitly rejects unpaid jobs.
An unpaid fast-travel follower now ends its old action through `EndAction` and
holds its same actual body in the current scene until ordinary scene cleanup.
No new road owner, payment, consent extension or invented return route is added.
The existing paid retirement path remains in place. Physical return to the
recruitment origin and a subsequent new agreement are not established by this fix.

`GoalSequenceProbe.FastTravelInteriorExpiry.cs` provides the future targeted
`companion-interior-expiry` mode. It requires a real, still-active fast-travel
follower already inside the actual building with a temporary door binding and
a settled body. It advances the native clock past the original end, then checks
that the original action is terminal, no active action remains, the same body
stays visible and stationary, and inventory/money/relationship remain unchanged.
`POTIB_FAST_TRAVEL_INTERIOR_UNPAID=1` additionally requires an actual unpaid action;
it never converts a hire or extends consent. The clock jump is explicitly labeled
controlled; no routine return is claimed. The runner now requires the dedicated
interior reload/expiry markers and the unpaid receipt when requested.

The probe was authored before the correction, but neither red nor green was run:
the user's testing/build pause remains in force. These findings are from source
inspection, not native observations. New code remains uncompiled/uninstalled.
After release, compile and execute the paid case and a suitable actual unpaid
case separately. A genuine unpaid fixture still needs sufficiently short native
travel to retain its original consent; the existing 26-minute paid fixture does
not prove that prerequisite. Installed bundle remains `CCFD01AB2D9C...`.

### Testing resumed: exterior fast-travel reload passes

The user released the testing pause on 28 October. Runtime compilation passes
with zero warnings/errors; Unity builds and installs bundle
`439F7DC458E1E88B724809B6C0F5460FE1752E1EF74A539AF1CF12CC30E82009`.
The existing continuation-plus-reload probe then exits 0: 26 native minutes,
10 gold exactly once, 7.223712 m physical follow before the save, and actual
disk reload at **0 m saved-position drift**. The restored companion physically
follows another 8.000001 m (maximum step 0.5000086 m), with one body and unchanged
inventory, relationship and original action/consent. This closes the earlier
2.413639-m exterior reload reproduction; it does not prove interior reload,
interior expiry, multiple companions or the new dungeon handoff cases.

Log: `work/companion-fast-travel-reload-20261028-pass.log`, SHA256
`8059C84EAB99A02B9360692D6BB77FDEBE04CAA298FE6F274B244F567D44BD74`.
The previously source-only changes now compile in that bundle; their distinct
native acceptance cases remain pending. The new `companion-interior-entry`
fixture mode uses the real SAVE18 follower and a native nearby tavern door.
Only observer placement is controlled; the NPC approaches through ordinary
movement and crosses through the production door adapter. It reuses the one
named interior checkpoint for subsequent focused reload/expiry runs.

### Native destination-building entry and initial reload fixture timing

`companion-interior-entry` passes with exit 0 on bundle
`8CB72DBC6EFD0EFC443DF9AE1E873569DC5AF68E50D415F7AA96AF09131B75BF`.
From SAVE18, the observer is explicitly placed at actual tavern 265 in Ripwych
Commons. Allan starts 72.4174 m from that approach, reaches it through normal
Follow, and completes the native door transition without a new action or consent.
Inventory, money and relationship are unchanged. The actual interior checkpoint
`People - fast travel interior follow` is saved. The initial distance is not a
measured full walking-distance receipt, and this does not prove lock/admission UI.

Log `work/companion-interior-entry-20261028-pass.log`, SHA256
`585AF24B4D626CA095B3BA9B72CE2C27C7B7D6BC83A9242F49DA04B07159D098`.
The next focused reload run exits 1 before its save/reload step:
`Actual fast-travel follower must be visible inside before the disk reload.`
This occurs immediately after `STREETS_READY`; the initial test precondition did
not await body materialization. The reload and expiry probes now wait up to 30
seconds for their actual visible interior body (expiry also requires its binding
and settled state). No actor, action or consent is injected by this readiness fix.
The initial failure is retained in
`work/companion-interior-reload-20261028-initial-red.log`, SHA256
`66ACA6A017B573F628959757927F307A107FA276BC20937941F395514DFAE51A`.
Whether readiness alone resolves this failure is pending the focused rerun.

### Interior reload passes; expiry exposes catch-up body removal

With bounded initial-body readiness, `companion-interior-reload` exits 0 on
bundle `D238BC2000CE094A1D1FE240EA70A0DA95F1F9EDD71BCA89A6DC6641EE99B65B`.
Actual save/load after completed entry restores exactly one body in map
1276329198/building 265, with unchanged inventory/money/relationship and original
action/consent. The first failure was initial test readiness, not the subsequent
disk reload. Log `work/companion-interior-reload-20261028-pass.log`, SHA256
`ADE3A6AD8876B7545F9C9925DCF4E4EA1F05021117430FA9875CC6DF2DF27C12`.

The separate paid `companion-interior-expiry` case exits 1 after its controlled
clock advance: `Expiry removed the visible companion body.` The native log shows
`INDOOR_TIME_CATCHUP` immediately before the failure. Static tracing confirms
that clock catch-up calls `ClearActors` before the action overlay can process
expiry. The source correction lets only that catch-up call retain actual bodies
bound to current travel door plans; ordinary scene cleanup still removes them.
It retains expired bindings deliberately so the existing action end/retirement
path can run. No new position, body or consent is created. The expiry probe now
records whether the actual paid action still has a deferred travel handoff.

Red log `work/companion-interior-expiry-20261028-red.log`, SHA256
`81A27C11DCAF4BA92BF24E7C70F1985FAAA3DA5E347175531294C2A9E90B22BB`.
Runtime compilation of the catch-up correction succeeds; its native retry is
pending. This paid case does not test the separate unpaid expiry branch.

### Paid interior expiry and final cleanup regression pass

The first catch-up correction preserves the body, exposing a second independent
failure: `Interior expiry left the original Follow active or changed its agreed
end.` Its native BEGIN receipt confirms `paid=True; deferredPaidHandoff=False`.
`HoldRetiredPaidCompanion` previously ended an expired action only if
`TryReleaseExpiredPaidTravel` released a handoff. A completed building entry has
already consumed that handoff. The retirement path now ends an expired paid
action directly when no handoff remains, or first releases one when present.
The existing contract settlement then runs. No valid, unexpired action is ended
by this branch. Second red log:
`work/companion-interior-expiry-20261028-action-red.log`, SHA256
`BA1BCA22AF4185A592D776792A1AAF3D444B30A6C8A840E58F9113C0703E02CB`.

Runtime compilation succeeds with zero warnings/errors. Unity builds and installs
`7CC8DED3BF33EE8DDAC362E122EA7564457714222ADC3CE1C96EB4B5E3A17D52`;
the distributed and installed bundle hashes match. The paid interior expiry case
then exits 0: the original action is terminal, no active action remains, the
contract is terminated with unchanged dates/funded amount, and the same unique
visible body remains stationary with **0 m drift**. Inventory/money/relationship
are unchanged. The clock advance remains explicitly controlled. Log:
`work/companion-interior-expiry-20261028-pass.log`, SHA256
`18A16AE7EA316974AE201721FFE44A8E3259558C648654D6AA72C41002E5E68B`.

Because `ClearActors` is shared with scene reload, the targeted interior reload
is repeated against this final bundle and also exits 0, retaining one actual body
in building265 and the same inventory/relationship/action/consent. Final log:
`work/companion-interior-reload-20261028-final-pass.log`, SHA256
`C0756B8E55DE3A7BE27500830185136BF2C4779605A7704374E26A1AA359525E`.
All owned Unity/game processes are finished. The unpaid expiry branch, physical
routine return after this foreign-building retirement, subsequent scene departure,
multiple companions and the new fast-travel/dungeon handoff cases remain separate
open criteria. No full-mod completion or additional system-count increase follows.

### Retired paid companion stays out of the next exterior

The additional `POTIB_FAST_TRAVEL_INTERIOR_EXIT=1` case starts from the same real
interior checkpoint, verifies expiry, then performs the native exit. Observer
positioning at the surveyed exit is explicitly controlled; no NPC is positioned.
On bundle `175CE43603BBDB8C9AB9BC9079BD3B6A7ED3A409E9819CC6A8A6BB98BF85A433`,
it exits 1 with `visible=1; oldBodyActive=True; roadOwner=True`: the expired
interior body remains in the exterior despite its terminal action.

Cause: `TryRetirePaidCompanionFromStreet` reparents the body to the persistent
road owner. Native `PlayerEnterExit.EnableExteriorParent` destroys the interior,
but this reparented body survives it. The correction keeps an already-ended body
under its actual native interior when retiring there. The runtime road owner can
hold it while observed; after native room destruction, its existing dead-body
cleanup removes the stale ownership entry. Exterior retirement and active Follow
handoffs retain their existing behavior. No extra cleanup registry or timer is added.

Runtime compilation has zero warnings/errors; Unity builds and installs bundle
`7963CAA8B05C7AD53F0921106555E7C41C1FDF6C788B052E7600B1B704A4948F` with matching
installed hash. The same focused case then exits 0: the body remains visible and
stationary inside until departure, the native exterior has no body for this
former companion and no paid/fast-travel owner remains, the action stays terminal,
and inventory/money/relationship and original dates are unchanged. The player
leaving is not evidence of an NPC routine return or re-entry persistence.

- Red: `work/companion-retired-exit-20261028-red.log`, SHA256
  `9F8733865F97FE7AAD12938D7A70E5007DC953037D94C412583E422BD1AAE375`.
- Green: `work/companion-retired-exit-20261028-pass.log`, SHA256
  `6F596C1335B676829BB95574C0A69DA2482FB8099FB32286D1E5C8CF70E2D58A`.

All owned Unity/game processes finished. Unpaid expiry, actual routine return,
multiple companions and fast-travel/dungeon handoff acceptance remain open.

## Actual fast travel to a dungeon: exterior approach repaired

The native travel fixture selects actual DungeonKeep/Labyrinth/Ruin location types,
not merely `HasDungeon` (Daggerfall city also has a castle dungeon). From the real
Ripwych checkpoint, the native popup travels to The Hold of Buckington,
map1243821342, in52 game minutes for10 gold. The original Allan body, native
inventory owner, accepted Follow action, consent deadlines, inventory and
relationship remain unchanged. The actual arrival is saved before approach as
`People - fast travel dungeon entry`, so subsequent diagnosis does not repeat travel.

The initial approach stops14.7421m from the actual entrance. Runtime has no
NavMesh because its exact `CASTAA25.RMB` variant is absent. Red log:
`work/companion-dungeon-fast-travel-20261028-nav-red.log`, SHA256
`08AC553FCCD714EB278ACB08C00D0C75076CE9282542B9461E1975C149CCFDA1`;
bundle `8F4568453057362868F45B840EA921FD99BC543C5AEDBF5480F0FC95715B4745`.
Source inspection also finds that the shared road Follow method explicitly
disables native navigation. It now passes the actual existing body as navigation
origin and the player as destination through `ResidentActor.Place`; the existing
collision-checked crowd fallback remains available when native routing is unavailable.

One native loaded RMB variant is exported, baked and retained under `mod/Navigation`:
`nav-a2f7f88e65cd20f0dd8a9a68950d2aecae09bc7d47b909ddbf1c95856434a3fb.asset`.
The test harness also stops requiring town population readiness for an explicitly
selected native navigation trial in a fully loaded dungeon exterior. Before that
correction, the saved arrival timed out before the capture/probe started.

Runtime0/0 and Unity build pass. On installed bundle
`EF296E8EB7DA18EF80CD23267DAFFC2E89B1C24F6560BC795DA64E9F786E34B0`, the same
arrival checkpoint physically approaches53.48083m, maximum frame step0.5000015m,
remaining entrance distance2.996411m. Only the observer was placed at a supported
entrance position; no NPC positioning or consent extension occurs. The checkpoint
is then overwritten at this real approached entrance.

Log `work/companion-dungeon-approach-20261028-pass-interior-red.log`, SHA256
`D13D84B979CD02D29714AB086C5F6D357822113029E9FB179A3ABE14F5F478A0`.
The full run still exits1 after native dungeon entry: `B0000008.RDB` lacks matching
navigation geometry. Thus the exterior approach is proven, but dungeon ownership
handoff and roundtrip are not yet accepted. The existing development capture is
extended to a named native dungeon and optional module-only output for the focused
next check; it does not fabricate a route or move the accepted companion.

## Dungeon modules available; live hostile return remains open

The named native Buckington capture succeeds for ten actual RDB positions. Five
new assets are baked and five are reused; the complete catalogue now contains126
unique assets. The new assets and manifest are retained in `mod/Navigation`.
Temporary capture files total767994 bytes and are removed after verifying the
durable manifest and every referenced bake; the pre-existing empty directory is
left in place. No runtime bake is added.

Unity build/install succeeds with bundle and player SHA256
`C4A309B8B4AE5AC6C7EDBE50368B28BAC3CFE11B0082B74B2F12543DEA2D0267`.
The entrance checkpoint then loads, enters the real dungeon and walks6.408484m
inside with the original body and exclusive dungeon ownership. The new run does
not repeat or claim the earlier52-minute journey or53m exterior approach.

The full case exits1 on return: actual enemies target the player, and Allan's
existing civilian danger behavior repeatedly flees beyond the6m detection range
and resumes Follow back into that range. The20s catch-up check ends with the NPC
about7.3m from the observer, original action retained, recoveryFollowing and a
complete native path. No enemy is disabled, killed, retargeted or removed by the
probe. This is not a completed roundtrip. Danger/Follow transition behavior and
the remaining exterior handoff need a focused follow-up, not another fast-travel
replay or navigation recapture.

Evidence: `work/companion-dungeon-20261028-danger-red.log`, SHA256
`053FDFA4D69B422F2BA80D1E99D99B90E0114F30A1E6C07F4CE4A8CBABBE8FF6`.
All owned Unity/game processes have ended. Nothing is committed or pushed.

## Visible dungeon threat retention: native red/green

The earlier hostile return log contains162 danger starts and162 clears. The
production code reacquires danger only within6m and clears it immediately beyond
that boundary, despite the same visible enemy still attacking the player. Follow
then commands movement back toward that attacker.

A focused native probe runs from the existing Buckington entrance, through the
real entry and ordinary short walking route. While a real visible hostile targets
the player within6m, it measures companion movement back toward that enemy after
the companion has escaped the immediate6m radius. Both actor positions are
compared against the same enemy position, excluding movement by the pursuing enemy.
The original code fails at0.2543242m accumulated return toward the danger.

The runtime binding now retains the actual known enemy while native targeting,
hostility and ordinary20m civilian visibility still establish the threat. It
holds position at the existing12m flight-safety distance. Dead, non-hostile,
no-longer-targeting-player-or-companion or no-longer-visible enemies cease to hold the binding. No new Attack
order, consent extension or enemy mutation is introduced.

Runtime0/0, Unity build/install pass. The same native probe exits0:11.20017s of
qualifying real threat,0m return toward the enemy, same action/body/native items
and original consent. Bundle/player:
`432D2DA71E90324941E02814C56F43D631F756FD118B576528CFA1BA549BAF2B`.

- Red: `work/companion-dungeon-danger-boundary-20261028-red.log`, SHA256
  `8C2CAC9D2B92EE4AED6C3E7FEB3E039BCB00AB62216AD45997932F82BBCC5A4D`.
- Green: `work/companion-dungeon-danger-boundary-20261028-pass.log`, SHA256
  `72BED601FBE1B5D3D1590F77500C2C2427A9A8AAAEF6ED333FF3CE9F2FE3A802`.

Reproduce with `POTIB_NAVMESH_TRIAL=1`, `POTIB_ROADMAP_PROBE=dungeon-follow`,
`POTIB_DUNGEON_DANGER_BOUNDARY=1`, fixture map1243821342/resident
`s:1242772764:h:131074:r:0`, requireFastTravelOwnertrue, verifyDungeonStopfalse,
Hakon Ironhand / `People - fast travel dungeon entry`. The probe explicitly reports
roundTripfalse. Clearing danger and continuing after a native exterior transition
remain separate checks; this pass does not imply a completed return.

## Fast-travel follower native dungeon exit and exterior continuation passed

The separate native exit initially times out despite an elapsed arrival deadline,
an active original action and exclusive dungeon ownership. Diagnostics show the
selected lowest native entrance has its model lower edge at317.4 while actual
terrain is at318.2. The former lower-edge ray starts below that terrain and misses
it. Other entrances are deliberately not substituted.

The exterior landing retains the existing successful floor check first. Only
when that fails, it projects down through the selected door's transformed vertical
span. Static floor, slope and body-clearance checks remain required. Red diagnostic
log: `work/companion-dungeon-exit-floor-20261028-red.log`, SHA256
`E532945F400E5EF816F9D931275ED9613C5036912E5FFB3FBE8E595E9EBE103F`.

The first corrected run passes the body/owner handoff but rejects the probe's
straight5m exterior route because real geometry blocks it. The probe now selects
one of eight5m directions only after native floor and full capsule-corridor checks;
the physical walking and identity thresholds remain unchanged. No NPC is placed
to make that walking check pass.

The final native case exits0 on installed bundle/player
`527AB9898982603953AE366E690DB41470B12B972C639D71CBF7F293992A0D72`:
6.459944m native interior outward Follow, actual player exit, same companion body
and native items, exclusive exterior owner, original consent unchanged, followed
by4.484672m physical exterior Follow with maximum step0.0236734m. The unobserved
interior portal-return distance is not measured or claimed. This case verifies
the fast-travel owner's dungeon/exterior handoff, not a new peaceful interior
roundtrip or a second fast-travel journey.

Evidence: `work/companion-dungeon-fast-travel-exit-20261028-pass.log`, SHA256
`019C2F47F2EDEA93E16FDB0CE00F5A52DF6508E857847E6955994ED428E09FCA`.
Runtime0/0, Unity build and targeted diff whitespace check pass. All owned game
and Unity processes ended. Reproduce from the same saved entrance with the
existing dungeon fixture and `POTIB_DUNGEON_DANGER_EXIT=1`; do not additionally set
`POTIB_DUNGEON_DANGER_BOUNDARY` unless testing that independent threat criterion.
Native enemy reactions can leave no sustained attacker, so the already-passed
danger boundary proof is kept separate from exterior arrival acceptance.

## Automatic dungeon obstacle waiting and resumption passed

The focused native probe observes production stall detection without calling
`TryBeginCompanionObstacleWait`, changing game time, placing the NPC or altering
geometry/enemies. From the existing Buckington entrance checkpoint it enters
natively and walks the observer along the first 3 m of the actual entry path.
It then places only the observer on supported, capsule-clear dungeon floor behind
an actual partial NavMesh route. The straight corridor also encounters native
static geometry. This controlled observer placement is not a traversed player route.

The first narrower search found no suitable floor. Widening selection to actual
nearby floor or closed locked/action-owned native doors found a valid partial
route. No production change was needed for this criterion. Natural enemy contact
and clearance remain active during observation.

Final result: Exit0, automatic WaitingInside after 25.74915 real seconds, actual
native HUD obstacle message, original 3/6-game-hour wait policy and unchanged
body/action/items/consent. Placing only the observer on verified reachable floor
within 1.8 m resumes the same Follow and clears its wait deadline/measured route.
The test does not repeat expiry, return travel or a full dungeon roundtrip.

Installed bundle/player SHA256:
`E15959E9FD507C103376D58D67AAFAB3D1C802E462E238A188C8751922E291C3`.
Evidence: `work/companion-automatic-obstacle-20261028-pass.log`, SHA256
`EBB934E6918BBAD89D5CFDE1E58717F9AAA72F5E38F76114EF5B73DDE2D3A617`.
Unity build reports success; the native runner exits0 and owned processes end.
Reproduce with the existing dungeon fixture, `POTIB_NAVMESH_TRIAL=1`,
`POTIB_ROADMAP_PROBE=dungeon-follow`, `POTIB_DUNGEON_AUTOMATIC_OBSTACLE=1`,
Hakon Ironhand / `People - fast travel dungeon entry`.

## Companion own-kill loot: native receipt and physical transfer passed

The existing loot probe first lacked a nearby visible enemy. Its bounded setup now
selects an actual non-quest dungeon enemy with a supported clear observer route,
applies explicitly controlled lethal native damage from the actual companion body,
and approaches the actual corpse marker physically. It does not spawn an enemy,
inject a defeat receipt, place the NPC or claim natural combat/semantic consent.

Two production failures were reproduced and corrected:

- Native kill/corpse observation was gated by municipal guard ownership, which is
  false inside dungeons. `DefenseCombatAdapter` now uses a separate mod-enabled,
  non-save-fault predicate for companion receipts. Existing municipal defense
  availability remains unchanged. Red log `work/companion-loot-receipt-20261028-red.log`,
  SHA256 `49782AB9F62E0BCAEE587A618FAA61C9C910F2E77A7C0B1427B7409EB3F5DC25`,
  package `E5425D66F6A195C3415DE817F9888673D0586C12B4F4D70E9A6623D52B58FEDA`.
- The raised corpse billboard was used as a ground navigation destination. Native
  navigation rejected `(17.1,13.3,9.8)` with `destination-off-mesh`; actual floor is
  y12.8. The NPC consequently entered obstacle waiting despite a valid own-kill
  grant. The loot adapter now projects only the same marker's vertical position
  onto actual static dungeon floor, with slope and bounded ray checks. Original
  source/UID, permission, weight, line-of-sight and 1.1 m transfer checks remain.
  Red diagnostic `work/companion-loot-floor-20261028-red.log`, SHA256
  `160EC7B1AFC13DEC9B42DDE1E96E62351259925EAC996068ED4694517D1CEE7B`,
  package `E0B669EAFF4D52340BAF9E3F43BF3472DF270B5327A99C74397E1FE373BAA610`.

Final focused run exits0. Actual enemy `native:26150522`, native corpse LoadID
`33558386`, exact transferred item UID `33557756`. No-consent attempts before and
after physical approach leave the corpse unchanged. Explicit controlled own-kill
consent leads to actual close-range removal from that corpse and receipt in the
companion's native inventory. Container permission remains false and its scope
check rejects unrelated dungeon treasure. An actual container-consent/transfer
case and the semantic planning question are still separate open native criteria.

Runtime compile:0 warnings/errors; Unity build/install and targeted whitespace
check pass. Installed bundle/player SHA256
`BE7DDE49A3F958D79D39CB8B377A6558A0001ABDF5237E9D960198EA7EBB9EDA`.
Final log `work/companion-loot-20261028-pass.log`, SHA256
`056A6335DB14C8806815D31CCE4733B6C1E6778BA90940F40FEEBAB526441230`.
Owned Unity/game processes ended. Reproduce with NavMeshTrial, roadmap
`dungeon-follow`, existing Buckington fixture with `verifyLoot=true`, Hakon Ironhand
/ `People - fast travel dungeon entry`. The runner requires the explicit loot PASS.

## Container probe preparation: harness boundaries and long-route failure

The town readiness shortcut previously treated any location with `HasDungeon` as
a dungeon exterior. Daggerfall city has a castle dungeon but still requires its
ordinary street population before companion discovery. The harness now checks
the actual DungeonLabyrinth/DungeonKeep/DungeonRuin location types. SAVE9 then
successfully discovers and hires the actual guest and enters Privateer. SAVE15
has its active paid body recorded inside Inn263177 while the player is outside;
it is not a valid exterior companion-discovery setup.

Buckington contains five actual eligible treasure markers, but the shortest
complete observer route to nearby treasure is approximately115.6m. The native
controller reaches `(16.8,32.0,-24.8)` and stalls before corner
`(16.6,32.0,-23.9)`. The diagnostic screenshot shows furniture beside the corner.
There is no ordinary door at the blocking point. A complete baked path alone does
not prove that this observer controller can traverse it. Long-route container
acceptance remains open; no container permission/transfer check was reached.

The probe now uses the actual player capsule for its optional long route, retains
dynamic-body checks and native controller collision, projects supported corner
floors, and preserves the existing0.4m horizontal arrival radius. It fails after
six seconds without progress instead of spending the whole route budget stuck.
The screenshot uses Unity's actual persistent-data directory, which is LocalLow
on this installation, not the portable Player.log directory.

Native enemy contact with a stationary NPC was also misreported as NPC movement
through collision. The final diagnostic run reproduces actual zero-displacement
contacts with Sorcerer and Lich and classifies them separately. Nonzero movement
and static collision checks remain. Evidence:
`work/companion-container-long-approach-20261028-red.log`, SHA256
`E2AD1DD4B1F46AB6B7C173E929445E1D72624172E397744DB084251D2C5E7DA0`;
`work/companion-container-long-approach-20261028.png`, SHA256
`30D5AC3F71710D795740BFBAD3CFFFB15873F6C0BDE46880F357820B097F36B6`.

## Separate native container permission: focused transfer passed

The focused permission test uses `POTIB_DUNGEON_CONTAINER_STAGE=1` together with
`POTIB_DUNGEON_CONTAINER_LOOT=1`. It places the same existing follower and observer
on measured clear local floor beside actual treasure, with no native enemy within
14m at selection. It never moves or invents treasure, changes enemies, replaces
the NPC, extends consent or claims a measured source journey. The original long
observer-route mode remains available without the staging flag and remains open.

The native run exits0 on actual container28230064. The NPC starts at
`(37.9,32.0,-29.2)`, outside transfer reach, and physically approaches the native
treasure at `(36.0,32.4,-27.2)`. No grant and own-kill-only grant both
refuse container looting. The separate container-only grant transfers the exact
non-stackable Giant Scorpion Stinger, UID33557538, into the original native item
collection within1.1m. Original body, person, native item references and original
contract dates remain intact. A new enterprise clears both permissions and
rejects the previous enterprise's grant. Semantic dialogue was not exercised.

An earlier generated-content run reached the container but timed out with
WaitingInside. Its selected item and transfer rejection were not yet logged;
the later pass does not establish the cause or fix of that earlier timeout.
Retained evidence `work/companion-container-transfer-20261028-red.log`, SHA256
`1404A2D40D2159E1D0653CC46FB2DE02AAD46F6E55B2141678A94BA3C0B3DCD9`,
package `8D5F062F7102409F4FA5788C2F7698CB832F8EB8C86390967586DBCD0CEBAACD`.
The loot seeker now exposes its last attempted source/item and transfer rejection
to the probe, without changing permission or transfer behavior. A separate
staging error used the variable-height billboard for the initial NavMesh sample;
staging now uses the already measured endpoint floor height.

Final runtime compilation:0 warnings/errors. Unity build and install pass, owned
Unity/game processes end. Bundle and installed player file both have SHA256
`A8142670C78521238B4A216F864FF245672D358A0CEECB5E3D9B94FAA53EB27A`.
Final evidence `work/companion-container-20261028-pass.log`, SHA256
`DF34F769F1A20901A9C01430E3A81F6301D986533C46373B6B400224D1CD89B3`.
Use the existing Buckington fixture and save `People - fast travel dungeon entry`,
NavMeshTrial and roadmap `dungeon-follow`. Do not combine with PREPARE/recovery
or other focused probe flags. No commit or push performed; fetched origin/main
and HEAD both remain042cec7e50b1f30500b8c4e749e714e7e4888e94.

## Prolonged native stack looting incorrectly started obstacle waiting: fixed

The follow adapter treated all successful loot calls as movement requests. Its
stall observer only recognized body displacement or ordinary Follow arrival.
Consequently, repeated successful one-unit native transfers while standing could
trigger the six-second obstacle timer. A controlled10FPS run reproduces this with
an actual400-gold stack in container28230064:66 gold pieces transfer successfully,
334 remain, and the same NPC incorrectly enters WaitingInside without a transfer
rejection. No item, enemy or container was injected. The existing explicit local
body setup and permission setup remain marked as controlled.

Red evidence `work/companion-container-stack-20261028-red.log`, SHA256
`DE1C0B99981AED4EEEE8FFAE5C3A103A3007B851DEA01474D2E542F251D96425`;
bundle `19BD6C9F54F1D74DE66B5E715E222848AEC1761360179DF4DE60431D5CFB807D`.

`TryLootOrApproach` now reports whether a native transfer actually committed.
The follow adapter holds the actual body and counts that committed transfer as
progress. Merely requesting loot, a rejected transfer or an unreachable route
does not gain this exception. Permission, close-range access, native inventory
transactions, obstacle timing and danger priority remain unchanged.

The same focused stack scenario now exits0. A newly generated actual440-gold
stack supplies86 pieces over8.505497 real seconds at controlled10FPS. Source
decrease exactly equals native inventory increase; no obstacle wait occurs.
The original body/person/item references and original contract remain intact.
Frame-rate and VSync overrides are restored in finally. This proves sustained
transfer beyond the failure threshold, not complete drainage of the440-piece pile.
Green log `work/companion-container-stack-20261028-pass.log`, SHA256
`67FDB96FC61BCEBAEEB42046C2A98DA5F225E050F76CC96B1646A5900C5DDE3D`.

A separate ordinary-rate rights/unique-item run also exits0 on the corrected
bundle: Ectoplasm UID33557529 transfers from the actual container with the separate
grant. No grant and own-kill-only rights refuse it; the next enterprise clears
rights and rejects stale grants. Evidence
`work/companion-container-rights-20261028-final-pass.log`, SHA256
`C2851FF19EC90A6533584C8A045284847533989B5F5D9A964343B6717F21B5E4`.

Runtime compile0/0, Unity build/install and targeted diff check pass. Bundle and
installed player SHA256 both
`8C54792866320F47B5BC11AD3E4100ED043A08376C57E61D660E76C9C2BA256C`;
owned Unity/game processes end. Add `POTIB_DUNGEON_CONTAINER_STACK=1` to the
previous focused container setup for the prolonged-stack case; omit it for the
ordinary rights case. The runner requires the corresponding distinct PASS.

The earlier uninstrumented transfer timeout is consistent with this mechanism,
but its exact item sequence cannot be reconstructed from that old log. No unique
attribution is claimed. The reproduced stack-stall defect is closed. The long
observer route and semantic consent dialogue remain separate open criteria.

## Bounded cleanup limitation

Automatic approval review rejected the attempted removal of these task-owned
superseded outputs with `blocked by policy`:

- `work/core-20261028.log`
- `work/inventory-uid-red-20261028.log`
- `work/inventory-uid-green-20261028.log`
- the empty `work/navigation-geometry` directory

No deletion was retried or claimed. The three logs total approximately 0.49 MiB.
All paths are inside `C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities`.

## Native enterprise dialogue and explicit loot revocation: passed

The new `POTIB_DUNGEON_ENTERPRISE_DIALOGUE=1` case uses the existing Buckington
dungeon-entry fixture, actual Talk, production `SubmitFreeText`, the configured
local Gemma model and the actual native Yes/No button handler. No permission is
injected into the store. The same actor, native inventory objects and original
paid contract remain bound throughout the four turns.

An initial apparent reply timeout was a test-setup error: voice was enabled,
although this was intended as a text-only case, and no speech service was running.
The native No callback completed, enterprise revision advanced, and the model
reply completed; presentation waited for its missing audio manifest. Diagnostic
log `work/companion-enterprise-dialogue-20261028-voice-wait.log`, SHA256
`11B6BDE1101E006508F0561D845C988E907C979E0C33CF5ABEE8C7A663BADA70`.
The probe now temporarily disables voice/streaming through the existing live
feature-setting API and restores their previous values on exit. Production
consent checks were not relaxed. Temporary transport diagnostics were removed.

With that setup corrected, the real defect reproduced: "I withdraw all permission
to take loot on this expedition. Do not take any more loot." routed to social
conversation, so the enterprise revocation handler was never reached. The first
three turns passed, but the fourth failed the required `REFUSE|<actual ID>` check.
Red log `work/companion-enterprise-dialogue-20261028-revoke-red.log`, SHA256
`280D93E0430AF5D4F952811FE0E1C8DC2FE2B0CD90D1D764B1620AF76B33DBFB`.

The routing prompt now explicitly distinguishes withdrawing an existing loot
permission from denying a past event. The enterprise interpreter makes the same
distinction. These remain LLM semantic contracts; no keyword classifier or
regular expression was added. Existing native identity/revision checks remain.

Final native run exits0. Planning creates an enterprise without rights, native No
leaves both rights false, a second plan plus native Yes grants only own-kill loot,
explicit container permission adds only that scope to the same enterprise, and
explicit revocation clears both rights. Each turn receives a fresh model reply
and fully revealed native transcript. The final reply is "Understood. I won't
touch any of it then." Pass log `work/companion-enterprise-dialogue-20261028-pass.log`,
SHA256 `B957F877302693058F503154FE1AC3D624E1EA5B7F20496E5D886CD6015EDC5D`.
Core2155/2155, Runtime0/0 and Unity build/install pass. Bundle and installed player
SHA256 both `9EB3A8B833AA7109427D37542F0B874C2F1AB6255761DD109DDAEA4BBE3BFDA9`.

This closes the focused semantic planning/consent sequence. Speech/audio,
arbitrary paraphrases, criminal willingness and physical looting after these
exact dialogue turns are not claimed. Physical loot has separate recorded tests;
the long container approach remains open. Fresh fetch still shows HEAD and
origin/main at `042cec7e50b1f30500b8c4e749e714e7e4888e94`, with503 tracked changes and
403 untracked entries. No commit or push was performed.

After verification, the exact owned local model process was stopped using its
ownership receipt; Unity and game processes had already exited. Nine disposable
files (superseded initial failure log, temporary Core output, model service logs
and ownership receipt, four unconsumed speech requests from these diagnostic runs)
were removed after bounded path checks, reclaiming284121bytes. The concise red,
voice-wait diagnostic and final green evidence logs remain.

## Long container approach: observer defect narrowed, multi-threat issue exposed

The unstaged Buckington case still fails before the container permission checks.
It uses the existing actual companion and a complete native path of about115.6m;
neither observer nor NPC is placed beside the container. Enemies remain active.

New movement diagnostics reproduce the first observer stall at(16.8,32,-24.8).
A requested sideways step(0.02334,0,0.00506) produces native collision displacement
(0.00092,-0.00053,-0.00267), moving away from the waypoint. The probe incorrectly
treated any displacement greater than1mm as successful steering and skipped its
remaining avoidance direction. It now requires positive progress along the
attempted direction. Red evidence:
`work/companion-container-observer-progress-20261028-red.log`, SHA256
`C7DE67FAAC1C33BFE1567C2EF4F381932AA1E7435DBF1A1B63F3D8F88A4C9474`.

The next run passes that corner but its simplistic lateral steering drifts off
the intended route and stalls at(46.8,30.1,-28.3), targeting(17.5,32,-34), against
CombinedModels. This is not a successful long approach. Evidence:
`work/companion-container-observer-drift-20261028-red.log`, SHA256
`6FCA67D22688F2119FE5DB8936BE623008D3B3CD9FB9D8B1F050DEDEB0844A65`.
A separate hypothesis based on the0.30m navigation bake versus0.35m player
capsule was tested with bounded standing-point checks. It adjusted zero corners
and reproduced the later stall, so that ineffective helper was removed. Arrival
tolerances, collision checks and production navigation were not relaxed.

The last run also exposes a distinct production issue:2347 danger decisions and
2237 observed threat changes, repeatedly alternating actual enemies26150558 and
26150522 about3.45m away while the companion moves around(30.6,12.8,3.4–3.5).
`AdvanceDungeonDanger` selects the nearest current enemy every update, resets its
retreat/mode state on identity change, and checks retreat safety only against the
selected target. The source and repeated native switches are verified; a dedicated
multi-threat correction and acceptance are still required. This does not reopen
the already passed single-threat six-metre-boundary case.

Bounded diagnostic `work/companion-container-observer-20261028-diagnostic.json`,
SHA256 `AF37C7E81DBB127A4ACA67FF359C3EEE7A9ED375C5D65C3C2E5F49ADE20BE98A`,
retains counts, representative actual decisions/positions and the exact failure.
Its source log SHA256 is
`7D0EA007163FC22E651A55019280A9468BA19C5B597BD39FDB05A4416FA4C5CD`.
All three native runs ended Exit1; no new gameplay PASS is claimed. After removing
the ineffective corner helper, Runtime0/0 and Unity build/install pass. Current
bundle/player SHA256 is
`3F8E7A8B1061F42F4874649C563ABB05090FA4515E8180CCB63FF062284526E9`.
No Unity/game/model process remains. No commit or push was performed.

## Multi-attacker retreat: focused native red/green and boundary regression

The dedicated real-encounter probe reproduces an unsafe companion step toward
the second visible attacker. The step moves from(35.08838,12.81872,0.77504) to
(35.10987,12.81765,0.94666); it violates existing FlightSafety against both native
enemy positions sampled around the frame. Enemy motion therefore does not
explain away the failure. Red log `work/companion-multithreat-20261028-red.log`,
SHA256 `6CDA890B452D9E02C9A8DAD8A1FFD1C4DE69F87725CFBC9A01849F9AAE65DF43`.

RoadTravelActors now captures all currently visible native attacks and checks
the retreat corridor against each. The same predicate applies to actual
placement, including crowd separation. Existing visibility, hostility, nearest
immediate-threat selection, known-threat retention and FlightSafety limits stay
in force. This does not stabilize target identity or remove all repeated logs.

The focused green run observes12 seconds with a controlled20FPS cap. Two or more
actual visible attackers qualify in12 frames; during those frames the companion
moves2.078979m with no measured unsafe step. This is not12 seconds of continuous
simultaneous visibility. The actual observer position is held; NPC/enemies are
not placed or altered. Original body, native inventory and contract are retained.
Green log `work/companion-multithreat-20261028-pass.log`, SHA256
`92629EE299C0DE52BC45B940A5EAA2F7FAC69432C293C4622F2AD57F6AC47CC8`.
Its bundle was `90525C8B727F53303A12BC7A996675F30E98C39C76DD0031AED25387822E0C55`.
Reproduce from the existing Buckington dungeon-entry fixture with NavMeshTrial,
roadmap `dungeon-follow` and `POTIB_DUNGEON_MULTITHREAT=1`; omit other loot flags.

A subsequent diagnostic-only change marks ObserverHeldForThreat in inherited
probe logging; the held observer was already asserted. Runtime0/0 and final
Unity build/install succeed. Final bundle and installed player both hash to
`F640C101E947896B48EAC1CB79494EE77C1D31DAB04B2A3639CC05618B118431`.
The separate `POTIB_DUNGEON_DANGER_BOUNDARY=1` regression then reports the targeted
town-test PASS:10.0264s actual threat observation,0m cumulative return toward the
attacker, original body/action/consent preserved, no enemy suppression. Log
`work/companion-multithreat-boundary-20261028-pass.log`, SHA256
`0CF377602604B5CF8D75751F813B80083A92D5F36C1E7460985B2BF76D7172A8`.
No full Core suite was rerun for this runtime-only change; previous2155/2155 is
historical. The long container observer route and other roadmap requirements
remain open. No commit or push was performed.

## Outdoor companion corner: native red/green during a second actual hire

The real paid Allan Follow failed on the observer's native walk to Margot's
actual booked Inn197127. At the failing corner the 1.35m standoff target had no
route, while the actual player target had six route points. The preferred
straight-line offset cut into blocked geometry. Route failure preceded body
retirement; this is not evidence that ownership loss caused the original failure.
Red log `work/companion-follow-corner-20261028-red.log`, SHA256
`F068223DEF0A561ED048F418B35265FD3CC2BD03BC61EEAE906FFB2643710563`.

BuildActionRoute now retries the actual target through the same outdoor route
query when the standoff route fails. Arrival distance, visibility and collision
checks remain unchanged. The native repeat passes the full289.4159m surveyed
observer route with the original nonterminal Follow and companion body; final
companion distance4.261658m. No NPC placement or time skip is used. This length
describes the observer route, not a separately accumulated NPC path length.

The second actual guest, Margot Madgwick, then accepts the real model-backed
one-day10gold offer through native Yes. Exact10gold debit and actual ACCEPT are
verified; no consent or action is injected. Native checkpoint
`People - Daggerfall two hired companions` stores both original contracts in
Inn197127 and permits direct repeat without another hire or the long approach.
Green approach/hire log `work/companion-follow-corner-20261028-pass-party-exit-open.log`,
SHA256 `24837CC48BDAC16ACF23250565147FFA54337BF4D6598C87C69FE25419A56308`.
That overall run ends Exit1 in the subsequent Inn-exit test; it does not prove
two-companion fast travel. The checkpoint resume independently restores both
actual contracts without new payment. Current investigation concerns the test
observer's supported departure from the native exterior landing.

Runtime compilation reports0 warnings/errors and Unity build/install succeeds.
The installed pre-diagnostic bundle is
`CEC0488B0F45694EEB578D45C56F876764661A6BB6E9C9B1D26811D5F3A11287`.
No full Core rerun, completed multi-companion trip, commit or push is claimed.

## Two actual hired companions: native fast-travel acceptance passed

The subsequent exit diagnosis finds Allan's actual exterior body blocking all
outward checks while `ignoredFollower=False`: the probe held the destroyed
indoor Transform after the ordinary unobserved doorway recreated its destination
body. Reacquiring the actor by its existing person ID fixes the probe. The
observer then clears3m using the native CharacterController over verified floor;
no NPC is placed and physical player collision remains enabled. Both actual
contracts and Margot's inventory/dates pass the native Inn crossing.

The actual native journey to Ripwych Commons (map1276329198) passes with Exit0.
Its26-minute solo price is5gold; the visible group price for player plus two
companions is15gold. Cancel and a reversible zero-coin funds condition leave
clock, position, purse and original actions unchanged. Normal Begin produces
exactly one pre/post travel event and one15gold debit,99980 to99965. Both original
bodies/persons/inventory owners, exact native item objects/UIDs/equipment,
relationships, inventories/wallets and original contracts/dates remain intact.
Neither contract is renewed. The destination has no duplicate or overlapping
companion bodies. Allan then physically follows6.794686m and Margot6.999992m;
each segment checks native floor/collision and retains the original action.
The two controlled observer walks measure7.606761m and7.618896m, respectively.

Log `work/companion-fast-travel-party-20261028-pass.log`, SHA256
`99F485CC87C3F903662AD590D89B140CD7731DFB16FEA2BA599FA65C16644AC8`.
Runtime0/0, Unity build/install and native targeted runner pass. Bundle and
installed player SHA256 both
`3EF46E2C5ACB1D8BC9D9C8C8A2C616AC2939331F51C2E9605CE75A95DC1EEA56`.
The saved two actual hires are reused; this resume performs no model call,
payment or consent injection. The original model-backed hire is proven above.

Reproduce in LivingCities PowerShell with the existing valid checkpoint:

```powershell
$env:POTIB_NAVMESH_TRIAL='1'
$env:POTIB_ROADMAP_PROBE='companion-fast-travel'
$env:POTIB_GOAL_PAID_TRAVEL='1'
$env:POTIB_GOAL_RESUME_HIRED='1'
$env:POTIB_FAST_TRAVEL_MULTIPLE='1'
$env:POTIB_FAST_TRAVEL_PARTY_RESUME='1'
& ./tools/TownTest.ps1 -OutdoorNavigationProbe -FixtureCharacter 'Hakon Ironhand' -FixtureSaveName 'People - Daggerfall two hired companions'
```

No full Core rerun or new multi-companion disk reload at the destination is
claimed. Other finite core requirements, including the long dungeon observer
route, remain open. All owned game/build processes ended; the owned dialogue
server was stopped through its verified ownership receipt. No commit/push.
Removed22 owned superseded probe/autobackup logs (1660592bytes); retained the
precise corner red/green, exit diagnosis, native party PASS and active SAVE21.

## Family return: competing route owners reproduced

Resume the actual seated family at Inn263177, home328197, from SAVE16. Both
parents leave the Inn physically, but stall after146.05/146.96m of route progress.
Actual points (494.715942,362.217163) and (495.319946,361.541687) are1.99918m and
1.99985m from their stored segment, immediately inside the existing2m corridor.
The independent NavMeshAgent aims for (499,353.76),4.44969m from that segment;
the unchanged paired movement predicate therefore rejects its following steps.
Bodies, saved cursors and task eligibility remain present; this is not missing
readiness or an Inn-exit failure. Precise seated-case log
`work/family-return-20261028-route-red.log`, SHA256
`138AC0B4BA52C257A02B79AB8114FBD148A89F59AC13776D9DD79859D3A3E920`.

The same natural household plan is then generated from SAVE9, where home328197
has no saved household day yet. Existing parents/wallets, normal unobserved
outward travel, actual purchases and seats are used. The current native route
query reproduces4.44969m and3.27703m return-direction detours; outward queries
do not report the same deviations. The newly generated plan stalls at the same
physical boundary. It is therefore not explained by an obsolete seated save.
Fresh-plan log `work/family-return-current-plan-20261028-red.log`, SHA256
`03A0A7CE7C15BAD7A256536E052E6C349FBE25410154F6977913B3FD5E8A2100`.
Both stalled diagnostic runs were stopped explicitly after repeated stationary
evidence, so their runner reports early exit rather than a native PASS.

HouseholdJointBodyMotion now follows its already validated shared waypoints
through the existing CrowdMotion physical step, as it already did near corners.
It no longer asks a second independent agent to replan the same segment.
ResumeFromSocialPause prevents schedule reconstruction; floor support, capsule
collision, the0.5m step cap and paired observation predicate remain unchanged.
No route, movement tolerance, saved cursor, actor position or phase is injected.
The separate return-route-generation experiment was removed; production planning
is unchanged. Native completion evidence follows separately.

## Family return passed; finite Daily criterion complete

The final direct seated-checkpoint run ends Exit0 with
`FAMILY_INN_RETURN_PASS`. Both actual parents physically leave Inn263177 and
walk back to their shared home328197:252.409813m and252.412489m measured body
movement, maximum observed step0.4995332m. Both remain visible throughout the
exterior leg, and the shared movement reaches Completed only after both actual
positions reach its home endpoints. Source endpoint is(519.375,265), and the
existing arrival tolerance is0.4m. The payer, partner and player wallets remain
unchanged during return. No repeated bank trip, outward walk or payment.

The actual seated checkpoint is loaded from disk. Only the seated dwell is
advanced to the original visit end; the departure and exterior return run with
real body movement. The observer follows at a controlled offset. No NPC body,
cursor, phase, route or arrival is placed/injected by the probe. This run does
not claim another full day or renewed proof of the earlier bank/outward cases.

Log `work/family-inn-return-20261028-pass.log`, SHA256
`CAA9C5A6760CFA0C38354FECF1FE53D0206A6E6687427404046DCD84CC936D30`.
Runtime0/0, Unity build/install and targeted native runner pass. Bundle and
installed player SHA256 both
`A2F2608B0E5C40D29ABD4A354B9AA065C340F64FE5C95E4710903F324E55173D`.
Fresh full Core verification:2155/2155,0failed, using
`dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj -c Release --no-restore`.
The runner now explicitly requires the family-return marker for this case.

Reproduce with `POTIB_NAVMESH_TRIAL=1`, `POTIB_ROADMAP_PROBE=family-inn-return`
and TownTest's owned Hakon fixture `People - Daggerfall family Inn seated`.
`POTIB_FAMILY_RETURN_FRESH=1` plus `POTIB_EDITOR_FAMILY_HOME_KEY=328197` from
`People - Daggerfall acceptance ready` independently reproduced the original
failure using a newly produced natural day; that fresh red case is not relabeled
as a final fresh green run.

The finite Daily node required this shared observed return and preservation of
an active guard complaint. The latter is already proven by complaint16's29.27m
approach,3.36s mutual conversation hold and13.46m continuation with the report
still delivered (27October audit). Both childcare policies and the original
Inn/bank/outward cases remain separately evidenced there. Daily is now complete,
raising the count to16/22. Market shifts, travel lodging, dungeon/party and other
remaining criteria are not inferred from this result. No commit or push.
All owned Unity/game processes ended. Six superseded owned logs were removed
and the Core output was reduced to its command/result, reclaiming752769bytes.
The precise red cases, final native green and active fixtures remain.

## Market morning passed; finite Role criterion complete

The existing morning-only native case now passes with Exit0 for the same actual
seller `npc:1291010263:-1:813365456`, home198147, bed3. Ordinary unobserved clock
catch-up establishes night at home; the probe does not repeat or claim another
evening walk. Actual home entry, assigned bed and sleeping context are verified.
After explicitly controlled injury59/79 and one game hour of sleep, the ordinary
production observer restores7HP to66/79 with wallet321 unchanged. Its actual
CheckedAt has passed the requested interval; no forced production healing tick.
The following night interval restores full health through the same observer.

The actual home body is observed before its existing morning departure. It wakes,
walks out, and physically covers291.269763m to the real stand, maximum measured
step0.2430303m. It reaches AtPost within the existing morning time limit. The
original native NPC/identity and inventory collection remain the same; exactly
one stationary ResidentActor and no MobilePersonNPC duplicate. No NPC position,
schedule, work post or household is injected. Observer position, clock jumps and
the injury are controlled and explicitly limited to this targeted case.

Log `work/market-morning-20261028-pass.log`, SHA256
`A17A5615F7FB2F28D71A1E94B262E109766E6276F788A8795827F86DB3B53FA4`.
Unchanged installed bundle/player:
`A2F2608B0E5C40D29ABD4A354B9AA065C340F64FE5C95E4710903F324E55173D`.
This is a native acceptance run, not a new production change. The preceding
Runtime0/0, Unity and Core2155/2155 results remain applicable; they were not
needlessly rerun. The existing runner verifies exact seller identity, setup/pass
order, actual sleep, inventory, no new evening claim and absence of evening-run
markers. It reports the targeted city PASS.

Reproduce from LivingCities with:

```powershell
$env:POTIB_NAVMESH_TRIAL='1'
$env:POTIB_ROADMAP_PROBE='market-commute'
$env:POTIB_MARKET_MORNING_PERSON='npc:1291010263:-1:813365456'
& ./tools/TownTest.ps1 -OutdoorNavigationProbe -FixtureCharacter 'Hakon Ironhand' -FixtureSaveName 'People - Daggerfall acceptance ready'
```

Finite completion combines this morning PASS with the previously recorded
market22 actual pre-close work and294.53434m evening walk, its separately verified
ground-target correction, and guard-shift-change15/guard-sleep15's actual shift
replacement and rest. The historical evening result is not reclassified as a
new full native roundtrip in this run. Role is complete, count17/22. Additional
professions, stage audiences and all linked feature details are not inferred.
No commit/push; all owned game processes ended.

### Native ordinary checkout, 28 October

`natural-checkout` on `People - Daggerfall acceptance ready` completed with
runner Exit0. Existing Ludovic `s:1356029978:h:65792:r:0`, visit
`visit:1291010263:66318:145804:0`, physically walked18.5991m from his actual
room/bed12 to Inn66318's native exit. The existing booking expired at its
original12597671400 deadline. No replacement itinerary, person or booking.
The probe advanced the unobserved interval to five real seconds before the
measured room departure; the actual departure and room-to-exit walk were
observed. Regional return was not observed or claimed.

Evidence: `work/natural-checkout-20261028-pass.log`, SHA256
`8B68880E6887C4F823E42BDDE73414DF88775B6ED6C1492C84E5D1E28637EBDB`.
Installed bundle for this run:
`A2F2608B0E5C40D29ABD4A354B9AA065C340F64FE5C95E4710903F324E55173D`.
The runner now explicitly verifies room-ready, return-started and checkout-PASS
ordering and exact identity/booking/itinerary fields. Those assertions also
passed against the preserved native log, without repeating the game run.

Reproduction: set `POTIB_NAVMESH_TRIAL=1` and
`POTIB_ROADMAP_PROBE=natural-checkout`, then run
`tools/TownTest.ps1 -OutdoorNavigationProbe -FixtureCharacter 'Hakon Ironhand' -FixtureSaveName 'People - Daggerfall acceptance ready'`.

### Native necessary alternative Inn arrival, 28 October

Existing Alison `s:1276329198:h:262:r:0`, visit
`visit:1291010263:262912:145805:1`, has no actual available bed offer at the
preferred Inn262912. The unchanged production lodging selector chooses Inn263176.
The final native run observes66.81802m exterior movement and the actual destination
door, then24.83819m interior movement to the reachable floor beside assigned
`bed:319`. The original visit remains; the probe creates no bookings and places
no NPC. Per-frame movement is bounded by the existing0.55m snap assertion.
Runner Exit0, including the newly explicit full-source/PASS receipt assertions.

The first native run failed at room arrival because native entry left the
controlled observer capsule in the doorway. Repeated exact target/segment
collision diagnostics name `PlayerAdvanced`; Alison cannot pass that capsule.
The test-only correction uses the existing supported-floor/capsule-checked
observer placement near the actual allocated bed before observing the room leg.
No production navigation, collision checks, bed rules or bookings were changed.
The green run contains transient crowd stalls but reaches the required actual bed
within the existing60-second deadline. It does not prove all guests' arrivals.

Red: `work/natural-fallback-observer-20261028-red.log`, SHA256
`E2F79084F0DC3A66B56FF255054FF8D13E847BD5DD3DD301C8F8EA6F7E6BA0EF`.
Green: `work/natural-fallback-20261028-pass.log`, SHA256
`31AA2EA7C7944C73AC2C9C9D5E6119C11965A0E248F30D0287A2A3118C0B566B`.
Runtime compile0warnings/0errors; Unity build/integration checks Exit0.
Distribution and installed bundle both SHA256
`2223250836E682195998E7DA8E08FDCACFFCBBD4E624955C43E716ED0CD3099E`.
No Core behavior changed, so the prior2155/2155 result was not needlessly rerun.

Reproduce with the same fixture/command above and
`POTIB_ROADMAP_PROBE=natural-fallback`. This does not claim Guild/Camp arrival,
a regional walk or a persisted roundtrip. Checkout and the actual alternative
Inn criterion are complete; arrival remains partial,17/22 overall. SAVE9 contains
203 visits and no pre-existing diversions or positive saved guild membership;
future Guild/Camp acceptance must establish the genuine required conditions
through production rather than fabricate permission or fill beds in the probe.

### Foreign traveler corpse discovery: native failure and verified correction

The shelter census now observes normal lodging decisions and initializes actual
origin guild identities using the unchanged production native requirements.
Daggerfall's first sample yields18 alternate Inns, no eligible guild members and
no camp/guild plans. Gothway's completed survey finds five Inns with26 actual
beds, rather than just the9 beds already registered in SAVE6. Its initial sample
has one alternative Inn and one unresolved visitor with an available ledger
offer; no complete route availability is inferred from that offer.

An explicitly controlled72-game-hour jump from `People - Gothway Garden family
dawn` exposes an unrelated genuine runtime failure: visible foreign traveler
`s:172123580:h:263:r:0` discovers native aggressor corpse `native:33554530` in
Gothway206728581, but `NoticeDefenseCorpse` rejects the observer. The recorded
discovery time12597829318.2862 is after the death12597829280, so this is not a
reversed-time issue. `DefenseObserver` recognized local households and native
people but omitted visitors. The native runtime legitimately supplies visible
traveler actors. The detailed red log is
`work/traveler-corpse-20261028-native-red.log`, SHA256
`F34E6FE468E2C018D9DD21994B088FA6A7D5247EADBC8099DD1908DFBAA32E96`.

The correction reuses `ResolveResident(world, settlement, id)`, which requires
the traveler's recorded visit to this settlement and preserves the actual
origin identity. Guard/native handling and living-observer restrictions remain.
Restore now validates travelers before their defense evidence. A new regression
fails before the fix with the same corpse-admission error; afterwards it verifies
valid discovery, direct combat witnessing, full snapshot/restore, historical
evidence after witness death, rejection of new dead-witness statements and
rejection after removal of the destination visit. Core2156/2156 passes.

The final native repeat sees the exact original traveler and corpse, records the
actual visible discovery and preserves that statement through DFU FullSerializer
and an independent validated WorldStateStore restore. No enemy, corpse, witness,
combat result or statement was injected. The72-hour clock jump remains an
explicit setup condition, not an observed regional journey. A first post-fix run
did not produce the specifically required discovery within its census interval;
it was not treated as green. The probe now accepts an actual foreign traveler
report with a bounded30-second observation wait; the final run nevertheless
reproduced the original exact IDs.

Final runner Exit0, `work/traveler-corpse-20261028-native-pass.log`, SHA256
`7E60733965C91C1F59A9875506A7AD4DDE72256BAF1360F68A3E9396F44556DA`.
Runtime0warnings/0errors; Unity build/integration checks passed. Installed bundle
SHA256 `CEA178EAE6151803A4A975BD3EAA08FC27662184D070E812ABC7E46D6256094D`.
Reproduce from LivingCities:

```powershell
$env:POTIB_NAVMESH_TRIAL='1'
$env:POTIB_ROADMAP_PROBE='natural-shelter-census'
$env:POTIB_SHELTER_CENSUS_HOURS='72'
$env:POTIB_SHELTER_REQUIRE_VISITOR_REPORT='1'
& ./tools/TownTest.ps1 -OutdoorNavigationProbe -FixtureCharacter 'Hakon Ironhand' -FixtureSaveName 'People - Gothway Garden family dawn'
```

Final shelter sample:7 alternate Inns,0 remaining selected unresolved visits,
0 native guild members,0 camp/guild plans, complete Inn inventory. This is no
Guild/Camp arrival acceptance; overall completion remains17/22. The native
statement proof does not claim the subsequent full guard-contact/cleanup walk.
