# Quest identity integration probe

Verified on 14 October 2026 against the installed DFU 1.1.1 player. This is baseline feasibility evidence, not a replacement quest system or complete quest compatibility.

## Reproduce

Run `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestProbe` from the Living Cities repository. The runner starts its own player, requires the disposable character naming convention, and refuses to interrupt an existing player. Normal gameplay never starts the probe. The harness disables live input only in this owned test process; native activation methods are invoked directly where needed.

The run first verifies existing population, street, interior, death and family behavior. It then enters a real Gothway Garden tavern through the native building-entry handler, binds an original adult StaticNPC to a minimal parsed quest, and checks:

- Original complete NPCData and display name remain unchanged after an in-place transform move.
- QuestResourceBehaviour retains quest UID, symbol and Person resource; ActiveQuestor recognizes the same NPC.
- The actual PlayerActivate.StaticNPCClick handler triggers a reset `clicked npc` quest task before and after movement.
- Changing any of hash, mapID, nameSeed or buildingKey fails native identity equality, establishing the matching contract's negative controls.
- Quest hide/show suppresses and restores the original actor without replacing identity.
- An actual save/load preserves quest UID and task state, reconstructs exactly one matching StaticNPC with a different Unity instance ID, restores binding/name/data, and accepts a new native quest click.

The passing fixture uses Vannolda Woodston in building 65542, quest UID 33554448. `SAVE16`, **Living Cities - quest identity probe**, belongs to disposable character `LivingCitiesTest-126494eb`; it intentionally contains the synthetic active quest. Normal family preview remains SAVE15. The prototype quest has no rewards or faction changes. Evidence: `work/quest-identity-pass.log`, native build log `../work/livingcities-build.log`, and SAVE16's original DFU quest/save data. The run also passed all previous family and street checks; core behavior tests remain 87/87.

## Integration consequences

Keep original StaticNPC and QuestResourceBehaviour identity when changing visuals or moving within the allowed interior. Native quest identity uses the original map/building/layout hash/name seed, not the current transform. A newly created generic mobile person with a matching appearance or name does not establish equivalent quest identity.

StaticNPC.Start derives runtime building information from player context. Cross-building/outdoor proxies therefore need a deliberate binding adapter and tests; this probe does not authorize recomputing identity from a new location. QuestResource.Tick also controls active state for bound people, so a future radius/pose controller cannot assume sole ownership of the same GameObject's active flag. Sleeping can change visual pose while retaining the original activation target, but actual bed placement, waking and service interaction still need gameplay acceptance.

## Harness correction found by this probe

Calling PlayerEnterExit.TransitionInterior directly bypassed PlayerActivate's building-discovery and shop/tavern/residence setup. With stale default building data, vanilla occupants were disabled as if the tavern were a closed shop; generated residents alone still passed earlier tests. A runtime census showed the original NPCs present but inactive, before StaticNPC.Start had filled location data. The shared test helper now calls PlayerGPS.DiscoverBuilding and the native PlayerActivate.TransitionInterior handler, then verifies the building key. This fixes test fidelity rather than changing normal game behavior. Failure evidence: `work/quest-probe-closed-npcs-red.log`.

The previous harness also accepted live desktop input during automated steps. A logged activation caused an unexpected exterior transition during an interior reload assertion. InputManager and PlayerActivate updates are now disabled only in the opt-in process; programmatic activation checks remain available. Evidence: `work/quest-probe-input-interference.log`. No normal gameplay input setting is changed.

## Remaining acceptance

### Quest destinations and household surnames

User requirement added 14 October: quests directing the player to a named person at a specific house must agree with the household simulation, for both side quests and main quests. Castles and palaces retain vanilla identities and arrangements.

The quest's authoritative person name, placement, destination and scripted relationships take precedence. Do not redirect a quest to a generated household merely because its surname matches; do not rename the quest person or rewrite journal/rumor/marker destinations to satisfy generated population data. Bind the original quest identity at its required location.

