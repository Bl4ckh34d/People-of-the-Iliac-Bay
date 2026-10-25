# General parameter feedback implementation plan

**Goal:** Send model-declared missing request parameters back once for correction, while preserving the user's full existing request and native action gates.

**Architecture:** Extend the existing one-attempt feedback path; do not add semantic phrase parsing or another decision model. The Core issue builder detects required Wait duration and explicit goal/step missing-detail lists. A repair may fill previously empty parameters and resolve associated missing-detail entries, but it cannot overwrite existing values, sources, kinds, order, conditions or exclusions. The model remains responsible for semantic resolution; unreported omissions are not universally detectable.

**Tech stack:** Existing C# 7.3 Core, DFU/Unity runtime and owned PowerShell test runner. Keep normal one-pass extraction and optional two-pass behavior unchanged except for feedback eligibility.

## Deliverables and ownership

- [x] Core worker: `NpcGoalTwoPassChat.cs` and focused RED/GREEN `NpcGoalTwoPassTests.cs` cases. Build a bounded issue summary from typed missing lists without interpreting their wording. Keep the native Wait-only required check separate. Test target, quantity, duration, goal-level and partial resolution; reject erased constraints or changed existing values.
- [x] Parent: use general issues in both initial parameter and repair validation. Retain exactly one repair and current-session/context/epoch checks. Update the owned native binding probe to exercise a model-declared missing quantity as well as missing Wait duration, with a real corrective Qwen call and no permitted native effect.
- [ ] Parent: inspect worker changes; run Core/adapter/runtime/Unity checks, install the combined build, then run owned native binding acceptance. Keep live semantic failures and no-target results visible rather than rerolling them into a success claim.
- [ ] Parent: update acceptance evidence, Roadmap and Obsidian with verified scope and limits. Leave the full roadmap goal active.

## Invariants

No player-language regex, keyword tables or handwritten number interpretation. All action durations mean game time. Never require a target or quantity merely because its extracted field is empty: native source binding and implicit single-item requests remain legitimate. Clearing missing-detail entries requires an actual newly filled parameter; step-level clearance requires a fill in that same step. Native target selection, complete-request validation and D3 willingness still precede any commitment. A failed correction produces unresolved feedback, never a default or partial task.

## Parallel independent work

Friendship native acceptance and NPC-specific outdoor/weather snapshot grounding have separate workers/file ownership. They are integrated only after their code and concrete scene/presence assumptions are inspected; a green Core suite does not prove their native acceptance.
