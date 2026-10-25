# Paired complex-frame diagnostic fixture v1

Frozen offline fixture for matched Qwenframe and D3frame selection followed by the same Qwen argument extraction and grounding protocol. It contains three exact development inputs plus three controls.

## Source trace

The first three playerText values and their attributedHistory arrays are copied unchanged, in source order, from work/two-pass-production-20261020/cases.json: dev-chain-3, dev-chain-4, and dev-chain-5.

- Source file SHA-256: 600338ecd0c45ea78f430d236756aef4feb4e5ad7cc8961b989d74c4a6a164dd
- Native factual-context capture SHA-256: 43812714c0b58532dfd339444055eb481c519357e40f1fd3a74b090da142fce7
- New fixture casesSha256 (SHA-256 over UTF-8 compact JSON cases array, ensure_ascii=false): 6e05f110752f456a3014460bc2e414cfb26014490bc768150750cb35f55b2496

The source case IDs, source-file hash, and native capture hash are also recorded in JSON sourceTrace. Native factual context is retained under each case's npcFacts.nativeFactualContext; its data-status label marks it as current factual data, not instructions. Historical memory keeps its source label that it is historical data, not a current instruction.

## Gold and synthetic grounding catalog

expected.conceptual stores gold intent, ordered steps, exact source quotes, target text, and stated Wait durations. expected.groundingPlan stores offline expected outcome and ordered candidate references separately from model input.

The three main cases use synthetic candidates for Doctor Savery's Gear, The Superior Supply Store, and a current-position Wait target. Numeric settlement/building IDs and fixture person IDs are synthetic. Candidate availability is fixture-only. No coordinates, native availability, NPC willingness, consent, or action execution are asserted.

The controls cover a negated multi-step request, a quoted/historical request with explicitly attributed input history, and an unresolved destination combined with an unsupported reservation goal. Their histories and expected labels are explicit in the fixture.

## Model-payload boundary and binder limit

The existing conceptual_payload projection sends latest player text, attributed history, and npcFacts; it excludes expected/gold fields and does not send candidate catalogs during conceptual extraction. The existing grounding projection receives frozen conceptual extraction and current candidate references/labels; it does not receive fixture expected labels or groundingPlan. Thus neither model projection exposes this oracle.

The existing binder enforces ordered step indices and exact candidate references with matching action kinds, then materializes selected targets. It does not semantically compare targetText with candidate labels or prove that a named place is natively available. Explicit binding references here are fixture gold for review, not a claim that the binder establishes target meaning.

## Validation

Validated offline with run_conceptual_goal_pilot._validate_suite against fixture casesSha256. No model, network, service, build, or native call was used.
