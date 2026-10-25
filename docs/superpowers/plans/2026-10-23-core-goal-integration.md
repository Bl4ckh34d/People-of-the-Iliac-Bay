# Core Goal Integration Implementation Plan

**Goal:** Connect persistent goal episodes to executable tasks and shared runtime arbitration without inventing effects or rewriting legacy commitments.

**Architecture:** Core owns immutable goal generations and admission provenance. A shared selector chooses feasible work under native safety and duty constraints; existing executors remain responsible for movement and confirmed effects. Source-specific observations determine whether needs are fulfilled.

**Tech Stack:** C# 7.3, .NET Standard Core, Unity/DFU native adapters, existing regression executables.

**Spec:** `docs/2026-10-23-core-integration-acceptance.md` and `docs/2026-10-23-core-systems-audit.md`; concrete arbitration design is being finalized separately.

## Constraints

Preserve existing dirty work, quest/native ownership and finite resources. Use Gemma/Breeze only. Do not resume STT or autobiographical compaction. No build while implementation agents are editing. No broad staging or automatic commits of existing work.

## 1. Persistent goal episodes and task admission

Owner: goal binding agent. Files: `NpcMotivationState.cs`, `NpcTasks.cs`, `NpcTaskStore.cs`, `WorldState.cs`, focused goal-binding regression project.

- [x] Add failing regression cases for changing evidence within an episode, later injury, external cure, legacy migration and forged admission.
- [x] Persist Core-assigned `GoalStartRevision`, preserving it through Active/Suspended changes and terminal transition; create a new generation when a terminal goal reactivates.
- [x] Add optional task `GoalId` and `GoalStartRevision`; validate current known active owner-bound goal at admission.
- [x] Validate immutable historical provenance on restore without requiring the current goal to remain unchanged.
- [x] Migrate schema 45 to 46 conservatively; legacy tasks remain unbound.
- [x] Run focused regression tests and report exact results before integration.

## 2. Shared arbitration and runtime use

- [ ] Finalize selector inputs, result, deterministic ties and native safety/duty precedence from current adapters.
- [x] Test health/safety/work conflicts and unavailable capabilities before implementing the selector.
- [x] Integrate real runtime selection and goal-bound healthcare task admission. Do not leave a pure selector unused by gameplay.
- [x] Recheck capabilities at execution; retain existing action/task single-owner and physical-return contracts.
- [x] Verify that partial treatment keeps the episode active and that only observed native recovery fulfills it.

## 3. Combined delivery

- [x] Review migration, historical bindings and runtime call sites independently.
- [x] Run affected focused suites, the full Core suite and RuntimeCompile.
- [x] Build/install only after agents finish; run native healthcare acceptance against the owned disposable fixture.
- [x] Verify unchanged fixture files and matching dist/installed package hashes.
- [x] Update ROADMAP/Mindmap with verified scope and remaining blockers.

Operational history retention follows its own design and migration; generation provenance must remain available if old receipts are later retired. Completion of this plan does not complete the full Mod goal.
