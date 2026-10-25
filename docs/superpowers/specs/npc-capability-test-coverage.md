# NPC capability test coverage specification and audit

Updated 2026-10-20 from the frozen coverage matrix, source fixture audit, saved offered-choice run summaries, and a separate real-Core probe. This compact crosswalk preserves every original capability and requirement ID; it does not claim full future-feature acceptance.

## Evidence boundary

The live model-choice evaluation exercised the frozen 72-root base suite and the separate 22-case extension. This proves offered-choice decisions were exercised, not that matrix predicates passed. The earlier `coverage-audit.json` is a pre-run fixture-mapping audit; its mapping counts and seven expectation findings are retained, while choice execution status below comes from saved run summaries.

| Status | Meaning |
|---|---|
| `fixture_present` | Candidate fixture mapped/present; this row has no verified live choice in these run summaries. Not a pass. |
| `live_decision_exercised` | At least one direct offered-choice fixture ran; this does not satisfy all acceptance predicates. |
| `no_test_coverage` | No direct executed fixture mapping recorded. |

| Test mode | Evidence |
|---|---|
| Live model choice | Executed for base72 and separate extension22. |
| Model replay | No pinned request/response replay evidence from these live choices. |
| Core contract | Separate CoreBridge self-test passed; deterministic Core only, not native action completion. |
| Simulation | No acceptance run recorded. |
| Native-only | No in-game native acceptance run recorded. |

## Choice run evidence

| Run | Suite | Adapter | Turns/attempts | Allowed / forbidden / unexpected / invalid | Accuracy | Weighted quality |
|---|---|---|---:|---:|---:|---:|
| `broad-run-v1` | base72 | d3 | 190/190 | 165/7/18/0 | 86.84% | 87.33% |
| `broad-run-v1` | base72 | qwen | 190/190 | 161/14/15/0 | 84.74% | 85.25% |
| `broad-flash-v1` | base72 | d3 | 190/190 | 175/9/6/0 | 92.11% | 92.17% |
| `gaps-run-v1` | extension22 | d3 | 44/44 | 40/4/0/0 | 90.91% | 89.09% |
| `gaps-run-v1` | extension22 | qwen | 44/44 | 40/4/0/0 | 90.91% | 90.00% |
| `gaps-flash-v1` | extension22 | d3 | 44/44 | 42/2/0/0 | 95.45% | 94.55% |

The base 190 and extension 44 turns per full summary include paired order arms, not unique roots. D3 and Qwen use different output adapters; later turns may follow divergent histories. These metrics apply to the synthetic offered choices only.

## Capability crosswalk

The source matrix has 86 original capability rows and 125 required scenario rows. IMM-21 is included below as one exploratory perception row, not approved implementation. Original requirements keep their planned acceptance predicates in the source matrix; a mapped choice is partial decision evidence only.

