# Closed-shop activities, witnessed events and personal commerce

Authorized follow-up to the in-game reports of 26 October. Work is in progress; this document is not an acceptance report.

## Gameplay

- Replace scattered, player-facing closed-shop waiters with two/three-person conversations, storefront browsing, pauses/walks and feasible nearby Inn diversions. Preserve the original destination; stagger return by up to 45 game minutes after opening without consuming the next appointment.
- Occupied NPCs look at their activity/partners. Only direct conversation and safety priorities interrupt their attention.
- Put actual activity, place, partners and reason in live conversation context. Never infer arrival or a conversation from a timetable or mere proximity.
- Remember directly perceived events separately from live state. Stable per-event random retention favours longer recall, with short and rare long-lived incidental memories. Important witnessed violence remains much longer. Repeating a perception check must not indefinitely renew an event.
- Preserve actual native attribution: witnessing a pursuit is not proof of guilt; hearing a disturbance is not witnessing its attacker.
- Record successful trade item directions, payment and assessment only after its receipt. Record cancellation without claiming a transfer. Supply same-day re-entry greetings with the result.
- Use native merchant cost/trade formulas as the personal-trade baseline, followed by existing disposition/trust/mood/haggling effects. Correct native potion-template misuse in mundane starting goods.
- Add subjective event valence (-5..+5), considering severity, affected person and the observer's relationship. Do not apply duplicate mood changes for events already appraised by the native experience pipeline.

## Integration and verification

1. Extend existing schedule, movement and social adapters; keep preparation bounded across frames.
2. Use bounded typed factual scene records and existing memory/context read boundaries. Existing conversation compaction is month-gated; do not create another speculative archive or LLM compaction service. Document event coverage and gaps, including illness/cure attribution, dungeon episodes and witnessed player harm.
3. Core regression tests: timing, temporal wrappers, expiry/reload/deduplication, importance, source attribution, trade receipt/cancellation and valuation.
4. Native isolated Unity checks: groups/facing/spacing, original-door return, real Inn transition, potion values, trade memories and frame costs. User has closed the player for these tests; verify process state before deployment.
5. Review, update README/testing evidence, build/install, commit and push main. Never present compiled source as an installed or in-game-verified change.

## Current boundaries

Native market-stall semantic anchors are not yet identified. Current browsing targets are real storefronts; do not label them as verified market merchandise. Existing family commitments and emergency/cash/medical priorities take precedence over optional diversions.
