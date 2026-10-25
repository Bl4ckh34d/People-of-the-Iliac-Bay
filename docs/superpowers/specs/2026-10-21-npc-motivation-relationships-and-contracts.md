# NPC motivation, relationships, decisions and paid companion contracts

Status: user-approved target framework, 21 October 2026. Approval establishes the shared architecture and staged integration direction, not implemented companions, betrayal, new relationship dimensions, numeric balancing or utility policies. Read alongside the 20 October NPC architecture and ROADMAP IMM-08 through IMM-18 / F01-F06. The [integration inventory and baseline](../../2026-10-21-framework-integration-map.md) maps this framework to current code and tests.

See the [NPC experience event catalogue](2026-10-21-npc-experience-events.md) for cross-domain scenarios, observer-specific evidence, conditional effects, deduplication, and native-adapter boundaries.

## 1. Intent and architectural choice

Optionality is a required boundary: the native base population/guard/routine simulation runs without LLM/D3. The optional LLM+D3 module supplies model-driven social behavior, complex interactions and plans. Optional TTS (including future speaking portraits) also works on supported base dialogue without LLM/D3. Separate optional STT requires LLM+D3 and does not require TTS. Settings and cancellation must respect these dependencies without discarding persistent NPC state. See the module acceptance matrix in [ROADMAP](../../ROADMAP.md). The next post-framework trial uses LLM+D3 plus TTS, deliberately no STT.

Use one shared description of the person, their relationships, knowledge, obligations and current circumstances across conversation and action decisions. Avoid independent thresholds and improvised prompts in each feature.

Three approaches were considered:

- One global obedience/loyalty score is cheap but conflates affection, reliability, fear, disclosure and duty. It cannot explain a disliked but dependable employee or a friendly liar.
- Unconstrained model-only choice is expressive but permits contradictory obligations, invented capabilities and unstable economic outcomes.
- Recommended: shared state, native feasibility and obligation rules, decision-specific utility profiles, and bounded model choice from legitimate options. Native code remains authoritative for commitments and effects. This extends the existing Qwen / D3 / native boundary rather than adding another decision model.

A score describes preference for one outcome in one situation. It is neither a universal NPC level nor a calibrated probability of truth, consent, combat success or survival. Numeric weights are versioned policy configuration, not purported psychological facts or model confidence. Tune against frozen contrasting scenarios before enabling them.

## 2. State taxonomy

| Layer | Proposed factors | Meaning and ownership |
| --- | --- | --- |
| Stable personality | Warmth/sociability, patience, privacy, honesty, dutifulness, courage, self-interest; values and specific loyalties | Persistent tendencies, not facts about the player. Preserve existing seeded traits; initialize additional axes once through a versioned migration. No rerolls on loading or talking. |
| Directed relationship | Existing disposition, familiarity, trust, resentment, interpersonal fear, attachment/loyalty | NPC's relationship toward a specific other actor. Disposition means liking/disliking, not proven reliability. Store only dimensions that have an actual consumer. |
| Current affect | Existing mood plus verified acute alarm/stress/fear where tracked | Mood is temporary outlook, separate from stable temperament and relationship. Injury, exhaustion, grief and danger are distinct observations, not conclusions invented from a mood scalar. |
| Needs and resources | Health, magicka, stamina/fatigue/rest needs where implemented, money, equipment, ammunition, capacity | Actual game state. Unknown or unsupported needs stay unknown; a model cannot fabricate fatigue or arrows to justify a decision. |
| Goals and obligations | Family/emergency responsibilities, schedules, appointments, accepted jobs, contracts, faction/personal commitments | Typed, dated, scoped obligations with priority and lifecycle. Employment is not another relationship scalar. |
| Knowledge and beliefs | Own observations, memories, attributed claims, inference, protected information, deliberate deception | Known by this NPC through an evidence path; never wholesale hidden world or quest truth. Each belief has source, age and uncertainty. |
| Situation | Actual location/time, NPC-owned weather, observed threat, reachable routes, available alternatives, witnesses where perceived | Captured once per decision and refreshed before effects. Actual world truth and NPC-perceived risk must remain distinguishable. |

