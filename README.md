# People of the Iliac Bay

Updated 26 October 2026: Inn capacity now uses only physical beds. Every bed has one stable ID and one occupant or booking; a family reserves one bed per person. Native rental markers and permanent occupants map directly to exact beds. No floor-area grouping participates in capacity, persistence or admission. Native inventory audits pass all 5 Gothway and all 19 Daggerfall City Inns; the reported Inn263174 also passes ordinary admission, physical doorway completion and interior materialization. After loading, loitering or another time jump, a guest whose scheduled arrival has passed never materializes briefly at the exterior door: after bed admission the guest first appears inside the loaded Inn. Only a continuously visible arrival without catch-up finishes its physical walk through the door. The new policy passes all **2147/2147** current-schema Core checks and RuntimeCompile with no warnings or errors; its updated native assertion still needs a fresh package run. [Evidence and limits](docs/audits/2026-10-26-inn-pending-door-crowd.md). Broader shop, guard-shift and dungeon checks remain in progress. See [Testing](docs/TESTING.md) for package checkpoints, the [playtest guide](docs/PLAYTEST.md) and [roadmap](docs/ROADMAP.md). Normal dialogue uses Gemma + Breeze; Rhubarb uses two CPU threads.

A Daggerfall Unity mod that brings NPCs to life through persistent households, daily itineraries, visitors, finite guards, crime reactions, and optional local LLM dialogue.

