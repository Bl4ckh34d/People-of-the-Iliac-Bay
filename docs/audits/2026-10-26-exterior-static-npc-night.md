# Stationary exterior people: night absence

Implemented in `mod/Scripts/PeopleOfTheIliacBayMod.StaticNpcNight.cs`. The helper temporarily deactivates ordinary exterior `StaticNPC` objects during the existing configured sleep window. It reactivates the same objects in the morning. It does not move, destroy, regenerate or replace the people, create homes, or add serialized state. Existing native identities, inventory owners, memories and native object state remain authoritative.

Unresolved Daggerfall haunting additionally requires absence from 19:00 until 05:00, using `SettlementHaunting.StreetsDangerous`. The normal waking window still applies after 05:00. Lifting the curse releases only the haunting condition.

## Ownership and context

The helper selects exterior `StaticNPC.Context.Custom` people under the current settlement directory. Building/dungeon contexts, foreign roots, inactive objects, managed residents, household-assigned native people and known guard roles remain with their existing owner. Initially inactive objects are never claimed. Only active objects hidden by this helper are restored. A saved death, retired bedroom flat, or quest-hidden/destroyed person is never revived by dawn or cleanup.

Native quest resources, native actions, authored individual factions, active questors and faction listeners retain native control. Native `WhenNpcIsAvailable` registers faction listeners only for validated individual factions; this is not an exemption for all merchants. `ActiveQuestor(NPCData)` matches the specific native questor data. Ordinary nonzero factions and exterior merchants have no general exemption.

An open native talk window anywhere in the UI stack postpones absence for its exact stationary speaker. This also protects a detached trade window over the talk window. The next refresh after closure applies absence. Existing verified role context is preserved; the appended context describes the outdoor post, configured hours, night absence, and the live conversation exception without inventing a home, journey, specific work task or performance.

The main mod must call `TickExteriorStaticNpcNight()` after population refresh and call `RestoreExteriorStaticNpcNight()` before new-world reset, load start, save-state restoration and destruction. The context hook appends `ExteriorStaticNpcNightContext(StaticNPC,double)` to the already selected static speaker plan. Settings disable, unavailable current world, interior transitions and directory changes also release this helper's activation changes.

## Verification

The executable regression project compiles the actual production partial and actual Core state against engine boundary doubles:

```powershell
dotnet run --project tests/ExteriorStaticNpcNight.Regression/ExteriorStaticNpcNight.Regression.csproj
```

RED first failed with `Missing production night controller: TickExteriorStaticNpcNight`. GREEN: 15/15 cases pass. These cover exact sleep/wake and haunting boundaries, changed settings, forward/backward time jumps, object/identity/state retention, ordinary nonzero factions, native conversation postponement, foreign/interior/inactive objects, quest and household/guard ownership, death and quest-hidden state during absence, directory/interior transitions, fault/disable/cleanup and Core snapshot restore with a newly created scene body. Output: `outputs/static-npc-night-regression.log`.

Isolated `tools/RuntimeCompile/RuntimeCompile.csproj` compilation against the real Player assemblies passed with zero warnings and zero errors. Output directory: `outputs/static-npc-night-compile`.

## Limits

This verification does not execute Unity callbacks, billboard/render/collider behavior, actual mod-provided exterior layouts, spontaneous conversations, a full native save-file reload or the installed Player. The regression scene and clock boundary are doubles; Core snapshot restore is not a native disk reload. Native build, deployment and Player/Unity runs remain serialized under the main agent and were not started by this task. Exterior decorations with no `StaticNPC` identity, or a mod that assigns an interior context to an exterior actor, are outside this selection contract. Such layouts need direct native evidence before broadening ownership.
