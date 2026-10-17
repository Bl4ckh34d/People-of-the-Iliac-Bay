# Compatibility and quest boundaries

See [Roadmap](ROADMAP.md) for planned adapters; all dated results below retain their stated scope. Installed does not mean every quest or layout is tested.

<a id="quests"></a>

# Quest Pack 1 and Quest Actions Extension audit

17 October 2026. These are separately installed third-party mods, not code or assets incorporated into People of the Iliac Bay.

## Installation and evidence

**Planning follow-up, 17 October 2026:** [quest-role design and representative cases](ROADMAP.md#detailed-design) and [delivery phase 6](ROADMAP.md#delivery-plan) consolidate mobility and spoiler-control options. Quest Pack mobility implementation is explicitly deferred. Review must eventually cover each quest's roles/stages, native bindings and disclosure gates; representative source analysis and installation tests are not certification of every quest. Existing native control remains the conservative default.

Installed from the user-supplied archives:

- `Quest Pack 1 dfmod-2-2025-11-20b-1763826811.zip`: Quest Pack 1 **2025.11.20b**.
- `Quest Actions Extension-677-2-0-0-1740947698.zip`: Quest Actions Extension **2.0.0**.

Both bundles target DFU 1.1.1. Player and editor copies match the archive-extracted SHA-256 values. Player load order is BasicRoads 18, Quest Actions Extension 19, Quest Pack 1 20, People of the Iliac Bay 21. Other enabled choices are preserved. The pre-install configuration is retained locally in `work/quest-mod-audit/Mods.before-install.json`.

Quest Pack 1 declares a nonoptional, nonpeer dependency on Quest Actions Extension version 1.12.0. DFU's `ModManager.CheckModDependencies` treats the declared version as a minimum (`IsVersionLowerOrEqual`), so installed 2.0.0 satisfies that version check. This is not proof that every script action is bug-free.

The owned-player startup test passed and registered all nine pack quest lists. The existing live dialogue regression also passed with both mods enabled. A data-only quest pack reporting “No mod loaders found” is expected; it contributes quest assets/lists rather than a runtime initializer. This audit does not claim all 302 quests have been played or that native quest integration is complete.

Local inspection evidence is under ignored `work/quest-mod-audit/`. The original bundles, extracted quest text and decompiled extension source are not redistributed in this repository. Existing NameGen/localization and DET material diagnostics still appear in startup logs; no new clean-whole-stack claim is made.

## Quest inventory

The bundle contains 303 QBN-bearing TextAssets: **302 quest scripts plus one literal `test` fixture**. Metadata exposes 298 loose resources, including the test fixture. Five additional `JHrepchange1`–`JHrepchange5` script assets are linked child quests, not missing orphan entries.

| Quest list | Active entries | Disabled entries |
| --- | ---: | ---: |
| Commoner | 23 | 0 |
| Dark Brotherhood | 25 | 0 |
| Fighters Guild | 41 | 2 |
| Knightly Orders | 47 | 0 |
| Mages Guild | 29 | 1 |
| Misc | 2 | 0 |
| Noble | 4 | 0 |
| Temples | 12 | 4 |
| Thieves Guild | 30 | 0 |
| Total | 213 | 7 |

These are list entries, not all directly offerable quest scripts. Loose quests include chained/supporting content. `QuestList-Misc:6` refers to `JHPCART`, while the available asset is `JHCART`; this is a source-level candidate defect requiring native lookup verification, not a reason to rewrite the downloaded pack silently.

There is no `A0C00Y14` or `S000...` asset/list reference in the pack. It does not override our narrow vanilla delivery pilot or the main-quest files inspected here. It still uses fixed main-world identities such as Gortwog.

Raw syntax census across all 303 scripts: 258 declare people, 199 declare questors, 117 contain `place npc`, 82 `hide npc`, 90 `place foe`, 134 `create foe`, 150 `killed`, 288 clocks and 85 daily time windows. These overlapping counts identify test coverage needs; they do not establish semantic compatibility.

## Representative NPC risks

| Case | Script and evidence | Required treatment |
| --- | --- | --- |
| Human/monster swap | `JHCAPA01` The Werewolf Hunt, declarations 385–391, hide/place sequence 513–521, kill/reward 528–535 | Preserve distinct Person and Foe resources and scripted visibility. Do not let routine lifecycle resurrect the human or remove the foe |
| Fixed named people and disguised actor | `JHDB121a` To Kill an Ambassador, Br'itsa/Gortwog 326–331, alias/vampire sequence 390–439 | Retain individual identity, alias semantics, quest placement and hostile replacement |
| Captive child/parent | `JHDB109` Going Home, 95–113 and 129–154 | Quest dungeon/home assignment overrides normal family routine; timed hide and parent ambush must remain native |
| Hostage | `JHDB113` To the Wolves, 68–117 | Preserve target, hide/face/dialogue transitions and timer outcomes |
| Priest escort and timed combat | `JHKN119` Rite of Vampire Dispelling, 115–149 and 181–213 | Temple service affiliation does not make this an ordinary staff actor; preserve hidden escort and event clock |
| Relocated service-role people | `JHCAPA01`, 385–411 and 458–489; `JHCG003`, 84–99 | Merchant/apothecary/chemist faction alone is not proof of a permanent shopkeeper or resident |
| Kill target represented as foe | `JHFG106`, 157–170 and 220–224 | Native kill event and foe identity must survive simulation; do not substitute a generic citizen |

All source identifiers above refer to extracted user-installed bundle TextAssets. Representative tests must cover click, item hand-in, reward, scripted relocation, hiding/restoration, timing, foe injury/death and save/load, not merely whether an NPC appears.

## Extension inventory

Quest Actions Extension registers **28 actions/conditions** at game start plus four console commands. It adds no NPC sprite sheets or global household system. Quest Pack 1 adds quest resources, not a global resident census or new animation assets.

| Group | Registered classes |
| --- | --- |
| Player stats/status | `ReducePlayerHealth`, `ReducePlayerFatigue`, `ReducePlayerMagicka`, `IncreasePlayerHealth`, `IncreasePlayerFatigue`, `IncreasePlayerMagicka`, `InfectPlayerWith` |
| Items | `PlayerHandsover`, `PlayerPossesses`, `EquippedWithItem`, `UpdateQuestItemMaterial`, `UpdateQuestItemAddEnchantment`, `UpdateQuestItemApplyMagicTemplate` |
| Time/location/state | `RaiseTime`, `WithinUnits`, `CurrentMapPixel`, `CurrentInBlockPixel`, `PlayerCurrentRegionIs`, `CurrentStateIs` |
| Thresholds/progression | `WhenHealthLevel`, `WhenFatigueLevel`, `WhenMagickaLevel`, `GuildRankAtLeast`, `EnemyHealthLowerHigher`, `KilledEnemiesOfClass`, `MagicEfectKeyIsOn`, `PlayerLegalReputeIs`, `PlayerFactionReputeIs` |

Important interactions:

- `WithinUnits` reads the actual QuestResourceBehaviour transform and vertical difference. It can follow the original moving resource; it cannot locate an unrelated substitute resident. Example: `JHMG222:131`.
- `CurrentMapPixel`, `CurrentInBlockPixel` and region conditions remain player-position checks. Do not satisfy them using an unloaded NPC's scheduled position.
- `RaiseTime` directly advances the game clock. Our schedules, sleep, stays, reporting and guard duties must handle clock jumps. A future 2× clock must change elapsed-time progression without incorrectly scaling explicit quest time jumps or deadlines.
- `GuildRankAtLeast`, legal reputation and faction reputation read native player state. NPC disposition is a separate value; planned guild expulsion must update native membership coherently rather than imitate it only in dialogue.
- `EnemyHealthLowerHigher` and `KilledEnemiesOfClass` depend on native quest foe state. Background cleanup must not count as a kill or lose required injury/death events.

The decompiled `CurrentStateIs.Pattern` appears to omit alternative separators before vampire/wereboar/werewolf forms. This is a candidate upstream parsing defect, not a confirmed gameplay failure or a mod change made by this audit. Check affected syntax in a native parser fixture before considering a compatibility shim. No downloaded bundle was modified.

## Acceptance backlog

- [ ] Native parser/lookup check for every contributed list entry and extension syntax used by the pack, including the `JHPCART` discrepancy.
- [ ] Original-resource identity and no duplicate actor across sleep, return, streaming and save/load.
- [ ] Captive/escort, disguise/monster swap, fixed individual, relocated service actor and kill/reward fixtures.
- [ ] Timed meetings, explicit clock jumps and quest deadlines under the eventual clock-rate policy.
- [ ] Guild/reputation conditions after any future disposition-driven expulsion system.
- [ ] Confirm pending and completed quest cleanup releases simulation ownership without reviving dead people.

Use the [NPC integration policy](NPC-SYSTEMS.md#inventory) for implementation ordering. Installing these mods does not expand the current automatic household-binding whitelist.


<a id="external-mods"></a>

# External mod installation and compatibility audit

## NPC and quest inventory — 17 October

The current cross-category inventory, housing gaps, quest ownership risks and animation coverage are recorded in [NPC integration audit](NPC-SYSTEMS.md#inventory). This source audit supersedes assumptions that every stationary NPC is already adopted or housed. Runtime census and generic adoption remain pending.

Quest Actions Extension 2.0.0 and Quest Pack 1 2025.11.20b are installed separately in player/editor, enabled before People of the Iliac Bay, and pass startup plus the existing live dialogue regression. See [quest-pack audit](COMPATIBILITY.md#quests) for the 302-script inventory, minimum dependency version, representative difficult NPCs and outstanding quest acceptance. No external bundle was modified.

## Pending: external stationary NPC integration — 17 October

The user reports added outdoor NPCs ignoring murders, enemies, bedtime and households. Lively Cities 1.3.4 is a confirmed installed source of static street NPCs, and the live log confirms its initializer runs. Its manifest explicitly describes this purpose and includes WorldData building replacements. Beautiful Cities 0.5.0 and Beautiful Villages also replace settlement layouts; the exact provenance of each reported actor still needs an in-game inventory. Do not attribute every external NPC to Lively Cities without identifying the actor and effective override.

Inspection of the installed Lively Cities assembly (`work/lively-cities-inspected.cs`) shows `LivelyCitiesMod.InitMod` registering NPC flat archives 1200 and 10015, then subscribing to town entry, weather, dawn and dusk. `UpdateExteriorNPCs` enumerates all exterior billboards including inactive objects, finds StaticNPC components with nonzero faction IDs, and toggles their GameObjects using flags 1 (hide by day), 2 (hide by night), and 4 (hide in rain). It does not implement walking home or persistent sleeping. Its filter is broad, so it could also toggle adopted actors unless ownership is coordinated.

Our `KeeperIdentities.SurveyKeeperIdentities` only scans the current interior and requires `StaticNPC.Context.Building` plus the current entry building key. It registers ordinary indoor flats while excluding quest resources, actions and individual factions. `HouseholdOccupants.ReconcileHouseFlats` likewise only reconciles residential interior decoration. Neither path adopts Lively Cities' exterior population. This is a registration/lifecycle integration gap, not merely missing fear settings.

### TODO — not implemented

1. Inventory active and inactive external NPCs after world-data loading: mod/override provenance, map/block/building/record identity, faction, native hash, appearance and quest/service ownership. Verify stable collision-free IDs across reloads; never use transient instance IDs for persistence. Include non-StaticNPC decorative figures in the audit without assuming every humanoid prop is an interactable citizen.
2. Adopt eligible ordinary exterior NPCs into the existing resident/native-person ledger and physical actor system. Preserve original services, faction/name/appearance and protected quest actors. Ensure one identity and one live representation, persistent deaths, dispositions, corpse loot and save/load state; do not duplicate them as both generated household residents and external actors.
3. Connect crime witnessing, hostile-creature awareness, flee/combat decisions, physical reporting, dialogue and indoor/outdoor continuity. Test murder witnesses and creature attacks explicitly.
4. Reserve real available beds and household roles before allocating additional generated residents. Route adopted citizens to their actual home and sleep at night. Define a capacity-aware fallback for genuinely unhoused workers/visitors; never invent unlimited beds or silently assign everyone to a nearby house.
5. Coordinate Lively Cities day/night/weather visibility only for adopted NPCs. Our schedules must own their lifecycle so weather/dusk callbacks cannot hide, revive or duplicate them. Preserve behavior for non-adopted decorations and protected actors; do not disable the dependency globally.
6. Audit added flat archives and Elves/Beastfolk replacements for directional movement, idle, fleeing, corpse and sleeping art. Preserve recognizable identity; a static pose is not automatically a full mobile animation set. Document gaps before generating assets.
7. Acceptance: reload/revisit, dawn/dusk/rain, death persistence, threats, guards receiving reports, home/bed ownership, navigation around added structures, quest/service compatibility and large-city frame-time checks. Use incremental scans rather than a new full-scene scan every frame.

Implementation is deferred at the user's request; this inspection and backlog capture are complete. The precise actor provenance and broad runtime coverage remain to be verified during integration.

## Rosy's Resources delayed entry correction — 17 October

The installed Rosy's Resources assembly contains `FlowerDeActivatorMod.FlowerDeActivator.TeleportPlayerToEnterDoorOrMarkerAfterDelay`. Inspection confirms it yields nine frames, then reads each entry marker's `gameObject.transform` without checking whether the player has left or the marker still exists. Its exterior handler only restores exterior renderers/colliders; it does not cancel the pending entry correction. The final rapid inn entry/exit in `work/final-disposition-native-check.log` destroys those markers before resumption and produces `NullReferenceException`. A stale routine can also reposition the player in the wrong scene if its old markers survive.

The compatibility fix lives in our optional `RosyInteriorCompatibilityHooks`, not in the external archive or game assemblies. It wraps only that coroutine, retains its original yields and valid entry behavior, and checks the captured transition generation, current active interior and live entry markers before every resume. New entry/exit invalidates earlier routines. Other external-mod exceptions are not swallowed. The dialogue regression now waits 25 frames after its final exit and requires both a completed normal correction and a cancelled stale correction when the dependency is installed. Hook/runtime compilation and Unity build pass. Live acceptance passes with two normal corrections and one stale cancellation, followed by a clean strict-runner exit 0 (`work/rosy-interior-lifetime-pass.log`). This supersedes the earlier unresolved compatibility status.

17 October rendered inn retest: the replacement inn 131078 binds successfully, finds 80 anchors, and passes keeper-first route generation, but its rendered patron stalls behind another occupant before reaching the keeper. The full movement test fails with counter=False, zero social stops and only 1.52 m travelled. This is a confirmed remaining runtime movement issue, not a missing doorway binding. `work/ModdedInnMovement-red.log` records the failure; `work/ModdedInnMovement-diagnostic.log` includes the blocked waypoint, nearby bodies and remaining route. The diagnostic run ended before completing the probe. No detour behavior change has been made yet. Work was redirected to the user's conversation/flirting additions; resume this movement investigation afterward.

17 October day-rest performance follow-up: when an external city-wall layout has no usable bed, guard rest preparation now waits four real seconds before another geometry survey. This prevents a full wall scan every update while preserving eventual retries after streaming/layout changes. Runtime compile and the 585/585 core suite pass; no Unity/runtime measurement is available until activation succeeds.

17 October rest interruption follow-up: dispatching a guard to an emergency clears its pending daytime-rest assignment, so a guard cannot resume sleeping after handling a crime. The regression passes with 585/585 core checks and the runtime compile remains clean. Unity packaging and live behavior remain blocked by editor activation.

17 October patrol rest source checkpoint: a paired night patrol now records a daylight rest intent at dawn, completes its physical return to its original post, then surveys reachable existing wall beds and walks to a distinct free bed until dusk. The intent is saved, restores during the return, and expires after an unassigned shift. Gate guards remain excluded. The runtime source compile is clean and core 584/584 passes. Unity still cannot package or exercise this because its license activation fails. This does not add a lying-down visual; guards use the existing sheltered idle state at the bedside.

Guard bed save validation now rejects duplicate live claims both before recall and during active wall rest. The regression reproduced the missing rejection before the fix. Core 582/582 and runtime compilation pass; this is still source-only, not a Unity gameplay result.

17 October per-bed follow-up supersedes the whole-building reservation limitation below. Gate shift preparation and authoritative scheduling now allow different bed records in the same wall building, while rejecting duplicate beds. Legacy patrol/refuge journeys without a bed remain exclusive. A regression first failed on the previous whole-building exclusion, then passed with two distinct beds, rejection of a third guard's duplicate claim and restored assignments. Core 582/582 passes; runtime compilation has zero warnings/errors. Unity packaging, sleep rendering and full shift coverage remain pending.

17 October wall-rest foundation: outside-gate shift recall now surveys actual beds in replacement wall interiors and routes a guard to a reachable bedside. `RestBedRecord` survives serialization, holds position until the exact next-shift release, and preserves legacy indoor patrol behavior when absent. Survey caches clear with the settlement. Core tests pass 581/581 and all runtime source compiles with zero warnings/errors. This is source-only: guards currently stand idle at the bedside, one guard reserves the entire wall building, and daytime rest for night patrols is not connected. Sleeping visuals, per-bed sharing of quarters, full shift assignment and native acceptance remain outstanding. No new furniture is injected by this production survey.

Inventory review follow-up: witnessed indoor key theft now calls the finite building-response dispatcher instead of only setting the crime flag. Inventory cleanup also runs on native window changes, so deposits are preserved immediately after closing rather than waiting for a gameplay update. The live regression now returns a taken key and verifies it becomes native dropped loot without replenishing the corpse claim. Runtime compilation remains clean and core 580/580 passes; these runtime assertions still await Unity license renewal and a packaged native run.

Keeper business keys are now source-implemented. Runtime requires a registered native identity, matching workplace building, shop/tavern type and merchant faction classification before requesting the workplace key. Core rejects a mismatched workplace and deduplicates a shop that is also home. An off-site keeper home/business/save-load regression raises the core suite to 580/580. The outdoor scene probe now exercises the native corpse inventory, take-all, persistent key depletion and retained body visual. Runtime source compiles without warnings/errors, but the new native probe is not yet run: the latest Unity build still fails license activation.

Corpse-key inventory adapter is now source-implemented for indoor and outdoor managed bodies. Opening the native inventory is harmless; actual key transfers commit taken-state and invoke witnessed-theft handling. Keys cannot stack. Deposited items are moved to native serializable ground loot when the window closes; unclaimed generated keys remain in the corpse ledger. This currently supplies home keys only, not general NPC equipment or explicit off-site business keys. All runtime source compiles against the installed player's assemblies with `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj` (zero warnings/errors), and core 579/579 passes. New native transfer assertions are added to the Unity build verifier but remain unrun. Unity's latest build still exits on licensing activation failure, so these changes are not packaged or live-tested.

Corpse-key ownership foundation now has `InitializeCorpseHomeKey`, resolving registered household/native homes and retired household generations rather than the death location. Repeated initialization preserves taken keys; native household heads receive no duplicate key from their two identity representations. A bedless native workplace is not assumed to be a home. Core suite: 579/579. This API is not yet connected to corpse inventory UI; business access for keepers living elsewhere still needs an explicit keeper ownership binding. These source changes await a Unity build after license renewal.

The controller failure is now traced and corrected in the installed build. On a first visit, exterior routine preparation pauses while indoors, but `Ready` previously stayed false until it finished. Household fallback plans now become ready before exterior surveying; newly calculated visits are published only after their routine ledger is stored. The retest binds inn 131078, finds 80 anchors and resolves keeper 308 with five common-room stops. Core tests remain 576/576 and Unity build passed. Full movement acceptance is still open: the synthetic movement fixture was not registering its forced destination in the authoritative routine ledger. Its source now registers/restores that fixture, but the next Unity build was refused because the editor license expired. This last probe change is not installed or runtime-verified. Evidence: `DFU/work/external-mods/inn-controller-bound-observed-visit-failure.log` and `license-expired.log`.

The subsequent readiness retest waits up to 30 real seconds after closing the entrance UI. It fails with `Live inn controller did not bind after entrance UI closed.` This narrows investigation to controller binding/readiness; a single-frame probe race does not explain the failure. No pathfinding fix is verified. Evidence: `DFU/work/external-mods/inn-binding-readiness-failure.log`. The owned test process exited.

Current display name: **People of the Iliac Bay**. Current installed order is recorded in `EXTERNAL-MOD-LOAD-ORDER.txt`; the initial list below is historical. The filename `livingcities.dfmod`, GUID and `mod_livingcities.txt` save names remain unchanged. Rename build/integration checks pass, as do 576 core tests and native custom-key inventory round-trip checks.

Final-selection retest still fails at inn building 131078: no surveyed doorway ground point in the live patron controller, after six rooms were successfully surveyed. This also occurs with Rosy's Residences disabled and both removed NPC mods absent, so it cannot be dismissed as their conflict. Diagnostic evidence: `DFU/work/external-mods/final-selection-tavern-failure.log`. No speculative doorway-search change was retained.

Latest selection supersedes the initial order/findings below: Lootable Villagers and Families of Daggerfall have been removed from player/editor Mods directories and Mods.json at the user's request. Their archives remain unchanged. Rosy's Residences is disabled; Rosy's Resources stays enabled as a dependency of Beautiful Cities/Villages. The active selection prioritizes Taverns Redone, Beautiful Cities/Villages, Lively Cities and Detailed City Walls. Corpse inventories and property keys belong to Living Cities, not a dependency.

The follow-up native tavern test (Families disabled) failed with `Inn movement probe lacks a surveyed doorway ground point`. This is a confirmed unresolved compatibility issue, not an accepted inn pass. Evidence: LivingCities/work/town-test.log. Doorway geometry investigation is underway.

16 October 2026. Mods installed separately; source archives retained under DFU/downloads.

## Enabled order

- 0: daggerfall expanded textures 1.3.0
- 1: RMB Resource Pack 0.3.0
- 2: DIEP - Rosy's Resources 1.1
- 3: Elves & Beastfolk - Resources 1.1
- 4: New Paintings 0.5.0
- 5: Flat Replacer 0.2.19
- 6: Harvestable Crops 1.2.2
- 7: DET Harvestable Crops 1.0.2
- 8: Windmills of Daggerfall 2.2
- 9: Finding My Religion 1.23
- 10: Fixed Desert Architecture 1.37
- 11: Taverns Redone 0.17
- 12: Detailed City Walls 1.23
- 13: Beautiful Villages of Daggerfall 1.4.2
- 14: Beautiful Cities of Daggerfall 0.5.0
- 15: Lively Cities 1.3.4
- 16: DIEP - Rosy's Residences 1.1
- 17: Elves & Beastfolk - Replaces 1.0
- 18: DIEP - Families of Daggerfall 1.1 — DISABLED (confirmed incompatibility)
- 19: Lootable Villagers 2.0
- 20: BasicRoads
- 21: Living Cities

Names of Tamriel Extended 1.1 is a loose StreamingAssets/Text override, not an entry in the mod list.

## Verified and unresolved findings

- Unity loaded all 20 downloaded bundles and extracted metadata. Required manifest dependencies were present. Load ordering respects non-peer dependency edges; resources precede consumers. Living Cities follows the installed gameplay and layout mods.
- Fresh Gothway Garden checks passed: 121 households, fresh bed capacity limits, movement, interior continuity, death persistence and save/load. This first run also logged an external Families exception; these specific passes do not certify the full stack.
- Families of Daggerfall is installed but disabled. Its FactionNPCInitializer unconditionally reads Player/Assets/StreamingAssets/Factions/Faction.Txt and throws DirectoryNotFoundException. Its TalkWindowScript also destroys native StaticNPC objects and substitutes CustomStaticNPC objects, conflicting with Living Cities binding. Disabling only dialogue would not fix the unconditional initializer.
- Rosy resources and residences remain enabled. Rosy residences load after Lively Cities: where both replace an entire building, Rosy wins. This preserves the selected residence layouts but does not merge conflicting decorations/NPCs. Beautiful Cities/Villages similarly override overlapping base architecture by their declared order.
- Lootable Villagers 2.0 remains enabled. Its source handles vanilla PopulationManager mobile NPC deaths, can spawn additional warriors and creates its own corpse. Managed Living Cities corpse loot/key integration is unfinished; do not claim that installing it adds keys to managed corpses.
- Fresh housing surveys traverse native WorldDataReplacement and therefore see modified RMB/interior beds. Existing saved households and routes lack layout fingerprints and migration. Their old BedCapacity and routes can remain stale. Do not regenerate identities or delete player saves as a workaround.
- DET 1.3.0 logs missing material JSON for its own bootstrap object named daggerfall expanded textures. Decompiled code compares that name against differently capitalized Daggerfall Expanded Textures. No claim that all material errors or all graphics are harmless; no upstream bundle modified.
- Flat Replacer and added racial NPC art need further appearance/binding verification. Existing generated sleep/combat art does not automatically cover new flats.
- Guard bed sleeping and persistent corpse property keys are still integration work, not features supplied automatically by installing these mods.

## Evidence

- DFU/work/external-mods/full-inspection.log and source-inspection.log
- DFU/work/external-mods/full-stack-first-smoke.log
- DFU/work/external-mods/dependency-inventory.json and layout-overlaps.json
- DFU/work/external-mods/Mods.before-full-install.json preserves previous enabled order
- LivingCities/work/external-decompiled contains local inspection only, not redistributed code


<a id="requirements"></a>

# External mod requirements

Latest user selection supersedes the original list below: Lootable Villagers and Families of Daggerfall are removed from both installations and the enabled-mod list. Original archives are retained. Living Cities will implement corpse inventory/keys itself and already owns households/relationships. Rosy's Residences is disabled in favor of the tavern/city/village improvements; Rosy's Resources remains enabled because those improvements require it. See EXTERNAL-MOD-AUDIT.md for the installed order and compatibility evidence.

## Installation checkpoint, 16 October

Later verification: native DFU `NameHelper` generated 400 nonempty names across all eight humanoid banks and both genders using the installed editor override. `DFU/work/external-mods/names-validation.log` records `EXTERNAL_NAMES_VERIFIED` and a clean Unity exit. This verifies generation, not saved-identity migration or visual gameplay.

Property-key integration is in development: custom item and exact-property access, per-corpse persisted claims, and post-transfer inventory observation. `NativeItemTransferHooks` compiles against the player assemblies and observes single/bulk non-stacking transfers only after the item leaves the source and enters the destination. It is not yet attached to corpse inventories; do not claim loot is functional.

The user is downloading archives into `DFU/downloads`. Names of Tamriel Extended 1.1 is installed separately into both player and editor `StreamingAssets/Text/NameGen.txt`. The original RAR remains unchanged. Its JSON omitted a comma between Monster3 sets 2 and 3; the staged copy adds that comma and passes JSON parsing. Both deployed files match SHA-256 `921B560087396021AE55030D5961C2D6010D4AF0B4721CEEEA1D11219857693D`. Runtime name-generation acceptance is pending. Existing saved identities must not be regenerated. This supersedes the initial login request below.

Daggerfall Expanded Textures 1.3.0 and RMB Resource Pack 0.3.0 are installed separately in player/editor Mods folders. Unity successfully loaded both bundles and extracted their actual metadata (`DFU/work/external-mods/inspection.log`, `EXTERNAL_MOD_INSPECTION_SUCCESS`). Player Mods.json enables both before BasicRoads and Living Cities, preserving the existing pair's relative order; its original copy is `DFU/work/external-mods/Mods.before-dependencies.json`. This validates archive loading, not in-game compatibility. Taverns Redone, Lootable Villagers and Detailed City Walls are not yet installed.

Detailed City Walls requires [Daggerfall Expanded Textures](https://www.nexusmods.com/daggerfallunity/mods/307) and [RMB Resource Pack](https://www.nexusmods.com/daggerfallunity/mods/528).

User-approved 16 October 2026. These mods are separately installed dependencies, not assets bundled inside Living Cities.

- Names of Tamriel — https://www.nexusmods.com/daggerfallunity/mods/1310 . Use the installed name generator for newly generated residents while preserving saved identities.
- Taverns Redone — https://www.nexusmods.com/daggerfallunity/mods/43 . Lodging census, room allocation and navigation must use the modified inn geometry.
- Lootable Villagers — https://www.nexusmods.com/daggerfallunity/mods/362 . Managed NPC corpses must offer persistent loot including their home/workplace key. Keys identify a settlement and building, survive inventory/save transitions and open the matching locked house, shop or inn. Prevent duplicate bodies/loot when both mods handle a death. Reuse a suitable native key inventory image if available; otherwise generate one.
- Detailed City Walls — https://www.nexusmods.com/daggerfallunity/mods/221 . Requires Daggerfall Expanded Textures and RMB Resource Pack per its author. Off-duty guards in walled settlements walk into the wall interiors and use unoccupied beds; daytime posts sleep at night and night patrols sleep during the day. Exterior gate guards retire before gates close. This supersedes the earlier off-duty wall-patrol placeholder. Unwalled settlements do not gain guard sleep/rest behavior from this feature.

Installation is pending: Nexus showed a logged-out download gate in the Codex browser. Requested login; do not claim these files are installed until their archives and deployed content are verified. Integration must inspect each mod's actual contents, interfaces and load ordering. Saved vanilla-layout room/route caches need invalidation or migration when replacement geometry changes.
