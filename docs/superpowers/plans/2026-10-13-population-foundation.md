# Population Foundation Implementation Plan

> **For agentic workers:** Use the executing-plans skill to implement this plan task by task in the current session. Code review uses the requesting-code-review skill. Steps use checkboxes for tracking.

**Goal:** Build a deterministic, testable population core and load it as a standard DFU mod with persisted state and observable integration diagnostics.

**Architecture:** Keep identities, household allocation, paths, schedules, and save validation in a Unity-independent C# core. The DFU adapter reads layout and time, owns mod lifecycle and persistence, and initially reports simulated state without displacing quest-bound vanilla actors. Actor replacement is the next independently tested slice, not an implied consequence of loading this foundation.

**Tech Stack:** C# 7.3, .NET Standard 2.0, .NET 8 console behavior tests, DFU 1.1.1, Unity 2019.4.40f1, DFU source asset bundles.

**Spec:** `../specs/2026-10-13-living-cities-design.md`, approved by the user on 13 October 2026.

## Global constraints

- Standard DFU mod; preserve quests and saves. Engine changes require a demonstrated limitation.
- Maximum three permanent residents per household. Identity and appearance may not depend on enumeration or frame order.
- NPC records survive scene changes. Save schemas are versioned and invalid input may not partially replace live state.
- No Unity APIs or process-global random state in the simulation core.
- C# 7.3 and .NET Standard 2.0 compatibility. No inference, networking, or expensive work in frame loops.
- Deaths persist; protected actors cannot be killed through the civilian death API. Recruitment is a later bounded subsystem.
- Do not claim physical actor integration from a simulation-only milestone.

## Task 1 Household identities

**Files:** `src/LivingCities.Core/Population.cs`, `StableSeed.cs`, `tests/LivingCities.Tests/PopulationTests.cs`, project files and `Program.cs`.

**Interfaces:** `Population.Create(uint seed, int settlementId, IEnumerable<int> homeKeys)` returns `SettlementPopulation`; records contain household keys, resident IDs, appearance seeds, and alive/protected state. `Population.MarkDead(SettlementPopulation, string)` updates only the matching non-protected resident.

- [x] Write behavior tests using a reversed building order, distinct settlements, duplicate building keys, and an empty layout. Mutation caught: allocation from loop index or process-global Random changes identities when input order changes.

```csharp
var first = Population.Create(42, 100, new[] { 20, 10 });
var second = Population.Create(42, 100, new[] { 10, 20 });
Check.Equal(Signature(first), Signature(second));
Check.True(first.Households.All(h => h.Residents.Count >= 1 && h.Residents.Count <= 3));
```

- [x] Run `dotnet run --project tests/LivingCities.Tests`; see explicit failing behavior results with the unimplemented contract.
- [x] Implement sorted unique home allocation with stable integer mixing and culture-independent IDs. Reject duplicate homes rather than silently creating duplicate residents.
- [x] Run all tests; add death idempotency and protected-person tests. Commit verified task.

## Task 2 Deterministic street routing

**Files:** `src/LivingCities.Core/GridPathfinder.cs`, `tests/LivingCities.Tests/PathTests.cs`.

**Interfaces:** `IWalkGrid.Width`, `Height`, and `Cost(int x, int z)`; zero means blocked. `GridPathfinder.Find(IWalkGrid, GridCell, GridCell, int)` returns a list of cells or an empty list for unreachable routes or exhausted search budget.

- [x] Write tests for an obstacle detour, an unreachable destination, costly shortcuts, start=end, out-of-bounds inputs, and deterministic tie-breaking.

```csharp
var path = GridPathfinder.Find(new TestGrid("111", "001", "111"),
    new GridCell(0, 0), new GridCell(0, 2), 100);
Check.Equal(7, path.Count);
Check.Equal(new GridCell(0, 2), path[path.Count - 1]);
```

- [x] Run and observe failures. Implement deterministic A* with bounded work, positive costs, a Manhattan lower bound, and stable ties.
- [x] Run tests and verify no route crosses a blocked cell. Commit verified task.

## Task 3 Continuous schedules

