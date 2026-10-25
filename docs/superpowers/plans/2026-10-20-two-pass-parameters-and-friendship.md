# Two-pass parameters and native friendship acceptance

**Goal:** Improve actual request decomposition without interpreting player language with regex, and verify gradual friendship in the native game.

**Scope:** Continue the approved NPC architecture. Preserve existing native consent, current-context binding, once-only effects, and game-time semantics. Existing two-stage development variants froze target parameters too early and remain offline.

## Design and ownership

1. Core worker adds `NpcGoalTwoPassChat`: first pass emits only intent and ordered canonical action kinds. Second pass extracts all target references, source spans, durations, quantities and constraints. Its formal grammar fixes the first pass's action count, order and kinds. The existing authoritative `NpcGoalChat.Parse` validates the full result. Both passes retain the same immutable conversation binding and exact original turn. No executable candidate menu enters extraction.
2. Evaluation worker prepares twelve reviewed cases with expectations separate from model inputs, including the three existing development cases and fresh positive/negative cases. Parent runs one-pass and two-pass variants once with the same model, factual context and history. Preserve raw payloads, failures, timing and actual Core parsing. Do not tune or retry until a desired score appears.
3. Parent adds opt-in runtime dispatch; normal launch remains one-pass unless evidence supports promotion. Each stage must consume its own current context token before starting the next stage. Failure, close, new speaker, scene change or save epoch must prevent late interpretation from continuing. No fallback may silently replace the full request with a simpler action. Detected missing Wait duration receives one bounded feedback pass in both strategies; a valid prior proposal's unrelated fields must remain unchanged. Invalid/still-incomplete repair rejects without a default or another retry.
4. Native worker adds an explicitly owned friendship probe through the existing typed social-effect boundary and real native game clock/save path. Verify per-conversation/shared credit, fractional accrual, increasing intervals, repeated-line/reopening resistance, and persistence. Classification and earned-friendship semantics are separate from ledger acceptance.

## Acceptance

- [x] Core RED/GREEN tests cover strict decomposition, fixed second-pass structure, provenance, immutable binding and 3/4/5-step typed parameters.
- [x] Twelve-case paired local-model diagnostic records raw outputs and exact Core/semantic results; model imperfections are retained.
- [ ] Runtime promotion, if warranted, passes current-context and native lifecycle checks before normal-launch enablement. Otherwise the experimental path stays opt-in/offline with the reason recorded.
- [ ] Native friendship probe passes actual clock/effect/reopen/persistence checks, with explicit limits for any untested disk/scene behavior.
- [x] Combined Core/adapter/runtime/Unity checks pass before installing a changed bundle. Current package and launcher behavior are recorded.
- [x] Roadmap, acceptance evidence and Obsidian overview reflect verified results; the broad goal remains active.

Detailed outcomes and remaining acceptance gaps: [parameter feedback and two-pass results](../../2026-10-20-parameter-feedback-and-two-pass-results.md). The corrected variant is opt-in only; native positive-chain planning and the full multi-day friendship probe do not pass yet. Initial friendship ledger/reopening checks and real disk persistence pass before the fixture's exact NPC legitimately projects indoors.

## User update: personal friendship tempo

Randomize the nominal 24/30/36-hour progression by ±15%. Use one deterministic 85–115% factor per saved NPC identity. Cap every actual interval at 36 game hours, including jitter; later stages retain this capped rate. Conversation reopening or save/load cannot reroll the factor. Keep earned credit unchanged; scale future accrual rates only. This requires no new save field or schema migration. Native acceptance must advance the selected NPC's actual personalized intervals and retain exact awake/active/registered home contact, fractional credit, disk persistence, and baseline restoration checks. Latest source passes 1272 Core tests and runtime compilation; personalized capped native acceptance remains pending. See [reboot handoff](../../2026-10-20-reboot-handoff.md) for installed-versus-source evidence.
