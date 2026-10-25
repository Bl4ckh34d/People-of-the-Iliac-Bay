# Schema 48 acceptance checkpoint

Schema 48 was built and locally installed after the implementation agents finished and source review completed. Native serialization, full healthcare recovery, physical actions, conversation commands and reply-outcome checks passed after repairing the controlled fixtures described below. These checks do not establish Gemma semantic quality or complete the overall mod roadmap.

## Verified source checks

- Main Core: 1694/1694, Release; RuntimeCompile: zero warnings/errors.
- Fresh focused suites: retention 12/12, capacity 4/4, admission 10/10, treatment outcomes 7/7, interaction projection 6/6. Writable TEMP/TMP is required for archive tests under this sandbox.
- Full longevity: 10,001 classified tasks and 100,010 actions; both cases pass. Peak retained history is 169 tasks and 1,698 actions. Final retained history is 165/1,650. Maintenance totals 44.138 seconds over 10,002 calls, maximum 13.657 ms. This is a Core simulation, not a native frame-pacing guarantee.
- Evidence: `work/life-core-20261023/temp/schema48-final-*.log`.

## Initial installed package

Distribution and installed bundle SHA256 both equal `47341F1D66E270ACB3FCA8D04AFFBB1C9B6ECB7E5F4582BF82B31CB9285CA699`. Unity reported `PEOPLE_OF_THE_ILIAC_BAY_BUILD_SUCCESS` and the build command exited zero. The restricted attempt could not access Unity's license IPC; the authorized unsandboxed retry succeeded. Portrait cache reused 20 entries and 2,160 textures.

## Native results and open repairs

The actual DFU FullSerializer check passed schema 48, including pruning two old task/step groups, retaining required dependencies and command cursors, and rejecting stale live commands after restore. This is a controlled native fixture, not Gemma semantic acceptance.

The enclosing healthcare probe then failed its autonomous recovery-goal assertion. Source investigation found that the preceding direct adapter checks left a completed, deliberately unbound care task on the same actor. The probe now reloads the exact owned baseline between direct and autonomous phases. This probe-only change is not yet rebuilt or verified in game.

The physical-action run failed after acceptance with `No safe local walking route reaches the target.` The reply-outcome run failed with `Rejected receipt did not append once to same native Talk and persistent store.` Both remain under investigation; neither is waived.

Archived evidence: `schema48-healthcare-first-failure.log`, `schema48-command-first-failure.log`, and `schema48-replies-first-failure.log` under `work/life-core-20261023/`.

After all three runs, all 13 original fixture files still match `fixture-baseline-hashes.json`. No original fixture was saved after mutation. No automatic PC shutdown was requested or performed.

## Final installed checkpoint

Final distribution and installed SHA256: `2ACDBD571E7147F62A2B888F4DB1D90D805A8595888E5378FB6681EBB8E1B8AF`. Build log: `schema48-package-build-final.log`; runtime compilation: `temp/schema48-final-probe-runtime.log`, zero warnings/errors. All 662 deployed runtime/Core source files matched the checkout before the final native tests.

The healthcare run exits zero. Actual native treatment heals 15 to 76 HP with five potions, keeps recovery goal generation 2 for all five treatments, reduces Health need 81 to 0, releases care, physically returns to the ordinary routine, and consumes no extra potion. The schema-48 DFU serializer check also passes in this run. Evidence: `schema48-healthcare-native.log` and `schema48-healthcare-player.log`.

The physical-action/command run exits zero. MoveTo physically arrives, typed Wait completes, same-turn reuse and post-close callbacks reject, following a moved player and cancellation work, and real transfers update inventories exactly once. Inbound player transfer records one ItemReceived projection; delivery moves two real items to another managed NPC. Evidence: `schema48-command-native.log` and `schema48-command-player.log`.

Two fixture corrections resolved the remaining failures without weakening gameplay assertions. Full-health generated vitals are valid before injury; real native injury must create a persisted revision before autonomous coordination. The focused indoor action test now waits for the optional Rosy compatibility correction before positioning its observer, avoiding a delayed native teleport during the NPC's approach. It retains the same selected actor and logs both positions. Production navigation and native healing formulas are unchanged.

The reply-outcome gate was rerun against this exact final bundle and exits zero (`schema48-replies-final-native.log`, `schema48-replies-final-player.log`). All 13 original fixture file hashes still match, and the final distribution/installed hashes still agree after all runs.

### Historical second package and native reruns

The reviewed probe corrections compile with zero warnings/errors; TownTest parses successfully. Rebuilt distribution/installed SHA256 is `65FA3D8BD408582FC6F49ACC024BD968FB0D3AA528E07F2C119D712CFB7614AE` (`schema48-package-build-repaired.log`).

The model-free reply-outcome run now exits zero: exact task/action source lookup, wrong proposal and unrelated turn rejection, single accepted/rejected acknowledgment, duplicate callback rejection and baseline restoration all pass. Evidence: `schema48-replies-native.log` and `schema48-replies-player.log`. Its earlier immediate-append assumption conflicted with configured delayed speech presentation. The explicit probe now disables inference only in its owned process, leaving persisted settings and ordinary gameplay unchanged.

Healthcare's second run exposed another fixture assumption: unchanged full-health native vitals need not already have a persisted override. The fresh post-reload baseline was incorrectly required to be persisted before injury. Physical action still fails; improved logging shows the player target moving from `(498.475,483.560,278.600)` to `(500.875,484.150,280.000)` while the NPC approaches. Both remain open, with evidence in `schema48-healthcare-second-failure.log` and `schema48-command-second-failure.log`.

These intermediate failures were resolved by the final checkpoint above. The experience inbox remains unbounded; CORE-04 and the overall mod goal remain open. Population level ranges remain a proposal. The next retention work is specified in `superpowers/plans/2026-10-23-experience-inbox-retention.md`.
