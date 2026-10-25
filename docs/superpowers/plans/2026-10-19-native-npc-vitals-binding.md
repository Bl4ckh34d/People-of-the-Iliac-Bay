# Native NPC Vitals Binding Plan

> **For agentic workers:** Use the inline execution workflow for these steps. Complete each test and review gate before moving on.

**Goal:** Bind seeded health and magicka maxima to eligible Living Cities residents and preserve their current native resources across actor streaming and game saves.

**Architecture:** Resolve an ordinary resident's stable archetype before materialization, read one seeded actor profile, and apply its maxima after DFU creates the native civilian entity. Store only changed current resources in the existing sparse actor ledger, capture them at bounded intervals and every actor retirement/save boundary, and restore them through DFU's resource setters. Keep native quest/protected actors and dead identities outside this adapter.

**Tech Stack:** C# 7.3, .NET Standard 2.0 core, DFU net472 runtime, Unity native entity API, FullSerializer save state.

**Spec:** `docs/ROADMAP.md`, detailed design section 6, “Native profile binding after inventory ownership.” This plan implements only HP/MP binding; equipment, spells, status effects, and skill-use rewards remain separate work.

## Status and source evidence

**Core and runtime source implemented; native verification pending.** Core suite passes 1051/1051. `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj -c Release --nologo` passes with zero warnings and errors. No Unity install, native damage/restore run, or disk save/load acceptance has verified live behavior.

Current Living Cities source computes deterministic `MaximumHealth` and `MaximumMagicka` in `NpcBaseProfile`, but no runtime code reads those values into a resident entity. `ResidentActor.Initialize()` gets the entity, binds inventory, and subscribes to death. `NpcActorProgress` saves archetype and skill experience only. `NativeNpcInventoryOwner` saves inventory and wallet only.

DFU source checkout `daggerfall-unity`, commit `fb26270`, confirms:

- `Assets/Scripts/Game/Entities/DaggerfallEntityBehaviour.cs`, `SetEntityType()` creates `CivilianEntity` for `EntityTypes.CivilianNPC` and calls `SetEntityDefaults()`.
- `Assets/Scripts/Game/Entities/CivilianEntity.cs`, `SetEntityDefaults()` sets `MaxHealth = 1` and `CurrentHealth = 1`; it does not initialize magicka. Base fields therefore leave maximum/current magicka at zero until another system sets them.
- `Assets/Scripts/Game/Entities/DaggerfallEntity.cs` exposes `MaxHealth`, `CurrentHealth`, `MaxMagicka`, and `CurrentMagicka`. Maximum properties write base values. Current-value properties call `SetHealth()` and `SetMagicka()`, which clamp to current maxima and raise death or magicka-depleted events at zero. `SetHealth` and `SetMagicka` also expose a `restoreMode` parameter that bypasses clamping; do not use it for ordinary resident restoration.
- The same base class exposes `OnDeath` and `OnMagickaDepleted`, but no general health-changed or magicka-changed event. `DaggerfallEntityBehaviour` exposes `DamageHealthFromSource()` and `DamageMagickaFromSource()`, but native spells and direct entity calls also mutate resources through other paths. Do not treat one damage hook as complete coverage.

Relevant current Living Cities files: `mod/Scripts/ResidentActor.cs`, `mod/Scripts/NativeNpcVitalsBinding.cs`, `mod/Scripts/NativeNpcInventoryOwner.cs`, `src/LivingCities.Core/NpcVitals.cs`, `NpcVitalsBindingPolicy.cs`, `NpcProfiles.cs`, and `NpcActorProgress.cs`. `ResidentActor.Initialize()` runs after the prefab's `DaggerfallEntityBehaviour.Entity` exists; the binding then applies seeded maxima and saved current values under `Quiesce`, using clamping setters. The adapter rejects ineligible adults, preserves an existing pinned archetype, and otherwise uses explicit `Citizen` fallback. It never writes zero HP or changes death state. It leaves `MaxMagickaModifier` and `MaxHealthLimiter` untouched.

