# NPC health need and treatment implementation plan

> Execute in this shared checkout with explicit file ownership, test-first core changes, independent review and native verification. The user approved the lifecycle in the preceding architecture review and requested implementation.

**Goal:** An eligible injured resident interrupts routine work, chooses feasible care from an owned healing item, rest, a verified local shop purchase or bounded Guild supply, applies the native effect, and retains grounded care evidence across save/load. The slice also covers local loaded-bed rest with paid multi-night lodging, actual Inn food purchase and sparse initial potion supply.

**Architecture:** Reuse motivation, task, action, inventory, vitals and memory stores. A small persisted care coordinator selects feasible actions; existing action movement remains the only movement owner. Loaded-bed rest, actual local Inn lodging payment, native healing and one-item consumption use authoritative action/effect receipts. Real shop or Guild purchase retains exact source, quoted price and NPC-wallet transaction; Guild assistance is a separate finite source.

**Tech stack:** C# 7.3-compatible Core/runtime, net8 regression harnesses, DFU native Unity runtime.

**Spec:** ROADMAP LIFE-15–21/31/33/39/41/43/44 and `docs/2026-10-23-world-life-simulation-design.md`; approved scope is the first health/work/treatment vertical slice.

## Constraints

- Preserve all existing dirty checkout work. Baseline copied under `../work/healthcare-20261023/baseline`.
- Only living ordinary adult residents with native bodies and known vitals participate. Quest control, combat ownership and established guard rules take precedence.
- Begin routine interruption at <=75% HP; once interrupted, retain recovery ownership until full health. Derive need pressure from real saved vitals independently of delayed emotional appraisal.
- Apply genuine native Heal-Health potions. Never fabricate or refill existing ordinary shop shelves; the explicitly authorized Guild supply is a separate finite, bounded source. Do not use player funds. Unsupported poison/disease treatments and static keeper bodies remain explicit separate adapters.
- A loaded shop can supply only existing, verified, reachable healing stock at its current quoted price. Missing stock, unavailable route or insufficient funds produce a durable blocked reason and bounded retry.
- Verified beds, actual Inn food/lodging purchases and Guild potion supply are in scope only when their bed/service/source is loaded and locally reachable. Lodging nights, access, price, inventory and treatment must use actual native transactions and receipts. Sparse initial potion supply must not duplicate on materialization.
- Initial native support is local loaded resident/action space. Do not claim autonomous cross-town temple treatment or offscreen healing.
- A material change in health, funds, bed or stock availability, quoted price, or reachable local action invalidates the dependent care choice before its next action. Confirmed evidence prevents duplicate purchase, rest booking or treatment.
- Snapshot/revision/epoch validation precedes effects. Restore native item and health state on failure; successful action, inventory and vitals evidence must publish together.
- No external chat messages after the user's stop instruction. No model changes or historical compaction.

## Scope boundary (23 October clarification)

This block covers health need and work interruption; scored care choices among an owned healing item, rest, shop purchase and bounded Guild supply; native potion effect and source-grounded recall; local loaded-bed rest with paid multi-night lodging; actual Inn food purchase; sparse initial potion supply; and the local navigation needed to reach loaded targets. Existing ordinary shop shelves are never fabricated or refilled. Explicitly authorized Guild potion support is a separate finite, bounded source.

Proactive planning questions and replies—including cooldowns, typed planning data kept separate from world facts, fallback planning after unclear/refused/missing answers, replanning when assumptions change, and communication of the selected plan, Inn/bed, booking and expected readiness/departure—are follow-up backlog only. Autonomous city-to-city routes and remote provider discovery, general economy and wages, broad training, and full multi-day companion stays with independent errands or detach/rejoin also remain backlog. These boundaries describe intended scope; they do not claim the listed behavior is implemented.

## Tasks

