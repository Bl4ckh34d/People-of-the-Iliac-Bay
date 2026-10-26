# Player violence: seeing and hearing — 26 October 2026

## Defect and repair

The player-violence observer accepted a world position rather than the observer's
body. Distance and LOS therefore admitted eyewitnesses even when the player or
victim was behind them. This was a separate path from the previously corrected
non-player attack adapter.

`CivilianResponses.Observe` now accepts the observer transform and uses the shared
scene-facing/LOS test for both player and victim. Inactive bodies are rejected.
The ordinary mobile `Murder` caller and both keeper-violence callers supply the
actual body. Existing supplied sleep state, invisible-player exclusion and indoor
hearing remain separate from visual evidence. Hearing without seeing the source
does not grant direct-witness status or an eyewitness assault memory.

## Native evidence

Run `./tools/ProfileInEditor.ps1 -RunName <unique> -ValidateSceneObservers` with
the game and owned project editor closed. The existing isolated SAVE82 fixture
now includes player-violence checks after the other observer tests.

`player-observers-red` passes visible observation, then fails
`mobile/player-behind; direct=True; heard=False; assaultMemory=True`.
`player-observers-green` passes nine cases for each of a mobile and stationary
body: visible, player behind, victim behind, invisible player, distant player,
supplied sleep state, inactive body, real wall occlusion and hearing without
seeing the player. Both hearing cases retain `heard=True` and `direct=False`.
Assertions check the stored person and assault memory, not just a geometry helper.
The mobile case calls ordinary `CivilianResponses.Murder`; the stationary case
calls the same observer boundary used by interior keeper-violence callers.

All eighteen cases, the twelve non-player attack cases, ten exterior stationary
cases and existing mobile/static health/restore checks pass in the same run.
`result.txt` is `complete`, with no exception entry. The existing scene-teardown
object-cleanup warning remains.

The fixture controls existing native bodies and real collider LOS without yielding
inside a case. It restores positions, active state, concealment and Unity random
state. Outcomes are recorded in detached world snapshots. The hearing case supplies
a valid building incident to the existing hearing contract while bodies remain
outside; it does not prove a physical Inn encounter. No melee swing, damage,
death, waking animation, guard arrival or witness interview is executed here.
Those paths and other event producers remain open. Hearing/eyewitness distinction
does not imply that a noise becomes a new long-term scene-memory category.

## Verification

Core 2121/2121, RuntimeCompile and QuestHooks without warnings/errors, source policy
(1370 C# / 116 Python), 70 PowerShell syntax checks, tooling protocols and whitespace
checks pass. Installed build status is recorded in [TESTING](../TESTING.md).
