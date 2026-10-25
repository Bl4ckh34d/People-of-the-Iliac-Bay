# Dated family activity implementation plan

**Goal:** Coordinate actual parents for one game day, including occasional jointly approved Inn outings, without repeating them every day or changing observed history.

**Architecture:** Persist one bounded latest-day plan per household generation. Keep ordinary route geometry in the existing routine cache; dated selections store original outing indexes and new departure times. Preserve their global visit indexes. A joint Inn outing owns both real itineraries and reserves visit index 16384, outside ordinary and player-appointment identities. The two participants share one immutable plan and are published together. A stale source fingerprint prevents activation rather than guessing new routes.

**Spec:** Existing family requirements in `2026-10-24-daily-activity-coverage.md`, including the explicitly authorized roughly ten-percent child-alone exception and locked home.

## Constraints

- Actual reciprocal partners and parent/child IDs determine family relationships.
- Ordinary optional absences alternate where feasible; essential work, appointments, health and danger keep their priority.
- Household/day/seed/generation determine shared-leisure selection. Reload and retry cannot reroll it.
- Past outings remain unchanged; an active original outing blocks publication of a new dated plan.
- All activity uses real route evidence and native service windows. Both wallets must pay before joint seating.
- Old saves get no invented joint history. Source-only milestones do not imply native activation.

## Delivery

- [x] Add tested dated plan composition: retain original visit indexes, shift feasible optional outings, preserve prior history, reject active rewrites, support one admitted evening joint event. `DailySchedule.ForDay` intrinsically returns to the base schedule on neighboring dates; timed metadata also handles expired recovery.
- [x] Add schema-66 detached persistence, immutable same-day retry, atomic pair publication, stale-source refusal and one latest record per household generation. Verify old schema migration and malformed future-state rejection. Storage is bounded to one latest record per settlement/home, including generation in the immutable plan identity.
- [ ] Connect native day preparation/publication with both home gates, appointments, observed visits, health/danger interruption and source revision invalidation. Complete route/recovery and dialogue consumers before activating shared activity.
  - [x] Core paired appointment composition: exact dated selections and original visit identities; required travel from real base/joint route graph; full other-parent return-tail coverage; building and point venues; paired joint suppression; typed physical-recovery-required result at/after departure.
  - [x] Schema67 monotonic joint suppression with routes retained; old66 false compatibility and true future-field rejection; no active/completed history erasure. Controlled native FullSerializer and required-meeting projection pass.
- [ ] Add physical group progress/waiting, both real purchases and paired table lease, actual attendance and split/return. Native child-only door and two-participant acceptance remain required.
- [ ] Review, Core/RuntimeCompile, build/install, native checks; update roadmap, mindmap and memory from evidence.

## Verified checkpoint

Schema66 package `7C1D3521554BB854BB8E1D18D5770300DC1146AFE4E5C04ED515BDA60A20CAEA` is built and installed. Core1839/1839 and RuntimeCompile pass. Native daily expansion again passes three adults / eight later visits. Controlled native serialization of an actual two-parent household196864 and actual saved Inn131078 route preserves both full plans and joint route/timing, rejects next-day replay, and checks the child-only Core lock. Owned test process48724 exited. Evidence is in `work/household-day-schema66-{package,native,player}.log` and [TESTING](../../TESTING.md). The native fixture is detached; it does not activate a live family event, buy anything, move actors or test the native door.

Review reproduced and fixed stale idempotent success, delayed publication that could invent active/completed trips, and recovery metadata that outlived its day. Do not activate the feature merely because this checkpoint passes.

## Remaining activation ports

The native producer/cache must publish both parents together only at a shared safe home boundary, recheck appointments/observed travel/trips/health/alarms, and key its cache by date, household revision and both source routines. Appointment composition must consume dated selections without shifting original visit identities or replacing required player meetings. Time-sensitive runtime metadata callers must pass their absolute query time.

Arrival continuity currently resolves only recurring routine routes. Recovery graphs also know only the recurring routine; `RoutineRecoveryLeg.OriginalStopIndex` is bounded below32 and cannot silently accept joint index16384. Add explicit event provenance and real event return geometry before interruption can use that route. Preserve safe physical return for the unaffected partner when the other cancels. `completed` status alone is not proof of attendance, conversation, purchase or memory.

Schema67 checkpoint supersedes the hash above: `E88176A94C920F84AF2AF465E286A5F61AFE357A6D204A99100DDE689E245597`, Core1852/1852, clean RuntimeCompile, Unity integration and native daily/controlled household-appointment acceptance. Logs: `work/household-appointments-{final-core,final-runtime,schema67-package,schema67-native,schema67-player}.log`. The game process57136 exited. Native activation is still deliberately pending.

`HouseholdDayAppointments` returns an ephemeral paired projection; its filtered ordinary selections are not permission to overwrite an immutable published day. Before activating a projected future joint cancellation, persist the monotonic suppression through `SuppressHouseholdJoint`, then refresh both participants' cache together. Once either departure has begun, use the typed `PhysicalRecoveryRequired` path and actual return geometry. Do not clear the date overlay and fall back to recurring positions. Bind future recovery to the household plan identity/fingerprint and both parents' appointment sources; the other parent's new meeting can change childcare availability.

## Recovery implementation in progress

