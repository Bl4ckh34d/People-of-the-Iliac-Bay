# Core completion implementation plan

**Goal:** Complete the approved core gameplay with six scoped workers, preserving working systems and avoiding invisible complexity.

**Architecture:** Extend existing Core stores and native DFU adapters. Root owns shared integration and serialized native execution. Workers edit disjoint scopes; cross-scope changes require explicit coordination.

**Tech Stack:** C# 7.3, existing .NET regression projects, Unity/DFU native adapters, PowerShell tooling.

**Spec:** `docs/superpowers/specs/2026-10-27-core-completion.md` (read in full).

## Global constraints

- **User explicitly resumed tests on 27 October with "You can resume".** The earlier VRAM-related pause is revoked. All six existing workers may resume isolated offline verification; root continues to own combined builds, Unity, Player and model/TTS runs. Source written during the pause remains unverified until fresh checks actually pass. No extra agents are authorized by the resume.

- User explicitly requests six new workers, each `gpt-6.1-sol` with `high`; no nested workers.
- Current checkout is shared and heavily dirty. Preserve all existing work. No reset, clean, broad rewrite, commit, push, or automatic formatting by workers.
- No Regex anywhere in own code/tools/tests; no keyword substitutes for language interpretation.
- No new assets, migrations, additional model families, hunger, world economics or speculative infrastructure.
- Workers may create narrowly scoped partial files and focused checks. Root owns shared `WorldState.cs`, schema/version changes, aggregate test registration, `StreetPopulation.cs`, `PeopleOfTheIliacBayMod.cs`, central harnesses, ROADMAP, MINDMAP, TESTING and PLAYTEST. Request a narrow hunk or provide an exact integration patch for these files.
- Before edits, each worker reports file ownership and dependencies. Coordinate overlaps directly, keep root informed. New standalone tests belong to the worker; shared Core/runtime build output must not be concurrently mutated.
- No Unity/editor/game/native probe or model-service execution by workers. Root schedules these. Offline checks may run with isolated outputs/copies; ask root before using shared aggregate build directories.
- Existing specs and behavior win over stale checkbox lists. First inspect actual code and evidence; implement actual gaps, not duplicates.
- Per-worker report: `work/core-completion-20261027/<scope>/RESULT.md`, with changed files, exact verification commands/results, integration needs and genuine remaining limits.

## 1. market_people — market, stationary people and commerce

Primary files: `mod/Scripts/NativeMarket*.cs`, `StreetPopulation.Market*.cs`, vendor-specific trade adapters; `tests/MarketCommute.Regression`, `MarketVisits.Regression`, `NativeVendorTrade.Regression`.

- [ ] Audit existing market person/commute/visit/vendor paths against the spec.
- [ ] Make NPC commercial market purchases preserve stock with real money/new item ownership; keep player and private transfers unchanged.
- [ ] Bind audience activity to an actual performer; finish real role, home/bed/identity and closing behavior through existing adapters.
- [ ] Resolve broader vendor-menu and mixed-trade gaps only where evidenced; coordinate common inventory changes with health_services.
- [ ] Exercise focused money/UID/nondepletion, stage presence, commute and interruption cases. Supply concrete native probe commands for root.

## 2. guards_law — guards, investigations and NPC custody

Primary files: `Guard*.cs`, `OutdoorBodyReporting.cs`, `OutdoorGuardResponse.cs`, `StreetPopulation.Custody.cs`, associated Core guard/corpse files. `GuardActor.cs` belongs to this worker; coordinate companion combat adapters rather than rewriting them.

- [ ] Audit current shift, wall, pursuit, haunting and investigation evidence; preserve sight/Halt native passes.
- [ ] Reuse the existing report hold for two-to-five-second physical conversations and nearby factual text, including real inquiry paths.
- [ ] Diagnose stationary witness inquiry dispatch without granting hidden knowledge; finish existing patrol/shift gaps.
- [ ] Implement minimal actual NPC surrender/escort/cell/release path only with verified usable native geometry; no fabricated jail.
- [ ] Implement provenance-based return/confiscation and actual wall-container-or-destruction rule. Coordinate item helpers with market_people before touching common files.
- [ ] Focused checks for knowledge, finite roster, hold cancellation, item ownership and custody lifecycle; supply native scenarios and actual geometry limitations.

## 3. dungeon_companions — traversal, waiting, party rights

