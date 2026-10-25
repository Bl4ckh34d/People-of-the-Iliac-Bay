# Semantic conversation intent implementation plan

The user approved the design on 19 October 2026: local-model interpretation of the current player's meaning, structured decisions, and deterministic game validation. The user explicitly assigned an Astra/low worker to the architecture change. Existing lexical APIs remain compatible as conservative fallbacks; they must not veto the new semantic path.

## Responsibilities and acceptance

1. Conversation classification: retain topic/map/tone compatibility, add action and appointment intents, and a separate social intent for busy pauses and direct family threats. A pause is sampled once for 6–12 game hours and persisted. Only genuine submitted player speech can cause it. The model distinguishes direct intent from negation, reports, quotations and hypothetical speech.
2. Action contract: bind structured decisions only to bounded current native options. Validate unique option IDs, supported kinds, exact source ownership, quantity bounds and duration. The model chooses willingness; acceptance remains intent, not evidence of completion. Keep request/session identity checks and existing execution revalidation.
3. Runtime actions: enumerate real capabilities without requiring an English verb pattern, use original player text, and keep ordered tasks, offers and cancellation grounded. No per-NPC model calls or scene scans on movement ticks.
4. Appointments: route semantic invitations and cancellations without the legacy request-phrase gate. Resolve only real offered places and bounded calendar/time fields; production booking still checks travel, conflicts, closing, haunting and home/sleep obligations. Missing details require clarification.
5. Integration: remove pre-model threat side effects; only use narrow lexical interpretation when semantic classification is unavailable. Preserve the actual player utterance and truthful NPC reply in history, including refusal/failure paths.
6. Verification: Core contract regression tests first; combined Core suite and RuntimeCompile; actual local-model paraphrase/negation/report fixtures; Unity install and native Talk checks when the game is closed. Distinguish contract tests, model fixtures and physical game acceptance in reports.

## Shared-checkout boundaries

- Social worker owns Core conversation classification and completion grammar.
- Astra worker owns semantic Core action binding and reviews architecture.
- Runtime worker owns native action option enumeration and action conversation adapters.
- Parent owns top-level runtime routing, semantic appointment handling, combined verification and documentation.

No unrelated changes, reset, clean, commit, or parallel Player sessions. Existing ordinary dialogue, exact map permissions and native executor safeguards remain authoritative.
