# Crime reports, physical guard response and crime scenes

User refinements, 14 October 2026. These distinguish eyewitness alarm, body discovery and a known murderer.

## Shop theft and delayed guard arrival

When detected shop theft calls guards, dispatch available members of the existing finite roster from nearby exterior posts, patrols or actual guard quarters, including reachable city-wall headquarters entrances. Guards must travel from their current locations to the shop and enter through its door as they arrive. Do not spawn a squad inside, teleport guards to the entrance, or treat every wall door as a guard source. Headquarters can supply only guards already assigned there.

Arrival time follows the actual reachable route, guard speed and door transitions. A distant garrison means a longer wait; no available guard means no invented immediate response. The player can leave during this delay and attempt escape. Keep the response advancing while the player remains inside, leaves, or reloads; crossing the shop threshold must neither summon responders instantly nor reset their journey. Once outside, pursuit uses actual sightings, last-known position and the planned search/escape rules rather than continuous knowledge of the player's position.

The shopkeeper can attack or remain passive after reporting. Select a stable response from capability, disposition and courage, keeping reporting separate from combat. Preserve the shopkeeper's identity and use the required appearance-specific combat animation when active; passivity does not cancel the report. This choice is separate from the indoor-murder fight-or-flight rules below.

Acceptance cases: nearby patrol versus distant post with measurably different arrival times; real headquarters exit; staggered responders entering as they arrive; player leaves before the first arrival and escapes unseen; player remains inside while exterior travel progresses; shopkeeper attacks versus stays passive; no available guards; duplicate theft reports do not duplicate guards; save/load preserves responder identity, location and journey progress. This refinement is planned for M4; vanilla theft guard spawning remains in the current preview.

## Indoor murder and discovery

### Sleeping deaths (latest decision, 14 October 2026)

One person occupies each bed, and household capacity equals actual bed count with no independent five-person cap. This supersedes the earlier shared-bed kill-all decision. A lethal hit kills that bed's sole occupant. Old saves retain their identities, but excess legacy residents receive no shared-bed assignments. Fresh compliant test populations are separate from preserved old saves.

Slain-in-bed sprites are still missing. Add covered corpse poses with the same green blanket and white pillows, distinguishable from living sleepers and retaining appearance. Bodies must remain until the established discovery/cleanup rule removes them; save/reload must preserve that state. The current preview removes deceased actors immediately and does not yet implement corpse persistence or crime-scene cleanup.

If a house has one occupant and the player kills that person in one blow without another witness, do not send an immediate guard alarm. On the player's departure, schedule guards from the finite roster to travel to and enter the house. They discover the body and clear it upon arrival. This specific discovery cleanup supersedes the generic one-day corpse-cleanup delay for this case. No teleporting reinforcements and no attribution of an unwitnessed murder to the player merely because a corpse exists.

From the guards' arrival, hold the crime scene for one game hour. If the player returns during that interval, guards attempt to take the player into custody as for a crime. Track this explicitly as crime-scene suspicion/custody behavior, separately from a witnessed murderer identity. After one hour the occupation expires and guards resume normal duty; the house remains vacant until its independent recovery/sale deadline. Do not use an unobserved corpse cleanup event to invent a witness.

Where other occupants are present, survivors react immediately. Stationary indoor NPCs must become active actors rather than remaining inert flats: those capable of fighting can become hostile and attack, while others flee outside and inform guards. A successful fleeing witness raises the alarm; responding guards pursue the player as hostile. The final clarification permits fight-or-flight, superseding the preceding blanket instruction that all survivors flee. Preserve persistent identity, faction, relationships, service ownership and quest bindings when changing actor mode. Do not replace a quest NPC with an unrelated generic enemy.

Keep witness knowledge and readiness to fight separate. Household members absent elsewhere do not witness the killing automatically. Partner-killing hostility and bereavement remain applicable, with flight to obtain help a possible immediate response. A murder that is not silent/instant, outside witnesses, failed escape or guards already present can follow the broader perception system; this one-blow special case is not blanket impunity for every indoor attack.

Use separate persisted events for killing, witnessing, fleeing/alarm, player departure, guard dispatch/arrival, corpse removal, custody suspicion and one-hour occupation expiry. Timers use game time and survive save/load/absence. Dispatch only available guards; tiny hamlets with no garrison cannot conjure responders. Record a pending report/discovery and respect later external response capacity rather than violating finite rosters.

## Public incidents and bounded warnings

Nearby NPCs who perceive a public killing flee toward home or a reachable safe interior such as a guild, castle or shop. Refuge selection respects access, opening hours and actual routes. While fleeing, each direct witness alerts guards within a configurable local radius and warns at most the next two eligible NPCs encountered nearby. Those recipients may flee or fight according to their capabilities and disposition, but do not relay secondhand warnings further. This one-hop rule bounds propagation from each witness and prevents an unlimited town-wide chain reaction.

Give each incident a stable ID. Deduplicate reports and recipients, retain each witness's two-recipient budget across actor unloading/save-load, and expire local alarm opportunities. A recipient already aware of this incident is not a new warning recipient. Multiple direct witnesses can each warn two people; genuinely new witnessed violence can create a new incident. Guard alerts update the same finite response rather than spawning a fresh squad for every report. Exposure to the same corpse or repeated reporting must not reset budgets.

## Player-caused deaths and quest compatibility

The user explicitly superseded blanket quest-NPC invulnerability: deliberate player kills may permanently kill important NPCs and break their quests. Preserve the NPC's technical identity, quest bindings and save integrity; do not silently resurrect or substitute another actor to repair story consequences. Keep protection from random background events as the default, separately from player-caused death policy. The current foundation's `IsQuestProtected` death restriction remains implemented until a versioned policy migration separates these cases.

Acceptance cases: silent single-occupant kill while player stays; exit then travel delay; corpse removal only after arrival; return before/after one-hour expiry; multiple occupants fighting/fleeing; stationary-to-active identity continuity; absent household member; no available guards; save/load during each phase; deliberate player kill of a quest NPC; ambient-event protection; owned-property handling; two warnings per direct witness; no secondhand relay; duplicate reports; multiple witnesses; save/load of warning budgets. This behavior is planned; current vanilla combat still spawns guards and the temporary hook probe does not implement these crime rules.