Production capture now uses `ResidentActor.CaptureState()` at save and retirement boundaries. `LivingCitiesMod.GetSaveData()` calls StreetPopulation and road-actor capture loops; StreetPopulation's actor dictionary includes home, destination, traveler, and visit actors. Lifecycle audit found two traveler-plan replacement paths that destroyed actors without capture; both now call `CaptureState()`. `OnDisable()` does not write state: Unity may defer teardown of an old actor until after `RestoreSaveData()` has installed another save branch. Each binding captures an opaque `WorldStateStore` write epoch, and both the vitals adapter and `ResidentActor.CaptureState/CaptureInventory()` reject writes after that store is restored. A Core regression verifies that a stale actor token cannot overwrite the restored branch. Death stays owned by `ResidentActor.Died` and resident death persistence. `CaptureState()` still captures inventory separately before vitals.

Sampling is actor-local at one unpaused real-time second interval. Unchanged samples skip Core and profile work. Changed resources use the cached archetype and vitals revision. Explicit capture refreshes the persisted timestamp for an already-persisted unchanged record. No global resident scan runs. This verifies source lifecycle coverage and compilation, not actual scene-unload callback ordering or disk persistence; native probe must verify stream/despawn/rebind and real save/load.

## Global constraints

- Keep `LivingCities.Core` independent of Unity and DFU assemblies.
- Preserve the deterministic version-1 profile generator and existing native identity keys.
- Never bind quest-protected or authored entities, children, unknown identities, or dead residents.
- Never award progression or create persistent state from LLM claims.
- Do not use global random state for profile or resource initialization.
- Keep capture work limited to active eligible resident actors and save/retirement events.
- Do not restore zero HP as a live actor or allow restoration to fire an unintended death transition.
- Do not claim spell/effect persistence, equipment progression, or complete combat support from HP/MP acceptance.

## Implementation tasks

### Task 1: Define sparse vitals state and restore policy

**Files:**

- Modify `src/LivingCities.Core/NpcActorProgress.cs` and the owning world-state copy/validation code.
- Add focused tests under `tests/LivingCities.Tests` and register them in `Program.cs`.

**Decision:** Add optional vitals fields to the existing per-identity progression record rather than a second independently keyed ledger. Keep a `VitalsInitialized` flag, current health, current magicka, and last-captured game time. Existing rows deserialize with vitals unset. Do not allocate a row just because an actor was materialized at full seeded resources; create/update one only when current values differ from the deterministic initialized state or a native resource change is captured.

- [x] Add failing tests for a legacy skill-only record restoring with `VitalsInitialized == false`, a captured health/magicka pair surviving snapshot/restore, independent branch copies, and malformed negative/out-of-range values being rejected.
- [x] Add a pure restore-policy test proving a dead identity stays dead and a saved resource is clamped to the newly computed effective maximum.
- [x] Run Core tests and confirm regressions fail before implementation.
- [x] Implement detached copy, validation, and snapshot behavior. Keep current resources as integers and timestamp as finite game seconds. Preserve old save defaults.
- [x] Re-run full Core suite. Verify old schema migration and unrelated progression records.

### Task 2: Resolve role and apply one profile to native civilians

**Files:**

- Modify `mod/Scripts/ResidentActor.cs` and the narrow native profile-binding adapter it owns.
- Add a Core policy test only if archetype resolution becomes a pure Core decision.
- Extend a source-level DFU editor check only when it can exercise actual `CivilianEntity` APIs without creating a false gameplay claim.

- [x] Trace resident identity provenance used by the adapter. No authoritative role field exists in the runtime `Resident`; existing profile pin wins, else explicit Citizen fallback. Quest protection excludes authored identities.
- [x] Add a failing adapter-policy test for ordinary adult eligibility, protected/child/dead rejection, and deterministic archetype fallback. Do not infer `Merchant` from conversation or a one-off shop visit.
- [x] After `DaggerfallEntityBehaviour` creates `CivilianEntity`, read the stable identity's seeded vitals view and selected archetype. Apply maxima before current values. Never set current HP unless identity and native entity are alive.
- [x] On first bind, initialize current resources to full seeded values. On later binds, restore captured values after maxima. Use clamping setters, not `restoreMode=true`.
- [x] Preserve active DFU modifiers: setters change base maxima only; binding leaves `MaxMagickaModifier` and `MaxHealthLimiter` untouched. Live behavior still needs native acceptance.
- [x] Keep inventory owner binding order and UID behavior unchanged. Binding attaches only once and declines a zero-HP entity without restoring it.
- [x] Run Core tests and `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj -c Release --nologo`.

