# Multi-turn fixture v2 rationale

V2 preserves the five paired episodes, ten arms, and twenty turns from v1. V1 remains unchanged. V2 adds numeric, typed synthetic snapshots so the prepared model context can follow current Core fields. All values below are authored test controls, not observations from a game save or native gameplay. No state is inferred from player text, and no model output mutates a later snapshot.

The current Core personality profile is copied exactly with its 17 stable trait keys. `npc.personalitySeed` is fixed at `0` in every arm to avoid accidental trait rerolls. `npc.disposition`, `npc.affinity`, mood, and directed relationship remain separate factors: legacy `high`/`ordinary`/`low` affinity controls map to affinity `75`/`50`/`25` and social disposition `55`/`0`/`-50` respectively.

Mood values use authored controls: calm/neutral `0`, pleased/appreciative `25`, wary `-25`, upset `-50`, and alarmed `-60`. Each turn carries its own complete pre-reply mood snapshot with `acceptedTurnSequence`, `stateRevision`, and `updatedAtGameSeconds` anchored to the snapshot's captured game time. These values test typed projection; they do not claim a mood appraisal occurred in the native game.

Where v1 explicitly supplied a fear or resentment contrast, v2 maps low/ordinary/high to `15`/`35`/`80` on the corresponding numeric field. Missing fear or resentment is authored as `0` for this synthetic test state. Relationship revision and `lastGameTime` advance only when the authored relationship state changes. A revision-zero state uses `lastGameTime: -1`.

Trust is known only when the source fixture explicitly described a reliable or unreliable relationship. Those controls map to reliability trust `75` or `20`, one evidence item, and confidence `10`. Unknown trust has `trustKnown: false`, no `reliabilityTrust` value, and zero evidence and confidence. The fixture never substitutes a default trust score. Evidence represented in the input is also retained as actor-known `npcKnowledge` with authored source and capture-time provenance.

Legacy obligation descriptions are represented as Core-shaped top-level `commitments` (`id`, `kind`, `status`, and `revision`). Any duty scope text remains a separate actor-known `npcKnowledge` fact with `AuthoredFixture` provenance. The fixture does not invent a motivation snapshot or unsupported commitment fields.

Only `turns[].input` is prepared as model-facing context. `turns[].oracle` remains evaluator-only. Three-choice D3 cases remain abstract choice-review examples; they do not represent native willingness or an applied gameplay effect. The suite continues to test paired causal contrasts, not exact prose or keyword matching.
