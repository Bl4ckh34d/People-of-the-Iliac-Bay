# Relationships, memory, and future inventories

User additions on 13 October 2026. These extend the active mod goal without replacing its earlier requirements.

## Current mod: relationships and bereavement

Track persistent NPC-to-NPC relationships using stable resident IDs, separately from disposition toward the player. Household membership does not automatically define a marriage: model the actual bond so dialogue can identify a partner, relative, friend, or housemate correctly.

On 14 October the user required all permanent residents of a household to share a surname. Persist the household surname and use it consistently in displayed NPC names and dialogue context, while preserving distinct given names and stable resident IDs. Visitors retain their own names. Shared surname alone does not establish a marriage or parenthood.

The user requested male/female family households, sometimes with children. They explicitly superseded the original maximum of three: four was approved, then five for two adults and three children. Most households should remain smaller. Their subsequent clarification makes beds a weak indicator, not a strict occupancy cap: couples can share a bed and children can sleep with parents. Use actual interior size, room arrangement and beds as weighted preferences; larger homes favor larger families. Count the specific interior rather than assuming all buildings of a type have equal furnishing. Existing three-person implementation is historical foundation behavior and needs a versioned family-aware migration, not blind regeneration of saved residents.

Separate workplace from residence. A tiny shop without beds, such as an alchemist or pawn shop, does not force its owner to sleep there. After closing, the owner travels to the assigned home/family, then returns for opening. A shop with suitable living quarters can remain its owner's home. Preserve a single persistent owner identity across shop, street and house; maintain services and quest bindings. Determine bed-sharing sleep anchors separately from household membership so sleepers do not all occupy the identical sprite position.

Record explicit adult/child and parent/partner relationships rather than inferring them from appearance. DFU's `TextureReader.IsChildNPCTexture` identifies existing child flats, including archives 181, 182 and 184; full walking/action animation coverage remains unverified. Children stay home or play/socialize in nearby outdoor groups, facing one another. They do not join adult work, shopping, tavern or inter-settlement travel schedules. Schedule local play in daylight and return home before danger/nightfall; group membership must remain stable across streaming.

The user delegates whether children can be spoken to. The selected behavior is brief age-appropriate conversation about home, family, playmates and nearby places, with knowledge constrained to what the child knows. Pinned DFU `TalkManager.TalkToStaticNPC` already permits child dialogue while excluding children from ordinary quest-offer branches. This source check does not establish every interaction in the original DOS executable. Keep children out of adult service and quest-giver allocation.

Seed friendships, rivalries and familiarity from household ties, neighbouring homes, shared factions/guilds, political attitudes and social standing. These are tendencies rather than hard barriers: cross-class and distant friendships should occur. Shared appointments reserve both participants' time and a reachable destination, including houses, taverns, squares and parks where available.

Household deaths produce persistent bereavement memories. Grief lasts one to two game years, with diminishing probability of mentioning the loss as time passes. Use DFU's calendar, stable event timestamps, and a bounded salience model. Local dialogue receives verified relationship, deceased identity, elapsed time and current salience. Avoid inventing extra relatives, causes of death, or a culprit. Repeated conversations should not mention the death on every turn.

When a resident witnesses the player kill their partner, record that attributed witnessed event. The survivor becomes hostile, refuses ordinary conversation and attempts to attack the player. Witnessing requires actual perception at the event: distance, visibility, alertness and relevant awareness. Unwitnessed deaths do not reveal the player as killer. Later secondhand reports must retain their source and certainty. The 14 October amendment allows player kills of important quest NPCs, including broken quests, while preserving technical bindings and save integrity. Separate player-caused deaths from default protection against background events; see `CRIME-SCENES.md`.

Persistent events must be idempotent across death callbacks, save/load, and scene changes. Recruitment must not reuse a deceased resident's ID or erase survivors' memories. Ordinary household replacement does not silently create a replacement spouse.

## Future inventory extension, possibly a separate mod

The user is considering a later inventory/pickpocket mod. Do not silently expand current implementation into a complete equipment system. Research vanilla sneak/crouch and pickpocket behavior from the pinned source before choosing hooks; no crouch/sneak-combining mod is assumed installed.

Preserve extension points for resident inventory, ownership, home keys, and theft events. Keys should identify actual households/buildings and work with nighttime locks. Support mobile and stationary NPC identities. Equipped clothes and armor can be looted only after death and only when the NPC's visible sprite represents those items. Do not permit stealing equipped trousers or armor from a living resident. Other carried items can follow a separate pickpocket rule when implemented.

NPC sprite variation can benefit both mods: combinations corresponding to clothing items that exist in Daggerfall, with metadata mapping visible outfits to equipment. Keep canonical clothing appearance separate from transient animation state and inventory ownership.

Full combat, spellcasting and corpse animation sets for all existing civilian flats remain a user consideration, not an approved requirement to generate every set immediately. The current architecture should support additional poses and animation sets without changing persistent identity.