### Task 3: Capture current resources at real lifecycle boundaries

**Files:**

- Add capture logic to `ResidentActor` or a focused `NativeNpcVitalsBinding` component.
- Update each actor retirement/save call site that currently calls `CaptureInventory()`, including `StreetPopulation.cs`, `ResidentDestinationActors.cs`, `RoadTravelActors.cs`, `StreetPopulation.Actions.Transitions.cs`, and routine-recovery transitions.
- Add a bounded active-actor sampler owned by each resident actor or the existing population update loop.

- [x] Add Core tests that unchanged full resources do not create sparse vitals records and stale captures cannot overwrite newer resources.
- [x] Inventory production destruction, save, transfer, and death paths. Fix the two traveler replacement paths that lacked capture. Do not write from `OnDisable()`; guard actor captures with a store-write epoch so deferred teardown after restore cannot contaminate the new branch. Actual Unity callback ordering remains unverified.
- [x] Implement changed-resource capture at an actor-local bounded cadence. Explicit save and retirement paths capture through `CaptureState()`. No world-wide per-frame scan.
- [x] Preserve existing `OnDeath` semantics. Dead capture is rejected; binding never restores zero HP.
- [x] Keep offscreen policy conservative: preserve last captured values, no passive healing or magicka regeneration.
- [x] Run Core suite and runtime compile. Work is per active bound actor and unchanged samples avoid Core/store calls; no runtime instrumentation counter was added.

### Task 4: Native setter, save/load, and stream acceptance

**Files:**

- Add a focused `NativeNpcVitalsProbe.cs` and explicit `TownTest.ps1` opt-in flag.
- Keep the probe fixture separate from unrelated inventory, no-show, and recovery probes.

- [ ] Use a real eligible resident with a stable saved identity. Record profile maxima, initial entity maxima, and current values.
- [ ] Change HP and MP through confirmed DFU entity APIs (`DamageHealthFromSource` / `DamageMagickaFromSource` or real native combat/effect execution); do not assign fake test values directly to the Living Cities ledger.
- [ ] Stream the actor out and back through the normal population lifecycle. Assert native entity maxima remain profile-bound and current values survive within the documented timing policy.
- [ ] Save through DFU disk `SaveLoadManager.Save`, load the created slot through native `Load`, and re-check identity, current HP/MP, maxima, alive state, and no duplicate actor/component binding.
- [ ] Exercise a zero-health death through native `OnDeath`; verify resident remains dead after stream and disk restore. Separately test zero magicka without treating it as death.
- [ ] Assert quest-protected and child actors retain their native entity defaults. Assert no full-world scan, per-frame profile generation, or LLM request occurs.
- [ ] Run the native probe only after a fresh Unity bundle build/install. Record source hash, Player log, runner exit code, and exact fixture. Mark only observed cases as passed.

### Task 5: Review boundary and document evidence

**Files:**

- Update `docs/TESTING.md` with measured native scope and limitations.
- Update `docs/ROADMAP.md` section 6 only after acceptance; preserve this plan as the implementation checklist.

- [ ] Review all native property writes, entity lifecycle paths, and restore ordering. Confirm current setters run after maxima and no restore calls `SetHealth(0)` for a live identity.
- [ ] Run Core suite, RuntimeCompile, Unity build, and native disk acceptance. No test result substitutes for a later gate.
- [ ] Document remaining gaps precisely: effects/diseases, fatigue, resistances, equipment, live spell use, level-up progression, and broad combat balance remain unimplemented unless separately proven.

## Acceptance boundary

Completion requires deterministic role/profile binding; native MaxHealth/MaxMagicka matching the profile for eligible ordinary adults; current HP/MP surviving real stream and disk save/load; death staying terminal; protected/child actors untouched; and bounded active-actor work. Core tests and compilation alone do not establish native combat acceptance. The source inspection in this plan confirms DFU APIs and the current one-HP civilian default; it does not verify any Living Cities HP/MP gameplay behavior.
