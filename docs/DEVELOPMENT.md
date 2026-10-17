# Development and architecture entry points

Player setup: [Installation](INSTALLATION.md). Delivery priorities: [Roadmap](ROADMAP.md). Test evidence: [Testing](TESTING.md).

## Keeper services, tenancy and keys — 17 October 2026

`NativeBusinessServices` mirrors native `PlayerActivate.StaticNPCClick`: ordinary merchant service/repair in a shop or room rental in an inn establishes keeper role; guild/custom service and quest-control branches take precedence. `WorkKey` is the authored NPC's source/work building, not proof of ownership. Dialogue now compares the current static speaker's full native identity and service role before supplying keeper/business context. It carries social role into free chat and does not infer work duties from placement alone. Mobile/static identity, memory, child checks and listener origin use `TalkManager.CurrentNPCType`.

A shop tenant with a saved home and bed claim keeps building access without becoming a keeper. Inns issue no property key, including for innkeepers and temporary bed occupants; known permanent-home keys remain valid. Runtime filters both new claims and old unclaimed ledger entries while retaining taken flags. Old inn keys already held in inventory no longer bypass the door hook. Previously transferred non-inn keys lack issuer provenance and are not deleted or retroactively revoked; distinguishing a historical invalid business claim from a valid home key requires a later migration design.

Do not confuse this role correction with universal housing. Generated travelers have origins/reservations; authored inn occupants do not all have permanent remote households. Main-hall people may be local staff/patrons or visitors. Ordinary bedroom occupants are candidates for visitor adoption, with native quests excluded. That classification and housing work remains planned.

## Occupied interior approaches — 17 October 2026

The recorded shelf flicker was a physically unreachable waypoint: another body stood 0.20 m from the target while required separation was 0.52 m. Comfort steering alternated directions without reaching it. Interior followers now wait without rotating or advancing their walking animation at an occupied target; existing overlap can still recover through geometry-checked movement. They resume when the target clears. Outdoor steering and personal walking/running speeds are unchanged.

Browsing additionally checks actual occupants rather than relying solely on planned reservation times. It can select a reachable free position within 1.2 m of the same visible shelf/activity target, checking at most 64 surveyed candidates and at most once per real second per active visit. A short reservation prevents simultaneous selection. Captured physical visits retain the alternate endpoint and reconnect subsequent legs, preserving their actual movement after reload. This addresses the observed occupied-goal case, not every possible interior route stall.

## Stable personal walking pace — 17 October 2026

Mobile citizens now derive walking speed from their persisted appearance seed through a separate deterministic hash: 1.3 m/s ±15%, rounded to four decimals (1.105–1.495 m/s). Scene changes, day changes and reloads do not reroll it. Existing fleeing already had stable 3.25–4.55 m/s variation and retains its original formula and values.

The personal pace feeds daily itinerary and fallback travel durations, rendered indoor movement, shop browsing/capture, inn/room/service routes, newly planned resident road travel and new generated traveler visits. Shared path geometry caches retain baseline durations; each citizen's itinerary copies those durations at their own pace. Legacy local 12× and fixed-speed 2× itineraries convert on restore, while already-personalized durations remain unchanged. Calendar time remains 2×. Previously documented migration limits for already-booked inter-settlement journeys still apply. Guard formation schedules retain their coordinated pace.

## Physical movement and the 2× clock — 17 October 2026

The reported resident slowdown came from cached daily itinerary durations still encoded at 12×. A native Daggerfall save contained a 58.47 m leg lasting 539.76 game seconds: 1.3 m/s at 12×, but only 0.217 m/s after loading with the 2× clock. Restore now detects those legacy rates from route geometry and repairs walking, return and gate durations. It also repairs affected caches already resaved as schema 22/23, while preserving dwell durations and leaving already-correct legs unchanged.