Disposition retains native reputation/service compatibility and the existing gradual friendship limits. Mood and disposition both shape voice; do not continually add disposition into stored mood, which would count the relationship twice.

Stable traits can shape how an NPC interprets and responds to an event, including the magnitude and persistence of mood or relationship effects. A humorous, thick-skinned person and a sensitive, insecure person may react differently to the same verified words; neither trait changes what was said, who heard it, or what happened. Interpret traits together with event severity, delivery/context, directed relationship and relevant history. Do not collapse them into a universal sensitivity score or let a general humor trait excuse repeated, targeted abuse. The current v2 saved profile has 17 axes: the original 14 plus Humor, SelfEsteem and Sensitivity. The deterministic v1-to-v2 migration preserves all original saved values and derives the new axes from saved identity and seed; loading never rerolls established traits.

Trust begins as an explicitly uncertain prior, not a claim that a stranger has proved trustworthy. Its first consumer is confidence in the player's reliability. Track supporting events and confidence; do not automatically equate this with the player's competence at magic, fighting or keeping secrets. Separate specialized trust only when a feature needs it.

Relationship growth and damage are asymmetric. Slow positive friendship accrual must not protect accumulated progress from severe insults or threats. Actual heard threats against the NPC or their family can rapidly reduce affection and reliability trust, with personality, existing attachment, context and severity shaping the scale and response. A conversation receipt establishes that the threat was said, not that its future violence is certain. Semantic appraisal must distinguish direct harmful speech from quotations, third-party reports, hypotheticals or contextual joking. Severe-negative appraisal needs an explicitly bounded policy beyond ordinary small outcome deltas; it can also justify resentment, fear, hostile delivery or ending/refusing conversation through native adapters. Do not implement this using word lists or regex, nor apply positive friendship gain limits to negative consequences.

Fear may cause compliance, avoidance or seeking protection; it is not friendship or loyalty. Resentment can persist after an apology without permanently fixing current mood. Loyalty means attachment to a person, group or promise and willingness to bear costs, not unlimited obedience. A paid professional may be dutiful with little affection.

### Fear and damaged reliability trust — clarified 21 October

An adverse experience attributable to the player can both increase directed fear and substantially damage reliability trust. Scale each consequence by the accepted evidence, severity, personality and relationship context. Apply both effects once from that source event; do not continuously subtract trust from a residual fear value, a sighting, a read or a recovery tick. Fear caused by another actor or an unknown source does not establish player blame.

Fear becoming weaker does not restore trust. Damaged trust remains until new source-bound reliability evidence warrants an increase, such as an attended appointment, fulfilled obligation, actual combat help or healing. Time, ordinary conversation, apology, gifts and money alone are not evidence of reliability. The current policy permits separate model-selected deltas within event-specific bounds; it does not guarantee a minimum trust loss whenever fear rises. Any future calibration must preserve this event ownership and must not introduce semantic regexes or automatic per-frame coupling.

## 3. Updates, memory and time

### Fear recovery and renewed perceived proximity — clarified 21 October

Separate acute situational fear from persistent fear of a specific actor. Once a non-player danger has ended, acute fear should diminish over in-game hours. Directed fear of the player should also diminish over in-game days without new adverse experiences. Severity and stable personality affect the rates; exact half-lives remain calibration choices, not user-specified constants.

When the NPC perceives the player again, the remaining directed fear can raise current acute fear. Mere proximity must not restore the original historical peak, add fear every frame, or restart the long-term recovery clock. The response depends on the residual fear still present. Do not infer awareness through walls or from global player coordinates; use supported observation/contact evidence, and keep unobserved situations unknown.

