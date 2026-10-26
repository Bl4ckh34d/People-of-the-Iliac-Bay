# Family cash waiting and reunion acceptance

The original `ProfileInEditor.ps1 -ValidateFamilyCash` fixture selected a shared Inn
outing, waits for both actual bodies to depart, then empties exactly one wallet
without changing its items. Normal population updates own admission, bank travel,
the waiting partner, payout, reunion and resumed shared travel.

The observer remains near the waiting partner. The fixture checks standing
posture and drift, both production journey contexts, a single wallet credit,
unchanged partner gold, visible body continuity and both shared route cursors
advancing after reunion. Bank service while its interior is unloaded is not
visual clerk acceptance; that is covered separately by the bank cash probe.
Inn-table waiting, two empty wallets and real evening bank closure remain open.

## Historical evening fixture controls

- The disposable SAVE82 contains 16 saved household days, none with a `JointInn`,
  and no shared progress or simulations. Advance to 05:00 the following day so
  ordinary planning can publish new outings while parents are home.
- Native `BuildingTypes.Bank` is index 3, with opening hours 08:00–15:00. The
  shared outing producer schedules departure from 16:30 onward. This focused
  test temporarily sets bank closing to 20:00, restoring it in `finally`.
  Production hours and production outing schedules are unchanged.
- Position the observer near the selected home and advance to its departure.
  Empty one wallet only after actual paired travel has begun. Do not inject
  shared movement, cash admission, service completion or conversation facts.

## Runs and limits

`family-cash-01` terminates without a candidate: requiring a naturally scheduled
shared evening outing during native bank hours is impossible. `family-cash-02`
also terminates without a candidate after the service-window correction; it
still uses the old same-day fixture with no saved shared outings. Neither run
establishes a production cash or reunion defect. The next run adds a regular
new-day preparation and explicit selection diagnostics. Physical acceptance
remains pending until its terminal result is recorded.

`family-cash-03` terminates after regular next-day preparation with
`active=0; waiting=0; serviceWindow=0; eligible=0; bankRoutes=0; banks=3`.
No active shared outing was published during the 180-second selection window;
the bank-distance filter is not the blocker in this run. Further diagnosis must
inspect shared-day admission and preparation before changing cash or movement
behavior. This is an explicitly failing acceptance probe, not a green family
feature or proof that increasing the timeout would solve it.

The fixture compiles without errors or warnings. Core 2122/2122, source policy,
70 PowerShell syntax checks and tooling protocols pass. Only the opt-in test
and its launcher are changed; the installed player bundle is unchanged.

## Subsequent proactive funding source checkpoint

The current source quotes both intended menu purchases from a real native Inn
catalog and assigns one payer. That payer can need a bank visit while still
holding some gold; the other partner does not start a duplicate withdrawal.
Morning bank admission respects native hours and estimated travel/service/return
time. After actual return, ordinary physical routine recovery still applies.
Existing gold is retained, receipt payer identity survives copying, and both
observed and unloaded purchases charge the same designated wallet. Context and
the departure event explain the planned shared Inn cost.

Core checks cover insufficient planned balances, partner funds not silently
substituting for the designated payer, exact menus, physical/projected payment,
partial-wallet top-up, early/insufficient/repeated withdrawal and reload. The
working source suite passes 2129/2129 with the concurrent street-timing checks.
This is not native acceptance of the new morning journey or a new installed mod.

Read-only evaluation of the actual SAVE82 seed and native `SharedDay` selector
shows day145809 selects homes327943,327944,328709. Each has a missing or legacy
parent itinerary. Day145811 selects four families with both itinerary sources.
The next fixture should select a naturally eligible day, retain normal bank
hours, reduce the designated payer below the combined budget before departure,
and require real admission, payment and continuation. The older emergency
fixture must not be claimed to validate that proactive behavior.

## Native morning fixture

The current `-ValidateFamilyCash` probe replaces the artificial evening bank
window with native 08:00–15:00 hours. It advances the disposable source to day
145811 at 05:00, waits for ordinary family publication, and selects a planned
shared Inn with a native budget. The clock, observer and wallets are controlled;
the peaceful variant also isolates existing local hostile enemies (see below).
Both wallets are emptied after the real payer is chosen; the test
requires that payer alone to obtain the shared budget before departure without
holding the partner. It then checks actual subsequent paired departure. Inn
payment and an observed bank interior remain separate acceptance scopes.

