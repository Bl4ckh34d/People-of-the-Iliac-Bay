# Resumed action integration and D3 frame experiment

The user resumed implementation on 20 October after the completed shutdown. The current work does not authorize another shutdown. Preserve the earlier shutdown checkpoint as historical evidence.

## Native action changes

- Offer-response classification exposes CANCEL when the captured conversation snapshot has an active action or task. Cancellation revalidates the same resident, actor, window, session, stage, pending offer instance and current commitment ID/revision. A stale reply cannot cancel a replacement task.
- A later Wait may use the exact static destination of the preceding MoveTo. Candidate preparation preserves existing option IDs and targets and adds a separate destination-bound option. Both sequence parsers reject selecting a mismatched target. Model interpretation still has to match the player's wording; structural target equality does not establish semantic correctness.
- The executor still requires a completed MoveTo receipt and observed physical arrival before starting the Wait. Follow does not become a saved Wait position.

Parent verification: 1206/1206 Core tests and 137 Python harness tests pass; RuntimeCompile has zero warnings/errors. Unity builds and installs the package. The first installed package was `B7079410262D5C734E2715C8555D016AFFEA791EECD50357192CF2BBC06B40B3`; the final tested package is `FDF63657D7928C5A546FAE1954812D9798B774A9C0B4435933ADCDB0D8AA58FC`.

The first native MoveTo/Wait test fails: the actor's action deadline expires with an 11.566 m remaining gap to building 131343. Wait does not start early. This is preserved at `work/resume-frame-20261020/native/move-wait-failed.log`. Diagnostic runs show over 214 m of circling around waypoint 5. The blocker is the CharacterController of stationary guard `g:206728581:n:3`, exactly at that waypoint. Crowd steering has no neighbors and zero comfort offset, disproving the initial soft-avoidance hypothesis. The following route point has a physically clear approach during part of the orbit. Two later diagnostic processes were intentionally stopped after the needed evidence was collected; neither is an acceptance pass.

The action follower now tracks improvement in distance to its current waypoint. After a one-second stall it considers at most four later points within 4.5 m, retrying at most every 1.5 seconds. Rejoining requires the same swept crowd separation plus physical furniture, capsule, and sampled floor-support checks used by movement. It changes only the guidance index; the final action target, deadline and arrival radius remain unchanged. Twenty seconds without waypoint progress reports a blocked route instead of crediting circle movement as progress.

The final native run passes on the same resident and building: the rejoin advances waypoint 5 to 6, the body walks 43.09 m without teleportation (maximum observed step 0.128 m), completes MoveTo 0.190 m from the exact target within the unchanged 0.250 m range, starts Wait only after the completed MoveTo receipt, waits 10.13 game seconds with zero drift, and releases action ownership to the ordinary routine. Runner exit is zero with both native and Town pass markers; evidence is `work/resume-frame-20261020/native/move-wait-passed.log`. This is focused loaded-game action movement acceptance, not universal navigation coverage. No claim of live-model offer/cancellation acceptance is made from Core tests.

## Offline decision-frame experiment

The API capability probe confirms that D3-mini accepts multiple choice questions per `/v1/systemone` request. Four successful requests cover a simple action and ordinary conversation in both question orders. This establishes API capability only.

The new offline protocol asks D3 to select action kinds sequentially, including explicit END, CLARIFY and eight-step overflow. Qwen extracts arguments for the frozen count/order/kinds and may report disagreement. Constraints or unresolved facts require clarification. Existing conceptual grounding then binds synthetic candidates. No gameplay authority is connected.

The experiment reuses the previously viewed 28-case corpus in two independently executed full-pipeline arms. It is development evidence, not fresh held-out evidence. Exact sources, models, requests and raw responses are saved before/after each bounded call under `work/resume-frame-20261020/frame-run1/`.

Results: 385 HTTP-200 calls (377 D3 selection, four Qwen argument extraction, four Qwen grounding). Across 56 arms: 42 clarify, 11 no-action and three plans. All three plans are false; fourteen expected plans are missed. Thirty-nine arms reach the eight-step cap, with 264 adjacent repeated action kinds. D3 selection median/p95/max latency is 104.174/142.473/589.741 ms. Qwen samples are too few for stable performance claims.

Outcome agreement is 35/56 versus 34/56 in the earlier Qwen-only conceptual experiment. That does not indicate improved planning: repeated steps usually abstain at the cap. One false plan turns the player's own planned purchase into eight NPC purchases; two turn one clerk interaction into three. Exact quote/target comparisons and constraint content are not fully covered by the existing typed/presence score; candidate-target fidelity remains unproven.

Decision: keep this sequential frame variant offline. A continuation prompt must explicitly distinguish already represented actions from remaining actions before another bounded diagnostic. Repeated kinds remain legitimate when the request contains distinct repeated actions. The broader hybrid architecture is not disproven by this one failed decomposition protocol.

## Remaining gates

- Extend the physically validated rejoin coverage to additional obstruction/failure cases; the focused guard-blocked native MoveTo/Wait case now passes.
- Exercise pending-offer cancellation through the actual chat callback, including a stale task revision.
- Test broader inventory directions and native target/argument fidelity.
- Keep willingness, consent, effects, memory, prose quality and concurrent game performance as independent acceptance requirements.
