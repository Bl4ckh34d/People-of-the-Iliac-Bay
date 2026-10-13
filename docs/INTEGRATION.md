# DFU integration evidence

Target inspected: upstream `v1.1.1-cve-2025`, commit `fb2627082fabe473bf108c816263b71c6e26ace6`, Unity 2019.4.40f1. Paths below are relative to the upstream checkout.

## Available interfaces

| Concern | Evidence | Implication |
| --- | --- | --- |
| Mod lifecycle and saves | `Assets/Game/Addons/ModSupport/ModTypes.cs`, `IHasModSaveData`; `SaveLoadManager.cs` save/load loops | Standard mod can own versioned population data without adding fields to vanilla saves. DFU catches individual mod failures. |
| Failed loads | `SaveLoadManager.cs` raises `OnStartLoad` before deserializing mod data | Suspend before loading, resume only after a valid restore; otherwise data from the previous character could remain active. |
| New characters | `StartGameBehaviour.OnNewGame` | Clear population and select a new persisted world seed. |
| Buildings | `BuildingDirectory.GetBuildingsOfType`, `GetBuildingSummary`; `StreamingWorld.GetCurrentBuildingDirectory` | Stable map/building keys available. StreamingWorld rejects mismatched map IDs while loading. |
| Streets | `CityNavigation` nav grid, conversion methods, `GetNavGridWeightLocal` | Feed walkability and road costs into a bounded CPU pathfinder. Conversion includes origin and coordinate conventions requiring in-world tests. |
| Mobile people | `PopulationManager.MobileNPCGenerator`, `OnMobileNPCCreate/Enable/Disable`, `PopulationPool` | Actor integration possible, but vanilla pooling randomizes/recycles citizens; lifecycle events alone do not establish persistent identities. |
| Quest people | `StaticNPC.AssignQuestResourceBehaviour`, `Data` fields and quest resource attachment | Preserve existing objects and quest bindings. Moving a visual proxy does not prove quest compatibility. Runtime probe still required. |
| Conversation UI | `UIWindowFactory.RegisterCustomUIWindow(UIWindowType.Talk, Type)` | Talk window extension is available. Other mods may replace the same window, so coexistence needs explicit checks. |
| Awareness | `EnemySenses.StealthCheck`, `FormulaHelper.CalculateStealthChance`, existing player crouch | Extend existing stealth semantics; avoid an unrelated second player skill. |
| Basic Roads | Built 1.3.1 source exposes `getPathData`, `getRoadPoint`, `getTrackPoint`, `getPathsPoint` messages | Wilderness path data available. Installed coexistence and actual NPC road following are separate acceptance checks. |

## Finite guard interception: feasibility passed, behavior pending

`PlayerEntity.SpawnCityGuards(bool)` (line 621) and `SpawnCityGuard(Vector3, Vector3)` (line 747) are public, non-virtual methods. No dedicated cancellation event was found in those methods. Subclass overrides cannot replace these calls.

The immediate interior branch creates 2–5 guards at a door. Exterior branches convert pooled people, and a fallback directly creates a foe spawner. Intercepting only `SpawnCityGuard` would miss that fallback.

Callers include `PlayerActivate`, `WeaponManager`, `DaggerfallEntityBehaviour`, inventory/trade/rest windows, player crime checks, and the quest `SpawnCityGuards` action. Some callers use the returned guard object. A replacement must honor return expectations and quest behavior while dispatching a finite roster. Deleting guards after creation does not satisfy the requirement.

A temporary precompiled standard mod using Harmony 2.3.6 intercepted both entry points in the shipped Mono player. The probe verified immediate/deferred batch calls, a supplied return object for the single-guard call, and removal of its own patches. It then stopped its owned process, and its temporary package and Unity staging were removed. Evidence: `work/guard-probe-player.log`; build log: sibling `../work/guard-probe-build.log`. Source and constraints: `probes/guard`.

This resolves basic runtime interception feasibility on this pinned Windows player. It does not prove the roster implementation, quest callers, arrest compatibility, other patching mods, or other platforms. No guard patches are installed by the main mod yet. Preserve vanilla fallback outside supported settlements in the eventual integration.

DFU's runtime source compiler excludes references to precompiled assemblies loaded from byte arrays because they have no file location. The Harmony-referencing probe therefore ships as a precompiled DLL alongside Harmony, both in the mod bundle as `.dll.bytes`. This avoids adding DLLs to the player installation's Managed directory or modifying the engine.

## Foundation limitations

Version 0.3.0 supersedes the demographic parts of the historical 0.2.0 checkpoint below. It adds housing-aware families, schema-3 migration and home-only child flats in the same pilot town. Children receive a brief greeting; this is the only shipped dialogue customization. Housing profiles count actual bed models 41000–41002 and derive an exterior footprint preference without opening interiors. These hints do not establish ownership or sleeping anchors.

Two runtime findings have regression coverage. The shipped mod compiler treats the readonly-reference parameter of `MapsFile.GetRmbBlockName` differently from the editor compiler; `HousingSurvey` resolves the method once and invokes it once per cached block, avoiding source compilation failure. `TalkManager` retains its last mobile target during later static conversations, so the child talk hook checks `CurrentNPCType.Mobile` before intercepting. The actual player passed family save/load, child greeting and stale-target checks. See `TEST-FIXTURES.md` for fixtures and boundaries.

Version 0.2.0 connects scheduled adult street visits and interior presence in Gothway Garden. Other towns retain vanilla physical populations. F8 reports household/schedule/actor counts. Saved routes avoid relying on `CityNavigation.Start` or exterior physics during indoor save loads; schema 1 migrates to schema 2 without changing residents. Exact door, animation, identity and death-save evidence is recorded in `TEST-FIXTURES.md`.

House types 1–6 are candidates; explicit `HouseForSale` types are excluded. Dynamically listed properties, player purchases, beds, existing permanent occupants, and quest reservations still require allocation rules before broad physical population replacement. The pilot allocation must not be mistaken for a complete housing census. Existing stationary quest/service NPCs are retained; replacing them without duplicate identities remains work.

The adult preview uses vanilla mobile sprite assets with stable names and identities; home-only children use original child flats. No changes to vanilla guard spawning, enemy behavior, crime or quest actors are shipped yet. Current indoor residents stand rather than use furniture. Full interior navigation, corner smoothing, actor avoidance and radius behavior across loaded map boundaries remain unfinished.
