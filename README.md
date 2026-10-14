# Living Cities for Daggerfall Unity

An in-development standard DFU mod for persistent residents, daily routines, civilian awareness, finite guards, nightlife, and optional local dialogue.

The user approved the scope and recommended architecture on 13 October 2026 and requested continuous implementation, testing, and improvement. The working name is Living Cities.

Target: DFU 1.1.1, Unity 2019.4.40f1, C# 7.3. The simulation core has no Unity dependency. The canonical source is this repository, not the ignored copy deployed under DFU's Assets/Game/Mods directory.

See [milestones](docs/MILESTONES.md), [approved design](docs/superpowers/specs/2026-10-13-living-cities-design.md), and [foundation plan](docs/superpowers/plans/2026-10-13-population-foundation.md).

This is experimental software. Use the isolated portable player and dedicated test saves. Individual milestone completion is recorded only after its acceptance checks pass.

M1 baseline probes are complete. The opt-in `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` verifies native quest activation, in-place original-NPC movement, hide/show and actual save/load reconstruction. See [quest identity evidence](docs/QUEST-IDENTITY.md) for boundaries; broad quest/service compatibility remains part of later milestones.

## Current checkpoint

Version 0.3.23 adds animated carried torches and warm point lights to visible exterior guards at night. Menu controls the toggle, radius and nearest-light budget; daytime, interiors and death extinguish them. See [guard settings and acceptance](docs/GUARDS.md). Native building crime reports still persist before approaches are surveyed, retain destinations through reload and retry once real routes and guards exist. Shopkeepers retain full native identities and head their on-site families, sharing surnames within actual bed capacity. Reserved-bed sleep and service restoration remain available for matching male flats182/0 and182/44. SAVE39 is the living sleeper fixture; SAVE35 contains the daytime Kinghart couple and SAVE38 the separate keeper death. Native combat health, spell damage, bereavement, commuting, travel to bed, broader sleep artwork and remaining guard/civilian routines stay unfinished. See [merchant integration](docs/SHOPPING-AND-STATIONARY-IDENTITIES.md) and [guard response](docs/GUARD-BUILDING-RESPONSE.md).

Source follow-up implements saved two-guard patrol plans and roster assignments, including physical joins, spaced circuits, stop/death/crime interruption, building response from actual positions and return to original posts.215 core tests and DFU world serialization pass. Street route survey and patrol actor/menu integration are not yet wired; installed0.3.23 remains unchanged. Integration and acceptance steps are recorded in [paired patrol plan](docs/superpowers/plans/2026-10-14-paired-guard-patrols.md).

Household capacity equals actual bed count, with one resident per bed and no separate five-person cap. Old saves remain unchanged. Fixture `LivingCitiesTest-f238d317` starts with 181 generated residents and eight children, with inside/outside saves at SAVE24/SAVE25. Native keeper families are added when registered; the original keeper occupies the head slot without a duplicate actor. Larger families occupy larger homes; bedless homes receive no generated residents.

The citizen response pilot observes weapon kills, checks nearby awake witnesses and line of sight, and selects stable adult fight-or-flight behavior. Children flee. Fleeing direct witnesses warn at most two people; secondhand warnings do not cascade. Incidents, spent budgets and reacting identities persist. Interior witnesses follow surveyed exits and report after traveling to a finite guard; missing routes retry without creating guard knowledge. Full stair navigation and individual movement/battle artwork remain incomplete. See [behavior and limitations](docs/CIVILIAN-RESPONSES.md).

Finite guards default on in supported Gothway Garden, with physical building responses, native surrender and timed scene investigations. Broader settlement coverage, paired torch patrols, city-wall duties, enemy defense and nightlife remain unfinished. Further sprite generation is deferred. Existing complete single-sleeper green-blanket/white-pillow assets remain available for two adult appearances.

Native mod settings expose population, awareness, theft/house locks, guards, sleep, schedules and diagnostics. The obsolete household-limit slider is removed. Schema 6 preserves existing identities, deaths, routes and quest contracts, plus bed capacity, native identity, civilian incidents and finite guard records. Broad quest compatibility remains open; the reviewed unchanged delivery pilot passes only within its documented scope.

Use the fresh fixture for current tests; preserve older test characters as historical snapshots. Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-f238d317 -ResponseProbe` for the civilian response test. Full milestones and broad quest compatibility remain incomplete.

Canonical source lives here; `tools/Build.ps1` deploys it into the upstream project's ignored mod folder, runs Unity's `LivingCitiesBuild.VerifyAndBuild`, waits for its exit and requires the success marker plus exit code 0. Run `tools/SmokeTest.ps1` for isolated headless startup checks; it stops only its owned process.

Evidence: `work/unity-build-result.json`, `work/player-smoke.log`, and sibling `../work/livingcities-build.log`. Build artifacts and machine-local logs are ignored by Git. `docs/INTEGRATION.md` records known engine limitations and deferred checks.

