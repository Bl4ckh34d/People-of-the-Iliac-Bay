# Quest Pack 1 and Quest Actions Extension audit

17 October 2026. These are separately installed third-party mods, not code or assets incorporated into People of the Iliac Bay.

## Installation and evidence

**Planning follow-up, 17 October 2026:** [quest-role design and representative cases](superpowers/specs/2026-10-17-npc-lifecycle-and-quest-dialogue-design.md) and [delivery phase 6](superpowers/plans/2026-10-17-consolidated-delivery-plan.md) consolidate mobility and spoiler-control options. Quest Pack mobility implementation is explicitly deferred. Review must eventually cover each quest's roles/stages, native bindings and disclosure gates; representative source analysis and installation tests are not certification of every quest. Existing native control remains the conservative default.

Installed from the user-supplied archives:

- `Quest Pack 1 dfmod-2-2025-11-20b-1763826811.zip`: Quest Pack 1 **2025.11.20b**.
- `Quest Actions Extension-677-2-0-0-1740947698.zip`: Quest Actions Extension **2.0.0**.

Both bundles target DFU 1.1.1. Player and editor copies match the archive-extracted SHA-256 values. Player load order is BasicRoads 18, Quest Actions Extension 19, Quest Pack 1 20, People of the Iliac Bay 21. Other enabled choices are preserved. The pre-install configuration is retained locally in `work/quest-mod-audit/Mods.before-install.json`.

Quest Pack 1 declares a nonoptional, nonpeer dependency on Quest Actions Extension version 1.12.0. DFU's `ModManager.CheckModDependencies` treats the declared version as a minimum (`IsVersionLowerOrEqual`), so installed 2.0.0 satisfies that version check. This is not proof that every script action is bug-free.

The owned-player startup test passed and registered all nine pack quest lists. The existing live dialogue regression also passed with both mods enabled. A data-only quest pack reporting “No mod loaders found” is expected; it contributes quest assets/lists rather than a runtime initializer. This audit does not claim all 302 quests have been played or that native quest integration is complete.

Local inspection evidence is under ignored `work/quest-mod-audit/`. The original bundles, extracted quest text and decompiled extension source are not redistributed in this repository. Existing NameGen/localization and DET material diagnostics still appear in startup logs; no new clean-whole-stack claim is made.

## Quest inventory

The bundle contains 303 QBN-bearing TextAssets: **302 quest scripts plus one literal `test` fixture**. Metadata exposes 298 loose resources, including the test fixture. Five additional `JHrepchange1`–`JHrepchange5` script assets are linked child quests, not missing orphan entries.

| Quest list | Active entries | Disabled entries |
| --- | ---: | ---: |
| Commoner | 23 | 0 |
| Dark Brotherhood | 25 | 0 |
| Fighters Guild | 41 | 2 |
| Knightly Orders | 47 | 0 |
| Mages Guild | 29 | 1 |
| Misc | 2 | 0 |
| Noble | 4 | 0 |
| Temples | 12 | 4 |
| Thieves Guild | 30 | 0 |
| Total | 213 | 7 |

These are list entries, not all directly offerable quest scripts. Loose quests include chained/supporting content. `QuestList-Misc:6` refers to `JHPCART`, while the available asset is `JHCART`; this is a source-level candidate defect requiring native lookup verification, not a reason to rewrite the downloaded pack silently.

There is no `A0C00Y14` or `S000...` asset/list reference in the pack. It does not override our narrow vanilla delivery pilot or the main-quest files inspected here. It still uses fixed main-world identities such as Gortwog.

Raw syntax census across all 303 scripts: 258 declare people, 199 declare questors, 117 contain `place npc`, 82 `hide npc`, 90 `place foe`, 134 `create foe`, 150 `killed`, 288 clocks and 85 daily time windows. These overlapping counts identify test coverage needs; they do not establish semantic compatibility.

## Representative NPC risks

