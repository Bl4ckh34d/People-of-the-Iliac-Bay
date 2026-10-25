# NPC level distribution — balancing proposal

Status: proposal in response to the user's tentative ranges; not an approved or implemented distribution. The installed native-body correction only supplies level one when an ordinary civilian body otherwise has an invalid zero level.

## Verified healing-potion consequence

The installed schema-48 healthcare acceptance recorded native level 1, health 15/76 and minimum potion gain 14 (`work/life-core-20261023/schema48-healthcare-player.log`, `NPC_HEALTHCARE_START`). The same run completed five actual treatments and explicitly confirmed no extra consumption after full recovery (`NPC_HEALTHCARE_AUTONOMOUS_PASS`). This is consistent with the 61-point deficit and the native regular healing recipe: `5 + 9 * casterLevel`, giving 14 points at level 1. The stronger native recipe gives `5 + 19 * casterLevel`. `FormulaHelper.CalculateCasterLevel` returns the actor level unless a registered formula override changes it.

With the same 61-point deficit, regular potions would require three at level 2 (23 each) or two at level 3 (32 each). These are arithmetic projections, not newly tested gameplay results. Do not buff potions or cap consumption merely to achieve a fixed count: persistent NPC levels, level-appropriate maxima and actual native recipe effects need to agree. The existing level-one fallback prevents invalid zero-level effects but does not implement the proposed population balancing.

Suggested initial levels:

| Population | Range | Distribution |
|---|---|---|
| Ordinary adults | 2–5 | Concentrated at 2–3; experienced people at 4–5 |
| City watch | 6–10 | Concentrated at 7–8 |
| Experienced soldiers/veterans | 8–14 | Depends on recorded training and role |
| Palace guards | 14–22 | Concentrated at 16–18; rare elite bodyguards/commanders at 22–26 |
| Adventurers | 6–22 | Often near the player's level at first generation; some weaker or stronger |

Persist the sampled level with the person. Do not resample or scale an existing person when the player returns. Player level may influence the distribution of newly created adventurers, not ordinary residents or the whole world. Later gains require actual progression. Treat level 22 as a normal generation ceiling rather than a hard lifetime progression cap.

Profession, combat training, rank, adventurer propensity and allegiance remain separate. A level-five smith is not automatically a level-five warrior, and noble rank is not a combat-level grant. Integrate level with class-appropriate skills, health, magicka and equipment rather than changing one native integer. Ordinary residents retain their adventure-averse routines. Strong low-level-player companions should have appropriate wages, interests and willingness, not automatically join because the player asked.

Native DFU `EnemyEntity.SetEnemyCareer()` currently initializes enemy city guards at player level plus three to six. Fixed persistent guard ranges intentionally change that behavior. Avoid native enemy initialization later overwriting the owned persisted profile during a combat transition. Children, exceptional guild masters, quest-owned actors, old-save initialization and guard-to-combat transitions need explicit policies and tests before delivery.

Sources inspected: local `Assets/Scripts/Game/Entities/EnemyEntity.cs`, `FormulaHelper.cs`, `HealHealth.cs`; mod `NpcProfiles.cs`, `NpcActorProgress.cs`, `NativeNpcVitalsBinding.cs`. Current Core profiles do not yet own a persistent level/progression field.
