# Living Cities for Daggerfall Unity

An in-development standard DFU mod for persistent residents, daily routines, civilian awareness, finite guards, nightlife, and optional local dialogue.

The user approved the scope and recommended architecture on 13 October 2026 and requested continuous implementation, testing, and improvement. The working name is Living Cities.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. The simulation core has no Unity dependency. The canonical source is this repository, not the ignored copy deployed under DFU's Assets/Game/Mods directory.

See [milestones](docs/MILESTONES.md), [approved design](docs/superpowers/specs/2026-10-13-living-cities-design.md), and [foundation plan](docs/superpowers/plans/2026-10-13-population-foundation.md).

This is experimental software. Use the isolated portable player and dedicated test saves. Individual milestone completion is recorded only after its acceptance checks pass.

M1 baseline probes are complete. The opt-in `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` verifies native quest activation, in-place original-NPC movement, hide/show and actual save/load reconstruction. See [quest identity evidence](docs/QUEST-IDENTITY.md) for boundaries; broad quest/service compatibility remains part of later milestones.

## Current checkpoint

Version 0.3.3 enforces one resident per bed and household capacity equal to actual bed count, without a separate five-person cap. Old saves remain unchanged. Fresh fixture `LivingCitiesTest-f238d317` has 181 residents and eight children, with inside/outside saves at SAVE24/SAVE25. Larger families occupy larger homes; bedless homes receive no generated residents.

The generated-citizen response pilot observes native weapon kills, checks nearby awake witnesses and line of sight, and selects stable adult fight-or-flight behavior. Children flee. Fleeing direct witnesses warn at most two people; secondhand warnings do not cascade. Incidents, spent budgets and reacting identities persist. Exterior flight retraces the resident's route home; indoor flight currently retreats within the room. See [behavior and limitations](docs/CIVILIAN-RESPONSES.md).

Finite guards, physical shop reinforcements, pursuit/search/escape, original stationary-NPC reactions, full indoor escape and corpse cleanup remain unfinished. Existing native guard spawning remains in this checkpoint. Further sprite generation is deferred. Existing complete single-sleeper green-blanket/white-pillow assets remain available for two adult appearances.

There are nine native settings: enable, physical population, citizen reactions, quest-household pilot, sleeping poses, wake/sleep hours, refresh interval and F8 diagnostics. The obsolete household-limit slider is removed. Schema 5 preserves existing identities, deaths, routes and quest contracts while storing bed capacity and civilian incidents.

Use the fresh fixture for current tests; preserve older test characters as historical snapshots. Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -ResponseProbe` for the civilian response test. Full milestones and broad quest compatibility remain incomplete.

Canonical source lives here; `tools/Build.ps1` deploys it into the upstream project's ignored mod folder, runs Unity's `LivingCitiesBuild.VerifyAndBuild`, waits for its exit and requires the success marker plus exit code 0. Run `tools/SmokeTest.ps1` for isolated headless startup checks; it stops only its owned process.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, and sibling `../work/livingcities-build.log`. Build artifacts and machine-local logs are ignored by Git. `docs/INTEGRATION.md` records known engine limitations and deferred checks.