`family-morning-01` fails before cash admission with zero active shared plans,
despite prepared personal routes for the selected-day parents. Native bank hours
are intact. This is a family-publication prerequisite failure, not evidence of
a cash-trip failure. The next diagnostic run records candidate validity and
parent readiness instead of extending the timeout or injecting a shared plan.

### Pair-route admission investigation

`family-morning-02` and `03` also terminate without a published shared day. The
408 homes are visited two per half-second, so a complete admission cycle takes
at least 102 seconds in addition to initial route preparation. The fixture now
allows 240 seconds for preparation and records the exact admission stage.

For home 327940, six native Inn catalogs and six routes are available but zero
pair lanes pass. The old pair-lane checker uses the grid/physics corridor path,
including its 24 m segment limit, although ordinary routes use native navigation.
The first native ray-only variants (`04`, `05`) also reject all examined routes;
those owned runs were intentionally stopped after rejection evidence, not
accepted as completed tests.

`family-morning-06` identifies a second essential distinction: rejected segments
have sampled native endpoints and **complete native paths**, despite a blocked
NavMesh ray. Examples are a 4.338 m segment with two native corners and a 1.225 m
segment with three corners. Horizontal sampling shift is zero; sampled heights
differ from terrain by approximately 0.010–0.030 m. Baked city modules are connected
by native links, which a direct NavMesh ray does not establish as traversable.
This diagnostic run was also stopped deliberately once those results were known.

The current repair uses native surface samples and accepts a blocked ray only
when a complete native path remains within the pair movement's existing 0.4 m
corridor. Offset interior points may move at most 0.05 m horizontally onto the
surface; actual doorway anchors retain the ordinary native route's 0.4 m sampling
tolerance. Terrain/baked-surface height differences do not loosen horizontal
limits. Grid-only scenes retain their original clearance checks.

`family-morning-07`, `08` and `09` publish the ordinary shared outing for home
327940 to Inn 263176. Home 328198 passes five of six pair lanes but its later
departure and dwell do not fit the haunted-town return deadline. This establishes
the route-admission correction, not acceptance of the entire physical pair trip.

All three runs then start the designated payer's native morning bank errand with
both wallets empty and a combined 4 g budget. The errand is interrupted before
service. Run `09` identifies the exact cause: the action overlay supplies
`danger=True`, and `ApplyNpcCashVisit` correctly executes its priority-cancellation
branch. The earlier `alarm=False` log used integer game time, whereas the overlay
uses fractional game time. An incident created in that fraction can be missed
by the integer query, so that log does not establish absence of danger. The
source and timestamp of the threat need identification before changing either
the fixture or the production priority rule. These are failing cash acceptance
tests, not completed bank visits.

`family-morning-10` confirms a newly observed native hostile Burglar, LoadID
33555292, at (514.9, 320.6, 171.8). The incident starts at the same fractional
game time as the cancellation, explaining the earlier integer-time discrepancy.
Production danger cancellation stays intact. The next peaceful variant disables
existing hostile enemies only after verifying their ownership in the currently
observed town. An enemy with missing or different scene ownership fails setup.
This explicit fixture isolation does not weaken the production threat response
or constitute acceptance of bank travel during danger.

Run `11` still observes a hostile Burglar appearing only after the observer/clock
change; the initial hostile-enemy enumeration is empty. The peaceful fixture now
also uses DFU's `PreventEnemySpawns` for its clock jumps and disables pending
native `FoeSpawner` components before the bank scenario. These fixture controls
are explicit; no production reaction or encounter setting is changed.

### Peaceful native acceptance

`family-morning-12` completes with `FAMILY_CASH_PASS` and a terminal `complete`
receipt. Native bank hours remain 08:00–15:00. Both wallets start empty; only the
designated payer collects money. The native allowance pays 489 g once at about
08:32, covering the combined 4 g Inn budget. The partner receives no duplicate
cash visit, forced waiting state or wallet mutation. The payer's dialogue context
includes collecting money for the planned visit with the partner.

