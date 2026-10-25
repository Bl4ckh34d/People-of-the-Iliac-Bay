# Shared household offscreen implementation plan

> **For agentic workers:** Execute inline with executing-plans and TDD. Delegated agents are read-only Luna/max reviewers; the parent writes code.

**Goal:** Complete honest offscreen continuation and reconstruction of the existing paired family lifecycle.

**Architecture:** A separate bounded simulation checkpoint preserves the last actual movement record. Explicit store ownership transfers connect deterministic travel, surveyed transactional Inn service and native reconstruction.

**Tech Stack:** C#, existing Core store/JSON migrations, DFU/Unity native probes.

**Spec:** `docs/superpowers/specs/2026-10-24-family-offscreen-design.md`.

## Global constraints

Latest user clarification: finish the full normal family lifecycle (live joint outings, scene changes, home return and unloaded continuation), then pause for a Codex restart. Do not stop at the current build or start another general Core block before this family outcome is done.

Accepted intermediate package73 `4FF0AD65FA86` passes Core2008, Inn21, clean RuntimeCompile, review, Unity build and targeted native acceptance. It adds unloaded source capture (18 pairs/338 frames, maximum80.078ms slice) and reciprocal observer-bound outing context across all four dialogue projections through the actual runtime adapter. Actual native Talk/LLM wording is not tested. Daily/960 childcare and the existing real two-purchase/table/egress fixture pass. Full simulation service/runtime activation below remains open; current evidence is `work/household-outing-context-schema73-verification.json`. Preserve its backed-up package and all13 original saves.

User addition: active NPC pairs must carry their real partner, shared destination and current phase into every dialogue projection. Capture this observer-bound information in the immutable request snapshot, preserve it through disposition successors, invalidate changed group ownership, and never infer current company from a dated plan alone. Group membership does not include the player. Missing partner position, purchases and companion speech remain unknown without their own evidence.

- Preserve current dirty workspace, original saves and the accepted schema71 DB207 package.
- Keep actual observations separate from calculated positions; never call Observe from simulation.
- Use canonical stored routes for exact transition replay. Preserve all priority owners and source reservations.
- No receipt, purchase, seat or conversation evidence from elapsed time or a phase alone.
- Keep live joint production gated until the full lifecycle and recovery acceptance passes.

## Task 1: Deterministic separate outdoor projection

Files: create `src/PeopleOfTheIliacBay.Core/HouseholdJointSimulation.cs` and `tests/PeopleOfTheIliacBay.Tests/HouseholdJointSimulationTests.cs`; register cases in Program.cs. Make HouseholdJointMovement partial to reuse its validated immutable route geometry without using its observation path.

Interfaces: `HouseholdJointSimulation.Begin(HouseholdDayPlan, HouseholdJointProgress, double)` and `Advance(HouseholdDayPlan, HouseholdJointSimulation, double, bool, bool, bool)`. Results retain `Base`, `AdvancedThrough`, `Phase`, `First`, `Second`, `Revision` and explicit provenance. Copy returns detached state. Virtual Inn arrival stops at its exact arrival time for the service consumer; it is not the final intended lifecycle.

- [x] Write failing cases:10m/300s gives5m after150s; original physical progress unchanged. Slower600s partner limits both to2.5m after150s. Paused100s gives no later catch-up. Returning completes only after the saved300s. One large jump and small increments agree. Source/time misuse is rejected without input mutation.
- [x] Run full Core with workspace TEMP/TMP; verify those cases fail because the simulation API is absent.
- [x] Implement the pure projection and detached copies. Preserve arrival time and unconsumed service time; no physical or resource writes.
- [x] Run Core, review edge cases, and add genuine boundary regressions before corrections.

Task1 evidence: `work/household-simulation-projection-red.log` fails eight missing-API cases. Leader waiting, missed-window and equal-time cases bring the first green to1965. Read-only Luna/max review finds late Begin/equal-time recovery and stale pause at virtual Inn; both new cases fail in `household-simulation-review-red.log` and pass with the fixes in `household-simulation-review-green.log` (**1967/1967**). `household-simulation-projection-runtime.log` passes with zero warnings/errors. Source only; no new package, native acceptance or activation. The current return test starts from an actually Returning anchor; it does not prove virtual Inn service/home return.

## Task 2: Store ownership and schema72

