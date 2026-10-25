# Typed goal extraction and native binding acceptance

## Result

Installed/dist bundle SHA-256: `025C14C917814C6ABC80AE251B8924BA929949C784D36C8FDF17875DCFE05CBF`, 640 runtime assets. The normal external `DFU/tools/StartGame.ps1` now supplies `-peopleoftheiliacbay-goal-extraction` when both Qwen and D3-mini are ready. Dialogue-only and failed-D3-startup launches retain the existing Qwen semantic path. Existing game processes are not restarted or retrofitted.

Qwen extracts a candidate-neutral conceptual goal with typed quantities and game-time values. Subsequent Qwen calls select current candidate IDs. D3 evaluates the complete bound proposal before native consent, target/resource checks and commitment. No regex, keyword intent rule or phrase/number-word converter interprets NPC requests in this path. Source quotes prove provenance, not semantic fidelity. The native code preserves typed values and does not reinterpret player language.

Pending offers and active commitments retain their existing LLM-based paths. The new extractor applies to new action requests. Unsupported actions or emitted unresolved conditions/exclusions block the whole extracted proposal. Model omissions cannot be proved absent by structural validation; D3 receives the original request as an additional semantic backstop, not a guarantee.

## Verified checks

- Core: **1231/1231**, including history-window integration, strict schema/provenance, quantity/game-time arithmetic, candidate identity, dependencies and defensive copies.
- Decision adapter: **77/77**.
- Clean RuntimeCompile and Unity build/install.
- Four isolated normal/dialogue-only/service-failure/existing-game launcher cases. The new flag assertion fails before the launcher change and passes afterward.
- Owned `TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b -FixtureSaveName 'People - Gothway Garden' -GoalBindingProbe` exits zero.

The native deterministic leg injects a typed authentic-source Wait proposal and D3 ACCEPT, while target selection still uses production Qwen and native candidates. The production continuation binds one target, dispatches willingness once, saves exactly 18.000 game seconds, appends the authoritative acceptance receipt and completes the Wait after Talk closes at clock scale 2. This verifies execution mechanics with injected consent, not live-model agreement. The fabricated quote `60 seconds` in a ten-second utterance is separately rejected before D3 without an action/task save.

The final live leg uses a second unused resident while the first physically returns to its routine. Real D3 routes ACTION_REQUEST; Qwen returns a fresh valid 18-game-second Wait proposal. That resident has no eligible current Wait target, so native binding rejects it before willingness; no action/task starts. The earlier preserved live run binds successfully and real D3 declines. Neither result is replaced or retried to manufacture agreement. Accepted execution and live-model decisions are reported separately.

Extraction now passes history through the existing windowable history argument. Current turn, factual context and schema stay fixed; older complete exchanges may trim under context pressure. Fixed content that cannot fit still fails closed. This does not add semantic memory recovery for a reference whose antecedent was trimmed.

## Preserved failures and limitations

An intermediate probe incorrectly required a completed Wait to release routine-return ownership immediately. Native execution succeeded, but the test stopped before its next leg. The corrected probe uses a second resident and does not clear ownership or teleport the original NPC. A reviewer also found stale diagnostic fields could falsely satisfy the live leg; the probe now clears all prior extraction fields and requires a fresh production trace plus an actual reply.

Five fresh offline requests all produce structurally valid output, but three contain wrong action kinds or omissions. Reported speech can become a false request; history-dependent movement can be dropped. This tiny sample is diagnostic, not a reliability estimate. No prompt-specific text rules or perfect-score gate are added. Complex conditions, broad action adapters, receipt-backed natural-language acknowledgments, full interruption coverage and the wider architecture remain unfinished.

## Evidence

`work/goal-binding-20261020/`: `core-rollout.log`, `adapter-rollout.log`, `runtime-rollout.log`, `build-rollout.log`, `launcher-red.log`, `launcher-green.log`, `native-rollout-final.log`, `native-rollout-passed.log`, `native-routine-precondition-failed.log`, `native-decline-passed.log`, and `fresh-extraction-reparsed.json`. Initial diagnostics with a null PowerShell-created binding are retained separately; saved model outputs were reparsed without new inference.

The full roadmap goal remains active. No shutdown is requested.
