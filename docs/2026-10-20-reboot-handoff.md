# Reboot handoff — 20 October 2026

The previous handoff was resumed after the manual restart. The personalized friendship native run below is complete. Do not restart the PC or launch another game run automatically.

## Current source status

Friendship accrual uses one stable factor of 85–115% per saved NPC identity. Nominal stages are 24, 30, then 36 game hours; every actual interval is capped at 36 game hours, including jitter. Later stages retain that capped rate. Reopening and loading cannot reroll the factor. Existing earned credit is preserved; no schema change is required.

`ConversationEffects.FriendshipIntervalSeconds` is shared by production and the native acceptance fixture. The fixture now solves an awake HomeDay contact window using actual I0/I1/I2 intervals, skips infeasible NPCs, and rounds explicit test clock advances upward to whole game seconds. It retains exact identity, active/awake home presence, fractional accrual, real disk save/load and baseline cleanup checks.

Parent verification at this source checkpoint: **1272/1272 Core tests**, RuntimeCompile **0 warnings, 0 errors**. Evidence: `work/friendship-cap-parent-green.log` and `work/friendship-cap-runtime.log`. The personalized friendship source was later installed and accepted in the native checkpoint below. The current checkout also contains persistent mood source changes; those later changes have not had native mood acceptance.

## Personalized friendship checkpoint — installed and native-tested, 20 October

Installed `../Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbay.dfmod` SHA-256:
`E6BA89C6F060B637B4851A61CF687C87D059CA041F22C47187AB7587E998CAC9`.

`work/friendship-personalized-20261020/native.log` records a passing owned fixture for begin/close/reopen Talk, the initial +5 cap, shared credit, repeated-utterance and elapsed-alone no-gain controls, fractional accrual and 24/30/36-hour progression, disk save/load, and baseline restore. The selected resident's stable interval factor is `1.0125`: nominal 24/30/36 hours become `24.3000/30.3750/36.0000` hours; stage 2 remains capped at 36 hours. `work/friendship-personalized-20261020/run.log` records the TownTest pass. This supersedes the earlier pending-install/native statements for personalized friendship, including resume step 3 below. The fixture injects `typed-tone=polite`; semantic classification is explicitly `not-tested`.

## Installed checkpoint and bounded native evidence

Installed `../Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbay.dfmod` SHA-256:
`3A70422E7D45C4B81D276518C510FF34FBA0F6EE830831EA558D1F920F4701CB`.

- The prior fixed-rate friendship fixture passes actual home contacts, fractional shared credit, duplicate/reopen resistance, progression through +20, disk persistence and restoration. `work/friendship-native-20261020/indoor-final-native.log`. Semantic tone classification is injected and is not tested by this fixture.
- One bounded repair now handles model-declared missing parameters as well as null Wait durations. Existing intent, action structure, provenance and supplied values remain frozen. Native real-Qwen missing Wait/quantity controls remain incomplete and safely reject without D3 or side effects. A separate live Wait reaches D3 ACCEPT and saves 18 game seconds; its completion was not observed. The controlled accepted Wait separately completes. `work/two-pass-production-20261020/general-GoalBindingProbe-native.log`.
- NPC-specific weather capture proves managed outdoor and static keeper indoor cases. The wider speaker suite still fails because other required role fixtures are missing. Managed indoor capture also passes during friendship acceptance. Persistent conversation mood now has source implementation in the current checkout; full integration and native save/load/reopen acceptance remain pending.
- Dynamic unresolved wording asks for bread quantity without side effects. The wording lifecycle probe passes, but a subsequent accepted Wait reply incorrectly mentions bread; this semantic failure remains open. `work/two-pass-production-20261020/general-ActionWordingProbe-native.log`.
- Optional two-pass extraction remains opt-in. Previous paired diagnostics are historical, not a general model-quality pass. Adapter verification earlier in this checkpoint was 77/77.

## Resume after reboot

1. Inspect current working tree; preserve the large integration checkout. Do not reset, clean or broadly stage it.
2. Build/install the latest source with `./tools/Build.ps1 -InteractiveOnly` when DFU is closed.
3. The personalized friendship fixture already passes at the installed hash above. Do not repeat unless later friendship-source changes affect the interval solver, accrual, or persistence checks. Preserve its dedicated logs; `work/town-test.log` is overwritten by later tests.
4. Continue IMM-09 from current source. Mood commit integration and native continuity acceptance remain open; do not infer them from the friendship pass.

The full mod roadmap remains active and incomplete. This handoff supersedes older checkpoint statements only where explicitly stated.