The fallback `DailySchedule.DayVisit` had a separate quantization problem: rounding a short commute to whole game minutes changed its physical speed when the clock slowed. Calendar selection still reserves whole-minute slots, but actual travel now uses the exact route length, physical speed and clock conversion. Walking remains 1.3 m/s; existing per-person fleeing speeds remain unchanged. Normal gameplay time remains 2×; Unity animation time is unchanged.

Tests first reproduced both failures. The owned-player movement probe loads the actual old Daggerfall fixture, validates saved daily leg speeds, and measures a rendered native resident walking and running over real seconds. This bounded correction does not rewrite older inter-settlement trips with paid room bookings or linked admission histories. Those require an atomic trip/booking/observed-visit migration; changing only their rates would invalidate saved references. New travel plans already use the current clock.

## Shared conversation archive implementation — 17 October 2026

Approved design: store conversations once in a shared archive and keep an immutable checkpoint in each game save. Ancestry, not game time, determines which memories a save can retrieve. Continuing an old checkpoint creates another branch automatically. Current world facts and the live transcript remain separate from retrieved memory.

Storage uses compressed, content-addressed chunks below `Saves/PeopleMemory/<archive-id>/`. Each chunk contains its parent hash and a bounded batch of dated, attributed NPC entries. SHA-256 verifies content, temporary files are flushed before atomic publication, and existing chunks are never overwritten. A save contains the archive ID/head hash and no committed transcript. Schema 22 inline journals remain readable and migrate on the next save. No automatic garbage collection removes older branches.

Completed execution plan:

- [x] Add failing core tests for old/new/branched checkpoints at identical game dates, NPC isolation, inline migration, unchanged-save deduplication, bounded save payload, missing/corrupt chunks, failed writes and archive portability.
- [x] Implement the archive codec/store and validated checkpoint. Keep pending entries in runtime snapshots; flush only for actual save snapshots. Validate the entire requested ancestry before replacing live state.
- [x] Connect runtime save/load and new-character reset. Preserve the checkpoint on failures and surface unavailable archives without silently starting blank memories.
- [x] Add portable save export/import which includes only referenced archive chunks and validates imported content; never overwrite an existing save or archive chunk.
- [x] Exercise native save serialization plus real disk save/load across divergent timelines, run regressions, build/install, and record measured checkpoint sizes and test evidence.