Visible travel totals 38.624 m; maximum sampled step is 0.500 m. The payer returns
within 0.589 m of the home door at about 08:33. After advancing to the real outing
departure, both actual bodies are active and have travelled 4.184 m and 4.338 m
on the shared outbound journey. This accepts proactive cash, service, one payout,
physical home return and subsequent paired departure in this peaceful fixture.
The bank interior is simulated while unobserved. Final Inn arrival/payment,
partner waiting during an already-started outing, danger recovery and ordinary
continuous-day behavior remain separate checks.

## Full funded outing acceptance plan

The optional `-ValidateFamilyCash -ValidateFamilyInn` continuation must use that
same published household day and the actual morning wallet. Observe both bodies
on the full exterior approach, enter their real Inn, require two current native
purchase receipts charged to the planned payer and matching both wallet deltas,
then observe both actual seats. Advance only the seated dwell; require physical
exit and the observed homeward leg with no repeated payment or body jumps.
The dedicated process limit is 1200 seconds to cover native preparation plus both
walked legs. Existing morning-only acceptance remains separately runnable.
Results of this new continuation are not yet accepted.

`funded-family-inn-01` was intentionally stopped during setup to strengthen the
acceptance boundary before collecting results: both purchase cursors must come
from physical service, not `world-time-projection`, and both bodies must remain
seated without moving more than 0.1 m for five seconds before the dwell skip.
It is not a completed run. `funded-family-inn-02` uses those stronger checks.

### Full approach failure and scoped pair clearance

`funded-family-inn-02` repeats the morning cash acceptance, then both visible
bodies walk more than 300 m. The pair remains `Outbound`: the second cursor
stops at 308.752 m, while the first oscillates around 308.25–308.33 m. Its actual
sampled movement reaches 519.139 m without reaching the shared endpoint. The
300-second leg ends with an exception; Inn service is not accepted.

Code inspection finds inconsistent application of the existing frame-scoped
walking-partner exception. Ordinary crowd steering excludes the partner, but
`TrySeparate` explicitly adds it back. The native overlap query also ignores
`Physics.IgnoreCollision` and treats the permitted partner as occupied space.
The repair uses the same scoped partner exception in overlap recovery and the
native query. Other residents, the player and action doors retain their existing
collision checks. Native NavMesh avoidance remains enabled. `funded-family-inn-03`
repeats the full approach and continuation to test that correction.

The red run separately logs `Lodging layout change requires reconciliation` from
`ResidentTripPlanner.PrepareLodgedTrip` via `NativeTavernRentals.RegisterInventory`.
This is a distinct live travel-planning failure requiring investigation; it must
not be hidden by a successful family completion marker.

`funded-family-inn-03` verifies the repaired exterior endpoint: both parents
reach `Visiting`, with sampled travel of 308.418 m and 358.375 m and a maximum
sampled step of 0.500 m. This accepts the doorway convergence repair, not the
entire outing. After entering Inn 263176, the continuation fails its 180-second
payment/seating check. No Inn reconstruction marker appears. The separate
lodging-layout error also remains. `funded-family-inn-04` adds test-only,
20-second diagnostics for cash ownership, simulation/admission, actual bodies,
projected seats and the room motion owner to distinguish the indoor failure.

`funded-family-inn-04` again completes the exterior leg (309.153/340.766 m,
maximum sampled step 0.500 m), then fails the same indoor requirement. Diagnostics
show no cash deficit, both parents available, an active simulation but no service
admission or projected bodies. The simulation proceeds to `Returning` while its
physical anchor remains `Visiting`. The target Inn 263176 also logs the rental
conflict: the copied SAVE82 mod state contains five rooms (319, 338, 343, 347,
402), while the current native inventory reports three rooms and occupant room 9.
The saved Inn service catalog therefore cannot be treated as evidence of the
current furniture layout. No migration of this disposable state is attempted.

`funded-family-inn-05-fresh` uses the new `-FreshModState` launcher option to
archive only the copied mod state and regenerate it. The native character and
source save remain intact. Its newly generated population seed makes this a
fresh integration scenario, not an identical-population comparison. Additional
fixture diagnostics count saved seat pairs matching and available in the live
room, and report the planned visit deadline. Exterior acceptance now also fails
if a previously observed body disappears before completing its leg.

The fresh `05` run fails setup: no eligible shared outing is published within
240 seconds. It does not reach the bank/Inn acceptance and is not a gameplay
pass. It also reports a different current catalog rejection in Inn 66574:
`Inn catalog routes do not connect the real entry, counter and selected seats`.
The source seed is now retained for fresh-state runs through a profile-only
environment override, avoiding an unrelated random population change. The
native family fixture logs that seed. `06-fresh` repeats with this corrected
reproducibility boundary; old rooms, catalogs and history are still regenerated.

