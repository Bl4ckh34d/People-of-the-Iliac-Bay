# Operational retention prerequisites implementation plan

**Goal:** Enable bounded task/action history without losing verified treatment facts, fabricating interaction memories, or permitting replay through a fresh operation ID.

**Architecture:** Preserve the installed schema-47 checkpoint while implementing the next source version. Keep treatment recall summaries separate from execution authority. Classify every new execution by a trusted conversation turn, healthcare episode revision, or task step; retain legacy saved work for completion. Only then enable bounded eviction of terminal groups whose effects and projections have durable outcomes.

**Tech stack:** C# Core, DFU/Unity runtime, focused .NET regression executables, native DFU FullSerializer and owned-fixture gameplay probes.

**Spec:** `docs/2026-10-23-operational-history-design.md`.

## Constraints

- Preserve existing saves and current uncommitted changes. Parent owns schema/version integration and final native build.
- No STT or autobiographical memory compaction. No model changes. No excluded external chat messages or shutdown.
- No package build while agents are editing. Serialize builds sharing the Core project.
- A summary is recall evidence, never authorization to repeat a native effect.
- An unavailable interaction outcome must not become a fabricated memory.
- New source identities must reject replay after detail eviction without an unbounded list of historical IDs.

## 1. Bounded treatment recall

- [ ] Add failing cases for verified owned, purchased and rest treatment recall after simulated detail removal; future facts; old history unavailable after bounded overflow; stale snapshot rejection; cross-person isolation and replay after summary overflow.
- [ ] Add a bounded typed per-person outcome history and exact projection API in `NpcTreatmentOutcomeHistory.cs`. Publish only after validating actual treatment and any linked purchase source.
- [ ] Integrate summary selection and authority into `NpcTreatmentRecall.cs` and `NpcMemoryReadSnapshot.cs`, preserving truthful query behavior and source attribution.
- [ ] Add a durable action projection marker so old still-retained actions cannot be reintroduced after summary overflow. Coordinate shared action fields with task 2.
- [ ] Parent integrates the history field and validation/copy migration into `WorldState.cs` after the agent's contract is fixed.

## 2. Explicit interaction projection outcome

- [ ] Add failing tests for reciprocal atomic publication, replay, failed validation, missing physical proof and restore.
- [ ] Replace ambiguous projection state with Pending/Recorded/Unavailable metadata while migrating the existing recorded flag faithfully.
- [ ] Prepare both journal changes and the action marker before publishing any mutation. Preserve exact source identity for deduplication.
- [ ] Wire definitive unavailability to real runtime/restore evidence; leave transient failures Pending. No historical encounter may be inferred merely from a completed action.
- [ ] Review shared action copy/validation and treatment-marker integration.

## 3. Classified admission

- [ ] Specify and test deterministic source identity for conversation turns, healthcare attempts and task steps, including restore, stale revision, wrong owner and fresh-ID replay.
- [ ] Implement typed source paths around private acceptance kernels. Public generic admission must not mint trusted source metadata.
- [ ] Persist source kind/key on accepted task/action rows; legacy rows remain executable without retroactively inventing authority.
- [ ] Convert runtime producers, native probes and affected test fixtures before closing the generic gate. No production test bypass.
- [ ] Parent integrates action-kernel changes after the shared action-field owner freezes.

## 4. Schema and retention integration

- [ ] Integrate the next schema version once shared contracts stabilize. Validate known owners, bounded counts, detached copies and migration immutability.
- [ ] Implement terminal-group eviction only after all dependencies have durable outcomes: care ownership, physical return, paid lodging, failure delivery, transfer projection, interaction disposition and treatment recall.
- [ ] Preserve active task goal origins and resource/source references. Distinguish all-pinned capacity pressure from ordinary rejection.
- [ ] Exercise multiple years beyond former caps, restore and branch boundaries, active pins and repeated source replays. Verify bounded work per maintenance tick.

## 5. Acceptance

- [ ] Review source changes and relevant regressions, then run full Core and RuntimeCompile.
- [ ] Build/install only after all writers freeze. Native tests must cover actual serialization, care, commands, transfers and new retention paths.
- [ ] Compare owned-fixture hashes and deployed sources/package. Update ROADMAP, Mindmap and acceptance evidence with tested scope and remaining gaps.

Schema 47 is already installed and verified separately in `docs/2026-10-23-schema47-acceptance.md`; it is not a claim that the tasks above are complete.

## Source integration checkpoint

Subsequent deployment: schema 48 is now built, installed and passes the three controlled native acceptance runs. Final bundle SHA256 `2ACDBD571E7147F62A2B888F4DB1D90D805A8595888E5378FB6681EBB8E1B8AF`; see `docs/2026-10-23-schema48-acceptance.md` for actual DFU serialization, healthcare, action/command and reply evidence. Later sections retain their source-stage history; their then-pending deployment statements are superseded by this checkpoint. The experience inbox and the broader roadmap remain open.

