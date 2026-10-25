# Appraisal 32 run card

This is a synthetic Core appraisal evaluation. It does not run inside the game and cannot commit world state. The separate TownSmoke/native framework acceptance checks the installed mod; it does not make these fixture events native observations.

## Source audit

The fixture has four stable person IDs, seeds and backgrounds, with eight scenarios per profile. `NpcPersonalityGenerator.Generate` deterministically selects three saved traits under personality V3; `CreateRequest` passes the typed profile, numeric mood/affinity, relationship known-state and event-specific evidence to `NpcExperienceAppraisalContract`. The generated prompt serializes those three traits and the supplied evidence. Each prepared row stores exact prompt text and its SHA-256; preserve the JSONL used for any later review.

The initial source audit found that human-readable `caseId` values flowed into receipt, episode, outcome and utterance IDs. `AppraisalFixturePreparation` now derives those identifiers deterministically from SHA-256 with neutral prefixes, so canonical IDs and deduplication keys no longer contain scenario names. The Python validator and Core preflight both reject label or scenario-ID leakage.

Labels are otherwise serialized only under `humanReview.label`; oracle fields are absent from the prepared model payload. `FromNativeOutcome` creates structurally valid fixture events, but its contract does not prove provenance. Treat all 32 cases as synthetic. Injury/status cause is not encoded; speech cases contain the supplied observed utterance, not a verified future action.

## Offline preparation commands

After the parent has built the current evaluator serially from the fixed source, use unique output paths:

```powershell
dotnet run --no-build --project tools/NpcFactorEvaluation -- self-test
dotnet run --no-build --project tools/NpcFactorEvaluation -- prepare-appraisal --output work/npc-factor-evaluation/run/appraisal-32-prepared.jsonl
python tools/NpcFactorEvaluation/validate_appraisal_preparation.py --prepared work/npc-factor-evaluation/run/appraisal-32-prepared.jsonl
```

The driver preflights the prepared rows against current Core, then performs one serial request per row to the loopback `/completion` endpoint. It writes exact request/response bodies and metadata; afterwards `record-appraisal` reconstructs the Core requests and validates every response with `NpcExperienceAppraisalContract.TryParseResponse`. It does not retry, reroll, or commit state.

```powershell
python tools/NpcFactorEvaluation/run_appraisal_evaluation.py --prepared work/npc-factor-evaluation/run/appraisal-32-prepared.jsonl --output work/npc-factor-evaluation/run/appraisal-32-raw.jsonl --validated-output work/npc-factor-evaluation/run/appraisal-32-validated.jsonl
```

`run_serial_evaluation.py` remains for the separate D3 decision then Qwen dialogue corpus; do not use it for appraisals.

The native combined acceptance gate is independent. After TownSmoke evidence is captured for the owned disposable fixture and exact assembly, validate it with:

```powershell
python tools/NpcFactorEvaluation/native_combined_acceptance.py --evidence work/npc-factor-evaluation/run/native-combined-acceptance.json --mod-assembly path\to\PeopleOfTheIliacBayMod.dll
```

## Manual review scorecard

For each case, retain the case ID, prompt hash, raw response, model/runtime metadata, Core parse result and notes. The response must bind to that request's branch, observer, subject, evidence IDs, relationship target and three revisions; a parse rejection stays a failed row.

| Dimension | Score | Review question |
| --- | ---: | --- |
| Evidence and attribution | 0–2 | Did it distinguish observer-known native evidence from unknown attribution or attributed speech? |
| Uncertainty and claims | 0–2 | Did it avoid turning a transfer, absence, quotation, hypothetical or claim into unsupported intent or action? |
| Personality and context | 0–2 | Were all traits and the separate numeric mood/relationship context considered without invented context? |
| Effect calibration | 0–2 | Are mood, affinity, trust, resentment and fear directions/strengths plausible under this policy and bounded inputs? |
| Policy compliance | pass/fail | Did it honor no-show's already-applied affinity penalty, preserve unknown gift value, and avoid direct state/action claims? |

Use 0 for a clear miss, 1 for mixed or weakly supported reasoning, and 2 for a sound, bounded appraisal. Add a short free-text rationale; do not use regex/keyword scoring, demand one exact delta, or treat the numeric summary as a validated world effect. Appraisal outputs remain proposals unless a real native capture ticket is committed through the authoritative store path.
