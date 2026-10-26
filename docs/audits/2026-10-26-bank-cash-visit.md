# Empty-wallet cash errand — 26 October 2026

## Reproduced cause

`bank-cash-05` reaches an open native bank with an eligible adult, an empty
saved wallet, completed exterior door survey, no task/service/appointment duty,
no medical visit and no conversation or pause. `ApplyNpcCashVisit` still declines.
The adapter calls `InitialWalletGold`, whose Core `StartingNpcGold` deliberately
returns an existing saved wallet. For an empty wallet that is zero. The admission
check consequently rejects every ordinary empty-wallet cash errand. Bank and
home payout paths also passed that zero to the completion contract.

`NpcCashWithdrawalGold` now calculates the existing deterministic personal
allowance separately from the saved balance. Initial inventory creation retains
its saved-wallet behavior. Bank, home and unobserved home completion use the
withdrawal amount; only the existing validated completion transaction credits it.
Residents with zero initial farm pocket money can collect one coin. This does
not add a bank-account or income simulation.

## Fixture boundaries

`ProfileInEditor.ps1 -ValidateBankCash` waits for the completed exterior door and
navigation survey, selects an ordinary eligible resident, enters an open native
bank and establishes a controlled empty wallet while preserving exact items.
It then runs the production cash overlay and physical counter visit. The player
is moved clear of the entry; ordinary population updates are temporarily paused
to isolate this visitor, while its actor and the production movement code run.

The fixture checks counter dwell, exactly one wallet credit, actual journey
context before and after payment, cash memory, detached snapshot restoration,
NPC exit, native player exit and physical return movement. It does not establish
the initial outdoor trip, a complete journey home, family rendezvous, actual
corpse looting, a spoken LLM response or native disk save/load.

Runs `01`–`03` are invalid readiness fixtures: general street readiness precedes
the sliced entrance survey. Entering an interior pauses that survey. These runs
do not prove a missing bank entrance or a broken outdoor return route. `04`
waits for the survey and reproduces refusal; `05` adds the diagnostic facts above.

## Offline verification

Core 2122/2122 pass, including allowance calculation with a zero saved wallet,
unchanged inventory revision/items/balance, snapshot restoration and a resident
whose initial farm allowance was zero. The existing cash-restock harness passes
service timing, source/revision/replay and bank/home selection checks. Runtime
and QuestHooks compile without warnings/errors; source and tooling checks pass.

## Native result

`bank-cash-06` completes the production bank overlay. The resident walks
29.24 metres before payout, stands at the counter for 13.81 real seconds and
receives 419 gold once. Both cash journey contexts, the factual bank memory,
detached snapshot restoration, the NPC's bank exit and the player's native faded
exit pass. After returning outside, the same resident physically moves another
2.12 metres toward home. `result.txt` is `complete`; no `Exception:` appears.
The existing scene-teardown object-cleanup warning is still present.

`bank-cash-07` additionally requires the actual native clerk's hash, a customer
distance of 0.6–3.5 m and a facing dot of at least 0.9 after the first second at
the counter. It completes with 8.80 seconds dwell, 0.807 m maximum planar
distance, minimum facing dot 1, one 419 g credit and 2.20 m return movement.
This independently sampled dwell also remains inside the requested 8–15 seconds.
The run completes without an `Exception:` entry.