- [ ] Health need lifecycle: `NpcHealthNeedLifecycle.cs`, `tests/NpcHealthNeed.Regression`. Test injury, escalation, repeat observation, complete healing, reload, unknown/dead/protected identities before implementation.
- [ ] Treatment action: append `UseHealingItem`, extend task acquisition dependencies and add `NpcTreatmentReceipt` plus atomic store commit. Test stale inputs, one-unit native outcome, failure rollback, duplicate completion, stacked items and restore.
- [ ] Persist care episode and pause location: `NpcHealthCare.cs`, WorldState schema44, `tests/NpcHealthCare.Regression`. Test threshold/hysteresis, task ownership, blocked retry, authoritative completion, malformed records and migration.
- [ ] Native effect adapter: `NativeNpcHealingItem.cs`, vitals cache rebase and native probe. Resolve actual potion recipe, call `EntityEffectManager.DrinkPotion`, consume one item, verify improvement, rollback failure.
- [ ] Resident integration: `StreetPopulation.HealthCare.cs`, action/task seams. Observe current resources, preempt incompatible ordinary tasks explicitly, retain physical position, score feasible owned-item/rest/verified local shop/Guild choices, and resume routine from actual completion location.
- [ ] Loaded bed, Inn lodging and food: discover an actually reachable loaded bed, reserve its access, book and charge planned nights once, persist occupancy through reload, and purchase Inn food at the actual counter. Verify native outcomes without implementing general daytime errands or booking extensions.
- [ ] Guild supply and initial inventory: support only the explicitly authorized finite bounded Guild potion source; keep ordinary shop stock untouched. Seed sparse initial potion supply without duplication after reload/materialization.
- [ ] Recall: extend existing source-bound memory read with confirmed treatment and linked purchase receipts. Test patient scope, exact cost, unknown injury cause, future filtering, stale publication and budgets.
- [ ] Verify complete core suite, focused harnesses and runtime compilation. Build/run a controlled native probe if supported; preserve baseline saves/settings and report exact coverage.
- [ ] Review changes, fix issues, update roadmap status with actual evidence and capture reusable results in Obsidian.

## Follow-up backlog (not in this implementation)

- Proactive planning dialogue may ask at most one targeted question plus one clarification per planning episode. The same missing field observes a configured game-time cooldown (24 game hours is an initial test value to calibrate). No answer, refusal or unresolved ambiguity must not block self-planning. Bind replies to the pending question, speaker, time, save branch and plan revision. Replies may set preferences or time constraints, never world facts or action authority.
- Replan when health, funds, bed/stock availability, quoted price, reachable action or obligation materially changes. Superseded questions and late replies cannot alter a new plan or repeat a purchase.
- Communicate the selected plan, proposed/confirmed Inn and bed, booking status, and estimated/confirmed readiness or departure time distinctly. Native evidence alone confirms access, booking, price and payment.
- Autonomous city-to-city routes, remote provider discovery, general economy/wages, broad training, and full multi-day companion stays with independent errands or detach/rejoin remain separate follow-up work.

## Baseline

`dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj`: 1670/1670 passed before implementation.

## Closing checkpoint

The bounded source implementation is complete and installed in bundle `f25585df768ef3419bb59e81825b85ca450087082e25c9b06e6657cf87c9a6ae`. Main Core remains 1670/1670; ten focused suites pass 78 cases. RuntimeCompile and the Unity package/integration build pass. Deployed Core/runtime sources match the checkout.

The final owned native healthcare probe exits 0. It verifies actual potion effect and exact consumption, payload/identity guards, deliberate rollback and replay rejection. The ordinary coordinator independently creates a care task; the resident improves from 15 to 20 HP and Health need intensity from 81 to 74. The exact baseline save is restored and the test process exits. No model/TTS inference is needed, and settings are not rewritten.

Bed/Inn/guild complete native walkthrough and full-recovery routine resumption remain acceptance follow-ups. Their source paths and bounded Core contracts are implemented; this owned-potion probe does not prove those other native paths. Cross-building treatment travel, poison/disease services and proactive planning dialogue remain explicitly outside this block. Inert care metadata for dead residents needs a later terminal-abort cleanup. No broader LIFE checkbox is marked complete.