Accepted: 670 core tests, native write/restart-read isolation, 157-character checkpoints, real-save transfer checks and the existing live dialogue/service/compatibility regression. See [Testing](TESTING.md#latest-acceptance). Test-only store clones now configure the active archive root; an unconfigured store deliberately rejects archived saves.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3/netstandard2.0, no new native database or model dependency. Archive history still consumes disk once; total retention/forgetting is a separate policy. Backup the entire Saves directory or use the portable export tool.

<a id="repository"></a>

# Repository setup and development baseline

Repository: https://github.com/Bl4ckh34d/People-of-the-Iliac-Bay

The existing local Git history predates the GitHub repository. Initialization preserves that history and the remote README history; it does not reset the project to a new empty repository. The initial publication is a development baseline containing unfinished work, not a release tag.

## Local layout

The current Windows scripts expect this sibling layout by default:

```text
DFU/
  LivingCities/       # this repository; retain this folder name for current scripts
  daggerfall-unity/   # separate DFU 1.1.1 source checkout
  Player/            # separately installed DFU player and portable app data
  GameFiles/         # user's game data
  downloads/         # external mod archives
  work/              # machine-local build evidence
```

Clone with an explicit folder name if setting up a new machine:

```sh
git clone https://github.com/Bl4ckh34d/People-of-the-Iliac-Bay.git LivingCities
```

Core tests require only the .NET 8 SDK and restored NuGet packages. The .NET Framework runtime/hook projects additionally require assemblies from the sibling player. `tools/Deploy.ps1` expects `work/Harmony/net472/0Harmony.dll` and the built hook assembly. This baseline does not yet provide a complete clean-machine dependency bootstrap. A fresh clone alone cannot package the Unity mod.

`tools/Build.ps1` uses Unity 2019.4.40f1 at its configured Windows Hub path, deploys into the separate DFU project's ignored mod directory, verifies and packages the mod. It refuses to overwrite a mod loaded by a running game. `tools/RuntimeCompile/RuntimeCompile.csproj` provides a faster source compilation check against installed assemblies.

## What is excluded

Runtime logs, local `work/`, `dist/`, `bin/`, `obj/`, downloaded bundles/archives, model weights, credentials and extracted native reference PNG directories are excluded. External mods remain separate installations. Decompiled external source and extracted quest scripts remain under ignored local audit directories; only findings and source identifiers are committed.

Generated mod artwork and its provenance metadata remain part of the project. Rejected pilots are identified explicitly in their art READMEs and must not be mistaken for accepted animation coverage. Historical commits are retained.

## Verification boundaries

The first GitHub baseline passes 647 core tests and runtime source compilation. It does not imply every included development change has a new Unity build or live acceptance. In particular, the saved-door survey and current/next destination changes require their remaining targeted native checks. Do not infer game readiness from the repository being clean.

Track future behavior changes in focused commits. Keep compatibility claims tied to named tests and distinguish source implementation, compilation, bundle installation and gameplay verification.


<a id="integration"></a>

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
| Quest people | `StaticNPC.AssignQuestResourceBehaviour`, `Data` fields and quest resource attachment | Original-object movement, native quest clicks, hide/show and reconstructed save/load passed the runtime probe. Cross-building proxies and broad compatibility remain unverified; see `QUEST-IDENTITY.md`. |
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

The quest identity baseline probe now passes in the shipped player. A synthetic quest bound to an original tavern NPC retains UID/resource/data/name, receives native click tasks after movement and reload, and honors hide/show. Runtime reconstruction is checked with a new instance ID and exactly one matching NPC. `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` reproduces it. The helper now follows native building discovery/entry setup, fixing previously hidden vanilla occupants in scripted test entries. This is not full service, main-quest or cross-location compatibility. Detailed evidence and constraints: `QUEST-IDENTITY.md`.

Version 0.3.0 supersedes the demographic parts of the historical 0.2.0 checkpoint below. It adds housing-aware families, schema-3 migration and home-only child flats in the same pilot town. Children receive a brief greeting; this is the only shipped dialogue customization. Housing profiles count actual bed models 41000–41002 and derive an exterior footprint preference without opening interiors. These hints do not establish ownership or sleeping anchors.

Two runtime findings have regression coverage. The shipped mod compiler treats the readonly-reference parameter of `MapsFile.GetRmbBlockName` differently from the editor compiler; `HousingSurvey` resolves the method once and invokes it once per cached block, avoiding source compilation failure. `TalkManager` retains its last mobile target during later static conversations, so the child talk hook checks `CurrentNPCType.Mobile` before intercepting. The actual player passed family save/load, child greeting and stale-target checks. See `TEST-FIXTURES.md` for fixtures and boundaries.

Version 0.2.0 connects scheduled adult street visits and interior presence in Gothway Garden. Other towns retain vanilla physical populations. F8 reports household/schedule/actor counts. Saved routes avoid relying on `CityNavigation.Start` or exterior physics during indoor save loads; schema 1 migrates to schema 2 without changing residents. Exact door, animation, identity and death-save evidence is recorded in `TEST-FIXTURES.md`.

House types 1–6 are candidates; explicit `HouseForSale` types are excluded. Dynamically listed properties, player purchases, beds, existing permanent occupants, and quest reservations still require allocation rules before broad physical population replacement. The pilot allocation must not be mistaken for a complete housing census. Existing stationary quest/service NPCs are retained; replacing them without duplicate identities remains work.

The adult preview uses vanilla mobile sprite assets with stable names and identities; home-only children use original child flats. No changes to vanilla guard spawning, enemy behavior, crime or quest actors are shipped yet. Current indoor residents stand rather than use furniture. Full interior navigation, corner smoothing, actor avoidance and radius behavior across loaded map boundaries remain unfinished.
