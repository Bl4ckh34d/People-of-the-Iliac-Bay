# D3 speech-candidate appraisal A/B

This offline experiment tests one narrow question: does giving Qwen the already-recorded canonical-order D3 speech-act suggestion improve its existing appraisal? It changes no production Core rule, event, parser bound, or native commit path. Cases remain synthetic fixture evidence.

The source set contains 12 exact speech cases: three utterance classes across four profiles. The held-out D3 cases do not enter this run. The candidate text is the exact D3 choice ID, including `QuotedOrReportedThreat`; the experiment does not refine that category to `QuotedThreat` or `ReportedThreat` from fixture labels.

Arm A sends the current Core appraisal completion body unchanged. Arm B appends one fixed sentence to the prompt: a D3 suggestion marked fallible, not an observation or ground truth. All other body fields, including Core grammar, temperature, token cap, and stop strings, stay fixed. The run counterbalances arm order and sends each row once, for at most 24 Qwen calls. Failed requests remain raw evidence; there are no retries or rerolls.

## Prepare fresh Core source

The D3 suite and result file stay pinned to their frozen hashes. The old Core source (`69b9fc70a8f26c998ada698847bd373c44ce4101af55743544cbeb1aecf73c86`) is retained as a semantic baseline. A fresh Core request file may have a different full-file hash because the Core prompt can evolve. Before reusing D3 labels, the runner verifies all 12 cases against both old and fresh prompt sources and the frozen suite: exact utterance, 17-trait projection, mood/revision, affinity, directed relationship, full observed event, captured time, commitments, eligible consequences, speaker rank, family IDs, and history. It fails closed if any projected fact differs or if nonempty history cannot be proven. The experiment manifest records both source hashes and each semantic projection hash. A changed fact requires a fresh D3 run; a changed prompt hash alone does not.

```powershell
New-Item -ItemType Directory -Path work/npc-factor-evaluation/speech-candidate-ab-20261021
dotnet run -c Release --no-build --project tools/NpcFactorEvaluation -- prepare-appraisal --fixture-version v2 --output work/npc-factor-evaluation/speech-candidate-ab-20261021/prepared-32.jsonl
python tools/NpcFactorEvaluation/validate_appraisal_preparation.py --fixture-version v2 --prepared work/npc-factor-evaluation/speech-candidate-ab-20261021/prepared-32.jsonl
Get-FileHash -Algorithm SHA256 work/npc-factor-evaluation/speech-candidate-ab-current-20261021/prepared-32.jsonl
```

The parent owns the serial Core build and fresh preparation. These commands must use the current Release assembly without rebuilding it. The verified fresh file for this run is pinned at `a1b4ad79046b78814c61e969ebfb01903822728046422eab16baddfba1dc4424`.

## Build A/B request files offline

Replace the quoted path and hash placeholders with the already-verified Qwen model pin. Choose a new output directory that does not exist.

```powershell
python tools/NpcFactorEvaluation/speech_candidate_experiment.py --prepared work/npc-factor-evaluation/speech-candidate-ab-current-20261021/prepared-32.jsonl --d3-results work/npc-factor-evaluation/speech-act-choice-diagnostic-v1-run/results.jsonl --output-root work/npc-factor-evaluation/speech-candidate-ab-current-20261021/prepared-ab --model-path "C:\path\to\pinned-model.gguf" --model-sha256 "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
```

This writes `variant-requests.jsonl`, `pair-manifest.jsonl`, and `experiment.json`. It sends no requests. Review source hashes and request count before inference.

## Run once

For the live run, use another new output directory. The command rebuilds pair artifacts there, validates the current prepared request against Core before sending, then sends 24 serial requests and invokes the experiment-only Core response recorder. Do not reuse the offline-preparation directory.

```powershell
python tools/NpcFactorEvaluation/speech_candidate_experiment.py --prepared work/npc-factor-evaluation/speech-candidate-ab-current-20261021/prepared-32.jsonl --d3-results work/npc-factor-evaluation/speech-act-choice-diagnostic-v1-run/results.jsonl --output-root work/npc-factor-evaluation/speech-candidate-ab-current-20261021/run-01 --model-path "C:\path\to\pinned-model.gguf" --model-sha256 "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" --execute
```

The recorder writes `core-validation.jsonl` with separate `productionRequestExact`, `coreParserValidated`, and `candidateAgreement` fields. A must match the exact current Core body. B must differ only by the fixed prompt note. Both responses use the unchanged `NpcExperienceAppraisalContract.TryParseResponse` against the unchanged fixture request. B is intentionally not reported as an exact production request. Candidate agreement groups `QuotedThreat` and `ReportedThreat` under the D3 `QuotedOrReportedThreat` label; it measures model agreement only, not correctness. Parser-rejected rows have null agreement. Every result sets `nativeCommitAvailable` to false.

## Review

Keep mechanical parsing separate from semantic review. Compare each A/B pair's parsed speech-act output, target, and effect deltas. Report whether Qwen echoed, ignored, or disagreed with the D3 suggestion; do not force the response to match it. Score against visible event/context and manual oracle separately. The suite is small and synthetic; results only justify a larger controlled test, not automatic classifier or Core-policy deployment.
