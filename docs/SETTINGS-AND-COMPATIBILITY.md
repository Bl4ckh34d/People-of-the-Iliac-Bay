# Settings and city-mod compatibility

User requirement added 13 October 2026: provide broad, practical player control through a mod menu, and add city-overhaul patches after the vanilla implementation is stable. This is part of the active goal, not a separate project.

Use DFU's native mod settings menu and live-settings callback. Add controls only when their corresponding behavior exists. Describe whether a setting takes effect immediately, affects future allocation only, or needs a reload. Disabling a subsystem must preserve its saved identities/deaths. The amended maximum is five for family households; beds and house size are soft allocation preferences, with shared beds allowed and most homes smaller. Preserve technical quest/save integrity while allowing the player's deliberate quest-NPC kills as later requested; separate background-event protection. The initial foundation used a 1–3 control before family-aware allocation and save migration.

Version 0.3.0 supersedes the earlier 1–3 limit described above: family allocation and schema-3 migration now support 1–5, default five. The isolated player's saved setting is also five; existing households are never rerolled.

First seven functional controls: simulation enable, Gothway Garden physical-population preview, initial household capacity (1–5; new settlements only), waking hour, sleeping hour, diagnostic refresh interval, and F8 diagnostics availability. The preview toggle clears generated actors and restores the vanilla civilian pool while retaining saved identities and routes. Validate settings even when configuration files are edited outside the menu. Existing residents must not disappear or resurrect when capacity changes. A live menu/toggle acceptance test remains pending; build checks cover native deserialization and defaults.

Later controls accompany implementation: crowd/activity weights and operating hours; stealth/detection/waking sensitivity; settlement guard counts, patrol/response/search settings and recruitment; nightlife/group behavior, event frequency/severity and haunting behavior; dialogue enable/model/response limits/timeouts; actor radius, simulation budgets and diagnostics. Add coherent presets and reset-to-defaults support when enough systems exist to make presets useful. Changes that affect saves require explicit descriptions and migration behavior.

Version 0.3.1 adds the eighth control, `DailyRoutine/SleepingPoses` (default true). It immediately enables/disables supported sleeping visuals while retaining identity and schedule state. Current coverage is explicitly one adult appearance on unshared home beds. Native deserialization/default checks cover this control; runtime tests exercise the underlying live transition. Clicking the actual settings menu remains a separate UI acceptance check.

## Distribution boundary

The ninth control is `General/QuestHouseholds` (default true). It enables the reviewed vanilla The Delivery Boy household-binding pilot in Gothway Garden. Changes take effect immediately for new bindings; turning it off also prevents an already staged binding from being accepted. Accepted quests keep their published names and targets while disabled. Actual-player tests invoke the real settings callback and verify these transitions across native offers and reload. Clicking the menu remains a separate UI check. Other quest types and modified quest sources keep native behavior.

The user requires a distributable standard mod, not a custom game installation. Living Cities is packaged as `livingcities.dfmod`; its runtime sources, settings and generated atlas are under its own mod directory. Editor-only build/survey tools are excluded from the package. Mod save data is separate (`mod_livingcities.txt`). No custom executable or replacement engine assembly is required. Any future inability to implement a feature through a standard mod must be reported rather than silently changing this distribution requirement.

Reverified 14 October 2026 after the automatic delivery checkpoint: the pinned upstream DFU checkout has no tracked changes, and all 598 files from the official `dfu_windows_64bit-v1.1.1.zip` match the installed player by SHA-256, with no missing or changed files. The archive's 642 entries also include directories. The current development build and installed Living Cities bundles match SHA-256 `51C146DA6454744422150FBD96BA981B0950CCD139418F6E21EECA2760E4916A`. The official security-patched player is the baseline release, not an engine patch made by Living Cities. Building inside the upstream ignored `Assets/Game/Mods/LivingCities` folder does not modify tracked engine source. The mod now packages Harmony and its quest hook library inside the `.dfmod`; hooks change behavior in memory and do not replace the original executable or engine assemblies. Distribution should contain the mod and required notices, not a copied game installation. Mod-specific save data remains a separate compatibility concern when removing the mod.

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