New adverse, source-bound experiences may increase fear and establish a fresh recovery anchor exactly once. Trust-only changes, read access, reloads and repeated sightings must not postpone fear recovery. Persist separate fear timing where needed rather than reusing a general relationship timestamp changed by unrelated events. Mood recovery, resentment, trust and incident memories remain distinct; becoming calmer neither deletes the event nor automatically restores trust or friendship.

This is the approved behavioral target. Checkpoint78 still has persistent directed fear without automatic decay; acute and directed recovery/proximity integration requires implementation and its own acceptance evidence.

Checkpoint91 supersedes the directed-decay limitation: separate persisted fear timing/rate and pure timed dialogue/appraisal/TTS projection are implemented and pass Core plus a controlled native FullSerializer fixture. Acute situational recovery and perceived-player reactivation remain open. Exact accepted scope, provisional calibration and conservative legacy handling are recorded in `docs/2026-10-21-directed-fear-recovery.md`.

Noble status sensitivity is a planned native social factor. Verified nobility has a higher sensitivity to disrespect, with individual personality, actual authority, player status and history shaping the response. A context-grounded direct insult or threat may cause rapid relationship damage, conversation refusal and a supported order/request to local guards. A noble's removal/protection order is distinct from a witnessed crime report; neither prose nor social appraisal creates criminal facts or unavailable guards. Persist the incident and grievance. The user explicitly accepts that alienating a noble quest speaker may permanently block main-quest access. Do not undo this consequence simply to preserve progression; keep actual native quest stages, authored facts and disclosure permissions authoritative.

- Keep stable personality, current mood and directed relationships separately persisted and separately projected.
- Qwen can interpret the semantic significance of a conversation through a bounded typed appraisal. Native code validates permitted fields, evidence references, magnitude, identity, save branch and exactly-once event key. Do not infer emotion, promises, consent or betrayal from keywords/regex.
- Speech is evidence of speech. A promise is not its fulfillment. Sustained friendship follows the existing rate limits; trust from verified kept/broken promises and shared danger weighs more than repeated flattering conversation. Refusal is not automatically an insult.
- Link changes to attributed journal entries or actual native outcome receipts. One event must not apply again through summarization, recall, reopened chat or a second feature's callback. A single event may intentionally affect different dimensions once under an explicit policy.
- Record whether an adverse result was witnessed, reported, intentional, unavoidable or uncertain. Failed delivery from a blocked route is not automatically deliberate dishonesty.
- Use elapsed game time for accrual, recovery, warning windows and contract periods. Do not let wall time or a UI reopen advance relationships. Mood recovers toward neutral, with stable per-person variation in the recovery pace; reading context, reopening chat and loading must not reroll that variation. Keep this separate from enduring disposition, trust and resentment. Constants remain calibration work. No automatic forgetting of a specific betrayal merely because mood improves.
- The new Core profile-aware mood recovery policy keeps the stable world-seed/person half-life variation and additionally applies a deterministic personality multiplier from the persisted v2 profile. The provisional policy gives EmotionalStability and Sensitivity the strongest influence, SelfEsteem a moderate influence, and Humor a slight influence; the combined multiplier is clamped to 0.70–1.40. Runtime/store caller wiring remains pending. Once wired, this changes only the elapsed-game-time rate at which mood approaches neutral. It does not reinterpret event severity, change evidence, or modify disposition, trust, resentment, affinity or incident memory. These numeric factors are provisional calibration, not psychological measurements. Future event-impact modulation remains semantic appraisal work and must consider event severity, context, directed relationship and history rather than a universal scalar.
- Calibrate cross-factor effects with paired evidence fixtures: hold event, observed words, context, relationship and history fixed while changing only a persisted trait profile, then verify the intended difference in bounded appraisal. Also vary context/history with traits fixed. Repeated, independently receipted targeted abuse must not be dismissed as joking solely because the target has a humor trait. Quoted, hypothetical, reported or ambiguous threats remain distinct from direct speech. Semantic appraisal may interpret these factors; native code still binds receipts, constrains magnitudes and commits effects once. No keyword/regex shortcuts.
- Preserve save-branch isolation, migration defaults, transactional updates and immutable snapshots. Do not allocate every possible NPC-to-NPC relationship pair; create directed records on meaningful contact.

