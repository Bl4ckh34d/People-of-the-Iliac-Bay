# Outdoor corpse discovery and investigation

## Current runtime evidence

The development bundle now connects outdoor discovery and absence cleanup, finite responders and patrol views. OutdoorSceneProbe passes actual-player discovery, cleanup, harmless return/examination, native serialization and patrol state. CrimeSceneProbe separately passes the revised indoor warning, Trespassing surrender and real save/reload. Evidence: work/OutdoorSceneProbe-pass.log and work/CrimeSceneWarning-review-pass.log. Current core suite: 176 tests. Actual corpse inventory/loot is still absent. Citizens can notify nearby guards but do not yet make a dedicated trip to report an outdoor corpse. Full end-to-end outdoor patrol/save/reload and all witness combinations remain open.

## Latest approved policy

The user approved a revised policy after the initial amendments below: mere presence at discovery is not evidence of murder either. Examining a body is harmless; only witnessed taking of belongings triggers Theft. Later arrival remains innocent. Known murderers retain their existing witnessed-murder accusation. Unidentified visitors to an indoor scene receive ten seconds of unpaused real time to leave; refusal causes Trespassing and native surrender/arrest. This supersedes both the first-discovery-presence accusation and the earlier immediate indoor Murder accusation. Searching outdoor guards do not converse. Placeholder corpses currently have no inventory, so there is no actual item-taking action yet; do not charge for examining them.

Historical initial amendment (superseded on accusation and examination by the approved policy above): outdoor cleanup requires discovery followed by departure. The initial request treated presence at discovery as Murder and any corpse interaction as Theft; neither of those accusation rules remains approved. One-hour investigation and unseen-body persistence remain required.

Implementation sequence:

1. Add a persistent, validated first-discovery record to outdoor corpses, with observer identity and time. Explicit observation of player absence authorizes cleanup only after discovery. Keep existing cleared legacy bodies cleared, and never invent witnesses on restore. Cover unseen bodies, presence, departure, return, time rollback, observer validity and save/load with core tests.
2. Connect nearby living citizens and finite roster guards to actual line-of-sight detection. Use a documented local scene radius to determine player presence; entering an interior or leaving the settlement counts as leaving the outdoor area. Preserve discovered incidents after body cleanup so cleanup does not cancel the guard response.
3. Dispatch only existing available guards over surveyed collision-safe routes. Preserve responder identity, first arrival and a one-hour search deadline. Charge Murder only when the saved discovery/witness evidence identifies the player, then offer native surrender on physical contact. Later passersby are not suspects. Patrol reachable local points, block conversation during the search, then return; repeat observations and reloads must not renew the hour. Witness reporting must respect physical reporting rather than instantly informing the whole roster. Add witnessed-corpse-interaction Theft separately when corpse activation is connected; retain an existing more serious known crime.
4. Verify native serialization and actual-player discovery, departure cleanup, finite arrival, on-site surrender, absent-player patrol/expiry and reload. Keep automated tests mouse-unlocked. Update installed version and docs only after the runtime checks.

Household routine locks remain the next separate feature after this amendment. Do not describe this plan or core-only state as completed outdoor gameplay.

## Core checkpoint

The core now records the first known living observer, discovery time and whether the player was present at that first observation. Later observations cannot rewrite that evidence. Unseen outdoor corpses no longer use elapsed-day cleanup; explicit player absence after discovery clears them. Already-cleared legacy bodies remain cleared. Indoor cleanup remains unchanged. Restore rejects unknown observers, invalid chronology and incompatible indoor discovery records.

Finite rosters can retain a collision-surveyed approach and closed local patrol loop, repeat that loop for exactly one game hour after each responder arrives, then return to their original posts. Repeated reports and restore preserve deadlines and arrival evidence. The caller supplies surveyed geometry; core tests do not prove runtime collision safety. Native FullSerializer checks verify discovery, cleanup, position and patrol timing. Core suite: 170 tests.

CleanupVersion 1 marks newly confirmed bodies. Unseen version-1 exterior cleanup is rejected on restore. Version-0 bodies already cleared by 0.3.12 remain cleared; uncleared legacy bodies upgrade to the new policy. This preserves historical saves without permitting the old one-day rule for newly created outdoor bodies. Restore also checks approach endpoints against search destinations and rejects null guard observers through validation.

Migration validation is not tamper-proof provenance: deliberately relabeling an edited record as version 0 makes it a legacy record. This is the explicit compatibility branch, not a claim that arbitrary save edits can be detected. The current runtime always creates version-1 records.

Historical core-only checkpoint: runtime integration was pending at that stage. Use the current runtime evidence above for installed behavior and remaining limits.