Files: WorldState.cs, new HouseholdJointSimulationStore.cs, HouseholdJointStore.cs and targeted store tests.

Interfaces: read/begin/advance simulation with expected actual sequence, then explicit reconstruction. The store computes projected candidates itself; it never accepts an arbitrary caller-authored simulated position.

- [x] RED/GREEN actual-owner exclusion, stale sequence/source, both-member priorities, restore/replay and older-schema rejection of future simulation data.
- [x] Persist one checkpoint per household; leave actual progress unchanged while simulating. Block actual SaveHouseholdJointProgress while simulation owns it.
- [x] Reconstruct both exterior actual positions atomically, retain simulation provenance and invalidate movement caches. Reject far or unrelated bodies. Visiting explicitly needs the separate Inn proof consumer in Task3/4.

Task2 accepted in schema72 `6D302528F748…`: Core1987/1987, Inn21/21, clean RuntimeCompile, read-only Luna/max review, Unity integration and native active/consumed serializer acceptance on controlled native points. Actual Inn service/egress, daily expansion and960 live childcare samples pass in the same run. All13 original SAVE0 hashes remain unchanged; owned Unity33148/game51316 exited. Evidence: `work/household-simulation-schema72-verification.json` and associated logs. No global tick, real scene materialization or live joint producer is claimed.

Review corrections: nullable `ReconstructedAt` distinguishes consumed time0 from active; active saves require the retained canonical day fingerprint. Transient priority and missing native route proof consume held time, not movement, so resolution cannot catch up. Stale routines mark persistent recovery. Moving recovery cannot be cleared by exterior materialization; only home/return handback is supported until Task4. A completed virtual return reconstructs actual Returning and needs a subsequent observation to finish. Task3 must add the explicit virtual Inn service/return transition; elapsed time cannot grant purchase/seat evidence. Task4 must also invalidate cached native body owners before any placement or movement; blocking store writes alone is insufficient for an already-cached controller.

## Task 3: Surveyed service and remaining lifecycle

Implementation sequence (same full lifecycle scope, not alternative completion criteria):

1. **Done in source**: factor a pure `NpcInnServiceIdentity` and receipt validation from `ObservedVisit`. Preserve legacy R-format and family G15 visit IDs exactly. The family identity excludes ObservedAt, so a simulated purchase and later physical visit already have one stable payment ID. Do not add a second competing ID or manufacture an ObservedVisit for service.
2. Persist a versioned, source-bound native service catalog: exact settlement/door/block/record/climate, native keeper hash/identity, base11-item menu, service hours, verified counter paths and compatible same-table pair routes. Quote inside Core from catalog and a native calendar tariff bound to the service day. Never cache adjusted prices across dates. Native DaggerfallDateTime.FromSeconds permits the correct earlier service date after large time jumps.
3. Add a distinct per-household admission/receipt checkpoint with simulated provenance and actual wallet expected revisions. Preflight source, keeper, hours, both wallets and pair capacity before any debit. Keep one real receipt per parent; partial purchase persists, grants no shared seating and causes return without a fabricated refund. Reuse inventory transactions and the existing visit payment high-water mark. Extend cursor provenance explicitly; never write a calculated start into the physical ObservedAt field. Cross-validate admission/receipt/cursor on restore and keep older schemas strict.
4. Keep Inn payments replay-safe through the service receipt ledger. A later real ObservedVisit reuses the family payment ID and settled receipt; it still requires real physical entry/counter/seat proof for those actions. Keep the current physical settlement gate strict.
5. Advance virtual AtInn through explicitly admitted service, dwell or recorded refusal, then saved return paths. Reservation intent is not physical chair occupancy. Test complete large-jump/chunked equivalence, mixed physical/simulated payment replay and capacity/priority/service-close recovery before Task4 activation.

Initial source schema73 foundation (before the installed loaded-capture checkpoint below): `NpcInnServiceCatalog` plus schema/store copying/registration and native-date/region quote calculation pass Core2000/2000, Inn21/21, clean RuntimeCompile and read-only Luna/max review (`work/inn-service-*` logs). Those initial FullSerializer cases are Core fixtures. Seal/Quote validate integrity/calculation; native survey/calendar authority must come from trusted runtime adapters. Persist exact catalog fingerprint and tariff date in service checkpoints even though existing family payment IDs exclude catalog identity. The new catalog hash normalizes numeric zero; old visit hashes are unchanged. Admission/debit remains open.

