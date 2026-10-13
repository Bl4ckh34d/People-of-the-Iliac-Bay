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

This tests a synthetic questor binding on an actual original tavern NPC, using the native activation handler directly. It does not test mouse raycasting, door locks/greeting gates, every named individual/main quest, turn-in rewards, shops/services while relocated, scripted placement into another building, death policy, or cross-map actor transfer. M1 probes are complete; the corresponding M2/M3 integration work remains open. No engine changes were required for this probe. Read-only Luna/high review found no confirmed critical or important issue; runtime evidence remains the acceptance authority.
