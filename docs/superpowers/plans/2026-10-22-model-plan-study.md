# Local model and multi-step extraction study

**Goal:** Identify the causes of failed three-to-five-step plans, compare several current local models, and integrate only improvements supported by complete-plan and correction evidence.

**Architecture:** Native game state owns truth, permissions and execution. Models propose interpretations; no benchmark executes actions. Compare the existing frozen decomposition with a revisable whole-plan proposal before considering additional model stages. The original utterance remains authoritative throughout repair.

**Tech stack:** C# Core parser, .NET 8 evaluation tools, Python fixtures and HTTP test doubles, llama.cpp GGUF, RTX 4070 SUPER 12 GiB, resident Breeze TTS.

**Spec:** `docs/2026-10-22-npc-conversation-acceptance.md` and the user's English-only, full-information, at-most-two-corrections requirement.

## Constraints

- Initially another agent owned the GPU for TTS clips. The user subsequently gave explicit greenlight for testing; serial GPU inference is now authorized. Preserve Breeze and restore the original owned services after every run.
- Preserve concurrent portrait work, existing dirty changes and current service configuration. Restore only services this study stops; never terminate an unrelated process.
- Pin downloaded revisions and verify SHA-256. Do not execute code from model repositories.
- Keep development and held-out fixtures separate. Never pass expected answers or evaluation errors derived from gold answers to a model.
- Three proposal attempts maximum. Report correctness after each attempt, false executable interpretations, unresolved information, latency and memory separately.
- A valid JSON object is not proof of complete semantics. A successful interpretation is not proof of native execution.

## Tasks

1. **Reproduce and classify existing failures.** Inspect original raw calls against Core contracts. Record wrong decomposition, missing parameters, altered evidence quotes, locked incorrect values, and repair feedback limitations.
2. **Make comparison transport fair.** Extend `tools/GoalAttemptEvaluation/Program.cs` with an explicit native GGUF template mode, loopback test endpoint, frozen settings and transport metadata. Preserve legacy mode. Test with CPU-only HTTP fixtures, including overflow, wrong identity, truncated output and gold-answer isolation.
3. **Freeze broader cases.** Add held-out English three/four/five-step plans, mixed statements and orders, quotes, hypotheticals, units, referents, missing information and constraints. Keep literal-contract and semantic duration scores distinguishable.
4. **Model-class analysis and acquisition.** Examine primary model cards for Qwen, Gemma, LFM, small dense assistants, tool specialists, discriminative extractors, embeddings and MoE. Download Qwen3.5-4B Q4_K_M, Gemma4 E2B/E4B Q4_K_M and LFM2.5-2.6B Q8_0. Treat model size, total weights, KV state and measured allocation separately.
5. **Serial experiments after GPU release.** Record baseline occupancy and exact server identities. Load candidates with their native template and bounded context. Run identical baseline cases, then test a revisable extraction protocol if needed. Record every response, manifest and resource sample. Restore original services even after failures.
6. **Integrate evidence-backed improvement.** Add failing regression tests before changing production contracts. Keep all effects behind native validation and complete-plan acceptance. Run Core, adapter and runtime checks; obtain code review using the installed review skill.
7. **Deliver and capture.** Write a sourced HTML report under the chat's `outputs/`, separating measured results from proposals. Update repository acceptance status and Obsidian only with verified reusable findings.

## Initial diagnosis

The existing first pass freezes action count and kinds. Later repair cannot correct a wrong decomposition. Parameter repair also preserves nonempty values which may be formally valid but semantically wrong. Exact quotes are sometimes paraphrased or capitalized; native rejection is correct but the feedback lacks field localization. Existing extraction uses hardcoded Qwen ChatML, unsuitable for a fair cross-family comparison. The evaluator currently treats equivalent duration units as different, so both literal preservation and normalized semantic accuracy must be recorded.
