# Mood in receipt-grounded action conversations

## Intended behavior

Use the same saved per-person mood and bounded model result for social replies and action wording. The existing validated action receipt remains the sole authority for actual game effects. Mood describes the NPC's reaction to the exchange; it does not authorize an action, rewrite a receipt, or infer success from prose.

## Implementation ownership

- Core protocol: permit mood output only for social requests or an existing structurally valid typed action receipt. Add optional mood fields to action rendering and preserve receipt constraints, history and the original request.
- Core persistence: two fixed-purpose public methods share one private atomic transaction. Social replies write `model` provenance; action replies write `action`. Both share the same accepted-turn sequence.
- Runtime: capture person, mood and sequence with the request. Parse JSON before duplicate checking. A duplicate rephrase discards the first delta. Only final nonduplicate wording for an unchanged receipt and current session can commit mood and memory once.
- Native acceptance: test one real action-render completion, controlled callback invariants and disk continuity without bypassing receipt validation. Reuse the existing owned fixture helpers rather than duplicating the fixture harness.
- Routing coverage: distinguish an intentionally selected spontaneous offer from a missing social reply. Do not assume every social route must produce a prose-generation request.

## Verification

Core tests cover typed opt-in/default-off, invalid non-social requests, receipt instructions, grammar/cache policy, shared sequence and exact memory provenance after restore. Native checks must cover final accepted mood, invalid and repeated callbacks, duplicate rephrase, stale sessions, changed receipts, and save/load according to the actual probe coverage. Keep unsupported cases explicit; do not claim broad model emotional quality from a neutral live completion.

Previous accepted social checkpoint: [20 October report](../../2026-10-20-persistent-mood-acceptance.md). The full IMM-09 milestone remains open until remaining conversation paths and acceptance requirements are covered.