| Capability | Severity | Implementation status | Acceptance predicate | Required scenarios | Choice status | Base cases | Extension cases |
|---|---|---|---|---|---|---|---|
| `CAP-IMM-01` | P1 | Paired diagnostics exist; complete semantic/native baseline open | Frozen turn retains request/config/hash, route, decision, display, latency and native result; score interpretation, arguments, willingness, effects and transport separately. | `SC-BASE-REPLAY`, `SC-FULL-TURN` | `no_test_coverage` | — | — |
| `CAP-IMM-02` | P0 | Snapshot/binding exists; actor/scene/branch coverage incomplete | Routing, willingness, social and validation share revision-bound speaker/role/time/physical activity/commitment; stale actor, scene or save result has no write/effect. | `SC-SNAPSHOT-SWITCH`, `SC-SNAPSHOT-STALE`, `SC-SNAPSHOT-RELOAD` | `live_decision_exercised` | `scene_change_invalidates_stale_reply_binding` | `gap21_repeated_dialogue_is_not_repeated_check`, `gap_elapsed_scene_reload_invalidates_old_result` |
| `CAP-IMM-03` | P1 | Topic and own-inventory paths exist; complete live tool orchestration/readers incomplete | Valid reads return permitted current data; invalid IDs yield typed error; one bounded correction may recover; paging, limits, cancellation and unavailable outcomes stay truthful. | `SC-READ-VALID`, `SC-READ-INVALID`, `SC-READ-PAGE`, `SC-READ-BUDGET` | `live_decision_exercised` | `knowledge.unseen_container_change`, `budget_owned_funds`, `tools.exhausted_budget_truthful_result` | `gap_map_stale_memory_needs_refresh` |
| `CAP-IMM-04` | P0 | Known/unknown context exists; full provenance model planned | Separate world fact, own observation, attributed claim, inference and stale knowledge; unseen changes/distant events stay unknown without valid source; weather follows NPC location. | `SC-KNOW-CLAIM`, `SC-KNOW-STALE`, `SC-KNOW-ACCESS` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory`, `npc_to_npc.rumor_keeps_source_and_uncertainty`, `knowledge.unseen_container_change` | `gap21_sneak_skill_without_check`, `gap21_wall_blocks_sight`, `gap21_sound_has_no_identity`, `gap21_wake_without_identification`, `gap21_player_screenshot_is_not_npc_view`, `gap_map_ambiguous_named_place`, `gap_map_stale_memory_needs_refresh`, `gap_weather_indoor_uses_npc_location`, `gap_weather_stale_outdoor_observation`, `gap_portable_branch_ancestry_isolation`, `gap_weather_and_map_route_unknown` |
| `CAP-IMM-05` | P1 | Attributed archive exists; evidence-linked semantic recall incomplete | Paraphrase/indirect follow-up recalls dated promise/correction with source, certainty and branch; claims/summaries never become verified fact. | `SC-MEM-PARAPHRASE`, `SC-MEM-CONTRADICTION`, `SC-MEM-ATTRIBUTION` | `live_decision_exercised` | `memory.long_term_paraphrase_attributed_promise`, `knowledge.player_claim_not_npc_memory`, `memory.branch_context.recall_only_visible_ancestry`, `npc_to_npc.rumor_keeps_source_and_uncertainty` | `gap_portable_missing_ancestor_stays_missing`, `gap_portable_branch_ancestry_isolation` |
| `CAP-IMM-06` | P1 | Archive/checkpoint exists; scale, compaction and workflow open | Configured multi-year/multi-NPC corpus has bounded load/recall/memory/save cost and retains salient evidence with exact ancestry. | `SC-MEM-SCALE`, `SC-MEM-RESTORE` | `live_decision_exercised` | `memory.large_history_relevant_recall_not_bulk_dump` | — |
| `CAP-IMM-07` | P1 | Failed-reply provenance fixed; activity anchors remain open | Genuine exchange/completed shared activity counts once and persists; unavailable/error reply counts zero; later greeting grounds in saved anchor. | `SC-SHARED-FAIL`, `SC-SHARED-COMPLETE`, `SC-SHARED-RELOAD` | `live_decision_exercised` | `shared_activity.failed_reply_not_shared_experience`, `shared_activity.completed_receipt_supports_greeting` | — |
| `CAP-IMM-08` | P1 | Disposition/familiarity exist; dimensions not distinct | Trust, affection, fear, resentment and obligation produce bounded, sourced, distinguishable choices; native service/reputation remains consistent. | `SC-REL-DISTINCT`, `SC-REL-BOUNDS`, `SC-REL-SAVE` | `live_decision_exercised` | `threat.refusal_and_protected_person`, `busy.dismissal_cooldown_is_not_permanent_hostility`, `friendship.reopen_does_not_farm_growth`, `friendship.wait_replenishes_allowance_not_score` | — |
| `CAP-IMM-09` | P1 | Derived mood exists; persistent typed conversational update not accepted | Context-grounded mood delta applies once, affects next reply, decays toward personal baseline and survives reopen/reload; only NPC outdoor weather applies. | `SC-MOOD-TURNS`, `SC-MOOD-WEATHER`, `SC-MOOD-RELOAD` | `live_decision_exercised` | `mood.weather_only_npc_outdoors`, `mood.indoor_excludes_player_weather` | `gap_weather_indoor_uses_npc_location` |
| `CAP-IMM-10` | P1 | Legacy offset cap exists; continuous accrual stages planned | +5/conversation and shared credit cap +5; 24/30/36h accrual earns fractional credit; waiting only replenishes; reopen/reload cannot reset; max enforced. | `SC-FRIEND-ACCRUAL`, `SC-FRIEND-BOUNDARY`, `SC-FRIEND-RELOAD` | `live_decision_exercised` | `friendship.wait_replenishes_allowance_not_score`, `friendship.reopen_does_not_farm_growth` | — |
| `CAP-IMM-11` | P2 | Traits/authored profiles exist; consequential consistency incomplete | Same person behaves coherently across days/topics for disclosure, spending, destination and risk without trait recital or invented persistent biography. | `SC-PERSONALITY-CONSISTENCY`, `SC-PERSONALITY-FACTS` | `live_decision_exercised` | `personality.stable_preference_disclosure` | — |
| `CAP-IMM-12` | P1 | Routine continuation exists; general multi-day goal ledger absent | Bounded goal with reason/progress/deadline/resources survives scenes/days; interruption/failure replans; duties, appointments and home/sleep protected. | `SC-GOAL-MULTIDAY`, `SC-GOAL-INTERRUPT` | `live_decision_exercised` | `goals.interruption_replan_without_duplicate_effect`, `replan_protect_appointment_sleep` | `gap_elapsed_time_no_offscreen_purchase_receipt`, `gap_elapsed_time_reconstruct_current_schedule` |
| `CAP-IMM-13` | P2 | Money/inventory/danger/duties exist; general utility linkage incomplete | Controlled relevant need/resource change changes feasible choice or willingness for grounded reason; irrelevant control does not. | `SC-NEED-CHOICE`, `SC-NEED-CONTROL` | `live_decision_exercised` | `seating.outdoor_good_weather_preference_capacity`, `market.stage_performance_real_interest_and_availability` | — |
| `CAP-IMM-14` | P1 | Offers/appointments/actions exist; goal-led initiative and negotiation incomplete | Eligible actor proposes recorded feasible goal; acceptance/refusal/alternative/cancel/failure updates once; consent, busy pause and cooldown prevent spam. | `SC-INIT-PROPOSE`, `SC-NEGOTIATE`, `SC-INIT-COOLDOWN` | `live_decision_exercised` | `relationship.familiarity_stranger_invitation`, `relationship.friendship_not_guaranteed_acceptance`, `threat.refusal_and_protected_person`, `busy.dismissal_cooldown_is_not_permanent_hostility` | — |
| `CAP-IMM-15` | P2 | Street-chat poses/deliveries exist; evidence/rumor effects planned | Bounded event records participants/topic/source/outcome and uncertain rumor; later recall truthful; facing pose alone creates no event. | `SC-NPC-NPC-EVENT`, `SC-NPC-NPC-NOPOSE` | `live_decision_exercised` | `npc_to_npc.rumor_keeps_source_and_uncertainty` | — |
| `CAP-IMM-16` | P1 | Analytic schedules/selective actors exist; event budget incomplete | Time skip reconstructs causally valid state without all-actor model calls or fake stock/effects; event budgets and cancellation measured. | `SC-SIM-TIMESKIP`, `SC-SIM-BUDGET`, `SC-SIM-RELOAD` | `live_decision_exercised` | `routine.time_skip_reconstruct_current_activity` | `gap21_sleeping_hearing_no_wake_receipt`, `gap_elapsed_time_no_offscreen_purchase_receipt`, `gap_elapsed_time_reconstruct_current_schedule`, `gap_elapsed_event_budget_reports_partial_work`, `gap_elapsed_scene_reload_invalidates_old_result` |
| `CAP-IMM-17` | P0 | Mobile/static/quest distinctions exist; census/capability breadth incomplete | Per-role/stage registry exposes only verified actions; static staff report true role and cannot promise movement; authored service/identity/quest control persist. | `SC-ACTOR-ROLE`, `SC-ACTOR-STATIC`, `SC-ACTOR-QUEST` | `live_decision_exercised` | `actor.static_jester_role_and_capability`, `actors.unknown_role_stays_unknown` | — |
| `CAP-IMM-18` | P0 | Typed saved-state acknowledgments/receipts exist; broad social/outage/journal coverage open | Proposal/accept/attempt/success/failure are distinct; success needs matching durable receipt; failures do not pollute memory; material effects happen once. | `SC-TRUTH-PHASES`, `SC-TRUTH-FAIL`, `SC-TRUTH-IDEMPOTENT` | `live_decision_exercised` | `market.browse_not_purchase`, `effect.pending_offer_cancelled_before_execution`, `dependent_handover_after_purchase`, `shared_activity.failed_reply_not_shared_experience`, `tools.exhausted_budget_truthful_result`, `goals.interruption_replan_without_duplicate_effect`, `task_persistence_reload` | `gap21_fair_suspicion_feedback`, `gap_portable_import_waits_for_receipt`, `gap_elapsed_time_no_offscreen_purchase_receipt`, `gap_elapsed_event_budget_reports_partial_work`, `gap_elapsed_scene_reload_invalidates_old_result` |
| `CAP-IMM-19` | P0 | Component probes exist; composite lifecycle remains planned | Observe stranger meeting, gradual relationship, commitment, interruption, completion/failure, recovery, separation, reload, days, branch rollback and later recall/initiation. | `SC-LIFECYCLE-HAPPY`, `SC-LIFECYCLE-FAIL`, `SC-LIFECYCLE-BRANCH` | `no_test_coverage` | — | — |
| `CAP-IMM-20` | P1 | Archive CLI exists; ordinary player portable-save workflow incomplete | Clean-install transfer restores exact ancestry/pending records; missing/corrupt/divergent dependencies report; interrupted publication recovers atomically. | `SC-PORTABLE-CLEAN`, `SC-PORTABLE-CORRUPT`, `SC-PORTABLE-INTERRUPT` | `live_decision_exercised` | `portable_memory_missing_dependency_stays_missing` | `gap_portable_import_waits_for_receipt`, `gap_portable_missing_ancestor_stays_missing`, `gap_portable_hash_mismatch_rejects_claim`, `gap_portable_interrupted_publish`, `gap_portable_branch_ancestry_isolation` |
| `CAP-B01` | P1 | Partial routes; breadth open | Movement/door continuity: observe physical approach/entry, correct identity and no teleport/clipping. | `SC-MOVE-DOOR` | `live_decision_exercised` | `ordered_wait_follow_move`, `follow_explicit_duration`, `move_to_ambiguous_destination` | — |
| `CAP-B02` | P1 | Partial | Destination context matches current route; planned destination never projected as current presence. | `SC-SNAPSHOT-STALE` | `live_decision_exercised` | `scene_change_invalidates_stale_reply_binding` | — |
| `CAP-B03` | P1 | Narrow implementation reported | Old-save migration preserves safe 2× clock, appointments and schedule boundaries. | `SC-CLOCK-MIGRATE` | `no_test_coverage` | — | — |
| `CAP-B04` | P2 | Sliced work partial | Survey/background work has bounded callback cost, cancellation and measured p95/p99/max. | `SC-SIM-BUDGET` | `no_test_coverage` | — | — |
| `CAP-I01` | P1 | Planned audit | Census records provider, identity, role, quest/service, appearance, home/work/bed/activity/ownership; separates decor. | `SC-CENSUS` | `no_test_coverage` | — | — |
| `CAP-I02` | P0 | Partial foundations | Stable identity survives stream/reload; ownership re-evaluates at lifecycle boundaries; no Unity instance ID persistence. | `SC-IDENTITY-OWNERSHIP` | `no_test_coverage` | — | — |
| `CAP-I03` | P0 | Partial migrations | Versioned adoption preserves identity, death, family, home, keys and possessions; capacity conflicts explicit. | `SC-MIGRATION` | `no_test_coverage` | — | — |
| `CAP-H01` | P1 | Planned | Local market worker has home in stall settlement, retains service, adds no duplicate. | `SC-MARKET-HOUSING` | `no_test_coverage` | — | — |
| `CAP-H02` | P1 | Planned | Worker physically commutes before opening/after closing with stable person/day variation. | `SC-MARKET-COMMUTE` | `no_test_coverage` | — | — |
| `CAP-H03` | P2 | Planned; calendar/balance open | Market demand/capacity/weather/danger/calendar control visits and bounded close; dialogue reports real opening state. | `SC-MARKET-DEMAND` | `live_decision_exercised` | — | `gap_weather_and_map_route_unknown` |
| `CAP-H04` | P1 | Planned | Authored upstairs tenant classified unless quest/staff evidence; actual bed and household reserved. | `SC-TENANT-BED` | `no_test_coverage` | — | — |
| `CAP-H05` | P1 | Planned | Keeper uses real available bed/home and walks/locks/unlocks correct public entrance while preserving service. | `SC-KEEPER-SERVICE` | `no_test_coverage` | — | — |
| `CAP-H06` | P0 | Planned | Tenant key opens only permitted residence; private/commercial areas protected; genuine corpse key persists once. | `SC-TENANT-ACCESS` | `no_test_coverage` | — | — |
| `CAP-H07` | P1 | Current traveler replacement; full origin/kin deferred | Inn traveler has supported/abstract origin and evidenced visit reason/kin; surname alone is not kinship. | `SC-INN-ORIGIN` | `no_test_coverage` | — | — |
| `CAP-H08` | P1 | Partial mobile travelers | Traveler visits locally, uses room, checks out and physically returns home; multi-day stay supported. | `SC-INN-STAY` | `no_test_coverage` | — | — |
| `CAP-H09` | P2 | Deferred | Explicit two-person shared-bed claims remain independent; third occupant is conflict; no inferred relationship. | `SC-INN-SHAREDBED` | `no_test_coverage` | — | — |
| `CAP-H10` | P1 | Planned | Institutional staff retain service/personality; reserve only real reachable beds; no new outside shift. | `SC-STAFF-ROLE` | `no_test_coverage` | — | — |
| `CAP-A01` | P2 | Standing/sitting partial; running pilot pending | Actual provider supports correct walk/run/idle/sleep direction/identity for declared appearances. | `SC-ANIM-COVERAGE` | `no_test_coverage` | — | — |
| `CAP-A02` | P2 | Deferred art proposal | Shared-bed render preserves independent faces, wake/death/hit selection, axis and scale. | `SC-INN-SHAREDBED` | `no_test_coverage` | — | — |
| `CAP-Q01` | P0 | Partial; broad compatibility unverified | Native quest placement, identity, visibility, activation, timers, services and control remain authoritative. | `SC-QUEST-PRESERVE` | `no_test_coverage` | — | — |
| `CAP-Q02` | P1 | Planned projection | Named story profiles ground voice/obligation; mood affects delivery only, not motives/canon. | `SC-QUEST-PERSONA` | `no_test_coverage` | — | — |
| `CAP-Q03` | P0 | Planned | Bed/movement allowed only by stage capability; returns actor to authored anchor. | `SC-QUEST-BED` | `no_test_coverage` | — | — |
| `CAP-Q04` | P0 | Planned | Speaker/stage projection filters observed, known and player-disclosable facts before ranking; invalidates stage cache. | `SC-QUEST-DISCLOSURE` | `no_test_coverage` | — | — |
| `CAP-Q05` | P0 | Planned | Player guess/private sibling memory/cache/summary cannot reintroduce blocked spoiler; claims retain source. | `SC-QUEST-SPOILER` | `no_test_coverage` | — | — |
| `CAP-Q06` | P0 | Planned; cases documented | Representative vanilla quest roles/stages exercise allow/deny adapter matrix and save/load. | `SC-QUEST-MATRIX` | `no_test_coverage` | — | — |
| `CAP-Q07` | P0 | Deferred | Unreviewed Quest Pack actors stay native until versioned per-stage review. | `SC-QUEST-PACK` | `no_test_coverage` | — | — |
| `CAP-M01` | P0 | Archive/journal partial | Dated memory binds stable NPC/save/time/place/participants/source/certainty/outcome to exact archive ancestry. | `SC-MEM-RESTORE`, `SC-MEM-ATTRIBUTION` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory`, `memory.branch_context.recall_only_visible_ancestry`, `npc_to_npc.rumor_keeps_source_and_uncertainty` | — |
| `CAP-M02` | P1 | Partial archive; semantic/continuity open | Relevant older evidence, recent exact turns and perceived shared activity/listener recover within bounded context. | `SC-MEM-PARAPHRASE`, `SC-SHARED-COMPLETE` | `live_decision_exercised` | `memory.long_term_paraphrase_attributed_promise`, `shared_activity.completed_receipt_supports_greeting` | — |
| `CAP-M03` | P0 | Partial current context; planned complete filter | Filter actor/access/branch/time/stage before ranking; preserve claims and uncertainty. | `SC-KNOW-ACCESS`, `SC-QUEST-DISCLOSURE` | `live_decision_exercised` | `knowledge.unseen_container_change` | — |
| `CAP-F01` | P1 | Shelved | Invitation uses willingness/availability; acceptance reserves time and follows named venue; cancel/abandon/reload releases safely. | `SC-INVITE-ACCEPT`, `SC-INVITE-CANCEL` | `live_decision_exercised` | `relationship.familiarity_stranger_invitation`, `relationship.friendship_not_guaranteed_acceptance`, `effect.pending_offer_cancelled_before_execution`, `cancel_active_commitment` | — |
| `CAP-F02` | P1 | Profile foundation; native bind deferred | Seeded human stats/effects persist once; native damage/status/resources only; no stream reroll or claimed XP. | `SC-ACTOR-PROFILE`, `SC-COMBAT-PERSIST` | `no_test_coverage` | — | — |
| `CAP-F03` | P0 | Shelved | Recruitment allows eligible nonquest actor only; consent/payment/refusal/leave and quest status remain authoritative. | `SC-RECRUIT-GATE`, `SC-RECRUIT-PAYMENT` | `no_test_coverage` | — | — |
| `CAP-F04` | P0 | Partial general inventory; companion scope excluded | Persistent UID/payload/gold/key ownership; capacity, transfer, save, death and stream avoid duplicates. | `SC-INVENTORY-UID`, `SC-INVENTORY-RELOAD` | `live_decision_exercised` | `gift_stack_exact`, `gift_quantity_not_loan`, `dependent_handover_after_purchase` | — |
| `CAP-F05` | P1 | Planned | Verified native skill use gives bounded versioned saved XP; branch/retry/time-skip cannot farm. | `SC-SKILL-PROGRESSION` | `no_test_coverage` | — | — |
| `CAP-F06` | P0 | Limited task actions; broad adapters deferred | Only registered adapters are advertised; target/consent/resource/law/quest checks precede execution and truthful outcome. | `SC-ACTION-ADAPTER-GATE` | `live_decision_exercised` | `access_lock_quest_capabilities`, `theft_service_quest_gate` | — |
| `CAP-P01` | P2 | Planned | Install guide accurately documents order, revisions, checksums, local endpoint, opt-out and recovery. | `SC-PACK-DOCS` | `no_test_coverage` | — | — |
| `CAP-P02` | P2 | Planned | Optional pinned bootstrap recovers from interruption; Python not mandatory solely for downloads. | `SC-PACK-BOOTSTRAP` | `no_test_coverage` | — | — |
| `CAP-P03` | P1 | Planned | Support claims/hardware limits follow measured full-game results for each listed device class. | `SC-PERF-HARDWARE` | `no_test_coverage` | — | — |
| `CAP-P04` | P1 | Planned | Module/toggle combinations preserve identity/state; schedules work without LLM. | `SC-PACK-MODULES` | `no_test_coverage` | — | — |
| `CAP-E01` | P0 | Mixed, defer to domain docs | Crime/guard/haunting/household/visits/visual domain invariants survive NPC changes. | `SC-REGRESSION-E01` | `no_test_coverage` | — | — |
| `CAP-E02` | P1 | Partial; broader matrix open | Paid services/prices/refusal/rank consequences use native rules and actual effects. | `SC-REGRESSION-E02` | `no_test_coverage` | — | — |
| `CAP-E03` | P0 | Planned | Disposition changes tone only; quest/topic/action/outcome remain native and stage authorized. | `SC-QUEST-DISCLOSURE`, `SC-QUEST-PERSONA` | `no_test_coverage` | — | — |
| `CAP-E04` | P2 | English policy; voices future, appearance partial | Player text stays English; voice/appearance expansions preserve identity/provider coverage. | `SC-VOICE-LANG`, `SC-ANIM-COVERAGE` | `no_test_coverage` | — | — |
| `CAP-APPOINTMENT` | P1 | Local-building adapter partial; other place adapters open | Typed dated booking separates proposal/willingness/availability/commit; observed attendance/outcome once, branch-safe. | `SC-APPT-BOOK`, `SC-APPT-CONFLICT`, `SC-APPT-NOSHOW` | `live_decision_exercised` | `fixed_appointment_priority`, `replan_protect_appointment_sleep`, `routine.remaining_day_protect_appointment_and_home`, `meeting.no_show_only_with_witnessed_absence` | — |
| `CAP-SHARED-ACTIVITY` | P1 | Planned; reported regression open | Talk reopen/door crossing preserves ongoing participants/activity; reunion only after actual separation and reload. | `SC-SHARED-CONTINUITY`, `SC-SHARED-SEPARATION` | `live_decision_exercised` | `meeting.active_shared_outing_continuation` | — |
| `CAP-FOLLOWER-DEPARTURE` | P1 | Planned | Follower proactively explains real duty/home/sleep and physically rejoins feasible schedule; reopen/skip/reload cannot reset. | `SC-FOLLOW-DEPART`, `SC-FOLLOW-LATE` | `no_test_coverage` | — | — |
| `CAP-AGENDA-URGENCY` | P1 | Planned extension | Persisted bounded urgency ranks feasible errands, protects obligations/home/sleep, expires optional leisure, eases speed under cap. | `SC-AGENDA-URGENCY`, `SC-AGENDA-RELOAD` | `no_test_coverage` | — | — |
| `CAP-INVENTORY-QUERY` | P0 | Own inventory partial; container knowledge open | Own inventory live/paged; foreign container last-observed with time/access; read does not grant disclosure or mutation. | `SC-READ-PAGE`, `SC-KNOW-STALE`, `SC-KNOW-ACCESS` | `live_decision_exercised` | `knowledge.unseen_container_change` | — |
| `CAP-OUTDOOR-SEATING` | P1 | Nearby action adapter exists; autonomous terrace unproven | Autonomous seat only in good weather/reachable geometry; elevated routes, reservations, release and weather change are observed without teleport. | `SC-SEAT-WEATHER`, `SC-SEAT-ELEVATION`, `SC-SEAT-RELOAD` | `live_decision_exercised` | `seating.outdoor_good_weather_preference_capacity`, `seating.bad_weather_blocks_outdoor`, `seating.elevated_terrace_requires_real_route` | `gap_weather_stale_outdoor_observation` |
| `CAP-MARKET-VISIT` | P2 | Planned | Visit discovered stalls/stage, move between POIs, reserve/facing points, distinguish browse/buy, reconstruct subactivity after skip/load. | `SC-MARKET-POI`, `SC-MARKET-RELOAD` | `live_decision_exercised` | `market.browse_not_purchase`, `market.stage_performance_real_interest_and_availability`, `routine.time_skip_reconstruct_current_activity` | — |
| `CAP-STATIONARY-ROLE` | P1 | Deferred audit/decision | Verified role/activity/capabilities reach dialogue; unknown remains unknown; unsupported travel promise rejected; native service retained. | `SC-ACTOR-STATIC`, `SC-QUEST-PERSONA` | `live_decision_exercised` | `actor.static_jester_role_and_capability` | — |
| `CAP-VOICE-TTS` | P2 | Future; backend open | Stable per-NPC TTS voice persists; off toggle preserves typed dialogue. | `SC-VOICE-IDENTITY`, `SC-VOICE-TOGGLE` | `no_test_coverage` | — | — |
| `CAP-VOICE-STT` | P2 | Open decision, not implemented | Optional STT preserves transcript provenance; off/error path preserves typed input. | `SC-STT-TOGGLE`, `SC-VOICE-LANG` | `no_test_coverage` | — | — |
| `CAP-PORTABLE-SAVE` | P1 | CLI foundation; workflow open | Player workflow exports ancestry/pending records, validates hashes, reports missing deps and atomically recovers interruption. | `SC-PORTABLE-CLEAN`, `SC-PORTABLE-CORRUPT`, `SC-PORTABLE-INTERRUPT` | `live_decision_exercised` | `portable_memory_missing_dependency_stays_missing` | `gap_portable_import_waits_for_receipt`, `gap_portable_missing_ancestor_stays_missing`, `gap_portable_hash_mismatch_rejects_claim`, `gap_portable_interrupted_publish` |
| `CAP-ACTION-MOVE` | P1 | Partial wait/move/follow; breadth planned | Follow/lead/escort/wait/regroup/travel/home/seat/leave early/meeting bind target, consent, schedule and observed route. | `SC-ACTION-MOVE`, `SC-ACTION-CANCEL` | `live_decision_exercised` | `ordered_wait_follow_move`, `explicit_wait_duration`, `follow_explicit_duration`, `cancel_active_commitment`, `combat_actor_died_cancel` | — |
| `CAP-ACTION-GOODS` | P0 | Purchase/transfer subset partial | Buy/sell/give/receive/exchange/carry/deliver/pay/equip/consume/use validate native UID/count/stock/funds/recipient; loans excluded. | `SC-ACTION-GOODS`, `SC-ACTION-ROLLBACK` | `live_decision_exercised` | `gift_stack_exact`, `dependent_handover_after_purchase`, `budget_owned_funds` | — |
| `CAP-ACTION-PEOPLE` | P1 | Planned | Introduce/relay/ask/negotiate/invite/socialize/warn/summon/report resolve participant/consent/source and record actual event. | `SC-ACTION-PEOPLE`, `SC-NPC-NPC-EVENT` | `live_decision_exercised` | `npc_to_npc.rumor_keeps_source_and_uncertainty` | — |
| `CAP-ACTION-STEALTH` | P0 | Most adapters absent | Keys/doors/lockpick/bash/climb/scout/watch/distract/theft/break-in require registered native adapter, access, skill, crime and quest checks. | `SC-ACTION-STEALTH-GATE`, `SC-QUEST-PRESERVE` | `no_test_coverage` | — | — |
| `CAP-ACTION-EXPLORE` | P1 | Planned | Guide/search/investigate/retrieve/watch/meet/split/reunite bind known reachable target; no fabricated discovery. | `SC-ACTION-EXPLORE`, `SC-KNOW-STALE` | `live_decision_exercised` | `knowledge.unseen_container_change` | `gap_map_ambiguous_named_place`, `gap_weather_and_map_route_unknown` |
| `CAP-ACTION-COMBAT` | P0 | Future; combat binding absent | Defend/attack/cover/heal/buff/share/retreat/flee/refuse require native stats, identified target/resources/safety/quest authority and observed result. | `SC-ACTION-COMBAT-GATE`, `SC-COMBAT-PERSIST` | `live_decision_exercised` | `combat_confirmed_hostile`, `combat_friendly_fire_block`, `combat_player_neutral`, `combat_unknown_target_no_attack` | — |
| `CAP-ACTION-NEGOTIATE` | P0 | Partial offers/acceptance/cancel | Proposal/assent/refusal/alternative/change/cancel/failure bind current offer revision; quoted/conditional/history text starts nothing. | `SC-NEGOTIATE`, `SC-OFFER-STALE` | `live_decision_exercised` | `relationship.familiarity_stranger_invitation`, `relationship.friendship_not_guaranteed_acceptance`, `threat.refusal_and_protected_person`, `effect.pending_offer_cancelled_before_execution`, `scene_change_invalidates_stale_reply_binding` | — |
| `CAP-DUNGEON-INTERACT` | P0 | Planned 20 Oct | Resolve reachable switch/lever/button; native linked effect observed; repeat/reload cannot toggle twice. | `SC-DUNGEON-SWITCH`, `SC-ACTION-CANCEL` | `live_decision_exercised` | `cancel_active_commitment`, `combat_actor_died_cancel` | — |
| `CAP-ROUTINE-REPLAN` | P1 | Planned | Explicit consent may revise schedule through typed commitment; quoted/hypothetical/negated text cannot; physical departure/effect once. | `SC-ROUTINE-REPLAN`, `SC-SNAPSHOT-STALE` | `live_decision_exercised` | `routine.remaining_day_protect_appointment_and_home`, `replan_protect_appointment_sleep`, `scene_change_invalidates_stale_reply_binding` | — |
| `CAP-PROFILE-PROGRESSION` | P1 | Foundation scoped; native binding disabled | Seeded versioned profile and sparse XP; only verified native use awards bounded XP; serializer/branch/retry exact. | `SC-ACTOR-PROFILE`, `SC-SKILL-PROGRESSION` | `no_test_coverage` | — | — |
| `CAP-MEMORY-LISTENER` | P1 | Planned | Same-room listener archives only supported perceived dialogue/event, never hidden script/private conversation. | `SC-MEM-LISTENER`, `SC-MEM-ATTRIBUTION` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory`, `memory.branch_context.recall_only_visible_ancestry`, `npc_to_npc.rumor_keeps_source_and_uncertainty` | — |
| `CAP-MODEL-PROMOTION` | P0 | Diagnostics exist; promotion gate unmet | Frozen held-out >=200 (>=100 controls, >=100 positives), >=5 actor snapshots, >=3 contexts, both option orders; zero critical false actions, >=90% exact positive parse per order, >=95% order agreement; separate transport denominator. | `SC-PROMOTION-HELDOUT` | `no_test_coverage` | — | — |
| `CAP-IMM-21` | P1 | Exploratory decision-test candidate; not accepted or implemented. | Keep claims/actions within supplied skill-aware evidence; distinguish sound events from source identity; do not treat player screenshots as NPC perception; avoid identity claims or sleep interruption without evidence; communicate uncertainty fairly. | — | `live_decision_exercised` | — | `gap21_sneak_skill_without_check`, `gap21_wall_blocks_sight`, `gap21_sound_has_no_identity`, `gap21_sleeping_hearing_no_wake_receipt`, `gap21_wake_without_identification`, `gap21_player_screenshot_is_not_npc_view`, `gap21_repeated_dialogue_is_not_repeated_check`, `gap21_fair_suspicion_feedback` |

## Requirement crosswalk

Each original requirement row appears once. Direct base case IDs come from the source audit mapping; the extension maps only at the capability level explicitly declared in its fixture, so it is not spread across every requirement of a related capability.

| Requirement | Status | Direct base case IDs | Source mapping |
|---|---|---|---|
| `SC-ACTION-ADAPTER-GATE` | `live_decision_exercised` | `access_lock_quest_capabilities`, `theft_service_quest_gate` | `partial_choice_fixture` |
| `SC-ACTION-CANCEL` | `live_decision_exercised` | `cancel_active_commitment`, `combat_actor_died_cancel` | `partial_choice_fixture` |
| `SC-ACTION-COMBAT-GATE` | `live_decision_exercised` | `combat_confirmed_hostile`, `combat_friendly_fire_block`, `combat_player_neutral`, `combat_unknown_target_no_attack` | `partial_choice_fixture` |
| `SC-ACTION-EXPLORE` | `no_test_coverage` | — | `uncovered` |
| `SC-ACTION-GOODS` | `live_decision_exercised` | `gift_stack_exact`, `dependent_handover_after_purchase`, `budget_owned_funds` | `partial_choice_fixture` |
| `SC-ACTION-MOVE` | `live_decision_exercised` | `ordered_wait_follow_move`, `explicit_wait_duration`, `follow_explicit_duration` | `partial_choice_fixture` |
| `SC-ACTION-PEOPLE` | `live_decision_exercised` | `npc_to_npc.rumor_keeps_source_and_uncertainty` | `partial_choice_fixture` |
| `SC-ACTION-ROLLBACK` | `no_test_coverage` | — | `uncovered` |
| `SC-ACTION-STEALTH-GATE` | `no_test_coverage` | — | `uncovered` |
| `SC-ACTOR-PROFILE` | `no_test_coverage` | — | `uncovered` |
| `SC-ACTOR-QUEST` | `no_test_coverage` | — | `uncovered` |
| `SC-ACTOR-ROLE` | `live_decision_exercised` | `actor.static_jester_role_and_capability`, `actors.unknown_role_stays_unknown` | `partial_choice_fixture` |
| `SC-ACTOR-STATIC` | `live_decision_exercised` | `actor.static_jester_role_and_capability` | `partial_choice_fixture` |
| `SC-AGENDA-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-AGENDA-URGENCY` | `no_test_coverage` | — | `uncovered` |
| `SC-ANIM-COVERAGE` | `no_test_coverage` | — | `uncovered` |
| `SC-APPT-BOOK` | `no_test_coverage` | — | `uncovered` |
| `SC-APPT-CONFLICT` | `live_decision_exercised` | `fixed_appointment_priority`, `replan_protect_appointment_sleep`, `routine.remaining_day_protect_appointment_and_home` | `partial_choice_fixture` |
| `SC-APPT-NOSHOW` | `live_decision_exercised` | `meeting.no_show_only_with_witnessed_absence` | `partial_choice_fixture` |
| `SC-BASE-REPLAY` | `no_test_coverage` | — | `uncovered` |
| `SC-CENSUS` | `no_test_coverage` | — | `uncovered` |
| `SC-CLOCK-MIGRATE` | `no_test_coverage` | — | `uncovered` |
| `SC-COMBAT-PERSIST` | `no_test_coverage` | — | `uncovered` |
| `SC-DUNGEON-SWITCH` | `no_test_coverage` | — | `uncovered` |
| `SC-FOLLOW-DEPART` | `no_test_coverage` | — | `uncovered` |
| `SC-FOLLOW-LATE` | `no_test_coverage` | — | `uncovered` |
| `SC-FRIEND-ACCRUAL` | `live_decision_exercised` | `friendship.wait_replenishes_allowance_not_score` | `partial_choice_fixture` |
| `SC-FRIEND-BOUNDARY` | `live_decision_exercised` | `friendship.reopen_does_not_farm_growth`, `friendship.wait_replenishes_allowance_not_score` | `partial_choice_fixture` |
| `SC-FRIEND-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-FULL-TURN` | `no_test_coverage` | — | `uncovered` |
| `SC-GOAL-INTERRUPT` | `live_decision_exercised` | `goals.interruption_replan_without_duplicate_effect`, `replan_protect_appointment_sleep` | `partial_choice_fixture` |
| `SC-GOAL-MULTIDAY` | `no_test_coverage` | — | `uncovered` |
| `SC-IDENTITY-OWNERSHIP` | `no_test_coverage` | — | `uncovered` |
| `SC-INIT-COOLDOWN` | `live_decision_exercised` | `busy.dismissal_cooldown_is_not_permanent_hostility` | `partial_choice_fixture` |
| `SC-INIT-PROPOSE` | `no_test_coverage` | — | `uncovered` |
| `SC-INN-ORIGIN` | `no_test_coverage` | — | `uncovered` |
| `SC-INN-SHAREDBED` | `no_test_coverage` | — | `uncovered` |
| `SC-INN-STAY` | `no_test_coverage` | — | `uncovered` |
| `SC-INVENTORY-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-INVENTORY-UID` | `live_decision_exercised` | `gift_stack_exact`, `gift_quantity_not_loan`, `dependent_handover_after_purchase` | `partial_choice_fixture` |
| `SC-INVITE-ACCEPT` | `live_decision_exercised` | `relationship.familiarity_stranger_invitation`, `relationship.friendship_not_guaranteed_acceptance` | `partial_choice_fixture` |
| `SC-INVITE-CANCEL` | `live_decision_exercised` | `effect.pending_offer_cancelled_before_execution`, `cancel_active_commitment` | `partial_choice_fixture` |
| `SC-KEEPER-SERVICE` | `no_test_coverage` | — | `uncovered` |
| `SC-KNOW-ACCESS` | `live_decision_exercised` | `knowledge.unseen_container_change` | `partial_choice_fixture` |
| `SC-KNOW-CLAIM` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory`, `npc_to_npc.rumor_keeps_source_and_uncertainty` | `partial_choice_fixture` |
| `SC-KNOW-STALE` | `live_decision_exercised` | `knowledge.unseen_container_change` | `partial_choice_fixture` |
| `SC-LIFECYCLE-BRANCH` | `no_test_coverage` | — | `uncovered` |
| `SC-LIFECYCLE-FAIL` | `no_test_coverage` | — | `uncovered` |
| `SC-LIFECYCLE-HAPPY` | `no_test_coverage` | — | `uncovered` |
| `SC-MARKET-COMMUTE` | `no_test_coverage` | — | `uncovered` |
| `SC-MARKET-DEMAND` | `no_test_coverage` | — | `uncovered` |
| `SC-MARKET-HOUSING` | `no_test_coverage` | — | `uncovered` |
| `SC-MARKET-POI` | `live_decision_exercised` | `market.browse_not_purchase`, `market.stage_performance_real_interest_and_availability` | `partial_choice_fixture` |
| `SC-MARKET-RELOAD` | `live_decision_exercised` | `routine.time_skip_reconstruct_current_activity` | `partial_choice_fixture` |
| `SC-MEM-ATTRIBUTION` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory`, `memory.branch_context.recall_only_visible_ancestry`, `npc_to_npc.rumor_keeps_source_and_uncertainty` | `partial_choice_fixture` |
| `SC-MEM-CONTRADICTION` | `live_decision_exercised` | `knowledge.player_claim_not_npc_memory` | `partial_choice_fixture` |
| `SC-MEM-LISTENER` | `no_test_coverage` | — | `uncovered` |
| `SC-MEM-PARAPHRASE` | `live_decision_exercised` | `memory.long_term_paraphrase_attributed_promise` | `partial_choice_fixture` |
| `SC-MEM-RESTORE` | `no_test_coverage` | — | `uncovered` |
| `SC-MEM-SCALE` | `live_decision_exercised` | `memory.large_history_relevant_recall_not_bulk_dump` | `partial_choice_fixture` |
| `SC-MIGRATION` | `no_test_coverage` | — | `uncovered` |
| `SC-MOOD-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-MOOD-TURNS` | `no_test_coverage` | — | `uncovered` |
| `SC-MOOD-WEATHER` | `live_decision_exercised` | `mood.weather_only_npc_outdoors`, `mood.indoor_excludes_player_weather` | `partial_choice_fixture` |
| `SC-MOVE-DOOR` | `live_decision_exercised` | `ordered_wait_follow_move`, `follow_explicit_duration`, `move_to_ambiguous_destination` | `partial_choice_fixture` |
| `SC-NEED-CHOICE` | `live_decision_exercised` | `seating.outdoor_good_weather_preference_capacity`, `market.stage_performance_real_interest_and_availability` | `partial_choice_fixture` |
| `SC-NEED-CONTROL` | `no_test_coverage` | — | `uncovered` |
| `SC-NEGOTIATE` | `live_decision_exercised` | `relationship.familiarity_stranger_invitation`, `relationship.friendship_not_guaranteed_acceptance`, `threat.refusal_and_protected_person` | `partial_choice_fixture` |
| `SC-NPC-NPC-EVENT` | `live_decision_exercised` | `npc_to_npc.rumor_keeps_source_and_uncertainty` | `partial_choice_fixture` |
| `SC-NPC-NPC-NOPOSE` | `no_test_coverage` | — | `uncovered` |
| `SC-OFFER-STALE` | `live_decision_exercised` | `effect.pending_offer_cancelled_before_execution`, `scene_change_invalidates_stale_reply_binding` | `partial_choice_fixture` |
| `SC-PACK-BOOTSTRAP` | `no_test_coverage` | — | `uncovered` |
| `SC-PACK-DOCS` | `no_test_coverage` | — | `uncovered` |
| `SC-PACK-MODULES` | `no_test_coverage` | — | `uncovered` |
| `SC-PERF-HARDWARE` | `no_test_coverage` | — | `uncovered` |
| `SC-PERSONALITY-CONSISTENCY` | `live_decision_exercised` | `personality.stable_preference_disclosure` | `partial_choice_fixture` |
| `SC-PERSONALITY-FACTS` | `no_test_coverage` | — | `uncovered` |
| `SC-PORTABLE-CLEAN` | `no_test_coverage` | — | `uncovered` |
| `SC-PORTABLE-CORRUPT` | `live_decision_exercised` | `portable_memory_missing_dependency_stays_missing` | `partial_choice_fixture` |
| `SC-PORTABLE-INTERRUPT` | `no_test_coverage` | — | `uncovered` |
| `SC-PROMOTION-HELDOUT` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-BED` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-DISCLOSURE` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-MATRIX` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-PACK` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-PERSONA` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-PRESERVE` | `no_test_coverage` | — | `uncovered` |
| `SC-QUEST-SPOILER` | `no_test_coverage` | — | `uncovered` |
| `SC-READ-BUDGET` | `live_decision_exercised` | `tools.exhausted_budget_truthful_result` | `partial_choice_fixture` |
| `SC-READ-INVALID` | `no_test_coverage` | — | `uncovered` |
| `SC-READ-PAGE` | `no_test_coverage` | — | `uncovered` |
| `SC-READ-VALID` | `live_decision_exercised` | `knowledge.unseen_container_change`, `budget_owned_funds` | `partial_choice_fixture` |
| `SC-RECRUIT-GATE` | `no_test_coverage` | — | `uncovered` |
| `SC-RECRUIT-PAYMENT` | `no_test_coverage` | — | `uncovered` |
| `SC-REGRESSION-E01` | `no_test_coverage` | — | `uncovered` |
| `SC-REGRESSION-E02` | `no_test_coverage` | — | `uncovered` |
| `SC-REL-BOUNDS` | `live_decision_exercised` | `friendship.reopen_does_not_farm_growth`, `friendship.wait_replenishes_allowance_not_score` | `partial_choice_fixture` |
| `SC-REL-DISTINCT` | `live_decision_exercised` | `threat.refusal_and_protected_person`, `busy.dismissal_cooldown_is_not_permanent_hostility` | `partial_choice_fixture` |
| `SC-REL-SAVE` | `no_test_coverage` | — | `uncovered` |
| `SC-ROUTINE-REPLAN` | `live_decision_exercised` | `routine.remaining_day_protect_appointment_and_home`, `replan_protect_appointment_sleep` | `partial_choice_fixture` |
| `SC-SEAT-ELEVATION` | `live_decision_exercised` | `seating.elevated_terrace_requires_real_route` | `partial_choice_fixture` |
| `SC-SEAT-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-SEAT-WEATHER` | `live_decision_exercised` | `seating.outdoor_good_weather_preference_capacity`, `seating.bad_weather_blocks_outdoor` | `partial_choice_fixture` |
| `SC-SHARED-COMPLETE` | `live_decision_exercised` | `shared_activity.completed_receipt_supports_greeting` | `partial_choice_fixture` |
| `SC-SHARED-CONTINUITY` | `live_decision_exercised` | `meeting.active_shared_outing_continuation` | `partial_choice_fixture` |
| `SC-SHARED-FAIL` | `live_decision_exercised` | `shared_activity.failed_reply_not_shared_experience` | `partial_choice_fixture` |
| `SC-SHARED-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-SHARED-SEPARATION` | `no_test_coverage` | — | `uncovered` |
| `SC-SIM-BUDGET` | `no_test_coverage` | — | `uncovered` |
| `SC-SIM-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-SIM-TIMESKIP` | `live_decision_exercised` | `routine.time_skip_reconstruct_current_activity` | `partial_choice_fixture` |
| `SC-SKILL-PROGRESSION` | `no_test_coverage` | — | `uncovered` |
| `SC-SNAPSHOT-RELOAD` | `no_test_coverage` | — | `uncovered` |
| `SC-SNAPSHOT-STALE` | `live_decision_exercised` | `scene_change_invalidates_stale_reply_binding` | `partial_choice_fixture` |
| `SC-SNAPSHOT-SWITCH` | `no_test_coverage` | — | `uncovered` |
| `SC-STAFF-ROLE` | `no_test_coverage` | — | `uncovered` |
| `SC-STT-TOGGLE` | `no_test_coverage` | — | `uncovered` |
| `SC-TENANT-ACCESS` | `no_test_coverage` | — | `uncovered` |
| `SC-TENANT-BED` | `no_test_coverage` | — | `uncovered` |
| `SC-TRUTH-FAIL` | `live_decision_exercised` | `shared_activity.failed_reply_not_shared_experience`, `tools.exhausted_budget_truthful_result` | `partial_choice_fixture` |
| `SC-TRUTH-IDEMPOTENT` | `live_decision_exercised` | `goals.interruption_replan_without_duplicate_effect`, `task_persistence_reload` | `partial_choice_fixture` |
| `SC-TRUTH-PHASES` | `live_decision_exercised` | `market.browse_not_purchase`, `effect.pending_offer_cancelled_before_execution`, `dependent_handover_after_purchase` | `partial_choice_fixture` |
| `SC-VOICE-IDENTITY` | `no_test_coverage` | — | `uncovered` |
| `SC-VOICE-LANG` | `no_test_coverage` | — | `uncovered` |
| `SC-VOICE-TOGGLE` | `no_test_coverage` | — | `uncovered` |

## Extension mappings

| Case | Declared capability associations |
|---|---|
| `gap21_sneak_skill_without_check` | `CAP-IMM-21`, `CAP-IMM-04` |
| `gap21_wall_blocks_sight` | `CAP-IMM-21`, `CAP-IMM-04` |
| `gap21_sound_has_no_identity` | `CAP-IMM-21`, `CAP-IMM-04` |
| `gap21_sleeping_hearing_no_wake_receipt` | `CAP-IMM-21`, `CAP-IMM-16` |
| `gap21_wake_without_identification` | `CAP-IMM-21`, `CAP-IMM-04` |
| `gap21_player_screenshot_is_not_npc_view` | `CAP-IMM-21`, `CAP-IMM-04` |
| `gap21_repeated_dialogue_is_not_repeated_check` | `CAP-IMM-21`, `CAP-IMM-02` |
| `gap21_fair_suspicion_feedback` | `CAP-IMM-21`, `CAP-IMM-18` |
| `gap_map_ambiguous_named_place` | `CAP-IMM-04`, `CAP-ACTION-EXPLORE` |
| `gap_map_stale_memory_needs_refresh` | `CAP-IMM-04`, `CAP-IMM-03` |
| `gap_weather_indoor_uses_npc_location` | `CAP-IMM-04`, `CAP-IMM-09` |
| `gap_weather_stale_outdoor_observation` | `CAP-IMM-04`, `CAP-OUTDOOR-SEATING` |
| `gap_portable_import_waits_for_receipt` | `CAP-IMM-20`, `CAP-PORTABLE-SAVE`, `CAP-IMM-18` |
| `gap_portable_missing_ancestor_stays_missing` | `CAP-IMM-20`, `CAP-PORTABLE-SAVE`, `CAP-IMM-05` |
| `gap_portable_hash_mismatch_rejects_claim` | `CAP-IMM-20`, `CAP-PORTABLE-SAVE` |
| `gap_portable_interrupted_publish` | `CAP-IMM-20`, `CAP-PORTABLE-SAVE` |
| `gap_portable_branch_ancestry_isolation` | `CAP-IMM-20`, `CAP-IMM-04`, `CAP-IMM-05` |
| `gap_elapsed_time_no_offscreen_purchase_receipt` | `CAP-IMM-16`, `CAP-IMM-12`, `CAP-IMM-18` |
| `gap_elapsed_time_reconstruct_current_schedule` | `CAP-IMM-16`, `CAP-IMM-12` |
| `gap_elapsed_event_budget_reports_partial_work` | `CAP-IMM-16`, `CAP-IMM-18` |
| `gap_elapsed_scene_reload_invalidates_old_result` | `CAP-IMM-16`, `CAP-IMM-02`, `CAP-IMM-18` |
| `gap_weather_and_map_route_unknown` | `CAP-IMM-04`, `CAP-ACTION-EXPLORE`, `CAP-H03` |

## Core/native evidence and limits

`dotnet run --project .\CoreBridge.csproj -- --self-test` passed in `work/architecture-probe-20261020/CoreBridge/` (six JSON cases plus direct Core boundary checks). It calls real `LivingCities.Core` validation and acceptance using synthetic residents. It creates no action job or item/gold effect. The separate review is `work/architecture-probe-20261020/CoreBridge/contracts-review.md`. Core acceptance does not establish live target resolution, reachability, or physical completion. The Unity outer binder’s 10–900 second `Wait`/`Follow` bounds are outside Core validation.

No simulation or native-only acceptance run is recorded. Native evidence still needs owned in-game checks for actor/quest guards, pathing/access, inventory/gold transactions and rollback, cancellation/reload, truthful UI receipts, serialization, collision/animation, and performance.

## Tracked harness and audit references

`tools/NpcArchitectureEval/README.md` documents the runner. Base fixtures: `tools/NpcArchitectureEval/fixtures/social-world.json`, `action-combat.json`; separate extension: `tools/NpcArchitectureEval/fixtures/coverage-gaps.json`. Deterministic runner tests: `tools/NpcArchitectureEval/tests/test_adapters.py`, `test_model_client.py`, `test_scenario_validation.py`, `test_visual_evaluation.py`. Run results and detailed adjudication remain in `work/npc-capability-eval-20261020/`; the pre-run fixture mapping and seven expectation findings are preserved in `coverage-audit.json` and summarized in the machine-readable crosswalk.

Machine-readable crosswalk: [`npc-capability-test-coverage.json`](npc-capability-test-coverage.json).