In an ordinary residence, establish whether the named person is a permanent resident before generating family ties. Generated relatives adopt that resident's surname, while retaining their own stable IDs, given names, relationships and death records. If a quest introduces an authoritative resident after initial allocation, reconcile the generated family surname through a persisted override with provenance; do not regenerate the family. A visitor, captive, enemy or temporary quest target must not rename the household. Explicit quest relationships, distinct fixed names and unrelated lodgers override the generated shared-surname convention. Do not guess a surname by blindly taking the last word of a title, mononym or unusual name. A quest ending must not randomly roll names again.

Keep castles, palaces and shared institutions outside this generated-family rename policy. Existing vanilla named NPCs remain authoritative. Ambiguous residency must not silently create a family or alter quest data; preserve native behavior until an explicit binding rule is established.

Required tests: a named home resident before initial population creation; a side quest starting after the house was visited; an actual main-quest home target; a temporary visitor/captive; conflicting fixed names; castle/palace NPCs; quest start/completion/cancellation and save/reload. Verify native person binding, journal/rumor destination, actual door/interior presence, conversation/task advancement, generated relatives' surname and unchanged resident IDs. This policy is recorded scope, not implemented behavior. The current synthetic tavern probe does not prove these cases.

### Broad destination and delivery integration

The user explicitly includes Fighters Guild and other quest givers whose procedurally selected destinations vary between offers. Cover destination-based main and side quests through a shared identity/location integration, not a list of individually rewritten quests. The recipient, household, offer, acceptance text, journal, directions/rumors, discovered building/map label, native activation and hand-in must refer to the same authoritative person and site. Use stable map/building/person/quest identifiers, not name-string matching. Cover delivery, retrieval, escort, rescue and other scripted destinations, including remote towns, previously visited houses, multiple concurrent quests using one building and destinations changed by later quest stages. Residency and relatedness still depend on role; a delivery recipient may be a guest.

Source inspection in the pinned DFU checkout establishes why a late rename is insufficient:

- `Person.AssignDisplayName()` independently sets a faction individual's fixed name or a generated full name.
- `Place.GetBuildingName()` generates a residence surname separately; non-residences use their fixed building name. A person's surname and the residence label must not be assumed equal in vanilla.
- `Person.ExpandMacro()` exposes person name and dialog-place building/town/region independently. The displayed destination can follow the current dialog place rather than always the home place.
- `DaggerfallGuildServicePopupWindow.OfferQuest()` creates the offer prompt before the quest is started. Its inherited `DaggerfallQuestPopupWindow.OfferQuest_OnButtonClick()` starts the quest only after acceptance. `QuestMachine.OnQuestStarted` alone is therefore too late for names already shown in an offer; partial quest-list previews must also remain free of persistent allocation side effects.
- `Place.SiteDetails` has a public setter. This is a possible integration boundary, not proof that changing it alone updates discovered labels, existing topic data or already rendered text.

Resolve procedural naming and household bindings before any player-visible offer is expanded, then keep those published bindings stable. Fixed story names and sites remain untouched. Do not silently rename an existing household every time another procedural quest chooses it or rewrite a previously issued destination. Conflicts need an explicit binding policy that preserves all active quest contracts. Rejection/cancellation must not leave an unaccepted preview's temporary family/name changes behind. Existing active quests loaded from saves require reconciliation without changing the directions the player already received. Tests must exercise actual guild offer timing and hand-in, plus distinct main-quest placement paths; the previous quest probe covers neither.

The breadth requirement includes every main/side quest family that supplies a destination, not only Fighters Guild deliveries. Before declaring compatibility, inventory the pinned quest scripts by person/place construction and macro/placement path. Use that inventory to select representative native end-to-end cases for each distinct path, and identify script-specific exceptions separately. Record which cases are tested, unsupported or still untested. A passing generic synthetic probe is not evidence that all shipped quests match. Include fixed story people, procedural household recipients, temporary visitors/captives, institutions/castles, cross-town destinations, chained or changing destinations and quests already active in an older save. The acceptance comparison is the exact offer/journal/directions/door/recipient/task contract, together with stable household identity and appropriate family surnames.

