# NPC systems and current coverage

Current coverage and source ownership are below. Planned behavior belongs in [Roadmap](ROADMAP.md); dated implementation details and older requirements remain in [History](HISTORY.md). This guide does not certify all NPCs, quests or buildings.

Keeper role follows native selling/repair or room rental, not merely a building key or bed. Ordinary authored guest-bedroom flats are replaced by the normal mobile traveler population; no instant guests are generated. Main-hall people, non-rentable inn staff rooms and quest/service actors remain separate. Living retired flats release their former bed/home reservations without fake deaths; dead identities retain corpse evidence. Ordinary upstairs shop-bedroom tenants are replaced by an on-site keeper household with at most one female partner when a second bed is free. Existing families and protected actors remain intact. Inns issue no inn keys. See [replacement boundaries](DEVELOPMENT.md#bedroom-flat-replacement-and-shop-households--17-october-2026).

| Area | Current boundary / remaining work |
| --- | --- |
| Identity and households | Generated residents are persistent. Authored local workers and mod NPCs still need universal census/adoption and housing priority. |
| Schedules and travel | Daily itineraries, guests and inter-settlement travel exist. Physical movement, inn arrival and stated/actual destination consistency still need acceptance. |
| Guards and crime | Finite guards, patrols, pursuit, reports, indoor responses, scenes and welfare exist across tested paths; this is not blanket native/mod quest coverage. |
| Mortality and access | Ordinary native people have partial reactions, corpse/property-key handling and household consequences. Do not assume all provider actors use these paths. |
| Sleep and art | Native bed surveys and sleeping art exist. Shared beds, complete external art coverage and universal mobile institutional staff remain future work. |
| Conversation | Saved per-person journals, bounded retrieval, sliding live transcript, current world context and trusted home disclosure are implemented. Quest logic remains native; authored story personas and broad quest capability review remain future work. |
| Commerce | Disposition influences implemented native commerce/service hooks; guild expulsion and noble consequences remain tracked separately in the roadmap. |

## Persistent conversation memory and home knowledge

Normal world time runs at 2× realtime. Mobile citizens walk at a stable personal pace of 1.105–1.495 m/s (±15% around 1.3); fleeing already varies from 3.25–4.55 m/s and keeps its existing values. Both derive from persisted identity appearance seeds and do not reroll on reload or transitions. Route timing and physical indoor movement use the citizen's pace. Loaded local daily caches encoded at 12× or the former fixed walking speed are repaired; fallback visits keep exact walking durations. Existing inter-settlement trips with linked bookings/admission records retain their previous timing pending a coordinated migration; newly planned trips use the personal pace and current clock. Guard formation schedules retain a coordinated pace. See [Testing](TESTING.md#latest-acceptance) for measured scope.

Completed typed exchanges and ordinary Talk-topic exchanges are stored in a shared archive under the active DFU `Saves/PeopleMemory` directory. Schema 23 saves contain a small `ConversationArchive` checkpoint; `ConversationMemories` contains only pending runtime entries or legacy inline data and is emptied after a successful archive commit. Closing a conversation also records the meeting's game date and actual building/settlement. Memories belong to the saved character and exact persistent NPC identity, never the display name. Building NPCs retain their native map/building/hash identity; custom quest actors use quest UID/resource symbol to avoid sharing memories with a replacement at the same position. Unidentified vanilla wanderers and unowned custom/dungeon actors retain the current transcript only; adopting them into persistent people remains a separate roadmap item.

Each new social reply retrieves relevant older exchanges and a recent observed meeting, bounded to 2,000 characters. The existing live conversation window still slides to fit the model's context; current identity, weather, clock, location, household and schedule context remains separate. Retrieval is local lexical ranking with recency, without embeddings or another model service. Compressed archive chunks store each committed batch once and refer to immutable parents. Loading an older checkpoint excludes its later children; continuing it creates another child, so equal game dates cannot merge divergent timelines. The archive ID/head is fixed-size regardless of transcript length. Runtime restore still reconstructs the selected history and recall scans that NPC's records; large multi-year load/recall performance remains unmeasured.

The archive retains saved records rather than silently discarding older turns. New conversations stay in RAM until a save commits them. Quitting or loading another save discards unsaved conversations. An interrupted save can leave unreferenced chunks, but no saved timeline can recall them. Total archive disk use grows once across saved branches, without copying the full history into every slot. No automatic deletion removes potentially referenced history. Legacy inline saves migrate on their next save; original old slots remain unchanged. Missing/corrupt ancestry rejects restoration without replacing it with blank or another branch's memories. Backup/export instructions are in [Installation](INSTALLATION.md#memory-backups).

Player claims and generated NPC anecdotes remain quoted speech, not verified shared events. The observed-event API currently records actual meetings; companion trips/actions do not exist yet and are not fabricated. Nearby visible indoor people are supplied as current context, without granting access to their private conversations. No hidden quest script or unrevealed quest facts are imported. Earlier conversations from builds that did not record memory cannot be reconstructed. A model can still misinterpret a retrieved fact; the journal and native actions provide inspectable ground truth.

Home questions resolve the actual saved `HomeKey`, not the model's invented geography. Effective disposition **20 or higher** permits disclosure. A trusted request can discover the exact home on the native map, respecting the map-request setting. Unnamed residences get a map note based on the actual speaker; existing quest labels and player notes are preserved. Remote or unassigned homes are not given invented local addresses. Intervening topic changes clear the implicit “mark it” home target.

Implementation: `SharedConversationArchive.cs`, `ConversationArchiveState.cs`, `ConversationMemory.cs`, `LivingCitiesMod.DialogueMemory.cs`, `LivingCitiesMod.DialogueHome.cs`. Verification and remaining limits: [Testing](TESTING.md).

<a id="inventory"></a>

# NPC integration inventory and compatibility plan

Audited 17 October 2026 against the local source, installed mod configuration, extracted bundle metadata, and DFU 1.1.1 source. This is a source audit, not proof that every actor in every settlement has been exercised in the player. Historical checkpoint documents describe successive states; this report distinguishes current code from proposed integration.

## Main findings

**Future-policy follow-up, 17 October 2026:** see the [TL;DR roadmap](ROADMAP.md), [consolidated requirements](ROADMAP.md#detailed-design) and [delivery plan](ROADMAP.md#delivery-plan). Those describe requested housing priorities, inn visitors/shared beds, tenant keys, market commutes, quest-safe story context and shelved companions. They do not change the implemented coverage recorded below.

There is no universal NPC adoption system yet. Generated household residents, generated visitors, ordinary native interior people, and quest-owned actors follow different paths. Outdoor authored NPCs from external mods are not automatically integrated. Registering an identity does not establish a home, commute, sleep schedule, or complete movement art.

| Population category | Current behavior | Home and beds | Missing coverage |
| --- | --- | --- | --- |
| Generated local residents | Persistent household identity, schedules, visits, reactions and physical actor paths | Household beds; bounded family capacity | Known interior movement and observed-arrival problems; not every routine completes reliably |
| Generated travelers | Separate trip/admission state and local representations; home absence is tracked | Reserved origin household and destination lodging | Full long-distance journey and all interior layouts still need gameplay acceptance |
| Native shopkeepers | Ordinary StaticNPC registered; original service identity retained; death/reaction handling | On-site free bed first, then an unoccupied bed in an existing residential household; on-site keeper family can fill remaining capacity | Off-site housing does not provide an implemented daily commute |
| Authored inn room occupants | Native room occupants are identified from room survey and block people records | Occupied room beds are reserved and unavailable to rent; supported sleep art is used at night | Not classified as local or foreign; no origin household/travel biography follows from the room reservation |
| Other inn common-room flats | Eligible ordinary interior NPCs register | No blanket home or rented-room assignment | Not all are inn residents, generated customers, or travelers; role classification needed |
| Ordinary guild/temple/castle staff | Eligible ordinary interior StaticNPC identities register | No general staff-home or institutional-bed allocator | General sleep, commute and staff duties absent |
| Individual-faction, quest-resource or action NPCs | Excluded from generic interior registration/reconciliation | Native placement remains authoritative | General mobile/sleeping quest integration absent |
| Outdoor authored/mod market NPCs | Not adopted by the indoor registration paths | No general home reservation before generated families | Full identity, housing, reactions, routines and lifecycle integration required |
| Guards managed by this mod | Separate finite roster and duty/response system | Wall-bed reservation/rest paths exist | Bedside idle is not a completed lying-down guard animation; broader wall-layout acceptance remains open |

Sources: `mod/Scripts/KeeperIdentities.cs` (`SurveyKeeperIdentities`, `KeeperSleeping`, `UpdateKeeperSleep`), `NativeInnOccupants.cs` (`Claim`), `NativeTavernRentals.cs`, `ResidentInnActors.cs`, `StreetPopulation.cs`; `src/LivingCities.Core/NativePeople.cs` (`AssignNativeBed`, `ReserveNativeBed`, `CreateKeeperHousehold`) and `WorldState.cs` (`GetOrCreateFamilies`).

## Housing order is not yet sufficient

`GetOrCreateFamilies` populates surveyed residential homes. Later, `SurveyKeeperIdentities` registers eligible indoor people. `ReserveNativeBed` refuses occupied family slots; it does not displace a generated resident. The current system therefore does **not** first reserve housing for all authored citizens, stall owners, guild staff and temple staff before filling the town.

The separate traveler-origin reservation mechanism preserves previously reserved households during partial-town completion. It is not an outdoor native-NPC census or a solution for market workers.

Proposed allocation order for a new town:

1. Read effective world-data replacements and enumerate authored people, including initially inactive people.
2. Classify identity, quest ownership, service role, apparent workplace and known residence. Do not assume workplace equals home.
3. Preserve explicit authored room occupants, existing saved homes, player-owned properties and active quest placements.
4. Allocate reachable institutional staff beds where appropriate; otherwise reserve real residential capacity for local workers.
5. Classify remaining inn occupants as staff, local patrons, lodgers or visitors using explicit policy. A static inn placement alone proves none of those origins.
6. Generate additional households only in remaining capacity. Preserve existing saves through explicit migration; never silently replace families or identities.

Institutions do not universally contain usable beds. Count actual loaded/replaced geometry, accessible floors and routes. If capacity is absent, record the person as needing housing; do not invent beds or move them into arbitrary occupied homes. Visitor status needs a persistent origin identity, not a convenient excuse for missing housing.

## Quest compatibility

### What works now

The automatic household binding is one deliberately narrow rule: unchanged vanilla `A0C00Y14` (The Delivery Boy), `_pickuplocal_`, Gothway Garden. `KnownQuestBindings.cs` checks exact source hash, settlement and role. Individual NPCs, questors and incompatible households are excluded. The offer adapter preserves quest UID, source name, person symbol, home map/building and published identity. Active contracts suppress duplicate generated actors.

The recorded native identity probe demonstrates that moving the **original** StaticNPC within an interior can preserve click-task activation and save/load identity. It does not prove arbitrary cross-building movement, sleeping, streaming replacement, all quests, or main-quest support.

### Why moving a quest NPC can break more than convenience

DFU matches questors using `hash`, `mapID`, `nameSeed` and `buildingKey` (`QuestMachine.IsNPCDataEqual`). A matching name or sprite is insufficient. `StaticNPC` derives some runtime data from the current player location. Recreating that person in another building can therefore change identity unless an adapter preserves the original binding.

`Person.GetDialogPlace` prefers the quest-assigned place. Quests can place, hide, mute, restrain, restore or replace resources. `QuestResourceBehaviour` controls activation and foe death/injury events. A routine controller that unhides a quest-hidden person, deletes a foe, or creates a new generic actor can invalidate the plot.

Fresh local script census: 265 vanilla quest `.txt` files, of which 34 have the main-quest `S000` prefix and 231 have other names. These are script counts, not distinct NPC counts or completed compatibility tests.

| Quest mechanic | Concrete local examples | Required preservation |
| --- | --- | --- |
| Delivery and reward | `A0C00Y14` | Published person/place, carried package, click handler, reward and completion |
| Fixed main-story people | `S0000003` Medora/Nulfaga; `S0000004` Morgiah; `S0000005` Nulfaga/Aubk-i | Individual identity, authored relationships, questor/mute transitions and required clicks |
| Human/monster substitution | `S0000007` Cyndassa/werewolf; `$CUREWER`; `A0C0XY04` The Impostor | Separate Person/Foe resources, hide/place sequence and native kill conditions; not a generic citizen transformation |
| Escort and relocation | `A0C00Y17` | Quest-directed destination and placement over normal home routine |
| Named relatives and service destinations | `A0C01Y09` | Explicit relationship and merchant-at-store assignment, not inferred household surname |
| Hunt/assassination | `M0B00Y00` and other kill tasks | Correct target, intended hostility and actual native death notification |
| Timed/restricted encounters | Quest clocks, assigned places, location conditions | Player can still reach the required target; schedule does not override scripted timing |

Native sources: `Assets/Scripts/Game/StaticNPC.cs`, `Questing/QuestMachine.cs`, `Questing/Person.cs`, `Questing/Components/QuestResourceBehaviour.cs`; quest assets under `Assets/StreamingAssets/Quests`. External bundle sources remain local and are not redistributed here.

### Proposed ownership policy

Use quest roles and capabilities, not a blanket main/side classification. A side-quest giver may be as fragile as a royal main-quest actor. An ordinary NPC may become quest-owned later.

- Quest identity and scripted placement/visibility/hostility take priority over routines.
- One persistent person has one active representation. Preserve original native activation and service bindings.
- Permit same-room sleep/movement only after that role's adapter passes native activation, save/load and quest transition checks.
- Cross-building movement requires a tested identity/placement adapter and consistent directions. Otherwise retain native placement until supported.
- Re-evaluate ownership when quests start, advance, relocate, finish or abort; do not classify once at initial spawn.
- Respect the user's policy allowing deliberate player kills even when a quest becomes impossible. Background simulation must not silently kill or replace important actors. Current generic exclusions do not implement universal quest-NPC mortality.
- Do not fabricate quest success, revive a dead target, or transfer a dead person's quest to an unrelated replacement citizen. Native quest failure/death semantics remain authoritative.

## Installed mods and art

| Mod | Verified contribution | Integration consequence |
| --- | --- | --- |
| Lively Cities 1.3.4 | Static exterior NPCs; registers archives 1200 and 10015; visibility callbacks for day/night/rain | No walking-home integration. Coordinate visibility ownership before adoption; its broad exterior callback can toggle adopted actors too |
| Beautiful Cities 0.5.0 / Beautiful Villages 1.4.2 | Settlement/world-data replacements | Census must use the effective loaded override. Do not attribute every market actor to Lively Cities |
| Taverns Redone 0.17 | Replaced inn layouts | Existing room survey can consume native block records, but full patron navigation is not accepted for every layout |
| Elves & Beastfolk Resources 1.1 / Replaces 1.0 | NPC appearance replacements, including 182/33 and mobile portrait asset `TFAC00I0.RCI_451-0` | Archive/record alone may select a vanilla sleeper for a replaced appearance. Portrait replacement is not proof of a new mobile animation set |
| Flat Replacer 0.2.19 | Replacement framework | Resolve actual appearance/provider before assigning animation art |
| Detailed City Walls 1.23 | Wall geometry/interiors | Guard quarters need real-bed and route survey |
| Rosy's Residences | Installed but disabled | Not an active source of present NPC placements; Rosy's Resources remains enabled |
| Families of Daggerfall / Lootable Villagers | Removed | Not sources of current actors; retained download archives are not active mods |

Lively texture candidates include 1200 records 14,15,17,19,53,54; 1210 records 8–20; 1230/21. These are bundle texture records, **not** a verified list of placed humans. Archive 10015 is registered but needs effective-source resolution. Decorative statues, crops, props and editor markers must not become citizens just because a texture looks humanoid.

Current art inventory:

- Native mobile citizens: 24 archives, 381–398 and 451–456. Five unique walking directions mirror to eight bearings. Record 5 provides two frontal idle frames; complete directional standing art is not accepted.
- Sleeping: current `SleepArtCatalog` contains 215 source mappings backed by 191 distinct generated appearances, including all 24 mobile appearances and many stationary flats. Eight requested source records remain absent: 184/5–184/12. Art availability does not supply a routine or bed.
- Sleeping runtime uses the full catalog, not only the historical 385/453 pilot. Unknown source identities receive no substitute face.
- Lively archives 1200 and 10015 have no sleep mappings. New/replaced appearances need inspected references and explicit provider-aware mapping.
- Running: archive 385 has a rejected pilot available for a bounded preview. It is not accepted all-NPC coverage. Stationary NPCs generally lack complete walking/running cycles.
- A stationary flat cannot become a proper walking animation merely by translating the billboard. Retain its native interactive identity while adding a separate directional renderer, once approved art exists.

Sources: `SleepArtCatalog.cs`, `SleepingArt.cs`, `art/sleep/coverage.json`, `art/standing/README.md`, `art/run/pilot-385/README.md`, installed bundle manifests and local asset lists.

## Required census and acceptance work

The category/source inventory above is complete for this audit. An exhaustive live census of every settlement and every active/inactive actor is **not** complete. No claim of universal compatibility follows from source inspection.

Record these fields for each distinct placed actor: stable map/block/building/record identity; effective provider/override; native hash and name seed; faction/service role; active quest UID/symbol and individual faction; appearance provider/archive/record; authored home/workplace; assigned bed; enabled state and visibility owner; available walk/run/idle/sleep assets; adoption status and exclusion reason. Never persist Unity instance IDs as identity.

Prioritized next work:

- [ ] Add incremental live census/export and compare source/replacement provenance, including disabled actors.
- [ ] Introduce a unified identity/ownership registry and migration design before generic adoption.
- [ ] Allocate authored-local housing before generated population, including staff and stall owners.
- [ ] Resolve inn occupant roles and institutional beds without assuming every interior has housing.
- [ ] Adapt external visibility callbacks for adopted actors only.
- [ ] Preserve native quest/service identity through same-interior movement/sleep; then test cross-building adapters.
- [ ] Build appearance coverage from actual rendered/replaced assets; prioritize common stall/staff flats before unique story actors.
- [ ] Test offer, rejection, acceptance, directions, journal, arrival, sleep/wake, hand-in, reward, hostility, injury/death, relocation, abort, completion, save/reload and concurrent quests.
- [ ] Test fresh and existing saves, mod toggle/load-order changes, dawn/dusk/rain, murder/creature reactions, housing capacity and large-city frame times.

Quest Pack 1 and Quest Actions Extension are included in the separate [additional quest audit](COMPATIBILITY.md#quests). Implementation should proceed by tested role adapters; generic movement of all authored NPCs is not yet safe.
