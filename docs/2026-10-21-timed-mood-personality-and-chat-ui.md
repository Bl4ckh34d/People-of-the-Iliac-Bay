# Timed mood, persistent personalities, relationship foundation and wider chat

## Subsequent schema-35 acceptance

The later installed development package is `2D92C85D28E83E3BBE8D27F5286CD6478EFAEAB8F8A4EB85A21C9EE9E7180ACF`, with 667 assets and save schema 35. Core **1364/1364**, RuntimeCompile and Unity build/install pass. The owned native mood test exits successfully and retains the UI, timed mood/personality, relationship-storage and final town-test pass markers.

The additional `EXPERIENCE_INBOX_STORAGE_PASS` marker verifies one controlled typed pending outcome through real disk save/reload, with duplicate replay rejected. Core tests cover automatic appointment attendance/no-show evidence production, bounded inbox reads, migration and appraisal request/result validation. These checks do not prove a live semantic appraisal consumer, atomic event application/acknowledgement, or gameplay-driven relationship changes; those remain open. Evidence: `core-next.log`, `runtime-next.log`, `build-next.log`, `native-next.log` and `run-next.log` under the existing evidence directory below. Adapter **77/77** is the preceding context-integration result, not a newly repeated run.

The following schema-34 section preserves the preceding accepted checkpoint.

Verified 21 October 2026. Installed development package SHA-256: `D0FC6AFDFC06369FCCCB64D5717B211F468ED77802A71888CA9076F3812B8475`; 665 runtime assets; save schema 34. Core **1347/1347**, decision adapter **77/77**, RuntimeCompile zero warnings/errors, and Unity build/install pass.

## Accepted behavior

- 24 October correction: active free-text LLM mode replaces the Talk background, removes all three tone buttons and their marker, and uses the full expanded input width. The optional D3 planner no longer controls this presentation. Disabling or losing the LLM restores native presentation. The earlier combined LLM-and-D3 gate described by the original 21 October acceptance was too strict and caused the native chooser to reappear in LLM-only sessions. Input expands from approximately 124 to 186 native UI pixels at the existing position. Missing or dimension-incompatible alternate art retains the original chooser and input width.
- The generated background contributes only rectangle `(245,4)-(316,49)` of the original 320x200 asset. Exactly 3193 pixels change; every outside pixel is preserved. The deployed PNG hash is recorded in `work/timed-mood-personality-20261021/ui-pixel-verification.json`.
- Mood cools toward neutral using elapsed game time, with a provisional four-game-hour half-life varied by stable per-person/world-seed +/-15 percent. Reads do not mutate anchors or reroll timing. Accepted social/action replies fold elapsed recovery before applying their bounded delta. A separate revision protects state updates without confusing them with accepted chat sequence numbers. Schema 32 migration anchors existing mood at the known loaded game clock.
- Fourteen concrete personality axes are generated once for registered identities and saved. The original five seeded choices remain compatible. Vocabulary, eloquence, integrity and status attitude are separate: a noble can be eloquent, coarse, manipulative or condescending. Verified noble background raises eloquence probability without guaranteeing courtesy or honesty. Authored quest actors are not given an overriding randomized persona.
- Directed reliability trust, evidence support/count, resentment and interpersonal fear have separate sparse persisted records. Missing trust is unknown, not numerical distrust. Canonical outcome identity, observer/target binding, row and ledger revisions, and detached copies protect updates and replay. Schema 33 migrates to an empty relationship ledger while preserving mood/personality.
- Social, action and semantic projections include a captured relationship when present. D3 willingness uses the semantic projection. A changed directed relationship invalidates an old conversation snapshot. The social instruction no longer equates negative disposition with distrust.

## Native evidence and boundaries

`tools/TownTest.ps1 -ConversationMoodProbe -FixtureCharacter PeopleOfTheIliacBayTest-9a5f197b -FixtureSaveName 'People - Gothway Garden'` passed. The probe opens a real managed NPC, verifies the alternate UI, obtains a real typed local-model social reply, exercises full free-text routing before/after cancellation pressure, accepts controlled mood results, checks stale/replayed/invalid callbacks, saves and reloads a temporary checkpoint, and restores the exact original save/clock/position. The temporary checkpoint is deleted.

The native mood anchor is **21**, and after the actual NPC half-life of **15840 game seconds**, the captured mood is **10**, with the same revision and accepted sequence. The saved anchor stays unchanged after the read. Fourteen personality values survive the disk round trip.

A **controlled typed relationship fixture** stores trust 55, evidence count 1, resentment 2 and fear 1. Native disk serialization preserves them; replay is rejected after reload; the intervening event invalidates a previously captured snapshot. This proves storage/projection/revision behavior, **not** actual gameplay event production or semantic appraisal. The appointment experience producer currently has Core evidence tests only; it is not connected to runtime appraisal.

The first native attempt passed UI and normal conversation checks but failed the new timed assertion. Its expected value used native `ToSeconds()`, which drops fractional seconds, while production snapshots retain them. At an odd-valued half-life boundary, 21 becomes 11 at the exact boundary and 10 just afterward. The corrected assertion uses the snapshot's actual game time; diagnostics record `12597524836.319336`, value 10 and matching revision/sequence. A focused Core test preserves this boundary. Original failing logs remain as `native-attempt1.log` and `run-attempt1.log`.

Evidence lives under `work/timed-mood-personality-20261021/`. The accepted native markers include `AGENTIC_DIALOGUE_UI_PASS`, `TIMED_MOOD_PERSONALITY_PASS`, `RELATIONSHIP_STORAGE_PASS` and the final town-test pass.

## Still open

Shared event appraisal and actual native event-to-relationship/mood consumers remain unfinished. The ordinary small relationship delta policy does not yet implement severe insult/threat damage. Nobility sensitivity, guard orders, persistent quest-access consequences, expressive portraits and future TTS visemes are planned, not enabled by this package. Model calibration, disclosure/deception, scoped utility, wages and physical paid companions remain later slices. The full roadmap and IMM-09 remain partial.

Follow the [experience catalogue](superpowers/specs/2026-10-21-npc-experience-events.md), [producer map](2026-10-21-npc-experience-producer-map.md), [acceptance matrix](2026-10-21-npc-factor-acceptance-matrix.md) and [delivery plan](superpowers/plans/2026-10-21-npc-motivation-and-contract-delivery.md).