Primary files: native dungeon/navigation/follow-specific files, `StreetPopulation.PaidCompanions.cs`, lawful-loot and companion travel adapters. Coordinate location entrances with travel_daily and semantic dialogue integration with dialogue_memory.

- [ ] Inspect existing follow/jump/platform paths and remove only concrete blockers; implement remaining reachable traversal with native abilities.
- [ ] Implement relationship-scaled obstacle wait, nearby dialogue/distant HUD notice, physical exit wait and return to recruitment origin/routine. Use existing relationship values; report chosen numerical bands.
- [ ] Preserve waits over current save/load and distinguish game time from paused real time.
- [ ] Bind expedition-specific loot permission to real request/consent and actual kill attribution; no container ownership inferred from kill permission.
- [ ] Verify existing wage/progression integration without changing agreed economics. Supply bounded traversal, wait and loot regressions plus native acceptance instructions.

## 4. travel_daily — routines, family, lodging and travelers

Primary files: household/routine/traveler/Inn admission/road travel partials; `HouseholdInnPhysicalProbe.cs`; existing traveler/family regressions. Root retains generic navigation and shared population tick wiring.

- [ ] Audit current natural lifecycle, bank/family/childcare and admission paths. Preserve bed-only rules and actual origin identity.
- [ ] Add alternative Inn, eligible guild-bed, then actual outside-settlement camp fallback through existing travel/rest state.
- [ ] Ensure unobserved travel creates no random combat consequences; preserve known routes and identity.
- [ ] Treat identifiable ordinary consumable purchases as consumed on home return while preserving gifts, equipment and task items; coordinate purchase provenance with market_people.
- [ ] Repair family probe null inventory prerequisite (latest `family-payer-player.log`) without manufacturing a production failure or bypassing money checks.
- [ ] Check routine resumption/opening/visible arrival gaps and current save roundtrips. Provide actual native day/family/travel cases for root.

## 5. dialogue_memory — semantic dialogue, biography, memory and task chains

Primary files: dialogue/goal/conversation-specific partials, `LocalDialogueClient*.cs`, Core semantic/biography/scene-memory files, `docs/NO-REGEX-DIALOGUE.md`; existing semantic and single-model regressions.

- [ ] Audit active legacy language overrides and remove them via existing semantic contracts/parsers, preserving cancellation and factual binding.
- [ ] Add compact stable first-relevant-conversation biography and three-to-four consistent family facts through the same LLM; never fabricate navigable entities.
- [ ] Preserve actual memories/context and existing fact summaries; fix evidenced event/context gaps, not introduce a new archive.
- [ ] Handle blocked dependent versus independent task steps truthfully using existing structured action relationships; coordinate loot prompt/context API with dungeon_companions.
- [ ] Focused parser, source/family consistency, consent and interrupted-task checks. No live model calls without root coordination; give root one accepted five-step native scenario that does not force willingness.

## 6. health_services — health, services, performance and persistence evidence

Primary files: `NativeNpcCompanionCast*.cs`, health/service/repair/training-specific adapters and regressions; scoped performance diagnostics. No broad rewrite of common inventory store; coordinate with market_people.

- [ ] Review existing Shield/cure/restore/native service gaps and implement concrete missing behavior using native rules.
- [ ] Finish repair custody/collection and exact selected temple lesson where actual gaps remain; do not build replacement services.
- [ ] Inspect measured skip/stall evidence and bound expensive existing work only for identified causes; no speculative GPU or context removal.
- [ ] Check relevant current-save restoration and retained actual item/health/service results; no old-save migration.
- [ ] Focused exact resource/state tests and reproducible native cases, with candid distinction between missing evidence and real bugs.

## Root integration and acceptance

- [ ] Collect ownership reports before overlapping changes, integrate shared hunks incrementally, resolve schema changes once with no migration.
- [ ] Review each result for player-visible behavior, missing runtime wiring and unnecessary complexity.
- [ ] Run combined current Core/runtime verification using actual project commands after coherent integration; serialize Unity package build and native Player tests.
- [ ] Validate market/money, routine/family, full traveler fallback, actual custody/guard inquiry, relationship waiting, accepted goal chains and health/services on suitable real fixtures. Failed setup is not a production defect or a passing feature.
- [ ] Reconcile ROADMAP/MINDMAP into implemented, game-verified, and genuinely missing distinctions; update mirror and exact build evidence. Capture durable verified outcomes in Obsidian.