## 4. Decision pipeline and score ownership

1. Capture authoritative speaker/context, actor-known evidence and exact player turn.
2. Qwen interprets the request into typed intent, targets, parameters and candidate outcomes. It may request bounded read tools for missing known data. No text rules interpret intent. Missing capability or knowledge stays explicit.
3. Native validation removes impossible/unauthorized outcomes: invalid target, unsupported adapter, missing resources, stale identity, forbidden quest actor, protected information or incompatible existing commitment. Legality is not a blanket prohibition on crime: model lawful/immoral preferences separately and require supported crime attribution/consequence adapters.
4. Select a decision profile: new voluntary request, contracted duty, disclosure, danger response, negotiation, resignation, or deliberate betrayal. Compute comparable utility terms from the snapshot, traits, relationship, scoped duty, benefit, effort, risk and alternatives. Any semantic appraisal must reference the exact proposal and available evidence; it cannot create a world fact.
5. D3 selects from the bounded eligible outcomes using those factors and utility guidance. Keep the existing ACCEPT/DECLINE contract for its current binary willingness caller. Richer choices require a separately versioned interface and acceptance tests; do not silently change that protocol or create a competing hidden final decision.
6. Native code rechecks feasibility, policy/contract scope and freshness, then commits the chosen outcome. A higher score is never a payment receipt or authorization. If choice is invalid or unavailable, do not perform an effect or invent a character's refusal motive.
7. Qwen renders the committed result and permitted conversational facts. Execute accepted work using existing task adapters; record actual completion/failure separately.

Conceptually, for each eligible outcome:

    utility = preference fit + relationship relevance + obligation + expected benefit
              - effort - perceived risk - value conflict - competing commitments

Normalize feature scales and bound each contribution; do not add unrelated raw values such as gold, hit points and disposition. Policy weights differ by profile. Keep any final display scale such as 0-100 a derived diagnostic, not saved truth. Diagnostic traces record contributing facts, policy version and selected outcome, with no chain-of-thought requirement. Do not claim model-reported numbers are calibrated probabilities.

Authority is explicit: deterministic native rules execute already-consented routine duties; discretionary decisions use D3's bounded selected option, with native utility ranking as guidance rather than a second hidden final selector. Bind the choice to offered option IDs, snapshot/proposal revisions and policy version. Mandatory contract behavior is enforced through the eligible outcome set, not by hoping an advisory wage bonus beats mood. If a legitimate discretionary choice disagrees with the ranking, preserve and evaluate it; do not silently substitute another selection or claim its ranking was a model confidence score.

### Decision-specific relevance

| Decision | Strong considerations | Outcomes to support |
| --- | --- | --- |
| Casual request | Liking, effort, mood, current duty, benefit | Agree, decline, ask, offer alternative, negotiate |
| Contracted task | Contract scope, pay status, professional duty, capability, agreed risk | Carry out duty, report limitation, tactical retreat, request support; reassess only on material breach |
| Disclosure | Knowledge, privacy, trust, protected role, harm to self/others, motive | Truthful answer, partial answer, uncertainty, decline, bounded deception when permitted |
| Combat persistence | Role, courage, training, resources, perceived danger, loyalty, duty | Fight, reposition, protect, heal, retreat or surrender where supported |
| Resignation | Sustained mistreatment/distrust, nonpayment, contract limits, emergency, alternative | Warn, renegotiate, terminate and settle, leave safely |
| Betrayal | Specific conflicting allegiance, grievance or gain, values, credible opportunity and expected consequence | Remain loyal, refuse, leave, or commit a supported deliberate hostile/deceptive act |

