# Tavern lodging and travelers

User amendment on 14 October 2026. The initial suggestion to keep one bed free was explicitly withdrawn. Do not reserve a guaranteed player bed.

Travelers have homes outside the current settlement. They travel into town, pay for one-night or multi-day lodging, visit local destinations during their stay, and return to the road when the visit ends. Local residents may visit taverns socially, but should not be generated as ordinary overnight travelers merely to fill rooms. Keep traveler identities, destinations and progress deterministic across unloaded regions.

Paid bookings reserve a room or its allocated sleeping places for a defined interval, including when the guest is outside. Physical NPC presence alone is not the booking record. First come, first served: check availability and commit the reservation atomically so player and NPC requests cannot double-book. Do not evict an existing paid player booking to make room for simulated guests.

Rooms occupied/reserved by another guest cannot be used by the player for sleeping. Show a clear occupied-room/bed message. Exclude all beds belonging to an exclusively reserved room from other bookings; model actual room grouping rather than treating every bed in one room as a separate private room. Household bed sharing does not automatically authorize strangers sharing a paid private room.

When fully booked, the keeper refuses the new rental and reports the earliest time an eligible room becomes free, calculated from active reservation checkout times. Use the game's calendar and check requested stay duration, not merely whether a room is empty at this instant. Future extensions of stays must respect already accepted reservations. Checkout releases occupancy; cancellation, death, missing/inaccessible room and mod-layout changes need explicit reconciliation without silently erasing payment or player entitlement.

Persist traveler ID, tavern/room identity, check-in/out times, payment/booking state, party membership where applicable and current visit schedule. Save/load and time skips must not duplicate travelers or leave permanent stale reservations. Reconcile vanilla player room rental, rest markers, quest tavern NPCs and room allocation before replacing those behaviors.

Delivery remains future work within civilian schedules/nightlife and travel integration. Expose settings for traveler demand, stay lengths and lodging behavior when implemented. Test no vacancies, competing requests, day rollover, multi-day stay, guest out during paid stay, player rental, early/late checkout, unloading/reloading and earliest-vacancy dialogue.
