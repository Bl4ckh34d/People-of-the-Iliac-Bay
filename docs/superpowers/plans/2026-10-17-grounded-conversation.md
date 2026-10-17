# Grounded conversation implementation plan

**Goal:** Finish the approved free-text conversation flow with native topic knowledge, validated map requests and persistent individual disposition.

**Architecture:** A bounded local-model classifier selects a supplied native topic ID, conversational intent and tone. The host resolves only real topics through TalkManager, which retains knowledge and quest rules. Model text cannot supply map coordinates or numeric disposition changes. Unknown/ambiguous facts produce clarification. Facts use native text; social chat uses the existing local model. No vector database or engine-source modifications.

**Tech stack:** C# 7.3, DFU native UI/TalkManager, llama.cpp localhost, existing world-state persistence.

User authorized implementation on 17 October after reviewing native retrieval/map-action approach. Routine choices proceed without another approval round.

- [x] Reproduce shortcut propagation, enable focused-text hotkey suppression, verify native dispatch restores after blur.
- [x] Add bounded topic selection/classification and regressions for invalid IDs, unknown topics, injection and explicit-only map intent.
- [x] Persist per-NPC disposition with neutral=0, polite=+1, rude=-2, insult=-8. Cap goodwill and repeated-line farming; migrate older saves without changing identities.
- [x] Connect native topic trees, unknown/group clarification, current-subject map follow-up and native knowledge/reveal checks. Keep exact factual answers canonical.
- [x] Add user settings for conversation effects and map requests; document limits and supersede display-only rule only for these validated actions.
- [x] Build, run core regressions and targeted live conversation tests covering focus, native locations, map discovery, disposition and model response.
- [x] Record evidence and hand back a restart/test instruction.

Evidence: core 605/605; `work/GroundedConversation-native-pass.log`; `work/GroundedConversation-build-pass.log`. Native checks include a map follow-up after an intervening left-hand topic response, quoted-insult neutrality and reaction-tier changes. No player save was overwritten.
