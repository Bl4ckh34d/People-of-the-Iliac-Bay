# Roadmap — TL;DR

Consolidated 17 October 2026. **Planning only. No new gameplay implementation is authorized by this document.** The user subsequently authorized the **2× game-clock fix and rebuild** as a separate bounded task. Other implementation or bug-fix slices will be chosen after review.

## Direction

Make one persistent person exist across home, work, streets, travel, conversation and combat. Preserve native quest/service ownership. Complete reliable physical movement and identity before adding more behaviors that depend on them.

The [current inventory](NPC-INTEGRATION-AUDIT.md) describes implemented coverage. The [future design and backlog](superpowers/specs/2026-10-17-npc-lifecycle-and-quest-dialogue-design.md) records the latest requirements. The [delivery plan](superpowers/plans/2026-10-17-consolidated-delivery-plan.md) gives ordered, testable work packages. Old milestone paragraphs remain historical evidence, not a competing current plan.

## Important answers

- Most side quests retain native quest control. They are **not automatically broken** because only one delivery quest has household binding. General quest integration remains unverified, and unrelated movement/combat/guard changes still need compatibility tests.
- Main-story people stay under native placement and quest control for now. Their authored personality, public story and disclosure permissions must override random personality generation. Mood can vary within those boundaries.
- Same-building bed movement can preserve identity, but staying in one building alone does not guarantee quest safety. Original native binding, activation, visibility, timing, service access and return position must also survive.
- Two authored NPCs in a one-bed inn room do not share that bed today. Excess occupants are logged as lacking a bed. The requested shared-bed exception is future work.
- Outdoor stall owners do not currently receive homes before generated families. This becomes a required allocation order.
- GitHub has source/build notes and a local-model launcher, not a complete end-user installer or a published release bundle. See [installation status and manual setup](INSTALLATION.md).

## Proposed sequence

| Order | Work package | Exit condition |
| --- | --- | --- |
| 0 | Stabilize observed movement, itinerary/dialogue agreement, door transitions and frame stalls | A tracked person reaches the stated destination, enters/exits physically, and remains the same person after reload; repeatable frame-time evidence |
| 1 | Incremental actor census and unified ownership | Every surveyed actor has stable provenance, identity, role, appearance, owner and a visible adoption/exclusion reason; no duplicate representation |
| 2 | Housing and bed allocation before generated population | Saved people and authored tenants keep capacity; local workers obtain homes before remaining families are generated; migration never evicts silently |
| 3 | Native locals and visitors become mobile by role | Stall workers commute; shopkeepers lock/unlock and commute when needed; shop tenants use keys; inn lodgers have origins, visits and checkout/return journeys |
| 4 | Bedside staff behavior and appearance coverage | Ordinary institutional staff retain services and use real reserved beds; movement/sleep art matches each actual vanilla or replacement appearance |
| 5 | Story-aware dialogue and persistent memory | Main NPCs keep authored character; stage-filtered facts do not leak secrets; encounters and shared activities persist with provenance and game dates |
| 6 | Broader vanilla quest adapters, then Quest Pack 1 | Per-quest/per-role capability records and native end-to-end tests; unsupported stages remain native rather than being moved speculatively |
| 7 | Modular packaging and public installation | No-LLM installation works without model/CUDA downloads; optional setup is reproducible on a clean machine with measured hardware requirements |
| Later | Invitations, durable human combat state, companions and action framework | Independent milestones after movement, ownership, memory and inventory foundations; explicitly shelved now |

Some work can be scoped independently: installation documentation and spoiler-protection design do not require full mobility. The order is a dependency guide, not permission to start a large combined implementation. Quest Pack 1 mobility implementation remains deferred despite its installation and source audit.

## Latest behavioral decisions

- Reserve housing for authored local stall owners, staff needing off-site homes and shop tenants before filling remaining homes. Keep traveler hometown reservations intact.
- Stall workers have slightly varied opening/closing commutes. Markets may stay open longer on weekends while demand remains, within finite closing and safety limits. Local residents can browse multiple stalls as well as indoor shops.
- Ordinary authored inn-bedroom occupants become visiting travelers with stable origins and a reason for visiting. They leave for daytime activities, return to lodging, and eventually check out and travel home. Quest actors and staff are classified separately.
- Allow two authored inn-room occupants to share one bed through an explicit capacity/claim exception and a proposed shared blanket with appearance-specific heads. Do not double all household capacity or infer a romance from co-occupancy.
- Ordinary guild/temple/castle staff keep their workplace duties; off-site daily routines are not required now. Reserve reachable beds if present and preserve each native service role.
- Upstairs shop-bedroom occupants are residential tenants. Their beds are unavailable to the shopkeeper. Tenants carry valid access keys and may return after closing; the same persistent key can be looted after death.
- Main and side quest actors keep original identity and script control. Only reviewed capabilities may be enabled per role/stage. Recruitment excludes main-quest and side-quest actors.

## Existing obligations retained

These additions do not replace earlier work: finite guards, crime/fleeing/reporting and pursuit, guard quarters, household recovery/house sales, corpse loot/property keys, human stats, skill-sensitive awareness, adult itineraries and sparse inter-settlement travel, spacing/facing/idle/running art, commerce and service disposition, rank-sensitive guild expulsion, noble insult consequences, optional quest tone, modular settings, context/memory/listener awareness, performance and versioned saves. See the design's backlog crosswalk and [milestones](MILESTONES.md).

Current blockers and uncertain evidence remain visible: physical inn movement is not broadly accepted; the prior current/next destination and saved-door fixes still need targeted native checks; game time remains 12× and a safe 2× migration is pending. No percentage-complete estimate should hide these distinctions.

## Next decision

After the separately requested clock fix, recommended next selectable slice: finish the existing destination/door/movement regression, then implement the read-only actor census. Alternative: prioritize story-safe context if plot leakage is the immediate concern. Choose one bounded slice before code changes; companions and exhaustive Quest Pack adaptation are not that slice.