## Mod-added quests

The compatibility target includes quest packs using DFU's native person/place and quest-text pipeline. Bind a procedural recipient to the appropriate household before names are published; preserve the actual quest resource identity, task conditions and hand-in behavior. A display-only name replacement is insufficient. The earlier prohibition on renaming a quest person applies to fixed or already published contracts; a future pre-publication binding may select a generated recipient's name consistently through the native resource. Fixed named characters and explicit scripted relationships remain authoritative. Do not assign a temporary visitor the family name merely because the quest places them in that house.

Custom C# quest engines, private placement systems or bypassed macro expansion may require explicit adapters. Inventory and test actual mod-added quest cases before claiming support, including accepted quests loaded from older saves. No quest-pack compatibility or pre-publication interception is implemented or verified yet.

## Pre-publication interception checkpoint, 14 October 2026

The preceding no-interception status is superseded only for a separate opt-in feasibility probe. A standard mod package successfully attaches a Harmony postfix to native `Parser.Parse` in the shipped player. For its synthetic procedural recipient, it selects an existing living male household resident at the native quest-selected local home and sets the native person display name and residence name before offer expansion. This is temporary probe behavior; normal Living Cities gameplay does not rewrite quests yet.

The expanded actual-player test passes:

- Partial header parses create no binding; fixed recipient Meridia and unrelated quests are excluded.
- Every non-name field in the person save data and site details remains unchanged. Quest UID survives native FullSerializer JSON round-trip and reconstruction.
- Native message macro output and `QuestMachine.CreateMessagePrompt` text labels contain both bound names before the quest starts.
- A subsequent explicit tavern assignment changes `GetDialogPlace` and message destination while retaining the person's bound name and original home name. That assigned destination survives serialization.
- The persistent Living Cities population remains byte-identical in its snapshot. No accepted quest, household allocation or new game save is created by the probe itself.
- Owner-specific unpatch removes interception; a subsequent parse no longer invokes it. The test runner removes the separate temporary `.dfmod` afterward. Base-game files and tracked engine source remain unchanged.

Reproduce: `tools/Build.ps1 -QuestBindingProbe`, then `tools/TownTest.ps1 -FixtureCharacter LivingCitiesTest-126494eb -QuestBindingProbe`. Both commands passed; initial red evidence is `work/quest-binding-red.log` (recipient unchanged without interception), final evidence `work/quest-binding-pass.log`. Core tests remain 97/97; native build checks pass. Read-only Luna/high review found no confirmed critical or important defect. The normal bundle contains only the dormant opt-in test bridge, not Harmony or the binding implementation.

The test constructs a native prompt but does not accept an actual guild quest, place or activate a renamed target, or perform hand-in. FullSerializer reconstruction is not an actual save-slot reload for this new probe. It does not establish native discovered map labels, rumors, concurrent quests, story relationships, semantic residency classification, mod-file/asset loading, or broad main/side quest compatibility. Native macro expansion logs an attempted dialog-link lookup before quest registration; the probe does not claim persisted talk-topic behavior.

The reproducible inventory now covers 265 shipped scripts and 125 explicit NPC placement actions. `QUEST-SOURCE-SURVEY.md` records counts, the merchant relocation counterexample and open coverage groups. `docs/superpowers/plans/2026-10-14-quest-binding.md` records the remaining persisted binding and actual actor/quest integration work.

This tests a synthetic questor binding on an actual original tavern NPC, using the native activation handler directly. It does not test mouse raycasting, door locks/greeting gates, every named individual/main quest, turn-in rewards, shops/services while relocated, scripted placement into another building, death policy, or cross-map actor transfer. M1 probes are complete; the corresponding M2/M3 integration work remains open. No engine changes were required for this probe. Read-only Luna/high review found no confirmed critical or important issue; runtime evidence remains the acceptance authority.
