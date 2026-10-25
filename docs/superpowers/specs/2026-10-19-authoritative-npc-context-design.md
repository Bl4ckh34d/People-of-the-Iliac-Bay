# Authoritative NPC conversation context — IMM-02 design

Status: approved IMM-02 implementation design. IMM-01 baseline assessment is complete and retains 7B under the current accepted protocol. Root has released the source freeze for approved IMM-02 implementation and evidence-based repairs. IMM-01 itself remains incomplete because the full native action/assent probe failed; preserve that failure as a separate acceptance gate and do not weaken native requirements.

## Goal

Routing, willingness, social replies and read tools must use compatible facts about the same speaker, conversation and save branch. A small immutable snapshot represents verified state at a particular point in the turn. It does not make every fact visible to every prompt or make the NPC omniscient.

## Current integration points

- `mod/Scripts/LivingCitiesMod.ConversationKnowledge.cs` binds a native speaker, records encounters and routes text. Some callbacks already retain person/session identifiers.
- `mod/Scripts/LivingCitiesMod.Dialogue.cs` independently constructs identity, role, disposition, location, weather, life context, recall and clock before the final reply.
- `mod/Scripts/LivingCitiesMod.DialogueLife.cs` mixes verified state and presentation. It currently derives outdoors from the player and emits that as the NPC's physical state.
- `mod/Scripts/LivingCitiesMod.DialogueSemanticTasks.cs` performs further asynchronous interpretation and candidate selection.
- `mod/Scripts/LivingCitiesMod.ContextTools.cs` validates the current speaker and pages topic/inventory data, but is not yet connected to completion orchestration.
- `src/LivingCities.Core/ConversationLocationSnapshot.cs` captures the player's fixed HERE location for appointments. Preserve that contract; the player's HERE point is not a substitute for the NPC's position.

## Proposed boundary

Create a small Core snapshot contract and a runtime capture adapter. Core owns detached data, validation and consistent projections; Unity owns native bindings and reads actual game state. Keep existing planners, memory journals, actions and native service ownership as their own authorities.

The snapshot contains:

1. **Binding:** persistent person ID when established, otherwise an explicitly ephemeral session identity; actor category; conversation session; request revision; opaque save/restore epoch; scene identity/revision; capture game time.
2. **Verified speaker facts:** known name/race/role and the evidence or capability category supporting that role. Unknown fields stay unknown. A non-guard native mobile NPC must not automatically become a verified traveler; a static placement must not imply ownership or a work schedule.
3. **Observed current state:** actual NPC scene/position/activity where confirmed, with freshness. Keep player scene and HERE location separately named. An itinerary is a plan, never evidence that the actor reached it. Explicitly unknown current state remains representable.
4. **Personal state already supported:** stable personality seed/traits, current disposition/familiarity and derived mood inputs. This slice does not implement the later richer relationship or persistent-mood systems.
5. **Current commitments:** bounded action/task/shared-activity status and relevant appointment/routine references, with revisions. Do not copy route geometry, full ledgers or every future stop into the snapshot.
6. **Read-source bindings:** owner/revision handles for permitted knowledge, inventory and recall providers. Full catalogs, item payloads and entire conversation journals remain outside always-present model context.

## Turn lifecycle and freshness

- Capture on the main thread at each submitted player message, not only when chat opens. All stages retain the same binding and original player text.
- Pass compact projections to each stage according to its purpose. A route classifier does not need the entire NPC biography or inventory merely because the snapshot can bind them.
- An accepted conversational effect can change disposition before the final reply. Produce an explicit successor snapshot/revision for that change; do not mutate the captured snapshot or let different stages silently re-read incompatible values.
- Read tools validate owner and revision before returning detached data. Unknown inventory must not be initialized merely to answer a read request. Provider-specific observation/access rules remain separate from willingness to disclose.
- Recheck native capability, distance, access, stock, money and consent when committing/executing an action. A context snapshot does not reserve resources or replace existing action validation.
- Speaker changes, save restoration, scene invalidation and newer submissions invalidate obsolete callbacks. Define freshness per source; ordinary clock progression alone must not endlessly cancel replies. Current confirmed changes take precedence over stale generated claims.
- Cancellation/failure must retain truthful transcript/outcome handling. Never deliver a stale reply to a different person or silently interpret failed model output as a new commitment.

## Knowledge and presentation rules

Keep world truth, NPC knowledge and disclosure distinct. This first snapshot can expose existing verified facts and explicitly unknown fields; the later observation/belief layer adds information paths. A compact formatter must not manufacture missing details, convert player context into NPC context, or label all retrieved statements as verified.

Keep personality stable. Compute existing weather mood only from verified NPC outdoors state; if unknown, omit weather-derived mood rather than borrowing the player's scene. Preserve appointment HERE semantics separately. Do not reveal quest secrets through a shared context builder; existing role/stage disclosure filters apply before model projection.

Current state must be refreshed in the actual model protocol supported by the selected model. The supplemental runner's successful single-initial-system merge provides transport evidence, but its test implementation is not automatically the production API design. Preserve attributed conversation history and bound prompt size using actual tokenization.

## Delivery and verification

1. Add the detached Core contract and tests for explicit unknown fields, copy isolation, projections and binding/revision checks.
2. Add the runtime capture adapter using current native actor/service/scene sources. Keep unknown and ephemeral actors supported without inventing persistent identity.
3. Route normal social replies and semantic classifiers through snapshot projections, preserving existing action/appointment and transcript behavior.
4. Bind the separately delivered IMM-03 read loop to the same snapshot/revision contract.
5. Run Core and runtime compile checks, then owned native tests after the selected model is accepted.

Required native cases: a stationary shop customer versus keeper; a managed resident with a future plan; a native actor with no verified itinerary; a guard; an ephemeral/unmanaged speaker; scene transitions; waiting and changed game time; changed weather; speaker switching; a threat/disposition change followed by a reply; an existing shared walk; close/reopen; save reload during an asynchronous request; and a delayed callback from an old save branch. Capture exact prompt projections and displayed replies. Verify that HERE still refers to the submission location while the player later moves.

## Limits

This delivery cannot guarantee factual correctness of unconstrained prose, create missing game adapters, or integrate every authored actor. It establishes consistent inputs, ownership and freshness so later memory, tools and agency improvements can be tested against a coherent boundary.
