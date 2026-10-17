# People of the Iliac Bay

A Daggerfall Unity mod that brings NPCs to life through persistent households, daily itineraries, visitors, finite guards, crime reactions, and optional local LLM dialogue.

**Development snapshot, not a finished release.** Several NPC categories and quest interactions remain incomplete. Start with the [NPC integration audit](docs/NPC-SYSTEMS.md#inventory) for verified coverage and limitations, rather than assuming every authored NPC is already simulated.

## Project status

Start with the [TL;DR roadmap](docs/ROADMAP.md), [future design/backlog](docs/ROADMAP.md#detailed-design), and [phased delivery plan](docs/ROADMAP.md#delivery-plan). These distinguish implemented behavior from proposed work. For playing and local-model setup, see the [installation guide](docs/INSTALLATION.md): there is no complete installer or published release bundle yet.

- Persistent generated residents, households, local activities and traveler systems are implemented, with remaining movement and arrival bugs.
- Native indoor people have partial identity, reaction, bed and service integration. Outdoor NPCs added by other mods do not yet have universal adoption or homes.
- Finite guards, patrols, building responses, crime scenes and pursuit are implemented across documented paths; broad compatibility remains under test.
- Local dialogue includes world context, disposition, saved per-NPC conversation memories and trusted home map requests. It is experimental and requires a separately configured local model server; unidentified/unadopted NPCs do not yet have persistent memory.
- General main/side/mod quest compatibility is **not complete**. One tightly scoped native delivery quest has automatic household binding.
- The game clock enforces 2× real time and migrates supported saved movement timing. A loaded-game check measured 8 game seconds over 4.01 real seconds. See [acceptance evidence](docs/TESTING.md#latest-acceptance) for scope and limits.

## Documentation

- [Roadmap](docs/ROADMAP.md): priorities, complete future requirements and delivery plan.
- [NPC systems](docs/NPC-SYSTEMS.md): current actor coverage, identity, housing and dialogue boundaries.
- [Compatibility](docs/COMPATIBILITY.md): native/mod quests, external mods and requirements.
- [Installation](docs/INSTALLATION.md): game installation and local LLM setup.
- [Testing](docs/TESTING.md): acceptance status, test saves and fixtures.
- [Development](docs/DEVELOPMENT.md): repository, builds and runtime integration.
- [History](docs/HISTORY.md): dated evidence preserved from the former 63 documents; not current feature status.
## Source layout

- `src/LivingCities.Core`: Unity-independent simulation and persistence.
- `src/LivingCities.QuestHooks`: native quest, service and compatibility hooks.
- `mod/Scripts`: DFU runtime adapters and opt-in integration probes.
- `mod/Textures`, `art`: generated assets, metadata and art documentation.
- `tests/LivingCities.Tests`: executable core regression suite.
- `tools`: deployment, Unity build, runtime compile and owned-process tests.
- `docs`: design decisions, acceptance evidence and explicit unfinished work.

The public project name is **People of the Iliac Bay**. Internal `LivingCities` namespaces, GUID, `livingcities.dfmod` and existing save identifiers remain stable for compatibility.

## Core tests

Install the .NET 8 SDK, then run:

```sh
dotnet run --project tests/LivingCities.Tests --configuration Release
```

GitHub Actions runs this Unity-independent suite. It does not establish in-game compatibility.

## Building and playing

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. Runtime builds require a separate DFU source checkout, player installation, game data, and Harmony. See [repository setup](docs/DEVELOPMENT.md#repository) and [integration notes](docs/DEVELOPMENT.md#integration).

Game files, third-party mod archives/bundles, model weights, extracted native sprite reference directories, saves, logs and build outputs are not supplied as project dependencies. Install external mods separately. See [requirements](docs/COMPATIBILITY.md#requirements) and [tested local order](docs/EXTERNAL-MOD-LOAD-ORDER.txt).

Use dedicated test saves. Source tests and historical native probes do not certify every quest, building layout, mod combination or save migration.
