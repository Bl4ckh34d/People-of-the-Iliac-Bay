# People of the Iliac Bay

Updated 26 October 2026: the playtest follow-up removes repeated full-world copies during NPC inventory creation, separates overlapping bodies, checks fleeing detours against obstacles, completes native starting outfits, and connects ambient shop purchases to actual stock and wallets. Trade keeps the original **NPC ITEMS** heading, omits the name, and uses a larger centered animated portrait. Trade handoff preserves the actual conversation; returning greetings use the speech pipeline. Rhubarb uses two CPU threads. Core 2121/2121 and focused regressions pass. See [Testing](docs/TESTING.md) for native evidence, installed-package status and remaining acceptance; [performance measurements](docs/audits/2026-10-26-loiter-performance.md) distinguish improvements from remaining stalls. Start with the [short playtest guide](docs/PLAYTEST.md) and [roadmap](docs/ROADMAP.md). Normal play uses Gemma + Breeze.

A Daggerfall Unity mod that brings NPCs to life through persistent households, daily itineraries, visitors, finite guards, crime reactions, and optional local LLM dialogue.

**Development snapshot, not a finished release.** Several NPC categories and quest interactions remain incomplete. Start with the [NPC integration audit](docs/NPC-SYSTEMS.md#inventory) for verified coverage and limitations, rather than assuming every authored NPC is already simulated.

Local test bundle **37348187B72D** is built and installed (26 October, 06:04); Unity package integration and **2121/2121** core checks pass. It adds a semantic request for an NPC to carry an anonymous murder report to a reachable guard outdoors, with consent, actual urgent movement and hearsay memories for both participants. Native delivery and seven local Gemma interpretation cases pass separately. The finite guard system must be enabled; it is enabled in the packaged defaults. Inn-exit repairs, urgent fleeing, event memory, social pricing and closed-shop diversions remain included. Live dialogue willingness, full hostile-speech complaint delivery, stationary encounters and full diversion journeys still need acceptance. The [active implementation and test checklist](docs/WORKLIST-2026-10-26.md) records that scope.

## Project status

Static shopkeeper personal trade now passes native Talk-to-window handoff, cancellation, partial-stack purchase and resale, with exact gold/UID transfers and unchanged shop shelves. The merchant-specific starting pool no longer creates an uninitialized potion bottle that DFU changes on restore. See the [repair and native evidence](docs/audits/2026-10-26-static-personal-trade.md); other static roles and free-form LLM trade requests still need acceptance.

Start with the [TL;DR roadmap](docs/ROADMAP.md), [future design/backlog](docs/ROADMAP.md#detailed-design), and [phased delivery plan](docs/ROADMAP.md#delivery-plan). These distinguish implemented behavior from proposed work. For playing and local-model setup, see the [installation guide](docs/INSTALLATION.md): there is no complete installer or published release bundle yet.

- Persistent generated residents, households, local activities and traveler systems are implemented, with remaining movement and arrival bugs.
- Native indoor people have partial identity, reaction, bed and service integration. Outdoor NPCs added by other mods do not yet have universal adoption or homes.
- Finite guards, patrols, building responses, crime scenes and pursuit are implemented across documented paths; broad compatibility remains under test.
- Local dialogue includes world context, disposition, shared per-NPC conversation archives and trusted home map requests. Each save references its own memory timeline. [Backups must include the archive](docs/INSTALLATION.md#memory-backups). Dialogue is experimental and requires a separately configured local model server; unidentified/unadopted NPCs do not yet have persistent memory.
- Managed residents use the same saved gold wallet for personal trade and corpse loot. Empty wallets trigger a bank visit, or a home withdrawal in settlements without banks. Shared outings pause for the errand; an already paid Inn visit can finish. Bank movement and family waiting remain pending native acceptance.
- New adult inventories include native tops/dresses, suitable lower garments, footwear, varied colors and role-weighted jewelry. Equipped items stay out of personal barter. Existing saved possessions and losses remain authoritative.
- Ordinary shopping plans skip stores that would still be closed on arrival. Existing waiting routines choose nearby clear standing/walking positions; native crowd recovery separates overlaps after reconstruction. Local shop buyers select actual available shelf items, pay at the counter, and keep purchases in their persistent inventory; nonbuyers leave without paying.
- General main/side/mod quest compatibility is **not complete**. One tightly scoped native delivery quest has automatic household binding.
- The game clock enforces 2× real time and migrates supported saved movement timing. A loaded-game check measured 8 game seconds over 4.01 real seconds. See [acceptance evidence](docs/TESTING.md#latest-acceptance) for scope and limits.

## Documentation

- [Roadmap](docs/ROADMAP.md): priorities, complete future requirements and delivery plan.
- [NPC systems](docs/NPC-SYSTEMS.md): current actor coverage, identity, housing and dialogue boundaries.
- [Compatibility](docs/COMPATIBILITY.md): native/mod quests, external mods and requirements.
- [Installation](docs/INSTALLATION.md): game installation and local LLM setup.
- [Testing](docs/TESTING.md): acceptance status, test saves and fixtures.
- [Development](docs/DEVELOPMENT.md): repository, builds and runtime integration.
- [No-regex dialogue boundary](docs/NO-REGEX-DIALOGUE.md): the LLM interprets natural-language intent; native adapters validate and execute game actions.
- [Approved NPC motivation framework](docs/superpowers/specs/2026-10-21-npc-motivation-relationships-and-contracts.md): shared personality, mood, relationships, obligations, decisions and future paid companions; [integration map and tests](docs/2026-10-21-framework-integration-map.md). This is historical design context; the simplified current roadmap defines active scope.
- [History](docs/HISTORY.md): dated evidence preserved from the former 63 documents; not current feature status.
## Source layout

- `src/PeopleOfTheIliacBay.Core`: Unity-independent simulation and persistence.
- `src/PeopleOfTheIliacBay.QuestHooks`: native quest, service and compatibility hooks.
- `mod/Scripts`: DFU runtime adapters and opt-in integration probes.
- `mod/Textures`, `art`: generated assets, metadata and art documentation.
- `tests/PeopleOfTheIliacBay.Tests`: executable core regression suite.
- `tools`: deployment, Unity build, runtime compile and owned-process tests.
- `docs`: design decisions, acceptance evidence and explicit unfinished work.

The public name is **People of the Iliac Bay**. Active source, namespaces, assemblies, scripts and test markers use `PeopleOfTheIliacBay`; package builds use `peopleoftheiliacbay.dfmod`. The mod GUID stays stable. Rename-only fixture conversion is handled offline; the runtime does not include personal-save migration aliases.

## Core tests

Install the .NET 8 SDK and PowerShell 7, then run from the repository root:

```sh
pwsh -NoProfile -File tools/RestoreTestDependencies.ps1
dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release
dotnet run --project tests/DecisionAdapter.Tests --configuration Release
dotnet run --project tests/PersonalBarterContract.Regression --configuration Release
dotnet run --project tests/LocalDialogueTransport.Regression --configuration Release
pwsh -NoProfile -File tools/TestToolingSyntax.ps1
pwsh -NoProfile -File tools/TestToolingProtocols.ps1
python tools/check_source_policy.py
```

The source-policy check requires Python 3. The dependency script downloads the pinned official DFU 1.1.1 release, verifies its SHA-256 and extracts only the native serializer and Unity managed test dependencies into `../Player/DaggerfallUnity_Data/Managed`. It does not start or install the game and never overwrites existing assemblies. A mismatched existing serializer fails explicitly. The tests execute outside Unity; some persistence tests deliberately use DFU's real serializer. GitHub Actions runs these checks without a model server or game session. Passing them does not establish in-game compatibility.

## Building and playing

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. Runtime builds require a separate DFU source checkout, player installation, game data, and Harmony. See [repository setup](docs/DEVELOPMENT.md#repository) and [integration notes](docs/DEVELOPMENT.md#integration).

Game files, third-party mod archives/bundles, model weights, extracted native sprite reference directories, saves, logs and build outputs are not supplied as project dependencies. Install external mods separately. See [requirements](docs/COMPATIBILITY.md#requirements) and [tested local order](docs/EXTERNAL-MOD-LOAD-ORDER.txt).

Use dedicated test saves. Source tests and historical native probes do not certify every quest, building layout, mod combination or save migration.

The versioned Windows host launchers are in `tools/host`. When setting up the parent DFU workspace, copy those three scripts to its `tools` directory, alongside `LivingCities`, `Player` and the separately configured model runtimes. They expect that parent layout; do not run the source copies in place. The launcher defaults to Gemma + Breeze when the optional decision-planner setting is absent. See [installation](docs/INSTALLATION.md) for the required runtimes. Local art-production workspaces and generated review outputs are not a complete release asset package.
