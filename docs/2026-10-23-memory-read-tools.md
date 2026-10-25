# Model-selected memory and mood reads

This source follow-up connects the memory architecture agreed with the roadmap chat. It is not a Unity bundle deployment or a new model-quality benchmark.

## Authority and selection

`NpcConversationLookup` extends the existing selector with `RECALL_OWN_MEMORY|query|0` and `READ_OWN_MOOD_CAUSES`. At most one additional model-selected read is allowed per turn. The bounded automatic speaker context, including ordinary recall and mood causes, remains intentional; NONE means no additional research, not a memory-free prompt. Memory queries are at most160 permitted ASCII characters. The model cannot supply a person, archive, time, revision, cursor or write operation. Existing topic and inventory reads remain available. The runtime offers memory/mood reads only when the captured speaker has the same durable identity as the open memory session.

The memory read uses at most2000 characters from the original query-backed archive/index. Mood reads use at most600 characters of recorded native mood influences. A missing result is explicit; it is not evidence that no event occurred. Source attribution and uncertainty remain part of the context. Reading private experience does not require the NPC to disclose it, and a remembered assertion is not promoted to an authoritative game fact.

## Publication

`PeopleOfTheIliacBayMod.MemoryReadTools.cs` retains the Core-owned `NpcMemoryReadSnapshot`, its exact store and the immutable request binding. Capture checks archive ID/head. Final stage consumption calls `IsNpcMemoryReadCurrent`, in addition to existing speaker/session/scene/save checks. Selected-read answers are buffered until that check, bypassing early sentence streaming and delayed text reveal. Heavy read validation does not run in per-frame pending-state checks.

The Core receipt is supplied by the other implementation chat. It binds exact store/state, own journal or mood/inbox authority and time eligibility. Same-value mood changes and same-head restores do not bypass its identity checks. Runtime time validation preserves fractional game seconds, matching snapshot capture. Save, session and new-request boundaries clear all retained read references so an old world state cannot remain pinned through the receipt. Same-binding successor snapshots retain the dependency until that boundary.

## Verification and limits

Selector:90 assertions, including preserved inventory checks, malformed/multiple commands, disabled reads and forbidden owner/cursor/write fields. Production-linked runtime gateway:18 assertions with the real Core and a clock facade; speaker isolation, changed source/store, save fault, successor provenance, fractional-time regression and lifecycle cleanup. Core receipt:34 assertions. Existing knowledge42, Core1659/1659, adapter88/88 and clean RuntimeCompile pass at this checkpoint.

These checks do not prove Gemma chooses the optimal search phrase or that all in-game emotional experiences are represented. Mood causes are limited to the implemented recorded native effects. Semantic retrieval, shared invented biography and complete native gameplay acceptance remain separate work. No embedding model, vector database or new service was added. The prior whole-mod report remains evidence for its earlier checkpoint.
