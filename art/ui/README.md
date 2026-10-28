# Personal trade window

`personal-trade-background-v2.png` is the generated background used by the personal trade window. Its identical packaged copy is `mod/Textures/personal-trade-background.png.bytes`.

- NPC inventory is on the **left**, player inventory on the **right**.
- The mixed item basket has one signed net gold balance. `NPC PRICE` and `YOUR OFFER` show the quoted total and the player's counteroffer, not separate purchase and sale subtotals.
- Only `OFFER`, `CLEAR`, and `EXIT` are action buttons. Clicking the `YOUR OFFER` field opens the gold editor.
- Price fields are light gray with no deep recessed shadow. Amounts and payment direction are rendered by the game in dark text.
- `PersonalTradeWindow` uses a 188×240 native canvas with point filtering. Native item scrollers fill each inventory column; transparent button hitboxes follow the artwork.
- Native DFU Mercantile/Personality item prices and the existing disposition/personality adjustments remain the baseline. Saved reliability trust and current mood modify the personal bargaining margin and counteroffer steps; Streetwise/Etiquette do not affect prices. Unknown trust is neutral. A basket's initial price limit stays fixed until its item selection changes.
- Trust and mood are weighted fractionally, without intermediate integer buckets. Only the resulting gold amounts are rounded; small changes may therefore retain the same visible price on inexpensive items.

Generated with the built-in image-generation tool from the user's cropped `download.png` reference and the first trade-background draft. No desktop control was used to display the artwork.

## Final edit prompts

Simplification: preserve the gray stone, two empty inventory columns, scroll tracks and yellow bitmap lettering. Remove BUY, INFO and SELL and their frames. Keep only OFFER, CLEAR and the red EXIT. Replace BUY PRICE/SELL PRICE with NPC PRICE/YOUR OFFER; give both fields light/medium gray interiors, a thin border, no black fill, inner shadow or drop shadow. Leave fields empty for runtime values.

Ownership correction: change only the top captions to NPC ITEMS on the left and YOUR ITEMS on the right; preserve the remaining layout and appearance.
