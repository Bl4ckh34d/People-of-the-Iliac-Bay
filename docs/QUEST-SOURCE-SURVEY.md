# Quest source coverage inventory

Source: pinned official DFU `v1.1.1-cve-2025`, commit `fb2627082fabe473bf108c816263b71c6e26ace6`, `Assets/StreamingAssets/Quests`. Run `tools/QuestSurvey.ps1`; the detailed JSON is written to ignored `work/quest-source-survey.json`, including filenames, line numbers and exact declarations/actions. This is a syntactic census, not semantic residency detection or a compatibility pass.

The survey finds 265 quest scripts: 245 declare people, 215 declare places, and 195 contain explicit placement actions. Categories below count declarations, not distinct NPCs or quests.

| Construction path | Count |
| --- | ---: |
| Fixed named individual | 97 |
| Questor | 181 |
| Generated person, explicit local home scope | 70 |
| Generated person, explicit remote home scope | 193 |
| Generated person, unspecified home scope | 33 |
| Local place | 70 |
| Remote place | 252 |
| Fixed place | 36 |
| Random choice of fixed places | 1 |

There are 455 explicit placement commands: 125 NPC, 224 foe, and 106 item placements. NPC placement may replace the dialog destination; item and foe placement must not be treated as resident allocation.

## Concrete counterexample to home-only binding

`A0C01Y09.txt`, The Birthday Present, declares a questor, a local sister and a local merchant (lines 151–153), plus a separate general store at line 157. Line 168 places the merchant at the store. A generated home field does not establish that the currently discussed destination is that house, or that a character is related to arbitrary existing residents. The story also has an explicit sister relationship in its prose. Automatic house/name matching must preserve those semantics, not merely fill a matching-sex resident slot.

Native `Person.GetDialogPlace()` selects the assigned place when present and otherwise the home. `Person.Tick()` may place a person at home on interior entry only when no explicit assignment exists. `PlaceNpc` calls `SetAssignedPlaceSymbol`; both initial creation and later stage changes need coverage.

## Mod quest entry paths

`QuestListsManager.LoadQuest()` reads either an external quest file or a TextAsset supplied by ModManager. Both branches call `QuestMachine.ParseQuest`, which calls `Parser.Parse`. A shared parse boundary can therefore support native-format quest packs without changing base files. This does not cover a custom quest engine that constructs resources outside these paths. Actual external-file and bundled-mod quest loading still need separate acceptance tests.

## Acceptance groups still open

- Actual main-quest fixed identities and story relationships; castle/palace exclusions.
- Guild offer/accept/reject and hand-in, including generated home recipients.
- Non-guild deliveries, retrievals, escort/rescue, guests and captives.
- Explicit initial placement and later destination changes.
- Remote and previously visited towns, concurrent quests using one building.
- Existing accepted quests from older saves and rejected/partial previews.
- Native-format mod quest files, bundled mod quest assets, and explicit adapters for custom engines.

The opt-in binding probe is evidence for interception timing and native name/resource behavior only. It does not close these end-to-end cases. No general quest-rewriting policy is enabled in normal play yet.

## Verified shipped case, 14 October 2026

The subsequent production pilot automatically binds this unchanged source in Gothway Garden only, before native offer text. A normalized-source SHA-256 guard rejects modified scripts, including mod overrides with the same quest name. Automatic eligibility, native social acceptance, save/reload and delivery now pass for this one reviewed rule; this does not generalize the census into a role classifier. Further survey/adapter expansion is deferred while M2/M3 gameplay and animations resume. See the current checkpoint in `QUEST-IDENTITY.md`.

`A0C00Y14` (**The Delivery Boy**) now passes the dedicated actual-player `-DeliveryProbe`: unchanged source loaded through QuestListsManager, explicit local household binding, native package startup, real pre-delivery save/reload, journal recipient consistency, native hand-in consuming the package, and collection of the script's gold reward. This covers one randomized package branch. The probe bypasses random social offer selection, mouse raycasting and quest eligibility rules; thief/failure/timeout branches remain untested. Remote counterpart `A0C00Y15` remains untested. See `QUEST-IDENTITY.md` for exact evidence and reproduction.