- [x] Schema68 adds optional household identity/content fingerprint and the other parent's appointment fingerprint to a one-day recovery. Legacy recovery lookup cannot activate these records or an unbound recovery on a current family day.
- [x] Joint recovery legs use an explicit family marker, ordinary index -1 and no appointment ID. Their schedule projection retains joint visit index16384. Ordinary skipped indexes remain bounded below32.
- [x] The planner validates both real parent/routine sources, uses exact dated visit times, and adds only the current member's saved joint route. Suppression retains route facts for a real return from the Inn.
- [x] Required duties of either parent suppress future shared leisure; required full absences of the other parent also gate ordinary childcare-sensitive outings. Required projections use the measured time rate and each parent's stable pace; a parent without outings/duties needs no invented route. Actual pair movement/interruptions still require native coordination.
- [x] Save/reload rejects future fields in older schemas; stale household sources remain inert. Save requires a current published household source and both current booking fingerprints, including the partner's exact-point venue. Original leg identities, actual booking identities/times and explicit joint disposition are checked in addition to route geometry. Recovery cannot predate its family source.
- [x] Verify Core and runtime, review, then extend the detached native FullSerializer fixture and build/install. This is a dependency for live activation, not acceptance of physical family behavior.

Installed schema68 verification: Core1872/1872 (`work/household-recovery-final-core.log`), RuntimeCompile zero warnings/errors (`work/household-recovery-final-runtime.log`). Read-only Luna/Max review found and helped reproduce omitted joint disposition, fake appointment binding, legacy override, and the empty-routine partner projection; all have regressions. RED evidence also covers missed partner duties, exact point changes and measured return tails. Unity build/integration and native FullSerializer acceptance passed; owned processes24548/46368 exited. Installed/dist SHA256 `89E2FE95DDF479140A87910C344A547A979FE0ABEC79AEE44985B12B44054C88`. Logs `work/household-recovery-schema68-{package,native,player}.log`. The detached native return fixture explicitly reports physicalFamily=false/nativeDoorInteraction=false; paired native activation remains the next integration task.

## Native activation implementation

First connect ordinary parental childcare days to the real producer, paired cache and actor state consumer. Keep joint leisure unactivated until shared motion, purchases and seating have their own executor. This is a staged implementation of the same family system, not replacement of the shared-leisure requirement.

- [x] Add owned route projection with exact selected departures and original/joint visit offsets; test omission, source changes and ownership isolation.
- [x] Add a transient routine-source revision on successful routine mutation/restore, with failure leaving the revision unchanged.
- [x] Prove missing runtime activation with a native failing regression: actual live publication, both GetState projections, changed behavior relative to overlapping baseline routes and childcare through the remaining day. No fixture publication or clock change is permitted in this probe.
- [x] Hook bounded safe-home production and atomic paired cache refresh outside per-frame getters. Require both individual source routes prepared, full stored source validation, actual parent/child links, no current higher-priority owner, direct saved-alarm check and actual observed return. Do not wait forever for unrelated citizens' pending migration.
- [x] Port timed metadata and observed-arrival route lookup to selected family routes; create/read typed recovery with both parents' real point proofs. Preserve active paired projections when refresh needs physical recovery; do not revert to a recurring position. Native physical motion/interruptions remain in the next acceptance task.
- [ ] Verify load/day/source lifecycle and native production. Emergency/care/active action interruption and shared walking/purchases/seating still need full paired physical acceptance before the complete family feature is done.

The native adapter must use each full private Plan.Routine (validated against stored full sources), not GetResidentDialogueRoutine: that projection intentionally omits redundant route data and does not carry the full source fingerprint.

Installed live activation checkpoint: Core1880/1880, clean RuntimeCompile, Unity integration and next-morning native960-sample acceptance pass. Package SHA256 `10EFF6518C290CB1FC851B363E2165EAC33C48C9254EDFAB543E6574A60ADF53`. The original12:05 fixture cannot activate a new plan while either parent is away; retain this failed attempt. Separate SAVE80 advances only its copied initial clock to the next05:00, while all13 original SAVE0 file hashes remain unchanged. Native probe itself is read-only. Evidence and remaining physical scope are in [TESTING](../../TESTING.md). Read-only Luna/Max review found no remaining concrete regression in the corrected pending/source-ownership/interval-availability boundaries.

## Next physical group executor

The schema69 [shared movement checkpoint](2026-10-24-family-joint-motion.md) now implements and native-serializes the paired controller/progress store (Core1917). It remains a foundation; the executor tasks below are not yet native physical acceptance.

Read-only Luna/Max source review identified the following integration order. These are implementation tasks, not native acceptance claims.

- [ ] Add one phase/progress owner per exact household/day/joint event. Preserve two actual route cursors and their shared phase across scene/unload; do not reuse single-resident `ObservedVisit` as a shared progress clock.
- [ ] Tick the pair before normal outdoor placement and mark both IDs handled. Move along each member's saved route using actual actor positions and bounded `CrowdMotion` steps. Limit the faster member's lead; hold both for dialogue, actual movement stalls or missing participants. Respect the later actual departure and wait for both real Inn arrivals.
- [ ] Define the pair-aware exterior-door/interior handoff before enabling the existing unsuppressed-JointInn activation gate. A door collider being ignored by motion is not an executed entry, and scene reconstruction must not split the pair back into independent world-time placement.
- [ ] Select compatible real seats through `FurnitureSeatCatalog.Slots` and `TryAcquirePair`; ensure both real purchases succeed before either sits. Release both leases on cancellation/revision/unload. Exclude pair-managed seats from ambient `FaceInnPartner`, whose standing placement currently ends sitting.
- [ ] Native acceptance must observe both real parents, pauses/stalls/resume without route jumps, actual common Inn entry, both purchase receipts, one compatible table pair, reload/cancellation and split/return. Include actual child-only household door interaction. Existing960-sample acceptance remains the schedule milestone only.
