# Future dialogue and English voices

User addition, recorded 14 October 2026. Speech is a future optional extension after the vanilla NPC simulation is stable. It does not replace the planned local dialogue milestone.

## Local dialogue model

The user delegates selection of the local 7B conversation model. Choose through a reproducible benchmark on the target hardware: grounded paraphrasing, free-text intent handling, lore/fact adherence, latency, memory consumption alongside DFU, and compatibility with optional speech. Record model revision, quantization and redistribution license. No final model has been selected or benchmarked yet.

Game state remains authoritative. Supply vanilla response/knowledge, disposition, race, gender, social standing, activity, relationships and verified memories. The model changes expression and responds within known facts; it must not award items, change quests, invent relatives or grant services. Keep existing topics, free-text input, cancellation and vanilla fallback.

## Optional English TTS

Investigate reusing the fast English speech engine from the user's Telegram-Codex-Bot project. The current local worker source uses MiraTTS and caches `encode_audio(reference_audio)` contexts. Its inspected reference-context path does not take a reference transcript. Recheck the installed model's requirements during integration; a 5–7 second sample plus transcript is a proposed production format, not a verified universal requirement.

Target at least 52 female and 52 male distinct base voices, with additional suitable fantasy voices such as low, rough orc voices. Pitch and speed variants supplement this base diversity rather than replacing the requested 104 base voices. Prefer generated voices or samples licensed for the intended use and distribution, with source/license metadata. Evaluate voice-design models and generation services, including the user's suggested ElevenLabs/11.ai option. Free access without an account, download rights and sample suitability remain unverified; do not make them a dependency.

Create a catalog with stable voice IDs, reference audio, optional transcript, language, descriptors, provenance, processing version and quality checks. Assign each NPC a compatible voice deterministically and persist the catalog voice ID plus bounded pitch/speed variation in the save. Do not reroll voices on travel, reload, death replacement or catalog reordering. Newly recruited NPCs receive new identities and assignments. Define missing-voice fallback and migration explicitly.

Separate synthesis from Unity's main thread. Support bounded queues, interruption when dialogue closes, cached repeated lines, subtitles, volume/speech toggles and graceful text-only fallback. Limit ambient overlap and maximum audible speakers. Benchmark first-audio latency, warm/cold performance and combined LLM/TTS/DFU VRAM use; the bot's historical latency is not a DFU performance guarantee. Avoid changing or interrupting the bot's running worker during mod development.

Acceptance requires at least 104 audibly distinct, intelligible base voices; stable NPC voice across reload; suitable orc/fantasy options; adjustable playback; bounded memory; no UI freeze; and immediate text fallback when synthesis fails. Generation, downloading and model integration remain future work.