`06-fresh` confirms seed 2047674263 and naturally selects household 328712,
Inn 131841. Morning funding passes: one 421 g payout, 134.963 m visible travel,
maximum step 0.436 m and home distance 0.592 m. Both parents then complete the
observed exterior leg (298.669/302.965 m; maximum step 0.500 m) without disappearing.
The live room reconstructs both bodies. All three catalog pairs match live seat
IDs; the visit still has more than 4,800 game seconds remaining. The shared room
motion starts but immediately rejects its second physical checkpoint:
`Shared Inn actual checkpoint route is unavailable: first=True/Walking; second=False/Walking`.
It then latches recovery, and the 180-second payment/seating check fails. This is
a current physical-route failure, distinct from the old fixture's room mismatch.
No purchase, stable seating or return acceptance is claimed.

`07-capture` uses the frozen deployed `06` source plus only checkpoint diagnostics,
not the parallel roadmap changes. It fails the 240-second setup window before
an eligible joint plan is published. Both parents at home 328712 have prepared
routes, but the household admission pass has not returned to them. The fixture
now budgets one additional complete pass for a fresh state: two homes per
half-second, plus five seconds. Production scheduling is unchanged. That revised
setup allowance and the detailed checkpoint diagnostics still require a repeat.

### Restored chair coordinates lose their semantic destination

`08-capture` passes fresh setup with the retained seed. The morning trip pays
421 g once and walks 135.279 m home (maximum step 0.417 m, home distance 0.552 m).
Both actual bodies then reach the Inn after 298.074/302.356 m; maximum step 0.500 m.
The indoor failure is now localized to the second member's saved connection leg 1:

- Target `(0.2249527, 0.00009918213, 8.675049)`.
- Live chair ground `(0.224945068, 0.0000915527344, 8.675049)`.
- Both identify chair `inn:131841:41105:225,275,8275/0`.

`ActivityRouteBetween` previously identified destination chairs only by an exact
Vector3 dictionary key. The restored world/local float rounding loses that lookup,
so ordinary geometry tries to route into the chair rather than through its known
approach. No general movement tolerance or furniture collision relaxation is needed.

Physical visit capture and movement now pass their existing assigned seat ID.
The route resolves that exact live chair and its approach; missing or excluded
chair IDs reject. Unassigned public destinations retain ordinary geometry checks.
RuntimeCompile passes with zero warnings/errors. Native repeat `09-seat-identity`
is pending; this is not yet a full Inn acceptance.

The repeat also checks perturbed local chair coordinates, canonical live endpoints
and rejection of unknown chair IDs. After actual purchases it verifies exact
participant memories, receipt facts and stable expiry through the five-second
seated hold. These checks read bounded existing indexes, not per-frame world snapshots.

`09-seat-identity` naturally selects household 327940 and Inn 197127 instead of
the earlier pair; this is additional-room coverage, not an identical-scenario
comparison. The morning trip pays 489 g once and returns home. Both exterior
bodies complete their approach (163.781/161.292 m; maximum step 0.500 m).
Three native chairs pass the perturbed-coordinate and unknown-ID checks. Both
parents reach `Haggling` without the former checkpoint recovery error. Payment
still never occurs, so the 180-second payment/seating requirement fails. Neither
the memory assertions nor full return are accepted yet.

### Pending reconstruction has an instantaneous counter stage

The pending-service reconstruction in `HouseholdInnReconstruction` intentionally
creates a counter leg with zero dwell. On physical arrival the runtime captures
a one-point Haggling leg, then calls the payment callback. `ObservedVisit.Evaluate`
correctly skips zero-duration intervals, so `AtInnCounter` previously saw the next
walking/table phase and rejected both payment and a no-purchase receipt. The
runtime kept waiting for payment before advancing: a stable deadlock.

Two new failing Core cases reproduce payment and refusal at that exact observed
arrival; a third covers earlier/later time, remaining travel and table stage.
`AtInnCounter` now additionally accepts only a first, one-point Haggling leg with
zero dwell at exactly `ObservedAt`. All household, keeper, wallet, identity and
replay checks remain. No artificial dwell or global time-projection change.
RED: 2145/2147; GREEN: 2147/2147. Existing Inn-consumption regressions and
RuntimeCompile pass. Native `10-purchase` retains the pre-fix payment code to
check the diagnostic prediction; native acceptance of this fix remains pending.