| Case | Script and evidence | Required treatment |
| --- | --- | --- |
| Human/monster swap | `JHCAPA01` The Werewolf Hunt, declarations 385–391, hide/place sequence 513–521, kill/reward 528–535 | Preserve distinct Person and Foe resources and scripted visibility. Do not let routine lifecycle resurrect the human or remove the foe |
| Fixed named people and disguised actor | `JHDB121a` To Kill an Ambassador, Br'itsa/Gortwog 326–331, alias/vampire sequence 390–439 | Retain individual identity, alias semantics, quest placement and hostile replacement |
| Captive child/parent | `JHDB109` Going Home, 95–113 and 129–154 | Quest dungeon/home assignment overrides normal family routine; timed hide and parent ambush must remain native |
| Hostage | `JHDB113` To the Wolves, 68–117 | Preserve target, hide/face/dialogue transitions and timer outcomes |
| Priest escort and timed combat | `JHKN119` Rite of Vampire Dispelling, 115–149 and 181–213 | Temple service affiliation does not make this an ordinary staff actor; preserve hidden escort and event clock |
| Relocated service-role people | `JHCAPA01`, 385–411 and 458–489; `JHCG003`, 84–99 | Merchant/apothecary/chemist faction alone is not proof of a permanent shopkeeper or resident |
| Kill target represented as foe | `JHFG106`, 157–170 and 220–224 | Native kill event and foe identity must survive simulation; do not substitute a generic citizen |

All source identifiers above refer to extracted user-installed bundle TextAssets. Representative tests must cover click, item hand-in, reward, scripted relocation, hiding/restoration, timing, foe injury/death and save/load, not merely whether an NPC appears.

## Extension inventory

Quest Actions Extension registers **28 actions/conditions** at game start plus four console commands. It adds no NPC sprite sheets or global household system. Quest Pack 1 adds quest resources, not a global resident census or new animation assets.

| Group | Registered classes |
| --- | --- |
| Player stats/status | `ReducePlayerHealth`, `ReducePlayerFatigue`, `ReducePlayerMagicka`, `IncreasePlayerHealth`, `IncreasePlayerFatigue`, `IncreasePlayerMagicka`, `InfectPlayerWith` |
| Items | `PlayerHandsover`, `PlayerPossesses`, `EquippedWithItem`, `UpdateQuestItemMaterial`, `UpdateQuestItemAddEnchantment`, `UpdateQuestItemApplyMagicTemplate` |
| Time/location/state | `RaiseTime`, `WithinUnits`, `CurrentMapPixel`, `CurrentInBlockPixel`, `PlayerCurrentRegionIs`, `CurrentStateIs` |
| Thresholds/progression | `WhenHealthLevel`, `WhenFatigueLevel`, `WhenMagickaLevel`, `GuildRankAtLeast`, `EnemyHealthLowerHigher`, `KilledEnemiesOfClass`, `MagicEfectKeyIsOn`, `PlayerLegalReputeIs`, `PlayerFactionReputeIs` |

Important interactions:

- `WithinUnits` reads the actual QuestResourceBehaviour transform and vertical difference. It can follow the original moving resource; it cannot locate an unrelated substitute resident. Example: `JHMG222:131`.
- `CurrentMapPixel`, `CurrentInBlockPixel` and region conditions remain player-position checks. Do not satisfy them using an unloaded NPC's scheduled position.
- `RaiseTime` directly advances the game clock. Our schedules, sleep, stays, reporting and guard duties must handle clock jumps. A future 2× clock must change elapsed-time progression without incorrectly scaling explicit quest time jumps or deadlines.
- `GuildRankAtLeast`, legal reputation and faction reputation read native player state. NPC disposition is a separate value; planned guild expulsion must update native membership coherently rather than imitate it only in dialogue.
- `EnemyHealthLowerHigher` and `KilledEnemiesOfClass` depend on native quest foe state. Background cleanup must not count as a kill or lose required injury/death events.

The decompiled `CurrentStateIs.Pattern` appears to omit alternative separators before vampire/wereboar/werewolf forms. This is a candidate upstream parsing defect, not a confirmed gameplay failure or a mod change made by this audit. Check affected syntax in a native parser fixture before considering a compatibility shim. No downloaded bundle was modified.

## Acceptance backlog

- [ ] Native parser/lookup check for every contributed list entry and extension syntax used by the pack, including the `JHPCART` discrepancy.
- [ ] Original-resource identity and no duplicate actor across sleep, return, streaming and save/load.
- [ ] Captive/escort, disguise/monster swap, fixed individual, relocated service actor and kill/reward fixtures.
- [ ] Timed meetings, explicit clock jumps and quest deadlines under the eventual clock-rate policy.
- [ ] Guild/reputation conditions after any future disposition-driven expulsion system.
- [ ] Confirm pending and completed quest cleanup releases simulation ownership without reviving dead people.

Use the [NPC integration policy](NPC-INTEGRATION-AUDIT.md) for implementation ordering. Installing these mods does not expand the current automatic household-binding whitelist.