Loaded native capture is now installed in schema73 `5ACE17AA3A14`: existing `ShopVisitGeometry` public counter/seat routes are captured cooperatively, with strict counter frontage and room-local seat poses, no actors/claims. `StreetPopulation` publishes once per room/geometry/store epoch and cancels on scene/disable/clear/restore changes. Native RED misses the API; GREEN verifies production scheduling,18 compatible pairs over29 frames, predicate/restore cancellation, unchanged wallets/observations and native reload. Existing actual shared Inn lifecycle passes again. Evidence: `work/inn-catalog-capture-*`; Core2000, Inn21, clean RuntimeCompile and read-only review. All13 original saves unchanged; owned Unity27724/game22364 exited. Failed capture is fail-closed until source binding changes; bounded retry is a resilience follow-up.

Next: reuse the geometry builder from model-only `TavernRoomSurvey` via `InnActivityLayout` for unloaded sources. Avoid registering temporary furniture in the global loaded registry across coroutine yields; pass destination climate and exact block/record, do not create NPC actors/quests. Enrich passive source with the real registered keeper and a trusted native menu snapshot, never guessed prices. Native calendar must calculate holiday for the actual service timestamp (`DaggerfallDateTime.FromSeconds`), not current player date after a large jump. Task3 capture checkbox remains open for unloaded sources. Keep all Task3/4 activation gates below until the complete lifecycle passes.

Unloaded source implementation now exists. A native owned run (`work/inn-catalog-unloaded-retry-player.log`, package0F1E8DC05CC1) passes18 compatible pairs, exact actor identities before/after every survey slice, no transient furniture in the live registry, unchanged wallets/observations/UI, cancellation and native reload, followed by the existing actual shared-Inn lifecycle. Its335-frame survey has a maximum81.956ms slice: do not claim smooth frame budgets. Profile synchronous model setup, furniture/sync, partition, layout construction and individual route calls before further optimization. A broader explicit-fixture survey has exposed stale-epoch/null-result handling in the rental cache; the new cancellation/retry fix still requires the full native regression before the unloaded checkbox is closed.

Native calendar adapter follow-up: reconstruct a new temporary `DaggerfallDateTime` from floor(serviceAt), never mutate the live calendar. Derive the classic epoch with `FromClassicDaggerfallTime(0)`; reject earlier times and the first wrapping uint classic minute. Pass the catalog destination region to `GetHolidayId`, retain exact AbsoluteDay and validate finite/range values. Native tests must cover epoch/uint boundaries, past-date quotes after a world-time skip, regional holidays, global holiday37 half-price and no player-only New Life free purchase. Also verify true native all-day hours at23:59:30: `DailyServiceHours` deliberately clamps resident schedules to1439, so service catalogs need their own correct native-hour mapping if that clamp excludes the last minute.

- [ ] Capture native keeper identity, menu/prices and service geometry in a versioned source-bound catalog.
- [ ] Separate service identity/admission from physical ObservedVisit so unloaded purchases use real wallets and replay-safe receipts without counter-contact fabrication.
- [ ] Add shared service/dwell/refusal/return simulation and cancellation/capacity recovery; test large time jumps through the full lifecycle.

## Task 4: Runtime owners and native acceptance

- [ ] Transfer ownership at actual teardown/capture boundaries and run a bounded global unloaded tick. Preserve failed-write body recovery before yielding.
- [ ] Reconstruct exterior/Inn/home bodies before individual schedules; complete scene crossings, paired social interaction and return/split.
- [ ] Read-only review, Core/RuntimeCompile, Unity package, native scene-change/save-load/full live family acceptance and child-only door test.
- [ ] Update roadmap, mindmap and memory with exact scope. Remove the live producer gate only when full acceptance proves the requested lifecycle.

Child-only native door acceptance must use a genuine source-bound shared outing after live activation, with both parents actually away and the child at home. Existing `BuildingIsUnlocked` checks and direct `TownSmokeHarness.EnterInterior` transitions do not prove locked-door activation. Target a real non-owned home without keys/quest exceptions; exercise the native Activate/raycast path and assert locked response without entry. Ordinary childcare remains unchanged; never invent absence to make this pass.
