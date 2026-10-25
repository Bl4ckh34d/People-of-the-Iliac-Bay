# Native Lead acceptance (pending in-game run)

Run only with a caller-owned disposable character and its existing city save:

```powershell
./tools/TownTest.ps1 -FixtureCharacter PeopleOfTheIliacBayTest-xxxxxxxx -FixtureSaveName 'YOUR CITY SAVE' -NpcLeadProbe
```

The probe does **not** select or invent a destination. In the loaded exterior, talk to a managed adult and choose a building the current native TalkManager says that NPC knows. Accept the production **Lead** action while the building is at least 12 metres from the NPC. The controlled fixture watches the resulting saved action; no LLM, GPU, NPC teleport, or synthetic completion is involved.

Follow the logged state markers:

1. `LEAD_PROBE_READY`: have the native conversation and accept Lead.
2. `LEAD_ACCEPTED`: confirm the recorded resident, current settlement ID, exact building key and resolved entrance.
3. `LEAD_MOVING`: after actual movement is measured, walk more than 9 metres away from the NPC. Stay away for two real seconds. The probe rejects more than 0.2 metres of NPC drift or a changed action.
4. `LEAD_PAUSED`: return within 8 metres and accompany the NPC. The probe requires another 0.5 metres of real NPC movement.
5. `LEAD_PROBE_PASS`: the saved last position reached that building's entrance, the player was nearby, and the completed action produced an effect ID with truthful `NpcActionFeedback` text. The harness requires this exact marker before reporting success.

This one-run witness covers physical start, distance pause/resume, target continuity and terminal feedback. Its `LEAD_PROBE_PASS` marker does **not** claim disk save/reload, rejoin, cancellation, timeout, or conversation-memory projection. Those require separate native runs: save during an active Lead, reload that save and inspect the same action/resident/target before walking to arrival; cancel another accepted Lead and verify terminal `Cancelled` without a completion effect; leave another Lead until its deadline and verify terminal `Failed` without a completion effect. Check the corresponding conversation receipt/memory against the persisted action ID and effect ID on each run. Do not count a CPU snapshot restore as a native disk reload. The focused CPU regression tests cover state transitions and JSON restore, but cannot prove native scene reconstruction or memory display.
