# Living Cities for Daggerfall Unity

An in-development standard DFU mod for persistent residents, daily routines, civilian awareness, finite guards, nightlife, and optional local dialogue.

The user approved the scope and recommended architecture on 13 October 2026 and requested continuous implementation, testing, and improvement. The working name is Living Cities.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. The simulation core has no Unity dependency. The canonical source is this repository, not the ignored copy deployed under DFU's Assets/Game/Mods directory.

See [milestones](docs/MILESTONES.md), [approved design](docs/superpowers/specs/2026-10-13-living-cities-design.md), and [foundation plan](docs/superpowers/plans/2026-10-13-population-foundation.md).

Latest 0.3.28 checkpoint adds separate floor nodes and collision-checked stair steps to interior escape routes. In native shop131343, all124 clear upper bedroom samples reach the exit without cutting stair treads; a controlled awake keeper follows its saved path downstairs and exits. Survey measured84ms in this fixture. Existing generated escape, delayed finite reporting and real reload regression pass. Core244/244 pass. Bed-to-floor awakening, broader interior coverage, nonlethal/step/theft waking, stationary mortality and Daggerfall-city support remain unfinished. Earlier0.3.27 added saved Stealth/distance-sensitive waking and neutral hearing reports. See [15 October player follow-up](docs/2026-10-15-PLAYER-TEST-FOLLOWUP.md).

This is experimental software. Use the isolated portable player and dedicated test saves. Individual milestone completion is recorded only after its acceptance checks pass.

M1 baseline probes are complete. The opt-in `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` verifies native quest activation, in-place original-NPC movement, hide/show and actual save/load reconstruction. See [quest identity evidence](docs/QUEST-IDENTITY.md) for boundaries; broad quest/service compatibility remains part of later milestones.

## Current checkpoint

Version 0.3.25 adds exterior enemy defense in Gothway Garden. Existing finite guards detect visible living hostile enemies, select up to two closest reachable responders and use native enemy combat even with enemy infighting disabled. Defense does not accuse an innocent player or clear an existing crime. Saved enemy identities and wounded guard state survive real reload; dead, pacified, missing or disabled targets cause physical return. Player assault transfers a defender back to native criminal response. The guard flame now uses the actual upright native torch210/17 rather than brazier210/0. SAVE46, `Living Cities - guard defense`, contains the native hostile-enemy encounter. Indoor enemy defense, lawful-defense loot/welfare dialogue, broader quest/terrain acceptance and profiling remain unfinished.

Version 0.3.24 connects finite two-guard night patrols to surveyed street circuits, animated walking and existing night lights. The mod menu enables patrols and controls the maximum pair count (0–4, default 2). At least four available guards remain at posts before a pair starts. Saved plans preserve shared circuit phase; dawn, disabling patrols, crime and partner death produce physical returns or responses from current positions. SAVE45, `Living Cities - night patrol`, is the paired patrol fixture. See [guard settings and acceptance](docs/GUARDS.md).

Shopkeepers retain full native identities and head their on-site families, sharing surnames within actual bed capacity. One bed houses only the keeper; the native service actor is not duplicated. SAVE35 contains the daytime Kinghart couple, SAVE38 the keeper death and SAVE39 the living sleeper. Bedless commuting, merchant combat/spells, bereavement, travel to bed and broader sleep artwork remain unfinished. See [merchant integration](docs/SHOPPING-AND-STATIONARY-IDENTITIES.md).

The standard `.dfmod` now packages the runtime DLL compiled by DFU's official mod assembly builder. Equivalent source-text compilation failed during native navigation; the precompiled package passes the live geometry/patrol checks. No tracked base-game scripts are changed. The precise runtime compiler failure remains unproven. Core216/216 pass; further acceptance and remaining scope are recorded in [paired patrol plan](docs/superpowers/plans/2026-10-14-paired-guard-patrols.md).

Household capacity equals actual bed count, with one resident per bed and no separate five-person cap. Old saves remain unchanged. Fixture `LivingCitiesTest-f238d317` starts with 181 generated residents and eight children, with inside/outside saves at SAVE24/SAVE25. Native keeper families are added when registered; the original keeper occupies the head slot without a duplicate actor. Larger families occupy larger homes; bedless homes receive no generated residents.

The citizen response pilot observes weapon kills, checks nearby awake witnesses and line of sight, and selects stable adult fight-or-flight behavior. Children flee. Fleeing direct witnesses warn at most two people; secondhand warnings do not cascade. Incidents, spent budgets and reacting identities persist. Interior witnesses follow surveyed exits and report after traveling to a finite guard; missing routes retry without creating guard knowledge. Full stair navigation and individual movement/battle artwork remain incomplete. See [behavior and limitations](docs/CIVILIAN-RESPONSES.md).

Finite guards default on in supported Gothway Garden, with physical building responses, native surrender, timed scene investigations and paired street patrols at night. Broader settlement coverage, city-wall duties, enemy defense and nightlife remain unfinished. Further sprite generation is deferred. Existing complete single-sleeper green-blanket/white-pillow assets remain available for two adult appearances.

Native mod settings expose population, awareness, theft/house locks, guards, sleep, schedules and diagnostics. The obsolete household-limit slider is removed. Schema 6 preserves existing identities, deaths, routes and quest contracts, plus bed capacity, native identity, civilian incidents and finite guard records. Broad quest compatibility remains open; the reviewed unchanged delivery pilot passes only within its documented scope.

Use the fresh fixture for current tests; preserve older test characters as historical snapshots. Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -ResponseProbe` for the civilian response test. Full milestones and broad quest compatibility remain incomplete.

Canonical source lives here; `tools/Build.ps1` deploys it into the upstream project's ignored mod folder, runs Unity's `LivingCitiesBuild.VerifyAndBuild`, waits for its exit and requires the success marker plus exit code 0. Run `tools/SmokeTest.ps1` for isolated headless startup checks; it stops only its owned process.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, and sibling `../work/livingcities-build.log`. Build artifacts and machine-local logs are ignored by Git. `docs/INTEGRATION.md` records known engine limitations and deferred checks.

