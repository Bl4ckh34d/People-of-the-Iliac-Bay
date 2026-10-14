# Daggerfall Unity NPC mod workspace

Approved by the user on 13 October 2026. This design covers a standard Daggerfall Unity mod with persistent residents, daily activities, finite guards, civilian stealth, and optional local language-model dialogue. The baseline sections preserve setup evidence at design time. Current implementation status and acceptance evidence live in `docs/MILESTONES.md` in the LivingCities repository.

User addition on 13 October 2026: provide a configurable mod menu with meaningful controls for implemented subsystems. After stable vanilla gameplay, add compatibility patches for city overhauls, added market stalls/buildings, and currently non-interactable vendor NPCs. Detailed constraints and sequencing are in `docs/SETTINGS-AND-COMPATIBILITY.md`.

## Setup as of 13 October 2026

- Workspace: `C:/Users/Daniel/Desktop/Code_Experiments/DFU`.
- `daggerfall-unity/`: official source at `v1.1.1-cve-2025`, commit `fb2627082fabe473bf108c816263b71c6e26ace6`.
- The pinned release declares Unity `2019.4.40f1`, matching `C:/Program Files/Unity/Hub/Editor/2019.4.40f1/Editor/Unity.exe`. Current upstream master declares a newer editor; do not accidentally upgrade this checkout by following master instructions.
- `GameFiles/arena2/`: original game assets from the archive linked by the official installation guide. ZIP CRC verification passed for all 1,688 entries. There are 472 texture files, 17 videos, and the required ARCH3D.BSA, BLOCKS.BSA, MAPS.BSA, DAGGER.SND, and WOODS.WLD files.
- Game archive SHA-256: `8be67a4ebb76ed684968f49d29cbbe57b0c10dc4554d111d64356a2355be5083`. This is a locally measured fingerprint, not an independently published checksum.
- `references/dfunity-mods/`: Hazelnut's source, commit `0af2ec99b5ad7e6722ddc9ce284987d4d6fa262d`. Basic Roads 1.3.1 is copied into `daggerfall-unity/Assets/Game/Mods/BasicRoads/`. This folder is ignored by the upstream repository. The mod was built locally and installed as `Player/DaggerfallUnity_Data/StreamingAssets/Mods/basicroads.dfmod` (112,355 bytes). Unity reopened the bundle, confirmed all 17 assets, and checked all four 500,000-byte path maps.
- `Player/`: official patched Windows x64 DFU 1.1.1 release. The archive matched GitHub's published SHA-256 `b4eb2620cdf31f973ed1105d06be8a31f21796915d65c253a93eeb02cb1d64bd` and passed CRC verification for all 642 entries.
- Unity activation initially blocked batch import. After the user activated the license, import and compilation succeeded with exit code 0. The Basic Roads build also succeeded with exit code 0. Evidence: `work/unity-import-activated.log`, `work/basicroads-build.log`, and `work/baseline-build-result.json`.
- A standalone headless startup loaded Basic Roads, logged `Finished mod init: BasicRoads`, and reached `GameManager ready`. Evidence: `work/player-startup-verified.log`. This validates startup and mod initialization, not visible road rendering or an actual playthrough. The temporary test process was stopped after verification.
- Existing installation `D:/Code_Experiments/DFU` is preserved. It contains source, a player, and original assets. Unity Hub instead references the nonexistent path `D:/Code Experiments/DFU/daggerfall-unity` with a space. Old global editor-mod settings mention a Persistent NPCs mod, but no matching source or package was found in the inspected old mod directories.

The old and new editors ordinarily share DFU's user settings and saves. Use a separate test save. The new standalone player uses DFU's supported `Portable.txt` mode, and its settings point to the new `GameFiles` folder. Its settings and saves remain in `Player/PortableAppdata`. `Open Unity Project.lnk` opens the new source project with the matching editor; `Play DFU.lnk` opens the new standalone player.

The local setup helper is preserved at `tools/DFUBaselineSetup.cs` and copied into `Assets/Game/Mods/DFUSetup/Editor/DFUBaselineSetup.cs`. It packages unchanged upstream Basic Roads source using DFU's source-as-text convention and does not implement any NPC gameplay. To rebuild, run the installed editor in batch mode with `-projectPath` pointing to this source checkout and `-executeMethod DFUBaselineSetup.BuildBasicRoads`. The upstream repository ignores the Mods folder. Unity-generated localization changes were recorded in `work/unity-import-generated.patch` and reverted to retain a clean upstream baseline.

## Confirmed preferences

Use the standard mod format and preserve technical quest bindings and save integrity. Engine changes are allowed only when a concrete integration limitation proves them necessary. Keep classic sprites and add action poses or animations where needed. Ordinary resident and guard deaths persist, with bounded, gradual recruitment and migration. The 14 October amendment allows the player to kill important quest NPCs and break their quests; separate that from default protection against background events. Very small hamlets have no garrison; small towns have a handful of guards; cities have larger rosters. Later crime, recovery, public-warning and family refinements are authoritative in `docs/MILESTONES.md` and its linked specifications.