Schema 48 source now carries bounded treatment outcomes, explicit interaction projection disposition, archive source-action provenance and classified operation admission. Runtime maintenance projects treatment outcomes, finalizes expired unverifiable interactions, and retires expired health lodging in separate four-row batches every two real-time seconds while gameplay is ready and conversation is closed.

The generic new-action/departure and task admission paths now reject missing source metadata/authority. Existing saved legacy work remains eligible for execution. Controlled native fixtures were converted to the production conversation prepare/issue/accept path, or genuine healthcare task dispatch. These are controlled adapter fixtures, not evidence of Gemma's semantic choices. The conversation capture API trusts the native adapter to supply a real canonical player message; it is not a security boundary against hostile in-process callers.

Parent verification after generic-gate closure passes: main Core 1694/1694, admission 10/10, treatment outcome history 7/7, and interaction projection 6/6, all exit zero. Logs are `work/life-core-20261023/temp/schema48-parent-*.log`. RuntimeCompile also passes with zero warnings/errors (`schema48-runtime-replies.log`). TownTest's expected native serialization marker is now schema 48 and its PowerShell syntax passes.

Runtime review caught four authoritative dialogue-reply callers still looking up legacy proposal-derived IDs after canonical admission. They now derive the exact ID from the captured turn/person/scope, distinguish departure IDs, and check the saved proposal/person before acknowledging a result. This compiles; native regression verification remains pending.

This paragraph records the earlier admission checkpoint; terminal-group eviction and capacity hooks were implemented afterward. No schema-48 native deployment is established here. Any subsequent source edits require affected verification again.

### Retention and longevity checkpoint

Final parent rerun after the one-validation optimization and selected-payload checks: main Core 1694/1694 and RuntimeCompile with zero warnings/errors pass. `work/life-core-20261023/temp/schema48-final-longevity.log` passes both full cases: 10,001 tasks, 100,010 actions, peak retained 169/1,698, final retained 165/1,650. Total elapsed is 93.602 seconds; 10,002 maintenance calls total 44.138 seconds, maximum 13.657 ms. The four-candidate runtime sample publishes in 3.302 ms with one full validation. This supersedes the earlier small-world timing below, not the separate population benchmark or native acceptance. All 13 baseline fixture files still match SHA256. Logs `schema48-final-core.log` and `schema48-final-runtime.log` record the fresh compilation checks.

Terminal-group pruning and targeted admission capacity hooks are now implemented. The focused retention suite reports **12/12** after adding transaction-isolation and malformed-selected-action rejection cases. The tests compare the complete `IncludeFields` graph before and after failed validation, and verify a preexisting detached snapshot stays unchanged after success. Capacity passes **4/4**, including full 1,000-task and 10,000-action ledgers, unchanged rejected proposals, and a still-valid retry after releasing a pin. The admission suite passes 10/10 after correcting field-based JSON equality to use `IncludeFields=true`.

The completed full longevity run (`work/life-core-20261023/temp/schema48-longevity-full.log`, exit zero) executes 10,001 classified tasks and 100,010 actions over more than five game years. Task steps are actually dispatched, observed complete and reconciled. Peak detailed history is 169 tasks / 1,698 actions; the final history is 165 / 1,650. Both cases pass, including active step/failure pins and restore/branch capability rejection. The run takes 138.865 seconds; the longest maintenance call is 21.792 ms. A runtime-budget publication at 166 tasks / 1,660 actions takes 11.488 ms. This small-world Core benchmark does not establish native frame pacing or large-world cost.

Independent source review found no additional persisted-reference, replay or atomic-publication defect after the recency and mutation-starvation fixes. Publication now uses a shallow candidate with detached task/action lists and individually copied motivation rows. Selected payloads are checked before deletion; **one** full `CopyValidated` validates all cross-ledger links before only those three owned ledgers are published. The original two-pass implementation was superseded. A 10,000-pinned-action pressure regression passes 4/4 with the other capacity cases.

The representative CPU benchmark creates real `GetOrCreate` settlements, 165 terminal tasks, 1,650 actions, personality profiles and memory journals, with the history owner near the end of the population. Two independent 20-publication runs measured 623 residents at p50 **2.242/1.846 ms**, p95 **10.612/11.145 ms**, max **17.125/16.678 ms**; 3,035 residents at p50 **9.681/8.476 ms**, p95 **12.620/10.425 ms**, max **14.064/10.674 ms**. Idle four-candidate p95 stayed at or below **0.278 ms**; isolated maxima in the larger world reached **17.906/16.722 ms**. These timings predate the small selected-payload preflight added after review. JSON `IncludeFields` graph size was about **2.07/2.94 MB** before pruning, not an exact native FullSerializer size. These measurements do not prove frame pacing in DFU. Final integrated verification and the post-optimization long-run rerun are separate checkpoints. **Schema 47 remains installed; schema-48 package build, installation and native acceptance remain open.**
