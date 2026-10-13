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

## Finite guard interception remains unresolved

`PlayerEntity.SpawnCityGuards(bool)` (line 621) and `SpawnCityGuard(Vector3, Vector3)` (line 747) are public, non-virtual methods. No dedicated cancellation event was found in those methods. Subclass overrides cannot replace these calls.

The immediate interior branch creates 2–5 guards at a door. Exterior branches convert pooled people, and a fallback directly creates a foe spawner. Intercepting only `SpawnCityGuard` would miss that fallback.

Callers include `PlayerActivate`, `WeaponManager`, `DaggerfallEntityBehaviour`, inventory/trade/rest windows, player crime checks, and the quest `SpawnCityGuards` action. Some callers use the returned guard object. A replacement must honor return expectations and quest behavior while dispatching a finite roster. Deleting guards after creation does not satisfy the requirement.

Next probe: test runtime interception against the pinned Mono player and preserve the original fallback outside supported settlements. Runtime patching is not yet installed or verified. If standard-mod interception cannot work reliably, document a minimal engine hook before choosing that route.

## Foundation limitations

This build persists virtual households and shows F8 diagnostics. Its live adapter currently assigns home/sleep schedules only. Core work/commute routing is tested separately; it is not yet connected to building entrances or physical NPCs.

House types 1–6 are eligible; explicit `HouseForSale` types are excluded. Dynamically listed properties, player purchases, beds, existing permanent occupants, and quest reservations still require allocation rules before any physical population replacement. The current virtual allocation must not be mistaken for a complete housing census.

No changes to vanilla guard spawning, civilian visuals, enemy behavior, crime, quest actors, or dialogue are shipped in this foundation. These remain milestone work.
