# Live D3 routing: first gameplay slice

The first awaited D3-mini route stage is installed in the existing free-text conversation path. The normal game launcher starts or reuses separate Qwen and D3 services; no Qwen model selection is replaced. `-DialogueOnly` skips Mini, and a technical Mini startup/request failure falls back to Qwen. Running games are not restarted.

D3 selects SOCIAL, ACTION_REQUEST, OFFER_RESPONSE or COMMITMENT_UPDATE from the existing native-state-aware menu. Qwen refines within the selected route, resolves targets, interprets consent and writes the response. Direct busy/threat social overrides remain supported. The native planner/executor and its ownership/receipt checks remain unchanged. This slice does not claim that final willingness or every planning decision already uses D3.

## Changes

- Await the decision at the existing captured player-turn boundary, with the existing main-thread stage tokens and session/save/scene ownership.
- Constrain Qwen's refinement grammar and validate the resulting typed selection. A SOCIAL route cannot turn into an immediate physical-action request during refinement.
- Drop an unaccepted offer when D3 routes a new instruction or commitment update; nested interpreters cannot turn that turn into assent to the old offer.
- Re-evaluate live offer expiry for decision freshness. A technical result on an otherwise current stage falls back once; cancelled or obsolete conversation ownership does not continue.
- Reuse all existing close/save/scene/disable cancellation hooks. No separate conversation or persistence system was introduced.
- Start/reuse the verified local Mini artifacts through loopback, model/context/slot and process-ownership checks. Keep model-quality mistakes separate from native integrity.

## Verification

Package SHA-256: `6B4494828ECF07D18DBAF635D6E4A48C94287A19C9EE96416E9629B31692EFB7`. Installed and dist hashes match.

- Adapter suite: **68/68**. RuntimeCompile and Unity integration/package builds pass. The package has 632 runtime assets.
- Four isolated launcher scenarios pass without launching actual processes: both services and routing metadata, DialogueOnly, Mini failure fallback, existing-game early return.
- The actual Mini launcher starts PID 4032 and subsequently reuses it without replacement. A real C# adapter wire smoke succeeds in 534.25 ms, with exact model identity and one main-thread callback. This synthetic smoke does not score semantic agreement or observe native effects.
- Native `TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b -FixtureSaveName 'People - Gothway Garden' -DecisionRoutingProbe` exits zero. Deterministic tests verify successful routing, technical-failure fallback, main-thread delivery and four cancelled in-flight requests. The cancellation cases use real Talk closure and invoked save-epoch, scene and disable hooks; they are not actual disk-load or scene-transition acceptance.
- The same native test then uses the real D3 transport and real Qwen service through production `SubmitFreeText`. It verifies the exact submitted turn and offered routes from the actual wire request, a valid expected-model response, progression into Qwen refinement and a remembered current response. Actual selected route is SOCIAL. The route stage itself leaves the measured archive/transcript/disposition/positions/action state unchanged before this deliberate chat.
- GPU memory sampled during the loaded native run is 10,614/12,282 MiB. This is one fixture observation, not broad graphics/performance certification.

Evidence: `work/live-routing-20261020/verified-result.json`, `native-passed.log`, `unity-build-passed.log`, service readiness metadata and retained failed/diagnostic logs.

## Probe correction and duplicate date

The first full production probe falsely timed out despite a completed chat. Its diagnostic recorded D3 success, stage sequence 7 to 10, zero callback failures, `conversationBusy=False` and the actual question/answer rows. `Application.logMessageReceived` did not deliver the expected event in this Player. The test now records the real transport response and request and checks progression into refinement instead of depending on that log event. The failed evidence remains preserved; no model behavior was tuned to repair the test.

The user's duplicate timestamp exposed a separate presentation bug: replayed `observed` memory records added an empty date section, followed by the current conversation's identical header. Observations now remain memory facts without creating empty transcript sections. Actual dated exchanges remain visible. Native assertions verify no adjacent duplicate date headers before submission and after the answer.

## Next scope

Continue the broad roadmap with decision-model selection among bounded native plans and willingness outcomes. Reuse this service and ownership boundary. Accept occasional model mistakes under the user's instruction; do not restore a universal zero-error semantic gate. Actual action-family effects, multi-step fidelity and full load/transition acceptance remain distinct work.