## Proposed architecture

### Persistent people and households

Give each resident a stable identity derived from settlement, building, and source identity or a deterministic generated slot. Separate that identity from Unity GameObjects and pooled renderers. Amendment on 14 October: the original maximum of three is superseded by up to five for family households (two adults and three children), with most households smaller. Beds and house size are soft allocation preferences: couples and children may share beds. Shopkeepers without living quarters commute to their assigned homes after closing. Household residents share a persistent surname and have explicit family roles. Children stay home or play locally with other children; they do not work, shop, visit taverns, or travel between settlements. See `docs/RELATIONSHIPS-AND-FUTURE-INVENTORIES.md` for current decisions. Visitors do not become household members. Shop staff and existing static NPCs retain their identities, services, factions, and quest bindings. Barracks are a separate garrison facility rather than a residential household.

Use versioned mod save data for the world seed, identity assignments, deaths, recruitment, witnessed incidents, relationships, schedule interruptions, and migrations. Never resurrect or duplicate a resident when a cell or scene reloads. Keep future migrations deterministic and capped by household capacity; recruit guards against roster vacancies rather than spawning them in response to crimes.

### Schedules and continuous time

Construct daily schedules from game time, occupation, service hours, sleep, and seeded preferences. Plans include destination reservations and travel time. Reconstruct unobserved progress analytically from absolute game time and saved interruptions instead of simulating every distant NPC every frame. Rest, fast travel, save/load, midnight, and leaving and returning to a town must produce consistent results.

Priority is quest obligations and scripted events, then immediate threats, then service duties, then ordinary activities. If a quest needs a person at a particular place, preserve that contract. Mobile quest NPCs can follow compatible schedules. Stationary quest NPCs can sleep within their allowed location only when activation and quest behavior remain intact; use a conservative fallback otherwise.

### Movement and activity selection

Start with CPU pathfinding over DFU's city navigation data, augmented by a portal graph connecting streets, doors, interiors, stairs, and validated wall walkways. Cache routes and batch expensive work. Basic Roads provides the wilderness road graph. Its map-pixel connections are not sufficient for precise obstacle avoidance near the player.

Use player distance and visibility for actor activation, with hysteresis and nearby-cell lookahead. Continue virtual schedules beyond the physical actor radius. Geometry availability still constrains where an actor can physically walk. Preserve global coordinates across DFU's world-origin shifts and keep exterior/interior spaces separate.

Choose destinations using attraction weights and actual capacity: markets, shops, guilds, temples, castles, authorities, taverns, houses, gates, and walking routes. Quiet wall corners attract fewer people. The heatmap influences choices; it does not create extra people. Meetings reserve compatible times for both participants. Activities include work, shopping, visiting, conversation, eating, drinking, sitting, walking, and sleeping, with suitable sprites and anchors.

Do not assume every shop has an upstairs bedroom or every wall has a usable walkway. Discover valid anchors from actual layouts, author corrections for exceptions, and report missing anchors. Never place a sleeping sprite through furniture or invent an inaccessible wall patrol.

### Civilian awareness and burglary

Extend DFU's existing crouch and Stealth skill rather than creating a competing skill. Civilian detection combines distance, line of sight, movement/noise, light, skill, and alertness. Sleeping NPCs require stronger stimuli and wake through an explicit state transition. Walls and floors block sight; sound uses a separate bounded model. Detection accumulates over time without frame-rate-dependent random rolls. Shop schedules and door locks agree with existing opening hours and quest requirements.

### Finite guards and crime response

Build a bounded roster per settlement from settlement class, resident population, and validated garrison capacity. Tiny hamlets have zero guards. Small towns have a small contingent. Cities allocate their larger roster among rest, reserve, posts, and patrols. Final counts remain configurable and should be tuned against actual layouts.

Night patrols operate in pairs with torches. Posts cover markets, shops, gates, and accessible wall positions. A reported theft dispatches available nearby guards; each guard travels from its real or reconstructed position. Entry into a shop occurs at the actual arrival time. Off-duty guards take time to mobilize. An exhausted roster produces no instant reinforcements.

Use explicit states: duty, investigating, pursuing, searching the last known area, arrest/combat, returning, resting, fleeing, and dead. Losing line of sight stops exact player tracking. Searches expire and guards return to duty, but a regional criminal record is distinct from immediate pursuit. Preserve DFU's arrest, surrender, punishment, and quest semantics. Guards defend citizens and the player against actually hostile actors according to crime and faction rules.

Vanilla `PlayerEntity.SpawnCityGuards` and `SpawnCityGuard` are called directly from several systems. A feasibility test must prove that a normal mod can intercept all relevant requests, including quest calls, before this subsystem is promised as fully compatible. If it cannot, propose the smallest necessary engine hook. Spawning and immediately deleting vanilla guards is not an acceptable substitute.

### Nightlife and dangerous events

