# Personality V3 native acceptance

Installed bundle SHA-256: `2258a0f32a408a81e1fe0e40aca8d13f71bed8c778ca64435fc7aba0a75ad2ad`. Built with Unity 2019.4.40f1 through `tools/Build.ps1 -InteractiveOnly`. Gemma4 E4B Q4 remains the language model; Breeze remains the voice backend. These tests did not exercise actual voice playback.

Seven final native runs used this exact bundle: six passed and one failed semantic acceptance. Evidence is under `work/personality-native-20261023/`, including build logs, deployed source hashes, per-run Player logs, result JSON and `verification.json`.

| Run | Result | Evidence scope |
| --- | --- | --- |
| identity-v3-02 | Pass | Three stable saved traits, FullSerializer round-trip, generic quest protection retains traits/relationships, native Individual suppresses random traits and voice payload, suppressed appraisal cannot recreate them, ordinary positive control works, native social groups 0–4 resolve. |
| fear-v3-green-01 | Pass | Actor ownership/epoch guards, throttle, supplied-time decay, default authored-Individual fear without random-profile creation, unresolved faction rejection, and ten controlled native witness cases. |
| mood-v3-01 | Pass | Two full SubmitFreeText routes, real typed Gemma completion, three-trait profile, controlled mood changes/decay, stale/replay rejection, disk reload and baseline rollback. |
| poison-v3-01 | Pass | Actual native Poison-Nux_Vomica application creates one observer event; Gemma proposes and commits mood −5 exactly once, without attributing the unknown cause to the player. Raw appraisal evidence copied into the run directory. |
| memory-write-v3-01 | Pass | Disk save/load, exact-index parity and same-date branch isolation. |
| memory-read-v3-01 | Pass | Separate-process restart, old-save isolation, separately attributed claims; Gemma correctly recalls the player's silver brooch purchase in Wayrest. |
| architecture-v3-01 | **Fail: 1 semantic case** | Three of three source bindings are valid. Old map memory and injury explanation pass. Gift-cause lookup succeeds with current receipt before publication, but the answer is vague and does not identify the recorded gift. |

The failed gift answer was: “I suppose some good things have simply been happening lately. Nothing to write a song about, really.” It did not invent a giver, but failed `gift-answer-not-substantive`. No retry was used to hide this failure, no answer rewriting or acceptance weakening was added, and no claim of all-usecase Gemma acceptance is made.

## Corrections verified during acceptance

The first identity run failed because the probe incorrectly required every generated resident to have a NativePerson record. Generated residents legitimately have no such record. Only the detached test clone now receives the controlled faction binding; the production identity path is unchanged.

The authored-fear regression first failed on bundle `c0dfc36c`: random-profile eligibility incorrectly controlled whether witnessed fear could be recorded. `ResidentActor.TryObserveAcuteThreatFear` now permits a verified native Individual to use Core's null-profile default fear policy while keeping its random traits suppressed. Unavailable faction data still defers the observation. Severe default fear is 65 with an 8,280-second half-life. The native green run verifies the correction.

The production `CivilianResponses.Threat` quest-protection filter remains intact. It still excludes protected actors before alarm/guard/safety-goal writes. The new authored-fear test exercises the actor seam, not a newly implemented fear-only path for protected quest actors.

## Cleanup and limits

All seven final runs restored settings. The original 739 baseline files were restored and hash-verified; additional generated fixture files were preserved. The final bundle remains installed. No DFU or Unity process remained after cleanup.

The earlier CPU checkpoint passed Core1669, Breeze124 and RuntimeCompile with no warnings/errors. The final native build additionally compiled the probe and fear correction. Native tests use the owned disposable Gothway Garden character and controlled fixtures; they are not a complete campaign playthrough or hours-long battle. Actual QuestResource component coverage, full authored-persona catalog, native dialogue reputation behavior, all native identity paths and V3 voice playback remain open.

Later user requirements for noble rank, combat roles, allegiance and conditional retreat are tracked in ROADMAP and are not implemented by this accepted V3 checkpoint. Genuine generational memory compaction is owned by the coordinated implementation chat.
