# Shared magic healing implementation plan

**Goal:** Execute real native self-healing through the existing health goal, task, action and bounded treatment-history lifecycle.

**Architecture:** The native spellbook supplies an exact single-effect CasterOnly Heal-Health candidate. The common health selector compares its real Magicka cost and conservative benefit with rest and potions. The accepted health task carries the immutable spell specification into its action. Only the trusted native transaction can attach a successful treatment receipt. Partial healing replans under the same recovery goal; full observed health permits ordinary recovery and routine return.

**Scope:** Heal-Health only. Cure-Poison, Cure-Disease, hostile casting, spell generation and the standalone experimental magic-care ledger are not wired into this path. Existing native quest, alarm, paid-service and conversation ownership checks remain in force. No new model or GPU service is introduced.

## Core action transaction

- [ ] Add a dedicated regression which admits a goal-bound self-heal task and proves that generic success, wrong spell, stale resources and replay cannot complete it.
- [ ] Add `NpcMagicHealingSpec` to task steps and actions; copy and validate it across admission, dispatch, save and restore.
- [ ] Add `TryCompleteNpcMagicHealingAction` to stage exact HP, MP, action and treatment receipt without callbacks after publication.
- [ ] Extend care continuation and bounded treatment history/recall with exact spell and Magicka provenance. Verify restore, bounded retirement and once-only replay behavior.

## Native adapter

- [ ] Use the real spellbook, exact native HealHealth type, native cost formula and conservative minimum magnitude.
- [ ] Execute one synchronous cast with native event notifications quiesced while provisional HP/MP changes are staged. Restore HP/MP if the Core transaction fails; reject unsupported or lingering effects.
- [ ] Rebaseline `NativeNpcVitalsBinding` only after the exact transaction commits.
- [ ] Add behavioral tests at the native transaction boundary, including failure after provisional mutation.

## Selection and runtime integration

- [ ] Add failing choice tests: insufficient MP, free safe rest versus expendable resources, emergency feasible casting, non-spell candidates carrying MP cost, and no candidate at full health.
- [ ] Add spell candidate kind and explicit Magicka facts/cost; keep ordinary rest preference and urgent time weighting.
- [ ] Discover candidates in `StreetPopulation.HealthCare.cs`, create the existing `HealthTask(care, now)` and let `NpcGoalArbiter` and paid-service interruption authorize it.
- [ ] Copy the bound spell spec at dispatch. Recheck live duty, goal generation, exact spell and resources at execution. Use the same action outcome, position and task reconciliation path.

## Verification and delivery

- [ ] Run focused magic/choice/treatment/goal/task regressions, full Core, RuntimeCompile and relevant native adapter tests. Review the actual changes after all agents finish.
- [ ] Inspect current Unity/game processes and license evidence before a package attempt. Do not interrupt a player's game or claim native acceptance from CPU tests.
- [ ] Update ROADMAP, TESTING and the relevant mindmap systems with verified scope and remaining gaps. Keep the complete mod goal active.
