# Extractive conversation-memory compaction

Conversation compaction is a derived cache over immutable raw archive records. The source `.pim` ancestry remains unchanged, and the cache lives under `DerivedCompaction`, outside the archive/export format. A cache group is bound to the exact archive ID, checkpoint head, person, native 30-day month, source rows, and captured salience-context fingerprint. Invalid or missing cache data falls back to raw recall.

The cooperative scanner reads at most 128 records per call. Each pass retains at most 16 complete person/month groups; if more groups remain, `HasMorePages` requests another full pass after that page’s selection work drains. Processed keys are job-local, so cancellation or reload safely starts over and exact completed cache groups are skipped again. This bounds retained source text per pass while allowing later groups through, at the cost of rescanning the immutable ancestry for each page.

A group is eligible only when every raw source is more than one native month old. Groups with more than eight rows or over the candidate-byte bound stay raw. The model receives bounded candidate text and may return only IDs; strict JSON parsing rejects empty, duplicate, unknown, or extra-content output. The current source-bound context fingerprint is captured again before accepting a response; a change reissues selection under the new context, and an unbuildable replacement leaves raw recall uncovered. The cache stores selected original rows and all candidate rows needed to prove exact coverage. It stores no generated factual summary and never deletes raw evidence.

Stable saved personality and uniquely source-bound accepted appraisal deltas may influence selection. Numeric appraisal values remain selection context only: they do not prove emotion, truth, or player culpability, and a positive mood delta is not confirmed joy. Query-matched raw rows retain priority during recall, preserving specific retrieval of a source the model omitted; pending duplicates remain raw.

The game-facing scheduler owns idle, proximity, checkpoint, cancellation, and shared-model dispatch policy. `ScanComplete` denotes the current archive pass; `HasMorePages` tells the scheduler to continue after the current page’s requests resolve. The scanner itself does no game, wall-clock, or model calls.

CPU regression coverage is in `tests/ConversationMemoryCompaction.Regression` and `tests/MemoryCompactionRuntime.Regression`.
