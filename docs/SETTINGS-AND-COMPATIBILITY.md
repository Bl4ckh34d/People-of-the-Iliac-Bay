# Settings and city-mod compatibility

User requirement added 13 October 2026: provide broad, practical player control through a mod menu, and add city-overhaul patches after the vanilla implementation is stable. This is part of the active goal, not a separate project.

Use DFU's native mod settings menu and live-settings callback. Add controls only when their corresponding behavior exists. Describe whether a setting takes effect immediately, affects future allocation only, or needs a reload. Disabling a subsystem must preserve its saved identities/deaths. The amended maximum is five for family households; beds and house size are soft allocation preferences, with shared beds allowed and most homes smaller. Preserve technical quest/save integrity while allowing the player's deliberate quest-NPC kills as later requested; separate background-event protection. The implemented foundation control remains 1–3 until family-aware allocation and save migration are implemented.

First seven functional controls: simulation enable, Gothway Garden physical-population preview, initial household capacity (1–3; new settlements only), waking hour, sleeping hour, diagnostic refresh interval, and F8 diagnostics availability. The preview toggle clears generated actors and restores the vanilla civilian pool while retaining saved identities and routes. Validate settings even when configuration files are edited outside the menu. Existing residents must not disappear or resurrect when capacity changes. A live menu/toggle acceptance test remains pending; build checks cover native deserialization and defaults.

Later controls accompany implementation: crowd/activity weights and operating hours; stealth/detection/waking sensitivity; settlement guard counts, patrol/response/search settings and recruitment; nightlife/group behavior, event frequency/severity and haunting behavior; dialogue enable/model/response limits/timeouts; actor radius, simulation budgets and diagnostics. Add coherent presets and reset-to-defaults support when enough systems exist to make presets useful. Changes that affect saves require explicit descriptions and migration behavior.

## City overhaul adapters, after vanilla acceptance

Keep settlement inventory, navigable routes, entrance/bed/activity anchors, vendor capabilities, and persistent identity mapping behind clear adapter boundaries. A patch can register additional market stalls, buildings, destinations, and actors. Stable IDs must include the supplying mod and object key so additions do not renumber existing residents.

Decorative seller NPCs should become interactable through patches only when valid stock, ownership, service hours, and dialogue rules can be supplied. Preserve vendor and quest identities from the source mod. Added stalls must affect crowd attraction, physical obstruction, ownership, and guard coverage together.

Detect supported mod/version combinations, apply patches only when installed, and document unsupported layouts. Test added/removed/updated city mods, interior transitions, path obstruction, quest access, save restoration, and duplicate vendors. Do not promise compatibility with unnamed city mods or guess which overhaul the user means. Identify target mods with the user once vanilla gameplay passes.

## Implementation sequence

- [x] Add behavior tests for bounded configuration and new-settlement-only household capacity. Five new tests observed failing before implementation, then passed; 57 total pass.
- [x] Implement configuration and wire current controls to the adapter.
- [x] Package native `modsettings.json`; validate it with DFU's actual settings deserializer.
- [x] Rebuild and smoke-test settings loading alongside Basic Roads. Unity build exit 0; settings-loaded and readiness markers present in a fresh player log.
- [ ] Inspect the live menu and exercise in-game changes when the gameplay harness is available.
- [ ] Expand controls alongside later milestones; vanilla-first city patches remain part of M7.
