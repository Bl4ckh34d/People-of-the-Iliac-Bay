# Personal trade fixes and cash errands

The user requested the following changes on 26 October 2026. This checklist is the acceptance scope, including later clarifications.

- One item entry per painted slot, independently of Enhanced Item Lists.
- Trading parchment back must not invalidate the basket. Its native zero value remains zero, including a one-way trade without gold.
- Hide keys, equipped clothes and other items rejected by the live staging predicate.
- Both money fields open an amount dialog. NPC PRICE means the NPC pays the player; YOUR OFFER means the player pays the NPC.
- Only integer digits are accepted. Use a short label, colon and visible gap; remove signed-number instructions.
- Show a single total in the payer's field, formatted `30 g`. Remove direction captions and redundant status totals. A zero balance shows `0 g` under YOUR OFFER.
- Increase the amount font while reserving room for five digits, a space and `g`; fit larger valid balances without clipping.
- Show the same NPC portrait at the upper left, beginning with the dialogue portrait's current emotion.
- Reuse the dialogue renderer, blink clock, idle gaze and emotional idle mouth. Keep animations active behind amount dialogs.
- React only to submitted offers, not typing or redraws. Favorable offers look happy, unfavorable offers sad, and sufficiently large disadvantages angry.
- Apply general mood nudges once per offer, without replacing existing mood or applying a second reward at commit.
- The final clarification selects full **50% steps**, based on the NPC's current quoted net amount. Expected 5 g, offered 10 g gives +2; offered 20 g gives +6. Sub-50% changes can alter expression without a general mood nudge. Zero-price baskets have no percentage baseline and no proportional nudge. All gold remains integer-valued; existing mood bounds still apply.
- Trading and corpse loot use the same saved wallet.
- An empty wallet causes a physical cash errand at the next free, awake opportunity: an open bank when the settlement has one, otherwise the resident's home. A closed or unreachable bank does not silently become a home withdrawal.
- Refill the resident's usual seeded pocket-money amount only after arrival and a short service dwell. This uses the existing abstract personal finances; it does not add an economy or bank-account simulation. Preserve pending visits across saves and prevent duplicate withdrawal.
- In a bank, walk to a native bank clerk, face the clerk for 8–15 real seconds, then walk to the exit and resume the routine. Reuse the existing physical interior follower and saved remaining-route representation.
- A resident with an empty wallet must collect money before shopping or visiting an Inn. Closed banks keep this need pending at home; ordinary and shared household outings cannot bypass it.
- A family cash errand pauses the shared outing rather than cancelling it. One partner collects money and returns to the retained rendezvous; the other waits. Preserve an existing Inn seat. Already paid visits may finish before another cash errand.
- The waiting NPC's current dialogue context states that their partner is completing an errand before joining them. Use the existing verified journey context; the dialogue model supplies the actual answer, without a keyword-triggered canned response.
- Update README/testing evidence, commit and push, then integrate into main. Never replace the mod while the user's game is running.

Runtime support for cash errands uses existing managed adult resident plans. Fixed native service/decorative NPCs have no mobile routine owner and cannot be claimed as physically visiting a bank. Their broader movement coverage remains part of the roadmap.

Implementation reuses native item eligibility, signed internal basket accounting, the existing portrait renderer, saved wallet transactions and resident movement/routine recovery. Native acceptance is separate from compilation and offline checks.
