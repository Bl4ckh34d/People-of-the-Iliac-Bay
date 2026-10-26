# Perception during attacks on residents — 26 October 2026

## Defects and repair

`BeginEnemyResidentAttack` used distance and line of sight for mobile bystanders,
but omitted facing. Its victim memory always named the source, including when
the victim could not see it. Native `CivilianEntity.Name` was also empty for the
resident attacker in the fixture, producing “I was attacked by .”

The adapter now reuses `SeesSceneEvent` for the victim's identification and for
both the attacker and victim in a mobile witness's view. This includes facing,
sleep, range and native LOS. Inactive or invisible sources cannot establish
visual identity. The attacker is excluded from its own bystander list. Resident
names come from the actual mobile person; other native sources retain their
entity name, with an explicit unidentified fallback when absent.

Perception and the source label are captured before damage. Turning around or
becoming invisible afterward neither grants retrospective knowledge nor erases
a witnessed hit. A damaged victim still remembers an assault when it cannot
identify the source. Motive remains unknown. No damage or an error creates no
new victim/mobile-bystander hit memory. This does not add hearing or infer a
player culprit. The existing stationary attack-attempt path is unchanged.

## Native evidence

Command: `./tools/ProfileInEditor.ps1 -RunName <unique-name> -ValidateSceneObservers`.
The owned editor uses the existing disposable SAVE82 and isolated data directory.

- `enemy-observers-red` stopped at a fixture readiness error: only two eligible
  resident bodies had spawned. It is not evidence of a production defect. The
  fixture now waits up to 75 seconds for three eligible native bodies.
- `enemy-observers-red-02` passes visible observation and fails the victim-facing-
  away case with `I was attacked by . I do not know why. Location: Daggerfall.`
- After correcting victim knowledge and names, `enemy-observers-red-03` passes
  both cases but fails `witness-facing-away; memory=True`.
- `enemy-observers-green` passes all twelve attack cases: visible, victim facing
  away, witness facing away, sleeping witness, invisible source, inactive source,
  distant witness, wall occlusion, no damage, failed callback, turning away after
  a witnessed hit, and turning toward a previously unseen hit. Assertions inspect
  saved observations, actual participant names, the affected person, absence of
  invented player responsibility and exclusion of the attacker as a bystander.
- The same run passes the ten existing stationary attack cases and both mobile
  and stationary health/restore tests. The result is `complete`; no exception
  entry occurs. The existing scene-teardown object-cleanup warning remains.

The new fixture uses three existing resident bodies, real Unity collider LOS and
native nonlethal health changes between the production attack callbacks. Its
attack ledger is a detached snapshot; positions, health, concealment and bindings
are restored without yielding between a case's setup and assertions. It does not
invoke an actual melee swing, projectile or lethal damage. The explicit non-player
source is a resident, not a newly spawned monster. Actual deaths, every monster
type, player-attack producers, indoors, natural sleep and guard interviews remain
separate acceptance work. The same captured mobile-witness list feeds the lethal
branch, but this run does not claim to validate that branch.

## Offline verification

Core 2121/2121; RuntimeCompile and QuestHooks without warnings/errors; source
policy (1369 C# and 116 Python sources), 70 PowerShell syntax checks, tooling
protocols and whitespace checks pass. Build and installed-package status are
recorded in [TESTING](../TESTING.md).