`10-purchase` confirms the exact prediction for both actual bodies at Inn 197127:
`visit=Haggling`, `savedPhase=Walking`, `routePoints=1`, `dwell=0`,
`canSettle=True`, and `purchaseFailure=purchase could not be recorded`.
The diagnostic editor was deliberately stopped after repeated identical evidence;
it is not a completed acceptance run. `11-zero-dwell` runs the corrected source
with the complete payment, memory, seating, exit and homeward checks.

`11-zero-dwell` verifies the same household 327940 / Inn 197127 as runs 09–10.
The morning cash trip pays 489 g once and returns home; both parents physically
reach the Inn. Both purchases succeed for a total of 4 g, both parents remain
seated for the five-second observed hold, and five entitled receipt-memory
perspectives pass exact count, known participant, factual and stable-expiry checks.
All three native chair identity checks pass. Both parents physically leave the Inn.

The return test is not accepted: it times out after about 100.09/98.87 m.
Direct inspection of the Unity game view during the stall shows a native quest
letter popup addressed to the disposable test character. This pauses gameplay;
the stationary return counters are not evidence of an NPC navigation defect.
The Editor then terminates with the fixture's exterior-leg timeout.

For repeat `12-quest-isolated`, the owned family-Inn fixture temporarily disables
native QuestMachine ticks, restoring its previous enabled state in `finally`.
This isolates unrelated scheduled quest popups during its time skips without
changing production movement or quest behavior. Exterior diagnostics now include
pause state and current UI window. RuntimeCompile has zero warnings/errors and
the current Core run passes 2147/2147. Full return acceptance remains pending.

`12-quest-isolated` returns to the original household 328712 / Inn 131841.
The cash trip pays 421 g once, walks 138.055 m and reaches 0.570 m from home;
the pair then walks 298.483/304.127 m to the Inn. All three native chair checks
pass and both actual purchases complete. One parent sits; the other never
reaches its chair before the 180-second limit. This is not full acceptance.

Existing `ROOM_ROUTE_STALL` diagnostics identify the controlled test observer,
`PlayerAdvanced`, directly on the blocked waypoint: player ground near
`(279.775, 319.760, 405.151)`, target `(279.775, 319.660, 404.951)`.
The chosen free floor point was in the chair access aisle. The fixture had checked
furniture and current bodies, but not future visit routes. The Unity window shows
normal gameplay without a popup, so this is distinct from run 11's paused quest.

Repeat `13-clear-observer` adds a 1.2 m clearance from both parents' remaining
visit segments when selecting the existing observer floor anchor. It fails the
fixture explicitly if no clear anchor exists. It changes neither player collision
nor NPC navigation. RuntimeCompile passes. The run reuses run 12's deployed
production source with only this fixture file refreshed; concurrent agent changes
are deliberately excluded from this causal repeat. Complete outing still pending.

`13-clear-observer` selects the original household 328712 / Inn 131841 again.
Both bodies walk 299.689/302.790 m to the Inn (maximum step 0.500 m). The new
observer anchor is `(284.1753, 319.684479, 403.550659)`, clear of both visit routes.
All three chair checks, both purchases for 4 g total, stable seating and five
receipt-memory perspectives now pass in this layout too. The subsequent physical
departure does not finish within 120 seconds after skipping the seated dwell.
No full exit/return acceptance is claimed. Unlike the earlier obstruction, there
is no post-seating room-route stall diagnostic identifying the player as blocker.

`14-departure` uses the installed 15:27 source and selects this same naturally
planned household via `POTIB_EDITOR_FAMILY_HOME_KEY=328712`. This optional fixture
input filters existing plans; it creates no family or synthetic trip. Diagnostics
now report both bodies, selected chair/approach, visit phase, movement failure,
clock and pause state during departure, not only before seating. This run failed
before the trip: that household had no admitted natural outing in this fresh
fixture. Another household (327940) had a prepared plan. This result does not
diagnose the original Inn departure. A subsequent run must use a currently
eligible natural family rather than fabricate the requested household's trip.