Ordinary moods have bounded influence on contracted duties. Danger is evaluated against the contract's agreed risk: a dungeon combatant should not interpret the mere presence of ordinary enemies as a new out-of-scope request. A clear suicidal order or attack on protected relatives is not covered simply by paying wages.

Do not roll for resignation or betrayal every frame. Reassess on material events, decision deadlines and bounded simulation ticks. Use different entry/exit margins, sustained-condition windows and persisted decision ownership to prevent oscillation; immediate emergencies can bypass a warning period. Record the reason, not just a threshold crossing.

## 5. Honesty, secrecy and betrayal

Do not combine willingness to answer with truthfulness. An honest private NPC can refuse; a warm NPC can lie. A wrong but honestly held belief is not a deliberate lie.

Before deliberate deception becomes executable, represent a typed speech outcome with audience, topic, proposition/evidence reference, what the NPC believes, whether the claim is deliberate deception, and its motive. Spoken lies remain attributed claims in memory and cannot overwrite verified world state. Later correction or discovery refers to the original claim.

A deliberate-lie label requires a native-validated conflicting belief or evidence known to the speaker at utterance time, plus a permitted deception intent. A model-assigned flag alone is insufficient. Unknown truth stays unknown; honestly repeating a mistaken belief remains attributed mistaken belief.

Secret handling is a native information boundary. Role/quest-protected facts are never sent to a renderer that may disclose them. Trust alone cannot reveal hidden quest scripts or another NPC's private transcript. Character choice among known permitted information and permitted lies is distinct from access authorization.

Betrayal is an explicit plan/event, never a synonym for low mood, retreat, honest disagreement or resignation. It requires motive, opportunity, actor-known facts and a supported action adapter. A model cannot spawn stolen gold, reveal unknown secrets, summon enemies or instantly attack by prose. Legal, faction and witness consequences follow actual effects. Ordinary mercenaries do not randomly backstab solely because an arbitrary timer fired.

## 6. Paid companion contract

Support non-quest hireable fighters, archers and mages as planned roles. Validate actual skills, equipment and resources; a role label does not grant attacks, spells or abilities. Existing quest exclusions and role eligibility rechecks remain in force.

A contract records stable parties, role, covered services/commands, rate, billing origin/period, funded interval, risk scope, expedition/end conditions, rest/resupply expectations, reserved obligations, termination/refund policy, state/reason, and authoritative ledger references. Proposal, accepted reservation, funded service and execution are distinct.

Recommended initial billing policy, pending balancing: prepay one 24-game-hour service block starting at the agreed actual service start, not each midnight. Accepting a proposal reserves its funds; it does not start unavailable service. Renew at the exact boundary only when player authorization and funds permit. All invoicing is explicit; never silently debit another period. Other terms may be negotiated later through typed supported options.

More precise proposed terms: acceptance reserves funds; the funded clock starts at the agreed actual ready/rendezvous service start, not while an unavailable NPC is still approaching. A current block does not by itself authorize another debit. Optional standing renewal permission must be an explicit contract field, revocable prospectively. Active agreed availability, including agreed rest, earns wages through elapsed game time even while the scene is unloaded; wall-clock pause earns nothing. Explicit suspension earns nothing, but does not silently extend the fixed expiry; negotiate an extension separately. Termination/death stops accrual at its event time, before return-home travel or cleanup. With this prepaid default, nonpayment means failed renewal at expiry, not an invented mid-block wage debt.

Reserve the player-debited payment in contract escrow. Accrue earned wages monotonically with eligible elapsed service time and settle at meaningful events; release earned amounts to the NPC wallet and refund the unearned remainder on early NPC withdrawal. Keep fractional accrual internally and a deterministic integer rounding policy that conserves gold. A departure at half a fully available prepaid period normally refunds approximately half, subject to agreed rounding. Full refund is possible before any service was earned; no duplicate refund or refund of already-earned wages. The model cannot calculate or transfer currency by prose. Rest/temporary interruptions count as service only as explicitly agreed.

