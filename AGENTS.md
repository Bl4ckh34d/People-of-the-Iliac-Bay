# DFU development scope

- Never use regular expressions anywhere in this project's code, tools or tests. Natural-language intent, references and consent belong to the existing LLM semantic contracts; do not replace Regex with keyword lists or string-pattern classifiers. Parse machine formats with proper parsers and validate native state independently. See `docs/NO-REGEX-DIALOGUE.md` for the audited replacement approach and remaining work.

- UI artwork, backgrounds and previews must be shown directly in chat from local files. Do not start/focus the game, control the desktop, or inject mouse/keyboard input merely to show an image. The user explicitly rejects desktop control for this purpose.

- The user revoked subagent delegation for this mod. Do not spawn subagents, restart completed subagents, or delegate further work. This supersedes all earlier requests to keep six agents active and all earlier model exceptions. The main agent performs implementation, review and verification alone. Do not spend further work polishing debug cameras.
- Walking waypoints need practical arrival tolerances. Shared family movement uses the existing 0.4 m arrival radius; do not reintroduce millimetre-exact corner completion or a separate precise movement owner.

- While this mod is in development, all game saves are disposable test data. The user explicitly confirms that no existing save has special value. Do not spend development time rescuing or migrating obsolete test saves. Do not invent valuable player progress that needs protecting.
- Implement systems as simply as possible. Use existing native DFU behavior whenever it covers the requirement. Add only the mechanics needed for the requested gameplay; avoid speculative subsystems, elaborate edge-case recovery, and scope creep.
- Verify the requested behavior with focused checks. Do not turn a small gameplay change into a large validation or compatibility project.
- `docs/ROADMAP.md` is the current scope and delivery order. Historical plans and the archived roadmap are evidence only; they do not reauthorize removed systems.
- Prioritize visible routines, family outings, dialogue, native actions, health/guards and relevant memory. Use native perception and commerce; do not add autonomous merchant supply chains, quantitative farming/animal production, regional economic simulation, or retrospective world histories.
- Ordinary residents keep seeded skills and three stable traits. Skill progression belongs to relevant companions; mood and relationships remain dynamic. Later companion hiring should use simple daily pay, without expanding minute-based escrow/refund machinery.
- The hunger system is canceled and must remain removed. Ordinary scheduled Inn visits and native food/drink purchases do not require a hunger simulation or a keeper goods inventory.
