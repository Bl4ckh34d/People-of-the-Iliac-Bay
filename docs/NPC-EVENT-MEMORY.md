# NPC events, memory and personal prices

This extends the existing native experience/appraisal pipeline and conversation journal. It does not replace native combat, commerce, perception or the month-gated conversation compaction service.

## What an NPC can remember

| Source | Remembered facts | Limits |
| --- | --- | --- |
| Confirmed personal trade | Actual item names/quantities in both directions, integer net gold, comparison with the opening quote | Written after a successful transfer receipt. Cancellation explicitly records no transfer and an unknown reason. |
| Native health episode | Injury/healing magnitude; player healer when the observed native treatment proves it | Ordinary sampling does not invent an attacker or healer. |
| Native poison admission and condition binding | Poison onset, illness onset, disease/poison improvement | A change in condition alone does not identify its cause. A successful observed player treatment can identify the healer. |
| Native money/item/gift receipt | Actual transferred quantity and known giver | Existing source admission remains mandatory; model claims cannot mint a transfer. |
| Native appointment/obligation/combat-help receipt | Attended/missed agreement, fulfilled/broken obligation, received combat assistance | Only producers that already confirm these outcomes feed the bridge. Declaring an event kind does not create a new game action. |
| Direct civilian perception | Assault, hostile threat, witnessed killing, unauthorized building entry | A reported crime or later corpse discovery is not proof that the NPC saw the killer. |
| Confirmed enemy attack on a resident | Native attacker name/type, victim and location | Motive remains unknown. Existing exterior attack callback supplies the witnesses. |
| Guard pursuit | Visible guard chasing the visible player | Pursuit is not proof of the underlying offense. |
| Native shop receipt | Witnessed purchase/theft; personally completed ambient purchase or browsing without purchase | Witnesses do not learn prices merely by seeing a customer. |
| Actual street conversation | Partner identity and location | Mere nearby presence does not establish a conversation. |
| Closed shop diversion | Original closed shop, actual current activity, nearby conversation partners, intended return | Live context distinguishes approaching, talking, browsing, visiting an Inn and returning. |
| Inn company | Up to five actually visible nearby people | Incidental company does not imply they spoke or know the player's business. |
| Observed player health change | Injury worsening or improving while continuously visible | Sampling is reset across invisibility, long gaps and time skips. No invented cause. |
| Visible player spell cast | That magic was cast | Effect, target, spell identity and motive are not inferred from casting alone. |
| Actual dungeon presence | Resident body present in a dungeon, and player company only when seen | Does not add dungeon-following support to actions that explicitly do not support it. |
| Completed cash errand | Having run out of spending money, collecting gold at home/bank, continuing plans | Live bank/partner waiting context remains separate from past recollection. |
| Accepted conversation/action outcomes | Exact shared dialogue, threats/insults and completed/failed/stopped actions | Existing journal, semantic appraisal and compact recall retain their authority. No keyword classifier. |

Observers must be alive, awake and present. New scene perception uses distance, forward-facing visibility and native line of sight. It runs in bounded/staggered samples rather than scanning every entity every frame. Facts carry the affected person separately from the known responsible person. Unknown facts remain unknown.

Mobile and ordinary stationary scene identities share this store. A new generation of a hostile incident is a new event, while repeated sightings of the same generation do not renew it. Same-moment native confirmation of the killer can refine an eyewitness record without renewing its age. Seeing the body later records corpse discovery, not a witnessed killing or knowledge of its perpetrator. The transient eyewitness lookup ages out old samples instead of retaining every encounter forever.

## Evaluation and forgetting

Every scene record carries an integer evaluation from **−5 to +5**. Harm to oneself is negative; harm to a liked player is negative, and harm to a disliked player can be positive. Established family ties and strong resentment also affect witnessed outcomes. Neutral events remain possible. Source-backed native appraisals refine the remembered evaluation when committed; they remain the only owner of their mood/relationship changes. Trade offer mood effects keep their existing exactly-once receipt/offer boundary.

Retention is deterministic for an event and observer, so saving/loading or reading memory never rerolls it. Casual memories usually last **30 minutes to four game hours**, with a small long tail reaching a day. The distribution favours longer durations. Event importance and emotional magnitude increase retention to days or weeks. Witnessed murder lasts at least **35 game days**, with variation extending to months. Ordinary trade details last through the current day; exceptionally emotional trades can remain much longer.

Each NPC retains at most **32 factual scene records**. Expired facts are removed before low-importance old records are displaced. Recall is bounded, prioritizes same-day player interactions and clearly labels these as past observations. Conversation history uses the existing summarization/compaction system; transient scene records are not copied into an eternal transcript that would defeat forgetting. The scene record itself is a compact factual summary, not a new LLM-generated archive.

## Personal prices

The baseline uses DFU `CalculateCost` and `CalculateTradePrice`, including native Mercantile, Personality, regional cost, condition and quantity. Ordinary residents use neutral merchant quality **10**; an actual shop with a different quality can therefore have a different baseline.

The social adjustment is capped at **45%** in either direction. Reliability trust contributes up to 20 percentage points and liking up to 15. Familiarity gradually amplifies that bond by up to 30%, requiring both elapsed shared history and substantive exchanges. Mood and remembered conduct by the player contribute up to three points each. Being pleased at the player's misfortune does not count as the player having treated the NPC well.

The adjustment is captured for the trade session; previewing, bargaining and reopening an amount field do not reroll it. Native item/wallet authority is still revalidated at commit. Gold is integer-only. Valid native potions use their recipe price: Stamina has a **25 g** base. The old generic “glass bottle” template was actually an uninitialized potion; mundane starting goods now use a glass jar, and invalid potion recipes cannot be traded.

Buy and sell quotes are calculated together for the same item and quantity. The socially improved buyback cannot exceed the socially discounted purchase price. This prevents an unchanged item from generating gold simply by reversing a trade at the same skill/social state; changing item condition, skill, quantities or social state remains a separate acceptance concern. The core regression reproduced the old 18 g purchase / 26 g resale at maximum friendship before the repair.

## Waiting behaviour

Closed-shop choices vary reproducibly by resident, destination and opening: small conversations, storefront browsing, short walks/pauses and feasible nearby Inn visits. Return is spread through the first **45 game minutes after opening**, shortened when the original visit would otherwise end. Existing emergency, medical, cash and currently active shared-family movement has priority. Original destination and diversion timing/routes persist through saving/loading. Occupied activities retain their own facing instead of watching an approaching player.

Browsing currently uses verified storefronts. Native market-stall merchandise anchors are not identified; the mod does not claim a storefront pause is inspection of market goods. Native testing and remaining acceptance are tracked in [TESTING](TESTING.md).

If feasible, an ordinary resident can advance the next already-planned open stop, then return to the original shop. The temporary schedule masks the old overlapping visit so the advanced stop is not visited twice. This reorder is saved and constrained by the original outing's end and service windows. Core tests pass; a complete native reordered outing remains open.