**Development snapshot, not a finished release.** Several NPC categories and quest interactions remain incomplete. Start with the [NPC integration audit](docs/NPC-SYSTEMS.md#inventory) for verified coverage and limitations, rather than assuming every authored NPC is already simulated.

Earlier integrated checkpoint **D3CA68C3D919** was installed (26 October), with matching
distribution/Player hashes and Unity package verification. Its Core source passed
**2162/2162** checks. Companion pay now uses **10 gold per actual NPC level** for
each begun 24-hour service block. Successful companion assistance can progress
the relevant skill; expired or suspended service cannot bypass eligibility.
Family grief now requires witnessed, discovered or delivered knowledge of a death,
and preserves greeting suppression across restoration. Focused tests pass;
native acceptance of these latest three changes remains open.
Controlled native guard tests now pass observed and unobserved
door crossings, interior reacquisition and target loss. Theft memory passes22
native mobile/stationary witness cases; private containers were staged in a shop.
Dungeon Follow source and its round-trip probe are included. The earlier native
runs stopped at traveler-fixture readiness. Subsequent native gate repairs pass
all19 tested Inns and physical gate crossings; ordinary admission is now verified
at the reported crowded Inn. Dungeon Follow acceptance remains separate. These scoped
results do not establish all layouts, natural combat or full game reload.

Native family tests confirm both purchases, stable seating and receipt memories
in two Inn layouts. Controlled run15 completes the full outing and physical
homeward journey for household327940 / Inn197127. The original Inn131841 still
fails departure after the seated time skip. The native Bank Enter→Wait
test exposed two fixture defects: its observer blocked the NPC, then its time
budget expired during a long walk. The next run proves task dispatch silently
replaces the longer requested budget with five minutes. The source now preserves
the bounded per-step travel budget; installed native run06 passes exact Bank
entry and subsequent offscreen Wait with unchanged identity, inventory and vitals.
These are not completed roadmap items. See the
[current checklist](docs/WORKLIST-2026-10-26.md), [family Inn audit](docs/audits/2026-10-26-family-cash-visit.md)
and [verification boundaries](docs/TESTING.md). Earlier companion, training and
dungeon slices remain included; their stated native acceptance limits still apply.

The current package also adds repair custody to inventory context,
prevent repair-held wearables being equipped after restoration, and record named
NPC-to-NPC handoffs in both participants' memories. Focused regressions pass;
their native movement, dialogue and game-reload acceptance remain open.

Earlier test bundle **7EC048CD49C7** was installed (26 October, 12:21); Unity package integration and **2131/2131** core checks pass. It includes the street-plan expansion, controlled solitary-shop wait release, and the source implementation of planned family Inn funding. Native linked routes now admit planned family outings. A peaceful native fixture passes morning bank service, one payout, physical home return and subsequent paired departure; final Inn arrival/payment remains open. The earlier empty-wallet bank repair and verified return to the home door remain included. The separate player-violence observer now uses actual body facing: seeing the victim alone does not identify a player behind the observer, and hearing alone does not establish an eyewitness. Eighteen mobile/static native boundary cases pass; actual combat swings, deaths and physical interior encounters remain separate acceptance work. See the [player-violence audit](docs/audits/2026-10-26-player-attack-perception.md). Earlier [non-player attack knowledge](docs/audits/2026-10-26-enemy-attack-knowledge.md), [free-text report delivery](docs/audits/2026-10-26-murder-report-dialogue.md), arrival-hold, housing-cache, trade and urgent-movement repairs remain included. Other dialogue cases, spontaneous encounters, Player performance and full diversion journeys remain open in the [active checklist](docs/WORKLIST-2026-10-26.md).

## Project status

The [street-density investigation](docs/audits/2026-10-26-street-density.md) reproduces
very sparse daytime streets: schedules send most residents home or indoors while
the spawn cap remains unused. Optional outings use a wider seeded departure
window. Ordinary single-destination visits can now enter the full day planner
instead of permanently excluding later activities. The afternoon sample improves
from three to eleven outdoor residents, but broader outdoor activity and routine
balance remain open. The earlier 12:21 checkpoint passed 2131 Core tests and
Unity package verification. Native family routes and peaceful proactive bank trips
through paired departure are verified; complete Inn arrival/payment remains open.

Controlled direct-threat reporting now passes an actual outdoor journey and
delayed dispatch of four existing guards: 10.94 m travelled at an average
8.00 km/h. Routine resumption passes after explicitly clearing the test crime;
free-text threat initiation and naturally resolved guard confrontations remain open;
see the [physical complaint audit](docs/audits/2026-10-26-physical-dialogue-complaint.md).

Native CPU profiling attributes the remaining editor time-skip spike to repeated
billboard replacement lookup. The [climate investigation](docs/audits/2026-10-26-native-climate-profile.md)
separates editor virtual-mod lookup from release behavior. No production texture
optimization or resolution of all reported Player freezes is claimed.

Static shopkeeper personal trade now passes native Talk-to-window handoff, cancellation, partial-stack purchase and resale, with exact gold/UID transfers and unchanged shop shelves. The merchant-specific starting pool no longer creates an uninitialized potion bottle that DFU changes on restore. See the [repair and native evidence](docs/audits/2026-10-26-static-personal-trade.md); other static roles and free-form LLM trade requests still need acceptance.

Start with the [TL;DR roadmap](docs/ROADMAP.md), [future design/backlog](docs/ROADMAP.md#detailed-design), and [phased delivery plan](docs/ROADMAP.md#delivery-plan). These distinguish implemented behavior from proposed work. For playing and local-model setup, see the [installation guide](docs/INSTALLATION.md): there is no complete installer or published release bundle yet.

- Persistent generated residents, households, local activities and traveler systems are implemented, with remaining movement and arrival bugs.
- Native indoor people have partial identity, reaction, bed and service integration. Outdoor NPCs added by other mods do not yet have universal adoption or homes.
- Finite guards, patrols, building responses, crime scenes and pursuit are implemented across documented paths; broad compatibility remains under test.
- Local dialogue includes world context, disposition, shared per-NPC conversation archives and trusted home map requests. Each save references its own memory timeline. [Backups must include the archive](docs/INSTALLATION.md#memory-backups). Dialogue is experimental and requires a separately configured local model server; unidentified/unadopted NPCs do not yet have persistent memory.
- Managed residents use the same saved gold wallet for personal trade and corpse loot. Empty wallets trigger a bank visit, or a home withdrawal in settlements without banks. Withdrawals use a personal allowance independently of the empty saved balance. Native bank-counter movement, the 8–15 second stay, single payout, memory, exit and return to the home door with a continuing daily plan have been tested. Shared outings pause for the errand; an already paid Inn visit can finish. Execution of the remaining outing and family waiting remain pending native acceptance.
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
