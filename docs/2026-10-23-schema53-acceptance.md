# Schema 53 — observed home meals

Status: the home-meal checkpoint is natively accepted and installed. The full ROADMAP goal remains open.

## Implemented scope

The meal source anchors on the first actual observation and becomes due after eight game hours; migration invents no earlier meal. Source-bound Food and Meal projections publish together. An awake adult household resident can complete one meal after 600 continuously observed game seconds in their own home. Scene changes, lost observation or conflicting duties cancel an incomplete attempt. Reload invalidates attempt tickets, and later due episodes use new goal generations. Home meals represent aggregate household consumption and do not add transferable items, alter containers or move money.

The destination policy prefers an accessible home and defers unknown access. The runtime currently waits for the resident's existing schedule to bring them home. Need-driven home journeys, Inn fallback, Traveler owners and Temple/Guild/Bank effects are not delivered by this checkpoint. The Inn implementation plan identifies the separate destination-visitor movement path it must integrate.

## Verification

- Main Core: 1695/1695 passed, `work/life-core-20261023/schema53-core.log`.
- Focused regressions: Meal 10, arbitration 27, healthcare atomic admission 10, service/health store 10, service pause 9, settlement 8; all passed. Logs use `schema53-*.Regression.log` in the same directory.
- RuntimeCompile passed without warnings or errors. Independent source/probe review found and corrected a malformed-save initial-food claim and probe cleanup/verification gaps.
- Unity built and installed the final candidate with matching dist/install SHA256 `1C5F7950F1CFFCFCCADB2B22D12CA7395E316CBC7F93BA762107429C18AF55CF`; log `schema53-home-meal-wire-build.log`.
- The earlier candidate with the same functional meal implementation passed native healthcare and funded service interruption/regain, without billing paused time. Evidence: `schema53-healthcare-native.log`, `schema53-healthcare-town.log`, `schema53-healthcare-player.log`.
- Final native home-meal run passed (exit 0). Its gate confirms actual home presence, interrupted dwell without effect, full dwell fulfillment, unchanged wallets/items/containers, exact saved frontier replay and restored original fixture. Both `PEOPLE_OF_THE_ILIAC_BAY_HOME_MEAL_NATIVE_PASS` and `PEOPLE_OF_THE_ILIAC_BAY_HOME_MEAL_ORIGINAL_BASELINE_RESTORED` were observed. Evidence: `schema53-home-meal-wire-native.log`, `schema53-home-meal-accepted-town.log`, `schema53-home-meal-accepted-player.log` under `work/life-core-20261023`.
- All 13 original fixture files match the pre-test SHA256 manifest. Dist, installed bundle and `work/life-core-20261023/schema53-home-meal-accepted.dfmod` match the package hash above. Temporary native checkpoints were removed after original-save restoration.

## Native test findings

The first probe incorrectly assumed a one-time 8x time override would persist. `PeopleOfTheIliacBayMod.Update` enforces 2x every frame. The diagnostic run observed only 316.3 game seconds and correctly produced no meal. The probe now observes the real 600 game seconds without a clock jump; the completion requirement is unchanged.

The next run completed the actual meal and its goal, then exposed an invalid test comparison. DFU FullSerializer on .NET Framework prints 15 significant digits: a sample `12597661809.512344` becomes `12597661809.5123`, a 44-microsecond difference. Both the source validator and eight-hour due interval remain valid. Detached native serializer reproductions are saved as `meal-json-roundtrip.ps1` and `meal-source-fullserializer-roundtrip.ps1` under `work/life-core-20261023`.

The corrected probe checks exact live-to-snapshot identity, receipts and revision fields; bounds live-to-wire time rounding to 1 millisecond; then requires the restored source to match the decoded wire exactly, including all times. It also verifies the original fulfilled goal generation and both source cursors. This permits native numeric formatting, not missing or changed effects. Attempt logs 1–3 are retained separately for audit.
