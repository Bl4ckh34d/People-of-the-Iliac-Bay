# Native routine distribution and closed-shop groups

## Method and correction

Owned Unity fixtures use the disposable SAVE82 copy and the current Daggerfall settlement. `destinations-morning-01` incorrectly reported zero visits: `DailySchedule.ScheduledVisits(now)` selects the applicable date but returns visit times in seconds since midnight. The diagnostic compared those times against absolute calendar seconds. `destinations-morning-02` corrects that comparison and rejects an empty audit instead of silently treating it as evidence of no activity.

The histogram reads ordinary schedules before discretionary closed-shop diversions. It counts planned visits, not unique visitors, observed arrivals, simultaneous occupancy, work attendance or emergency errands. The available-building column counts surveyed entrances, not a guarantee that each destination is suitable for every resident. No selection weights were changed.

## Snapshot at approximately 09:10

286 living residents with schedules contribute 1,074 planned visits:

| Native building type | Surveyed entrances | Planned visits |
| --- | ---: | ---: |
| Alchemist | 3 | 79 |
| Armorer | 4 | 93 |
| Bookseller | 4 | 77 |
| Clothing store | 1 | 30 |
| Gem store | 3 | 100 |
| General store | 8 | 216 |
| Guild hall | 4 | 66 |
| Pawn shop | 4 | 69 |
| Tavern | 19 | 137 |
| Temple | 1 | 172 |
| Weaponsmith | 1 | 35 |

The only clothing store, building `263170`, receives 30 planned visits (2.79% of all visits). Hour buckets 10 through 15 contain 12, 3, 4, 3, 6 and 2 visits respectively. The observed pre-opening fixture contains four waiting residents with four diversions, two grouped participants, one Inn diversion and four distinct return times. This does not support a town-wide preference for the clothing store; it does show a local pre-opening cluster and a peak during its opening hour.

The single temple receives 172 planned visits, including 56 in the 09:00 hour and 41 in the 10:00 hour. Taverns receive 30 visits in the 16:00 hour and 107 in the 17:00 hour. These are stronger candidates for further concentration checks. The current planner explicitly reserves a candidate per service class and limits ordinary social visits to the evening. Those mechanisms may contribute, but this snapshot alone does not prove why each saved itinerary was selected. Measure physical arrival/occupancy and fresh plans before changing selection rules.

Raw type, individual-building and hour records are in `work/playtest-2026-10-26/editor/destinations-morning-02/editor.log`, prefixed `PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_`.

The run completes: all four tracked residents retain their individual diversion after 10:00, and none retains a not-yet-due return at 10:45. This checks waiting-state deadlines, not physical entry into the clothing store. The editor still emits the known scene-cleanup warning at shutdown; it is not resolved by these checks.

## Physical group acceptance

`leisure-physical-03` passes the controlled three-resident fixture after the existing entrance-survey readiness and ground-height correction. Actual native bodies retain a minimum separation of 1.247074 m, with minimum partner-facing dot product 0.9999999 while the player is nearby. Context names the actual partners and a released middle slot can be reused.

This fixture initially places bodies in their assigned group slots. It verifies occupied group spacing, facing and context, not free approach, a full autonomous encounter, the Inn round trip or behavior under every interruption. Those remain on the active worklist.

The extended `leisure-approach-02` fixture starts all three bodies at surveyed points at least 2 m outside their slots. Production `PlaceClosedShopWait` and native navigation move them 2.093 m, 2.472 m and 2.400 m, with a maximum frame step of 0.092887 m. All arrive, keep a minimum 1.247063 m separation and face partners (minimum dot product 1). An expired return releases the waiting movement adapter and removes that participant from the other speaker's current conversation context. This is not a full scheduler-driven departure or shop entry. The first run failed before movement because its selected group lacked three clear approach corridors; the fixture now searches surveyed entrances for a valid setup without relaxing production collision or navigation rules.

The additional animation assertion in `leisure-approach-03` was invalid: native `IsIdle` is deliberately false for `DirectionalStandingPose`, which holds a directional walking record with `animSpeed=0` or displays dedicated standing artwork. That failure does not prove running in place. The corrected assertion checks `ResidentActor.IsStandingStill` and requires zero native animation speed when a directional record is held.

`leisure-approach-04` passes the complete extended fixture, including that animation check: body travel 2.096 m, 2.443 m and 2.400 m; maximum step 0.085675 m; minimum separation 1.247063 m; partner-facing dot product 1; stopped animation, slot reuse, real partner context and expired-wait release all pass. Runtime compilation has no warnings/errors. No production behavior changed for this acceptance extension.

Runtime compilation passes without warnings or errors. The installed Player bundle is unchanged; this checkpoint changes an opt-in diagnostic and documents previously unverified behavior.
