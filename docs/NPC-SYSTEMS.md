# NPC systems and current coverage

Latest accepted development slice: [timed mood, stable personality, sparse relationship state and wider chat](2026-10-21-timed-mood-personality-and-chat-ui.md). Storage/context and owned native checks pass; actual shared event appraisal, severe speech consequences and paid companions remain incomplete.

Optional NPC speech is separately wired in source, with limited native pilot evidence for the opening greeting and portrait mouth shapes. Offline streaming/normalization checks and this pilot do not establish full native live-stream or module-matrix acceptance; see the [TTS and STT roadmap](ROADMAP.md#unique-npc-voices-tts-and-optional-stt--requested-19-october-2026).

## Approved framework and current boundary — 21 October 2026

The [approved framework](superpowers/specs/2026-10-21-npc-motivation-relationships-and-contracts.md) is the common target for dialogue and decisions. Personality, temporary mood, directed affinity/disposition, trust, conflicts, resources, knowledge and scoped obligations have distinct meanings. Decision-specific utility guides bounded choices; native rules own eligibility, information access, accepted commitments and real effects. Paid companions will use duties and a wage/refund ledger rather than an obedience score.

Current code supplies mood, native disposition/friendship credit, snapshots and action/task receipts. Directed reliability trust is persisted and changes only from source-whitelisted evidence such as attended appointments, observed combat help, healing and fulfilled obligations; ordinary friendly prose changes affinity/disposition but does not silently prove dependability. Unified motivation projection, the complete utility policy and companion contracts/escrow remain incomplete. See the [integration map and test baseline](2026-10-21-framework-integration-map.md), [delivery plan](superpowers/plans/2026-10-21-npc-motivation-and-contract-delivery.md) and current [action implementation ledger](ROADMAP.md#shared-actor-framework-target--architecture-discussion-18-october).

19 October semantic architecture integration: the normal online conversation path lets the local model interpret the complete current message with attributed history, then binds typed decisions to bounded native candidates. Legacy phrase recognizers do not veto successful online decisions. Proposed busy pauses and family threats receive a separate `speaker|mode` decision; only `PLAYER|DIRECT` can persist the effect. Callbacks retain the original person, session, text and submission time. Actual busy controls, pause disk restore, and mobile/static threat refusal now pass native checks; [Testing](TESTING.md) records their bundle hashes and limits. Online action decomposition preserves original contiguous source spans, and dependency candidates bind a later handover to the earlier step's eventual exact acquired item. Purchase candidates include real quoted prices and the NPC's current balance. All commitments and effects still require native validation; a request is not consent, and acceptance is not completion. Their bounded native acceptance is recorded below and in Testing. On-demand general inventory/container querying, truthful stationary roles, and shared-walk greeting continuity remain unchecked work in [Roadmap](ROADMAP.md).

19 October physical continuation follow-up: the local errand fixture now passes real disk reload during return, exact inventory/wallet preservation, measured approach to a retained later visit and home, and actual entry into both interiors. Long route/dwell intervals are explicitly advanced, so this is not uninterrupted full-day certification. Route-less fallback HomeDay records are excluded from outdoor migration rather than incorrectly replacing nonexistent saved routine rows. Dedicated home-container and no-show probes remain separate acceptance steps; see Testing.

19 October feedback acceptance: committed ordered tasks now retain a truthful acknowledgment when the final social reply is unavailable, exceeds context, or cannot dispatch. The reply is stored with the existing `action` memory source. Failed tasks emit their saved partial-completion receipt once through English HUD and observed memory without undoing completed purchases. Native fault injection, archive recall and serialization checks pass within the scopes recorded in Testing. Only actual meeting observations use the short pinned meeting excerpt; detailed observed outcomes receive the normal event retrieval budget.

19 October final integration acceptance: the exact outdoor-point fixture now physically arrives, opens the real native conversation with a visible grounded meeting opener, and retains point/outcome/receipt through disk save/load. The paused Talk window is allowed only in its already-verified post-opening path. All three final-reply fault modes also pass actual local-model purchase-to-handover tests. Core is 1128/1128 and runtime compilation is clean. Earlier source-only or pending-native statements above describe their dated checkpoints; current bundle hashes, quantitative evidence and remaining limits are in [Testing](TESTING.md).

## Per-stop routine continuation — local runtime checkpoint, 19 October

Schema 30 adds an optional `RoutineRecoveryPlan` only for an interrupted resident. `RoutineRecoveryPlanner.TryCreate(settlementId, resident, routine, ordinary, bookings, day, now, origin, departAt, wake, sleep, secondsPerMetre, out plan, out reason)` builds a connected continuation from a known building entrance. `departAt` is the measured time at which the actor can resume at that entrance, not permission to teleport there. An infeasible optional visit is omitted individually; feasible later visits remain. Already expired visits do not replay. Current-origin visits can retain their remaining stay. Required appointments retain their fixed slot, including an already-started slot at the actual venue; an unreachable appointment rejects planning instead of silently disappearing. Return home must still fit before sleep.

The planner reuses the saved routine's small route graph. It performs no Unity physics survey. `RoutineRecoveryIdentity` fingerprints route geometry, timings, home and waking policy, plus the day's reserved appointment slots. `SaveRoutineRecovery` validates the current live owner and route provenance; `GetRoutineRecovery` requires the exact resident/day/routine/appointment identities and returns a detached plan. Saved route segments are checked through spatial buckets rather than a quadratic segment scan. Schema 29 migrates with an empty ledger. Historical death or household retirement does not invalidate the save.

`DailySchedule.ContinueDay(ordinary, plan)` exposes remaining visits, activity, route positions and home arrival through the existing schedule API. Its evaluation does not rewrite earlier history or another day. `ReturnActionToRoutine` now resumes a feasible current indoor visit from the actual body or walks to a fixed known routine doorway, then saves the continuation at actual arrival. The optional final planner argument `originOutside` prevents a departed current optional visit from being replayed and keeps exterior waiting outside; required appointments and future visits at the same building remain eligible. Runtime planning starts from the base routine or its prior continuation, with bookings inserted separately, avoiding appointment child-index collisions. Observed visit checkpoints are cleared only for the affected resident after commitment. The day/revision cache adopts the saved continuation; outdoor arrival uses its actual leg routes. Private readers share that cache without cloning route points per frame.

Core 944/944, clean runtime compilation, installed Unity build, actual doorway commitment with a feasible later visit, direct indoor resumption, and native plan serialization pass. Full physical execution of every subsequent leg, live-game reload mid-return, scene changes during a join, and complete remote errands remain open. Exact evidence and limits are in Testing. The user's confirmed crowd resolution removes that item from the current Nexus limitations.

Ordered task foundation (schema 29): `NpcTasks.cs` defines at most eight immutable typed steps; `NpcTaskStore.cs` owns revision checks, action bindings, completed effect IDs and restore validation. `StreetPopulation.Actions.Tasks.cs` advances only from the existing resident update, using indexed task lookups and fresh native checks at each dispatch. Direct actions cannot bypass active task ownership. Stop cancels the remaining sequence while retaining physical routine recovery. `NpcActionSequenceChat.cs` accepts explicit `then` chains all-or-nothing, with exact bounded durations. Adapters cover Wait first, Follow last, MoveTo, Interact (approach/face only), inventory Transfer, and loaded-shop Purchase. An immediately following `give it to me` / `give them to me` clause can bind to either a purchase's actual acquired UID (including a native split stack) or a completed own-home container retrieval's exact UID. The dependency resolves only after the source action is complete and the same item remains in the NPC inventory; it does not substitute another matching item. Home access is limited to the resident's own permanent private house container, excluding player-owned, shop, inn, faction-owned, rentable-room, public, custom-drop, corpse and quest-action containers. This is not general container access. Lending, combat, lockpicking and climbing remain excluded. Native evidence and limitations belong in Testing.

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

## Supervised action integration — 18 October

Expanded native chat acceptance verifies polite Wait/Stop wording, negative historical requests, a natural follow offer, explicit assent and cancellation. Cancelled/completed jobs can retain physical routine recovery, but that recovery no longer blocks a new conversational offer. `ActiveNpcAction` deliberately includes recovery jobs; callers deciding whether a new request is possible must distinguish terminal recovery from unfinished execution. Native disk save/load now verifies a running wait and completed-transfer inventory/feedback conservation. See the expanded acceptance section in [Testing](TESTING.md) for exact cases and remaining interruption coverage.

General shop purchases use actual current-day stock in the loaded open shop. An exact unambiguous item name binds a native shelf LoadID, item UID/count, keeper, and quoted price. The NPC pays its own wallet using its own trade stats; the keeper receives the money. Native ownership updates reject stale revisions, replay, inaccessible/quest/private stock, and already-owned source UIDs before splitting. Purchase retains the acquired item; either a separately accepted transfer or a preaccepted dependent task step moves it to the nearby player. Example: `Buy one Steel Saber then give it to me`. Both steps require agreement before the chain starts; acquisition is resolved only after the actual transaction. No lending, automatic stock refresh or cross-building shopping is included.

Parent-run native acceptance (`work/astra-commerce-player.log`) verifies purchase of a real Steel Saber, exact stock removal, keeper/buyer/player wallet conservation, physical handover, native serialization and replay rejection. The first native run exposed an unsafe assumption that the radial person-approach endpoint was walkable. Indoor person approaches now validate the preferred point and at most 31 surveyed alternatives within the original interaction range, with a real route and line of sight. The fallback has a core regression; the successful native rerun used the preferred point. Exact movement and counter targets retain their own routes. This fixture directly accepts typed options; it does not test model willingness or arbitrary prose.

**Player-to-NPC receive evidence, source checkpoint 21 October:** a completed incoming item action persists an exact receipt on its existing action row, including the source UID, transient moved UID, retained destination UID after native merge, quantity, recipient, player counterparty and game time. The event uses the retained UID and is recorded as neutral `ItemReceived`; it does not infer gift purpose, debt, gratitude, obligation or trust. When appraisal is ready, the worker scans at most 32 action rows per tick on its existing 0.5-second cadence; the durable action receipt remains authoritative if the optional inbox is unavailable. Core passes 1477/1477 and runtime compilation has zero warnings/errors. `NativeNpcInventoryProbe` covers the adapter path in the existing build harness. Native editor/item API execution and loaded-player acceptance are pending parent verification; this does not certify the broader inventory or action framework.

Named appointment dates use the native calendar and the request-time clock. Parent-run acceptance (`work/astra-appointments-player.log`) verifies constrained parsing, the production booking callback, delayed-response date binding, physical next-day walking, attendance, neutral memory, and native serialization. Same-day physical replanning remains deferred and returns an honest refusal. The fixture injects a parsed decision rather than invoking the language model.

Local FOLLOW continues across supported building/exterior doors in the same settlement. A persisted verified source route determines travel time; the NPC waits for a clear real entrance and keeps its identity, inventory and original consent deadline. Parent-run acceptance (`work/astra-follow-player.log`) verifies both door directions, unique timed appearance, native serialization, cancellation and physical routine recovery. Route work is bounded per transition; there is no background simulation of every person's movement. Unsupported exits, distant settlement/dungeon changes, danger or expiry end following honestly. Current Talk cannot address an absent follower during transit, so remote stop is not exposed; stop before crossing or after arrival works. This does not yet provide general companion travel.

## NPC actor framework foundation — source checkpoint, 18 October

`NpcProfiles.cs` provides versioned deterministic base attributes, all 35 native-indexed skills, class training affinities and maximum HP/MP. `NpcActorProgress.cs` adds schema-27 branch-local experience for changed people only; untouched profile reads create no save records. `ReadNpcActor` returns one detached batch view for a materialized actor, avoiding 35 independent profile regenerations. Experience uses integer cumulative thresholds and caps; old saves start with an empty ledger. Native FullSerializer independent restore passed. These are core capabilities, not yet native combat or skill-use bindings.

`NpcActionContracts.cs` defines stable typed targets and legal action phase transitions. Completion requires an executing action and native success confirmation. Eligibility includes actual adapter availability, consent, life state, quest ownership and schedule compatibility. Enum entries for following, combat, lockpicking, climbing, seating or replanning do not implement those actions or advertise them to the LLM. The action executor, registered native adapters, persistent jobs and inventory ownership remain the next stages in [the canonical framework plan](ROADMAP.md#shared-actor-framework-target--architecture-discussion-18-october).

## Persistent conversation memory and home knowledge

22 October source correction: current `ConversationRetrieval`/`ConversationFeatureIndex` use lexical and concept features with bounded index warmup; the older purely lexical description below is historical. This is not general semantic understanding. Household counts and recorded family links now enter the exact speaker's shared social snapshot through `NpcHouseholdKnowledge`; disclosure remains optional and missing legacy family links do not establish childlessness. New five-week Core regressions preserve corrected names/attribution, claims versus observations, and NPC/branch isolation. Semantic compaction remains planned: `ConversationMemoryCompactionEligibility` supplies only its age gate. See the [expanded acceptance scope](2026-10-22-npc-conversation-acceptance.md).

Normal world time runs at 2× realtime. Mobile citizens walk at a stable personal pace of 4.2–6.4 km/h (1.1667–1.7778 m/s); the mean is about 5.3 km/h. Fleeing keeps its existing 3.25–4.55 m/s values. Personal paces derive from persisted identity appearance seeds and do not reroll on reload or transitions. Route timing and physical indoor movement use the citizen's pace. Loaded local daily caches encoded at 12× or the former fixed walking speed are repaired; saved personal route version 2 routines rebuild at the current pace. Existing inter-settlement trips with linked bookings/admission records retain their previous timing pending a coordinated migration; newly planned trips use the personal pace and current clock. Guard formation schedules retain a coordinated pace. See [Testing](TESTING.md#latest-acceptance) for measured scope.

Guard roster targets are twice the previous ordinary quota: two guards for hamlets below 40 residents; otherwise twice `min(350, max(city ? 16 : 8, ceil(residents × 0.08)))`. Four additional slots per surveyed city gate cover two inside and two outside posts. For Daggerfall's saved census of 290 residents and four gates, this requests 48 ordinary plus 16 gate guards, or 64 total. Existing rosters expand from the computed census target, never by multiplying the saved target. Deaths, injuries and IDs remain; population expansion does not bypass casualty replacement delays. New posts are selected across the available range. Public-post and gate surveys run in slices, including completion after an indoor load. Reachable distinct posts are required for expansion; insufficient geometry logs a pending expansion instead of creating overlapping guards. Actor radius and patrol-pair limits are unchanged.

Completed typed exchanges and ordinary Talk-topic exchanges are stored in a shared archive under the active DFU `Saves/PeopleMemory` directory. Schema 23 saves contain a small `ConversationArchive` checkpoint; `ConversationMemories` contains only pending runtime entries or legacy inline data and is emptied after a successful archive commit. Closing a conversation also records the meeting's game date and actual building/settlement. Memories belong to the saved character and exact persistent NPC identity, never the display name. Building NPCs retain their native map/building/hash identity; custom quest actors use quest UID/resource symbol to avoid sharing memories with a replacement at the same position. Unidentified vanilla wanderers and unowned custom/dungeon actors retain the current transcript only; adopting them into persistent people remains a separate roadmap item.

Each new social reply retrieves relevant older exchanges and a recent observed meeting, bounded to 2,000 characters. The live conversation window slides to fit the model's context; current identity, weather, location, household and schedule remain fixed context. Every conversation opening has one compact timestamp, displayed centered in the native font and recorded as an observed session marker. Its latest timestamp is pinned once in fixed model context; older session timestamps remain with historical messages. Fresh authoritative context follows older chat messages so physical location takes precedence over remembered destinations. Retrieval is local lexical ranking with recency, without embeddings or another model service. Compressed archive chunks store each committed batch once and refer to immutable parents. Loading an older checkpoint excludes its later children; continuing it creates another child, so equal game dates cannot merge divergent timelines. The archive ID/head is fixed-size regardless of transcript length. Runtime restore still reconstructs the selected history and recall scans that NPC's records; large multi-year load/recall performance remains unmeasured.

The archive retains saved records rather than silently discarding older turns. New conversations stay in RAM until a save commits them. Quitting or loading another save discards unsaved conversations. An interrupted save can leave unreferenced chunks, but no saved timeline can recall them. Total archive disk use grows once across saved branches, without copying the full history into every slot. No automatic deletion removes potentially referenced history. Legacy inline saves migrate on their next save; original old slots remain unchanged. Missing/corrupt ancestry rejects restoration without replacing it with blank or another branch's memories. Backup/export instructions are in [Installation](INSTALLATION.md#memory-backups).

Player claims and generated NPC anecdotes remain quoted speech, not verified shared events. The observed-event API currently records actual meetings; companion trips/actions do not exist yet and are not fabricated. Nearby visible indoor people are supplied as current context, without granting access to their private conversations. No hidden quest script or unrevealed quest facts are imported. Earlier conversations from builds that did not record memory cannot be reconstructed. A model can still misinterpret a retrieved fact; the journal and native actions provide inspectable ground truth.

Home questions resolve the actual saved `HomeKey`, not the model's invented geography. Effective disposition **20 or higher** permits disclosure. A trusted request can discover the exact home on the native map, respecting the map-request setting. Unnamed residences get a map note based on the actual speaker; existing quest labels and player notes are preserved. Remote or unassigned homes are not given invented local addresses. Intervening topic changes clear the implicit “mark it” home target.

Implementation: `SharedConversationArchive.cs`, `ConversationArchiveState.cs`, `ConversationMemory.cs`, `LivingCitiesMod.DialogueMemory.cs`, `LivingCitiesMod.DialogueHome.cs`. Verification and remaining limits: [Testing](TESTING.md).

## Browsing local NPC knowledge

Broad typed questions can search the current NPC's native location topics instead of requiring a destination's exact name. Inns/lodging resolve to Taverns, shrines to Local temples, and shop, shop-service, guild, bank and library queries resolve to their available native categories. Specific services such as potion shops narrow a general shop query. Personal questions about “your inn” remain roleplay rather than a search for other businesses.

The search evaluates and caches vanilla `GetNPCKnowledgeAboutItem` results for actual local buildings and checks native willingness, including the mod's disposition effects. It preserves the native RNG seed and does not run answer macros while listing places. Quest-resource topics remain on their native path. Up to three nearby known choices, with native compass directions, enter the LLM context for natural wording. Only choices actually named in the displayed reply become the current conversation's suggested set.

An explicit “mark one” request randomly chooses from that set after rechecking current native topic identity, knowledge and willingness. A direct category request such as “mark an inn” can choose any known matching destination. The real native map function executes before the model receives its success/failure context. Disabled map requests and indoor direction-only rules remain effective. Category changes, intervening native topic selections and a new conversation clear stale suggestions; no suggestion list is added to the save format. Exact named destinations and trusted personal-home requests retain their existing handling.

This extends structured native-topic retrieval; it adds no vector database, embedding service or model download. The location search is bounded to public native local-building topics, not a new mechanism for browsing hidden quest people, secrets or arbitrary world data. Model prose can still require human quality testing; generated text does not grant knowledge or execute map actions.

Current search/action results also enter a separate system message after the sliding history, and remain intact if older turns are trimmed. The displayed location reply is checked for required supplied names, excluded native destinations and marker-result contradictions. A failed wording check uses the actual dynamic native-fact answer, such as the real named inn choices or the completed/failed marker result. This protects location answers without applying a location validator to ordinary social conversation. The displayed, verified answer is what enters memory and the follow-up selection set.

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

- Native mobile citizens: 24 archives, 381–398 and 451–456. Five unique walking directions mirror to eight bearings. Record 5 provides frontal idle art. All 24 appearances now have eight installed single standing frames, including byte-identical native fronts. `StandingArt` lazily caches authored 4×2 atlases; `StandingPose` uses actual native record-5 dimensions, independent bearings without mirroring, and releases to native locomotion/conversation or sitting/sleeping renderers. Unsupported/replaced appearances retain their fallback. Explicit free-standing contexts turn toward a visible player within 2.5 m (3 m release distance), smoothly at 180 degrees/second, with visibility checks capped at four per second. Focused activities and seating retain task heading. Native fixture verifies all eight bearings, size/anchor, player attention and transitions; see `work/standing-native1-player.log`. This verifies the renderer on one appearance, while package/pixel checks cover all 24 sets.
- Sleeping: current `SleepArtCatalog` contains 326 source mappings backed by 300 distinct generated appearances. The original 215 mappings are preserved; eight vanilla gaps (184/5–184/12) and 103 provider-qualified humanoid references add 109 distinct images in 14 atlases. Animals, including the three cat-shaped Alfiq references, are excluded. A broader provider scan subsequently found DET humanoid resources under billboard folders, including effective source art for 10015/0; those additional groups are not covered by this 326-mapping inventory. Corrected sheet 09 is user-accepted; its measured blanket/head ratio is within about 1% of the approved full-body reference. Art availability does not supply a routine or bed.
- Sleeping runtime uses the full catalog, not only the historical 385/453 pilot. Replacement art follows native loose-file/enabled-mod precedence. Unknown replacements receive no substitute vanilla face; appearance resolution and decoded atlases are cached for the owner's lifetime. Two installed-provider examples passed native precedence, PNG/UV loading and repeated lookup checks. This does not certify every new appearance on an actual occupied bed.
- Six Lively archive-1200 references have provider-qualified sleeping art; the broader effective-source inventory, including 10015, is not complete. New/replaced appearances need inspected references and explicit provider-aware mapping.
- Running: archive 385 has a rejected historical pilot available for a bounded preview. New Astra/high artwork is in review, with exactly four frames per direction to match native walking and eight independently consistent bearings. The user requires the full pilot sheet for critique, followed by an art-worker pause. No new running artwork is installed or accepted. Stationary NPCs generally lack complete walking/running cycles.
- A stationary flat cannot become a proper walking animation merely by translating the billboard. Retain its native interactive identity while adding a separate directional renderer, once approved art exists.

Sources: `SleepArtCatalog.cs`, `SleepArtProviderCatalog.cs`, `SleepingArt.cs`, `art/sleep/coverage.json`, `art/sleep/missing-coverage.json`, `art/standing/README.md`, `art/run/pilot-385/README.md`, installed bundle manifests and local asset lists.

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
## Starting wallets

Starting cash is deterministic from world seed and persistent NPC identity. Large-city residents and native Nobility social-group members receive 250–500 gold; medium settlements 50–150; villages 10–35; farm residents/workers 0–15. Nobility takes precedence. The authoritative household, reserved permanent lodger home or traveler HomeSettlement determines origin, never the current visit or inn. Native non-guest workers use their registered settlement. Unknown-origin rented-bedroom guests use a conservative 10–35 fallback without treating the inn as home.

Existing saved balances always take precedence and are never rerolled or replenished by this policy. Native binding, paid services, first corpse inventory and first shopkeeper wallet use the same initializer. Origin lookup happens only before wallet materialization, not per frame. No new save fields are required. `NpcInventoryTests` and `InnLodgerHomeTests` cover inclusive ranges, determinism, nobility, farm workers, traveler/lodger origins, and saved-balance preservation.
