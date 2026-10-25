# Personality: three random traits, story exceptions

## Explicit user requirements

- Ordinary NPCs receive exactly three randomly selected personality traits from a broad positive, neutral and negative vocabulary. There is no good/bad quota: three negative, three positive or any mixture are valid.
- Authored story/key characters follow their source-backed characterization. Generic sidequest participation alone does not replace an ordinary NPC's personality or relationship. A difficult relationship may prevent quest access; the player must earn cooperation.
- Negative traits explicitly requested include slyness, impatience, volatile temperament, dishonesty, greed, stinginess, unfriendliness and vulgarity. These describe game behavior and speech; they need not be a clinical personality taxonomy.
- The diagram must communicate this range without becoming a long visible document.

## Historical mismatch before this change

`NpcPersonalityProfile` version 2 stores 17 named axes. `NpcPersonalityGenerator` selects values for every axis; this is not a three-trait system. `SelectedTraits` exposes all 17 and `Description()` renders them. Existing vocabulary includes deceitful, self-serving, sly, manipulative, cynical, volatile, reckless, condescending and coarse. Greed and stinginess are not separate traits in this profile. Reserved/guarded are not equivalent to hostile, and brisk/easily wearied do not fully express impatience.

## Follow-up implementation constraints

Persist the three distinct selections for a stable NPC identity; do not reroll on load, dialogue or generator changes. Sample without a moral balancing rule. Greed (desiring more) and stinginess (reluctance to give/spend) must remain distinguishable. Friendliness must not imply honesty; vulgarity must not imply cruelty. Duplicate handling is necessary; handling directly contradictory trait pairs remains a design detail and must not quietly introduce a good/bad quota.

Separate the three defining traits from identity, role, acquired relationships and temporary mood. Review existing consumers of all 17 fields, including salience, speech, social appraisal and saved-profile validation. Plan a versioned migration for existing saves; do not silently reroll established people or populate another 14 hidden personality traits while claiming only three.

Authored story authority takes precedence across native identity paths. Generic `IsQuestProtected` and `QuestResourceBehaviour` flags are not proof of authored characterization. Broad ordinary-NPC coverage also requires stable identity on native speaker paths, not only residents.

Required tests: exactly three unique saved traits, no moral quota, reproducibility/reload stability, old-save migration, ordinary identity-path coverage, story protection and authored personality priority. English model trials must show actual behavioral differentiation, grounded intentional deception, and consistent emotional memory selection without inventing world facts.

## Source implementation and verification

Version 3 now stores exactly three populated trait axes, selected without a moral quota. The remaining fields stay null. New vocabulary includes explicit impatience, unfriendliness, greed and stinginess; generosity, compassion, forgiveness and humility are separate axes. Selection removes the chosen axis to prevent duplicate or directly opposing values on that axis.

Legacy profiles migrate to a deterministic subset of three saved values. Reload and later generation inputs preserve the selection. Previously recorded mood/fear/relationship recovery rates remain anchored to their original events; new events use the active profile. Breeze receives a sparse schema-2 three-trait payload; legacy schema 1 remains accepted. Semantic motives do not automatically imply a particular voice sound.

Generic sidequest residents retain traits, mood modulation and voice inputs. Native Individual factions conservatively suppress random traits as an authored-character proxy. This is not an authored persona catalog. Native presentation and background appraisal refresh eligibility even for existing profiles; suppressed profiles cannot be recreated by experience capture. Native social groups remain distinct and dialogue/appraisal instructions preserve story authority and native reaction priority. Existing native reputation calculations are unchanged.

CPU evidence: Core 1669/1669 and Breeze 124/124 pass; RuntimeCompile reports zero warnings/errors. The duplicate-profile capture regression failed before its fix. Existing adapter and memory/mood suites passed earlier in this source checkpoint. Evidence is in `work/personality-v3/`.

Independent review found a stale-profile path in idle memory selection; the context callback now refreshes authored eligibility before reading traits. The reviewer confirmed both initial scan and async-result context refresh execute on Unity's main thread, not the transport worker. No remaining blocker was reported in that scoped review.

Native update: V3 bundle `2258a0f3` is installed. Six of seven final native runs pass; the seventh has three valid source bindings but a vague gift explanation in one case. Three-trait persistence, generic quest identity continuity, authored suppression, corrected authored default fear, mood, native poison and archive restart are covered by bounded fixtures. See [native evidence and limits](2026-10-23-personality-v3-native-acceptance.md). An authored persona catalog, full ordinary native identity coverage, live-model behavioral differentiation and actual aged-memory compaction remain open. New role/rank/allegiance rules are recorded separately in ROADMAP.
