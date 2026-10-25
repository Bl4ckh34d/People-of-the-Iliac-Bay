# Physical direct-threat complaint — 26 October 2026

## Scope and result

`ProfileInEditor.ps1 -RunName complaint-travel-03 -CountersOnly -ValidateComplaint`
passes the existing exterior complaint path with an actual resident and finite
native guard roster. The disposable SAVE82 fixture advances to noon and chooses
an ordinary adult with a reachable posted guard. It supplies a controlled,
issued `DirectThreat` speech receipt; it does not test LLM interpretation or the
free conversation UI. Ordinary insults do not create this police report.

The witness `s:1291010263:h:263682:r:0` starts 13.14 m from guard
`g:1291010263:n:31`, moves 10.94484 m over 4.92790 real seconds, and finishes
within the existing arrival radius. Average observed speed is **7.99558 km/h**,
above the requested 3.5 km/h minimum on this route. The largest sampled body step
is 0.500028 m. This measurement includes the fixture's snapshot check and normal
editor work; it is not a universal minimum under obstacles or a release-player
performance benchmark.

The check verifies:

- Receiptless dislike cannot start a complaint or manufacture a crime.
- Saved route progress matches the actual body within 0.1 m.
- The original incident coordinates remain unchanged while the witness moves.
- A detached store restored from a mid-route snapshot retains receipt, position,
  waypoint and `Started` status. This is not native disk save/reload.
- No player crime appears before arrival. Delivery produces the existing typed
  verbal-threat receipt and its native Assault carrier.
- The matching report dispatches four existing guards without increasing the
  roster. This is a direct-threat report, separate from anonymous murder hearsay.

The run completes without exception entries or the scene-teardown warning.
Owned Unity exits; Hub is untouched. Core 2121/2121, RuntimeCompile without
warnings/errors, source policy and tooling syntax/protocols pass.

## Invalid initial fixtures

`complaint-travel-01` and `02` time out. Their unchecked three-metre player offset
placed the incident at a point for which the guard response route was empty.
The witness reached the guard but dispatch could not succeed. It then travelled
approximately 393 m in repeated close movement during the 120-second deadline.
The second run logged a zero-length response route and a report with zero
responders; there was no early charge.

Those are not acceptance runs and do not demonstrate failed delivery from a
valid incident point. The corrected fixture chooses an adjacent position only
after checking physical clearance and a native route from the selected guard.
The accepted incident has three response waypoints. No production behavior was
changed to make this test pass.

## Remaining checks

Full free-text threat interpretation, actual guard-body mutual facing/visibility,
interior reporting, native disk restore while travelling, interruptions, and
post-delivery routine resumption remain separate acceptance work. The actual
behavior when a legitimate incident becomes unreachable also needs a deliberate
test: the witness should not circle the guard indefinitely while dispatch fails.
Do not weaken receipt validation or invent an investigation to hide that case.

The initial `b581d2e` checkpoint added only the fixture and documentation. The
following repair changes production movement and requires a new package.

## Arrival hold and recovery

`-ValidateComplaintWait` extends the same fixture with explicit fault injection.
It captures a valid incident first, temporarily substitutes a nearby point with
no response route in the pending record, and never moves the player into that
invalid point. After five seconds of waiting beside the guard, it restores the
real incident and requires successful dispatch with the same receipt. This tests
dispatch unavailability; it is not a reproduction of a real door closing.

`complaint-wait-red` fails: 17.21317 m travelled during the five-second check,
2.83928 m maximum drift, facing dot −0.60353 and an active walking animation.
The movement code still steered toward the guard's centre after reaching the
2.25 m reporting radius. If dispatch failed, steering and crowd avoidance kept
the witness moving around the listener.

The production fix uses the existing arrival radius to stop locomotion and face
the guard target independently of dispatch success. Report validation, expiry,
incident coordinates and charges are unchanged. If the guard moves out of range,
ordinary approach resumes. No successful report or investigation is fabricated.

`complaint-wait-green` completes the five-second check with 0.000397 m movement
and drift, facing dot 0.9999999 and a frozen directional standing animation. No
charge is created while response routing is unavailable. Restoring the actual
incident permits normal finite dispatch. Travel excluding the intentional wait
averages 7.65423 km/h; largest body step is 0.499976 m. The same saved-progress
and snapshot checks pass. The run has no exception or teardown-warning entries.

The pending-case fix covers visible locomotion, not retry-cost optimization,
offscreen arrival, actual door interruption, mutual guard-body facing or routine
resumption after delivery. Those remain separate checks.

Build/install at 07:47: distribution and Player SHA256 both equal
`A31A6036B1AF075ACFC25EBFF2D998F5FCB27D4E64AFCABC47CF99D0B6BB88F5`.
Unity integration/build exits 0. Core 2121/2121, runtime and QuestHooks builds,
source policy, tooling checks and whitespace checks pass. Owned editor exits;
Hub remains untouched. This package supersedes the earlier housing-cache bundle.
