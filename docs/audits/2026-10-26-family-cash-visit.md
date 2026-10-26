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