Use cumulative earned gold = floor(block price * eligible service seconds / 86400), clamped to the block price. Transfer only the difference from already released cumulative earnings, not a newly rounded amount per tick. Keep fractional progress until block settlement; refund the exact integer remainder. Funding, release and final settlement each require atomic revision-bound ledger receipts; final status cannot commit separately from its ownership transfers. Terminal escrow must be zero. Restore the entire ledger with its save branch.

| Termination reason | Initial proposed settlement |
| --- | --- |
| Canceled before service starts | Return all reserved funds; earn zero. |
| NPC resigns, emergency withdrawal, or newly excluded quest actor | Earn through the actual termination time; refund unused escrow. |
| Player dismissal or abandonment | Same earned/unearned split; no unspecified penalty or confiscation. |
| Natural expiry or declined/unfunded renewal | Settle the existing block; no future debit or uncontracted free service. Any suspended/unearned remainder is refunded. |
| Companion death | Earn through death; put earned owned wages in the actual corpse/estate inventory, refund unused escrow. No transfer to a destroyed transient actor. |
| Employer breach/abuse | Stop unsafe work and settle the same split; any different penalty or dishonest retention is a separately agreed or explicitly executed breach, never an invented ledger exception. |

If the payer dies or cannot receive funds immediately, preserve a claim against the payer's stable estate/inventory; never discard the refund or issue it again on reload. Test the exact boundary at 24 hours as well as one simulation step before/after, including suspended intervals and multiple renewals.

The initial honest settlement policy is not permission to invent dishonest refund avoidance. A future dishonest companion retaining escrow needs an explicit permitted theft/breach outcome and actual ledger ownership transition, with consequences. If a contract intentionally pays the whole wage immediately instead, refund is a real debt limited by resources; it must not create gold. Pick one native policy before implementation.

An active funded contract changes duty priority. Accepted ordinary role tasks within scope do not require fresh friendship-level approval for every enemy or movement command. Low initial trust or a passing bad mood must not silently revoke paid service. Severe deterioration, abuse, nonpayment, exhaustion, family emergency or extreme danger can trigger warning, negotiation, withdrawal or immediate flight according to the terms and personality. Refusing a particular out-of-scope order does not automatically cancel employment.

Hire consent covers only the specified class services and agreed risk/target categories. Protected people, explicit personal red lines, theft/crime outside agreed supported terms and materially expanded work remain separate checks. Do not ask for model consent on every ordinary strike, but do not interpret a paid combat role as permission to attack anyone the player names.

Distinguish temporary tactical retreat, rest, leaving a completed expedition and permanent resignation. On termination: stop accepting new duties, finish/cancel owned actions safely, settle exactly once, notify the player with a grounded reason, and enter a persistent returning-home state. Return to the NPC's actual home, not a travel inn. Escape danger before routing home when necessary. Unreachable home/scene unloading must retain a resumable return or safe waiting fallback, never teleport, erase or duplicate the person. Home arrival resumes a feasible schedule.

The contract must explicitly authorize a bounded expedition override of ordinary daily outings and usual nightly home return. Otherwise the existing casual-companion time boundary would make dungeon hiring unusable. Keep essential family/service/quest obligations as admission constraints or explicit interruptions. Support rest and return planning; do not force hired dungeon companions to leave at the first routine bedtime or suspend their lives indefinitely.

Existing accepted appointments cannot be erased by hiring: reject conflicting dates, negotiate a permitted reschedule with affected parties, or restrict contract availability. A newly imposed quest/service prohibition suspends incompatible work immediately and triggers safe settlement/termination; a genuine family emergency may interrupt according to terms. Return-home planning considers outstanding accepted appointments and survival constraints rather than deleting them. Initial home-return recruitment requires a verified persistent home; actors without one remain ineligible until a separate typed return-destination policy is implemented.