**Files:** `src/LivingCities.Core/DailySchedule.cs`, `tests/LivingCities.Tests/ScheduleTests.cs`.

**Interfaces:** `DailySchedule.Workday(homeKey, workKey, route, openMinute, closeMinute, commuteMinutes, wakeMinute, sleepMinute)`; `Evaluate(long absoluteMinute)` returns activity, interior building key or exterior route position, and normalized route progress. `DailySchedule.HomeDay(homeKey, wakeMinute, sleepMinute)` handles residents without a valid commute. Routes consist of immutable copied points, and reverse trips follow the same route backward.

- [x] Write boundary tests for sleep, outbound travel, arrival exactly at opening, departure exactly at closing, return, midnight, repeated days, and time jumps. Reject overlapping or impossible schedule windows and negative time.

```csharp
var plan = DailySchedule.Workday(10, 20,
    new[] { new RoutePoint(0, 0), new RoutePoint(100, 0) }, 480, 1020, 20, 360, 1320);
Check.Equal(Activity.Travel, plan.Evaluate(470).Activity);
Check.Near(50, plan.Evaluate(470).Position.X);
Check.Equal(20, plan.Evaluate(480).BuildingKey);
Check.Equal(Activity.Sleep, plan.Evaluate(1440 + 270).Activity);
```

- [x] Observe failures, implement a pure analytical schedule without frame-by-frame catch-up, then test all boundaries and route interpolation. Commit verified task.

## Task 4 Save validation and transactional restore

**Files:** `src/LivingCities.Core/WorldState.cs`, `tests/LivingCities.Tests/SaveTests.cs`.

**Interfaces:** `WorldState` has `Version`, `WorldSeed`, and `Settlements`; `WorldStateStore.Snapshot()` and `Restore(WorldState)` deep-copy and validate the entire graph before replacement; `GetOrCreate(int settlementId, IEnumerable<int> homeKeys)` returns existing records without regenerating deaths.

- [x] Test round-trip identities/deaths, snapshot aliasing, future-version rejection, duplicate IDs, wrong household ownership, over-capacity homes, malformed null collections, and preservation of live state after rejected restore.

```csharp
var before = store.Snapshot();
var invalid = store.Snapshot();
invalid.Version = 999;
Check.Throws<ArgumentException>(() => store.Restore(invalid));
Check.Equal(Signature(before), Signature(store.Snapshot()));
```

- [x] Observe failures, implement deep copies and validation, run the suite, and commit verified task.

## Task 5 DFU host adapter and build

**Files:** `mod/Scripts/LivingCitiesMod.cs`, `mod/LivingCities.dfmod.json`, `tools/Deploy.ps1`, `tools/Editor/LivingCitiesBuild.cs`.

**Interfaces:** implement DFU's `IHasModSaveData`. Register at Start, wait for GameManager readiness, read current BuildingDirectory, use home types House1 through House6, and avoid for-sale homes. Track current settlement by MapID and report generated counts in a diagnostic panel toggled with F8. The panel is development-only and must identify the simulation as virtual until actor replacement is implemented.

- [x] Add a Unity integration verifier that constructs and serializes/restores real core data with FullSerializer, validates DFU type/method contracts, and fails when required hooks are absent.
- [x] Deploy source to `Assets/Game/Mods/LivingCities`, compile with Unity, then build the standard source bundle. The output must include all manifest assets and no editor/test code.
- [x] Start the isolated player, verify Living Cities initialization and Basic Roads coexistence, then stop only the owned test process. Record the difference between startup verification and gameplay verification.
- [x] Commit source, tests, plan status, and evidence references.

## Task 6 Review and integration risk report

**Files:** `docs/INTEGRATION.md`, `docs/MILESTONES.md`.

- [x] Record direct non-virtual guard spawn call sites and available quest, save, population, and UI hooks from the pinned source. Do not label runtime interception successful before testing it.
- [x] Use a read-only Luna/high reviewer for the completed foundation. Fix critical and important findings, rerun affected checks, and capture verified reusable results in Obsidian.
- [x] Keep the overall goal active. M2 remains incomplete until physical actors and in-world continuity pass testing; guard interception feasibility passed a separate temporary standard-mod probe; roster and quest behavior remain pending.
