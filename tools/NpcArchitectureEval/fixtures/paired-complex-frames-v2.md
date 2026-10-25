# Paired complex-frame diagnostic fixture v2

Version 2 corrects only the synthetic Wait target catalog and the corresponding expected binding references in the three complex-chain cases. It preserves v1 unchanged.

Parent Core preflight on v1 reported: “A later wait must bind to the preceding static MoveTo destination.” This is a fixture capability and gold-binding correction made before inference. No binder or model change is part of this fixture revision.

## Preserved source and gold

The first three playerText values and attributedHistory arrays remain exact copies, in source order, of dev-chain-3, dev-chain-4, and dev-chain-5 from work/two-pass-production-20261020/cases.json. The native factualContext remains under npcFacts.nativeFactualContext with the explicit data-only label. Conceptual intent, steps, exact source quotes, targets, durations, conditions, and missing facts match v1. Control cases also remain unchanged.

- Source cases.json SHA-256: 600338ecd0c45ea78f430d236756aef4feb4e5ad7cc8961b989d74c4a6a164dd
- Native factual-context capture SHA-256: 43812714c0b58532dfd339444055eb481c519357e40f1fd3a74b090da142fce7
- v2 casesSha256 (SHA-256 over UTF-8 compact JSON cases array, ensure_ascii=false): 5d3b35a57f4fbc5c10396f4c3d19d0a086c1f8fafb61be01abefa6eff3d39fb9

The JSON sourceTrace records source case IDs, source hash, native capture hash, and the reason for this revision.

## Wait candidates

Each main chain now includes wait_doctor_saverys_gear and wait_superior_supply_store. Each Wait candidate's target is an exact clone of its corresponding MoveTo Building target, including synthetic SettlementId and BuildingKey. Labels identify the location. Gold bindings use the Doctor Savery's Gear Wait candidate after the first MoveTo, and the Superior Supply Store Wait candidate after its MoveTo.

All candidate IDs and candidate availability remain synthetic. No coordinates, native availability, NPC willingness, consent, or action execution are asserted. The three controls retain their v1 candidate catalogs.

## Model-payload boundary and binder limit

The existing conceptual_payload projection sends latest player text, attributed history, and npcFacts; it excludes expected/gold fields and candidate catalogs. The grounding projection receives frozen conceptual extraction and current candidate references/labels; it does not receive expected labels or groundingPlan. Neither projection exposes this oracle.

The existing binder enforces ordered step indices and candidate references with matching action kinds, then materializes selected targets. It does not semantically compare targetText with candidate labels or prove native availability. V2 corrects the fixture's expected static-location capability so parent can evaluate the actual Core gold preflight.

## Validation

Validated offline with run_conceptual_goal_pilot._validate_suite against v2 casesSha256. No model, network, service, build, or native call was used.
