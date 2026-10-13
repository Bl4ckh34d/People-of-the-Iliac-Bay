# Living Cities for Daggerfall Unity

An in-development standard DFU mod for persistent residents, daily routines, civilian awareness, finite guards, nightlife, and optional local dialogue.

The user approved the scope and recommended architecture on 13 October 2026 and requested continuous implementation, testing, and improvement. The working name is Living Cities.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. The simulation core has no Unity dependency. The canonical source is this repository, not the ignored copy deployed under DFU's Assets/Game/Mods directory.

See [milestones](docs/MILESTONES.md), [approved design](docs/superpowers/specs/2026-10-13-living-cities-design.md), and [foundation plan](docs/superpowers/plans/2026-10-13-population-foundation.md).

This is experimental software. Use the isolated portable player and dedicated test saves. Individual milestone completion is recorded only after its acceptance checks pass.

M1 baseline probes are complete. The opt-in `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` verifies native quest activation, in-place original-NPC movement, hide/show and actual save/load reconstruction. See [quest identity evidence](docs/QUEST-IDENTITY.md) for boundaries; broad quest/service compatibility remains part of later milestones.

## Verified foundation

`dotnet run --project tests/LivingCities.Tests` passes 87 behavior tests: family allocation and relationships, household identity/capacity, persistent deaths, deterministic weighted paths, fractional-second schedules, bounded visits, save migration and route persistence, atomic restore rejection, and bounded settings.

The standard `livingcities.dfmod` builds, reopens, and initializes alongside Basic Roads in the isolated player. Unity's integration verifier exercises DFU's real FullSerializer and the mod save adapter. Version 0.3.0 adds family households and home-only child actors to the existing Gothway Garden adult street/interior preview. Nearby actors use a 140-metre activation radius, 165-metre removal radius and an 80-actor exterior cap. Other towns retain vanilla physical populations for now. F8 shows development diagnostics.

The real-player harness verifies inside/outside fixture loads, walking frame changes, door arrival with unchanged identity, re-entry without duplicates, and interior death save/load. Save schema 3 retains route plans and family fields; schema 1/2 saves migrate without regenerating residents or turning legacy adults into children. Old indoor saves without plans show home residents and survey routes after the first exit. Use `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-a2dd8579` for legacy compatibility and `-FixtureCharacter LivingCitiesTest-126494eb` for the family fixture. The test changes only the selected disposable character's test saves.

New households contain up to five residents, with soft preferences from actual bed models and exterior footprints, shared surnames, and saved parent/partner/age/sex fields. The family fixture has 402 residents including 118 children; a five-person family and child appearance survive indoor reload. Children use original boy/girl flats, remain home and receive a brief greeting. Child play, the 12 requested peasant/noble child variants and directional animation coverage remain unfinished. Required assets and sleeping-first production order are recorded in `docs/DIRECTIONAL-CIVILIAN-ANIMATION.md`.

Indoor actors currently stand at conservatively checked reachable floor anchors. Sleep poses/bed occupancy, real shopkeeper/service mapping, quest movement, guard replacement and extended dialogue remain unfinished. Housing profiles do not yet establish ownership or replace vanilla residents/services. Full town connectivity, origin shifts, crowd collision avoidance, route-cache size and large-city performance still need validation. Curved paths and visual polish are deferred in favor of functional milestones.

The latest family requirement allows up to five residents, with shared surnames and local child play. Beds and house size influence family size without a strict one-person-per-bed rule. Existing saved households remain unchanged when the cap changes. See the milestone file for current scope and implementation boundaries; optional future English voice plans are in `docs/FUTURE-DIALOGUE-AND-VOICE.md`.

The native mod settings menu provides seven controls: simulation enable, the Gothway physical-population preview, maximum initial household size (1–5, new settlements only), wake/sleep hours, refresh interval, and F8 diagnostics availability. Settings load through DFU's live callback and are validated again at runtime. More controls accompany their gameplay systems; city-overhaul and market-vendor patches follow vanilla stability. See `docs/SETTINGS-AND-COMPATIBILITY.md`.

Canonical source lives here; `tools/Build.ps1` deploys it into the upstream project's ignored mod folder, runs Unity's `LivingCitiesBuild.VerifyAndBuild`, waits for its exit and requires the success marker plus exit code 0. Run `tools/SmokeTest.ps1` for isolated headless startup checks; it stops only its owned process.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, and sibling `../work/livingcities-build.log`. Build artifacts and machine-local logs are ignored by Git. `docs/INTEGRATION.md` records known engine limitations and deferred checks.

