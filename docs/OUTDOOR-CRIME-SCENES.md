# Outdoor corpse discovery and investigation

User amendments, 14 October 2026: an outdoor corpse clears after the player leaves its area only if an NPC or guard has seen it. An unseen body does not clear merely because time elapsed. Player presence at first discovery is distinct from arriving later: the former triggers the requested murder response, but later arrival alone never creates a murder accusation. A previously witnessed murder still applies. If the player is absent, guards patrol the area for one game hour. Searching guards cannot be talked to. Interacting with the corpse while an NPC or guard sees the interaction triggers Theft, without inventing Murder. This supersedes the previous generic one-day outdoor cleanup rule; indoor rules remain separate.

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

Runtime detection, physical reporting, patrol views, evidence-based arrest, conversation refusal and witnessed corpse-interaction Theft are not connected yet. The installed 0.3.12 bundle remains unchanged; do not treat these core tests as evidence that the new outdoor behavior is available in the game.
