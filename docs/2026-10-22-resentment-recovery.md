# Independent resentment recovery — checkpoint 148c

Directed resentment now cools over game time independently of player fear. Each
relationship saves its own anchor, selected personality/severity rate and fractional
remainder. The provisional base half-life is fourteen game days, compared with
seven for directed fear. These are calibration defaults, not user-specified constants.
An unrelated trust or fear event cannot restart the resentment clock. Resentment
recovery does not repair trust or erase the underlying event history.

Signed and delayed updates apply against the aged baseline. Timed conversation,
willingness, appraisal and TTS context use projected resentment; stored values remain
explicit for persistence. World schema 42 and relationship row version 3 preserve
existing fear fields. Legacy schemas 40/41 retain resentment without inventing a
historical anchor; a subsequent attributable update can establish forward recovery.

## Verification

- Core: 1,589/1,589; adapter: 84/84; factor-evaluator self-test passes.
- RuntimeCompile: zero warnings/errors. Unity builds and installs 742 assets.
- Owned native `native-resentment-148c` passes a cold real Talk capture, independent
  clocks, signed fractional update, native FullSerializer round-trip, schema-41
  migration and unchanged unknown trust. It uses six controlled source-bound
  receipts in a detached store and sixteen supplied game-days. It does not simulate
  sixteen actual gameplay days or verify a natural resentment-producing encounter.
- The live fixture relationship ledger remains unchanged, the owned baseline save
  reloads, and external mod settings restore byte-for-byte.

Installed bundle SHA-256:
`11355169981D78701D3CA944C518E6179511E651CC93314DDE0AA1BFE2FBF72A`.
Raw evidence: `work/breeze-tts-20261021/resentment-green-148d.log`,
`adapter-148.log`, `factor-selftest-148.log`, `runtime-compile-148e.log`,
`unity-build-148c.log`, and `native-resentment-148c/`.

The preserved native failures `native-resentment-148` and `148b` exposed an overly
strict probe comparison: native FullSerializer rounds epoch-sized double timestamps
by roughly 0.000044 game seconds. The probe now permits one game millisecond for
those timestamps only. Integer projections, saved rates and fractional values have
separate checks. Production serialization and recovery arithmetic were not relaxed.

## Model follow-up

`work/breeze-tts-20261021/resentment-eval-148/` retains twelve first D3/Qwen pairs,
all HTTP 200 without retries. Four controlled personality profiles each carry stored
resentment 80 projected to 80, 40 and 10 after zero, one and three saved half-lives.
Trust stays 20, fear and mood stay zero. Shared history was aligned before inference.
Every proposal is the same ten-game-minute WAIT; real typed D3 choices are all
ACCEPT. Isolated Core receipts remain AcceptedPending without execution effects.
Qwen receives those actual decisions and current projected resentment.

This narrow corpus has no refusal oracle and proves neither general accuracy nor
a causal personality effect. Qwen repeats the previous NPC sentence exactly in
7/12 replies and uses a close variant in 4/12. No reply claims execution has begun
or completed. Preserve this weakness rather than tuning the prompt to these cases.
Independent review and exact requests/responses remain beside the raw results.

## Limits and next work

The old fear-only view API remains compatible; chaining it after a previously timed
combined view can retain an older resentment projection. Current production
time-aware consumers use `ProjectRecovery`, so no such live caller was found.
Two older integration probes still compare raw resentment without all new projected
clock fields; the new native probe supplies that focused coverage.

The separately reviewed three-callsite Qwen-consent correction is included in this
build. Actual classifier-to-Qwen dispatch for every single-action/offer/task route
still needs discriminating native acceptance. See
[consent follow-up](2026-10-22-qwen-consent-callsite-followup.md).