Fewer ordinary residents travel at night, often in small groups around taverns. Evening and nighttime tavern occupancy comes from scheduled residents, not a separate duplicate crowd. The later threat-response clarification uses attackers from vanilla quests and installed mods; this mod does not add a separate random-incursion spawner. Guards intervene against active hostile assassins, thieves, creatures and other attackers, including quest-owned enemies. Nearby citizens flee to safe interiors and report the threat. Lawful defense does not accuse the player of murder; existing crimes remain independently enforceable. Official scripted placement and technical quest bindings retain ownership.

While Daggerfall's haunting is active, civilians overwhelmingly stay indoors. Guards still patrol but flee recognized haunting threats toward reachable unlocked shelters or safe wall access. Do not treat every building or wall as a valid refuge. Read the relevant quest state so ordinary nightlife can return after the haunting is resolved. Do not replace or multiply the main quest's supernatural encounters.

### Optional local dialogue

Use an on-demand, loopback-only llama.cpp process with a quantized 7B instruction model as the initial candidate. Qwen2.5-7B-Instruct Q4_K_M is a reproducible starting point, subject to latency, memory, and dialogue-quality tests on the RTX 4070 SUPER. No model has been downloaded or benchmarked for this project yet.

DFU supplies the authoritative answer, known facts, disposition, social role, lore context, race/culture, gender, current activity, and a stable personality. The model changes wording and tone. It cannot grant rewards, create quests, alter schedules, change disposition, invent knowledge, or execute commands. Free-text input resolves supported topics or grounded small talk. Use bounded context, output checks, response caching, cancellation, and a timeout with the original DFU answer as fallback. Keep the existing topic list. Dialogue must work with the model disabled. Text only is the initial scope; voice is not required.

## Alternatives and tradeoffs

1. **Recommended: standard mod with deterministic simulation and CPU routes.** Best fit for compatibility, testing, and sharing GPU memory with local dialogue. Add a minimal engine hook only after demonstrating a missing interception point.
2. **GPU-first population simulation.** Potentially useful for very large active crowds, but introduces synchronization and tooling costs before a bottleneck is known. Defer until profiling demonstrates a need.
3. **Custom DFU fork.** Gives direct control over guard spawning and engine internals but adds ongoing engine-merge and distribution work. Keep as a fallback, consistent with the user's chosen compatibility target.

## Proposed milestones and acceptance checks

1. **Working baseline and integration probes:** import and compile pinned DFU, build and load Basic Roads, validate game startup, inspect quest-bound NPC movement and guard interception, and prepare an isolated test save.
2. **One-town population slice:** assign households; implement work, travel, and sleep; render persistent residents; save/load state. Verify household caps, no duplicates, no visible spawning, plausible arrival times, and identical progress after leave/return and time skips.
3. **Civilian awareness and activities:** burglary, wake-up behavior, doors, meetings, tavern groups, and sprite poses. Test reachable anchors, closed shops, quest accessibility, and sleeping detection.
4. **Finite guard service:** settlement-specific rosters, posts, patrol pairs, reinforcement travel, pursuit/search/return, hostility response, and bounded replacements. Test repeated crimes without roster growth, wall and interior routes, surrender, deaths, and escaped pursuit.
5. **Threat responses and haunting:** guard intervention and citizen flight for existing vanilla/quest/mod attackers, nightlife distribution, refuges, and the main-quest override. Test threat ownership, innocent-player handling, fear behavior, and haunting resolution.
6. **Local dialogue and broader rollout:** add free-text UI and model service, then test mixed towns and nearby settlements, profiler budgets, save migration, other mods, and fallback behavior.

Implementation is authorized across the seven milestones. Each subsystem receives focused design and integration tests. Current acceptance evidence lives in `docs/MILESTONES.md`; this approved design is not a claim that all gameplay or engine hooks are complete.

## Primary sources

- [DFU patched release](https://github.com/Interkarma/daggerfall-unity/releases/tag/v1.1.1-cve-2025)
- [Official installation guide and game archive](https://github.com/Interkarma/daggerfall-unity/wiki/Installing-Daggerfall-Unity-Cross-Platform)
- [Pinned DFU source](https://github.com/Interkarma/daggerfall-unity/tree/fb2627082fabe473bf108c816263b71c6e26ace6)
- [Basic Roads source](https://github.com/ajrb/dfunity-mods/tree/0af2ec99b5ad7e6722ddc9ce284987d4d6fa262d/BasicRoads)
- [Basic Roads release page](https://www.nexusmods.com/daggerfallunity/mods/134)
- [llama.cpp](https://github.com/ggml-org/llama.cpp)
- [Qwen2.5 7B GGUF model](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF)

Useful source entry points include `PopulationManager`, `CityNavigation`, `MobilePersonMotor`, `StaticNPC`, `IHasModSaveData`, `UIWindowFactory.RegisterCustomUIWindow`, `DaggerfallTalkWindow`, `FormulaHelper.CalculateStealthChance`, `PlayerEntity.SpawnCityGuards`, and Basic Roads' `getPathData`, `getRoadPoint`, `getTrackPoint`, and `getPathsPoint` messages.