Combat reactions and movement remain native, resource-checked controllers. No LLM call per attack or frame. High-level role/goal/risk changes can use event-driven bounded decisions. Unsupported combat, unloaded combat and route transitions remain unavailable until their own adapters pass acceptance.

## 7. Migration and integration contracts

- Reuse existing WorldStateStore, ConversationAttitude, ConversationMood, action/task receipts, reservations, memory archive and snapshot bindings. Add versioned relationship/contract components rather than scatter new flags among dialogue windows.
- Keep existing disposition and friendship ledger intact. Initialize unknown trust/confidence neutrally and preserve native relationship baseline; do not convert accumulated affection into invented verified loyalty/trust.
- Existing scalar mood remains usable. Additional affect dimensions require a measured gameplay need and migration; no compulsory emotion simulator is proposed.
- Provide one immutable motivation projection for social voice, willingness, action voice and future contract policy. Route classification remains a narrow speech-act task; it does not require every private trait/secret.
- Define proposed record responsibilities before implementation: PersonalityProfile, DirectedRelationshipState, MotivationSnapshot, DecisionPolicyProfile, DecisionOutcome, CompanionContract and CompanionWageLedger. Names are planning concepts, not existing APIs.
- Save/load must retain evidence IDs, relationship direction, contract obligations, next billing point, accrued earnings, settlement receipts, warnings and return-home progress. An interrupted conversation or model-service outage cannot cancel a funded job or transfer money.
- Make schedules aware of admitted contract reservations; reconcile only missed feasible routine stops after release. Keep true terminal outcomes distinct from waiting for a supported adapter or a route.

## 8. Acceptance matrix and delivery order

1. Relationship foundation: same disposition with different evidence-based trust; same mood with different personality; same person over time/reload; unknown trust; independent positive/negative updates; existing friendship caps; no event replay or save-branch leak.
2. Voice and willingness: cold disliked stranger versus angry friend; high trust/low affection versus warm unreliable acquaintance; neutral refusal without relationship penalty; same task voluntary versus funded duty. Preserve uncertain facts and exact target/parameters.
3. Duty policy: ordinarily feasible contracted combat/following continues with moderate low mood/trust; hazardous but agreed dungeon work remains in scope; impossible resources, explicit red line and suicidal orders cannot be bought away. Refuse one order without necessarily resigning.
4. Information choices: truthful belief, mistaken belief, deliberate permitted lie, protected secret, partial disclosure and refusal remain distinguishable in memory. No private/quest leakage and no unearned trust from repeating claims.
5. Contracts/economics: exact funding, refused hire without debit, 24-hour boundary, multi-day waits, insufficient renewal funds, termination before service, partial refund, full earned day, rest terms, integer rounding, death, dismissal, scene transition, restart and divergent-save replay. Player debit equals remaining escrow plus NPC earnings plus refunds, adjusted only by explicit real subsequent spending or permitted breach.
6. Companion lifecycle: each class's genuine combat resources and target constraints, tactical retreat versus resignation, agreed night expedition versus casual home return, home unreachable, new quest binding, player abandonment, major abuse, warning persistence, no frame-by-frame resignation roll, and at-most-once settlement.
7. Betrayal is a later independent capability: test credible motive/opportunity, no arbitrary low-mood hostility, permitted effects only, theft ownership, witnesses/consequences and discoverable attributed memory before exposing it to live decisions.

Implement the common data/projection and action-specific policy seams first; then relationship appraisal and disclosure; then the contract ledger; then combat-role adapters and lifecycle; then deliberate betrayal. Each slice requires Core invariants, frozen model contrasts and owned native acceptance before becoming a claimed feature. Numeric defaults, prices, warning durations, rest terms and betrayal frequency remain calibration choices rather than silently fixed rules.
