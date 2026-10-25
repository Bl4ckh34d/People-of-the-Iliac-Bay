# Regional voice direction proposal

**Status: proposal for listening; not canon and not implemented.** Frame: *Daggerfall*, 3E 405. This uses period-appropriate Iliac Bay text and local *Daggerfall Unity* geography/climate data. It does not borrow post-Warp political borders or 4E assumptions. None of the cited lore defines spoken accents; accent names below are real-world performance references chosen for this project, not claims about Tamrielic phonology.

## Keep three controls separate

1. **Home-origin dialect:** choose from a stable recorded birthplace, childhood home, or permanent home settlement, in that order. Resolve the settlement's own `DFLocation` and `Climate.WorldClimate`; persist that origin. Travel, current inn, weather, NPC race, and dialogue text never reroll the accent. When origin data is absent, use the project's neutral Southern English fallback.
2. **Race timbre:** use the separate race voice-design cue. It can affect resonance, texture, and register. It must not select an accent. A Redguard from a mountain settlement can have a Highland Scottish dialect and a deep, dark, resonant timbre. Khajiit do not get an Arabic accent by default. Any Orc roughness is timbre, not automatic anger or menace. Keep gender presentation audible where requested; a lower register does not make a female voice masculine.
3. **Personality and mood:** continue using the structured resident snapshot for temporary expression. Fear, resentment, trust, disposition, and traits alter delivery/prosody only; they must not change the saved profile, reference audio, voice seed, home accent, dialogue text, facts, goals, or disclosure rules.

## Accent palette to audition

These are eight reusable families. Choose only from explicit home-origin geography/culture; do not map an entire race or province to one accent. A sample count or profile label is not evidence that an accent is audible; decide by listening to actual backend output.

| Family | Geography-led assignment examples | Performance direction |
| --- | --- | --- |
| Southern English | Urban/trade homes in Daggerfall, Wayrest, Anticlere, Bhoriane, and similar settled centres | Clear contemporary Southern English; neutral reference, not aristocratic by default. |
| English north | Inland northern High Rock homes in Northmoor, Glenumbra Moors, Shalgora, and comparable locations | Restrained northern English reference; keep it distinct from Highland Scottish. |
| West Country English | Southwest High Rock homes with western/coastal or rural settlement geography; choose from the exact home location, not all of Daggerfall or Wayrest | Light West Country English; relaxed vowels, no comic spelling or caricature. |
| Welsh English | Wooded upland homes, with Dwynnen and Glenpoint as candidate areas | Light Welsh English lilt and rhythm; keep consonants and words intelligible. |
| Highland Scottish English | Homes whose exact location is mountainous, including candidate settlements in Dragontail Mountains, Ephesus, and Wrothgarian Mountains | Restrained Highland Scottish English; do not turn it into Scots unless the character actually speaks Scots. |
| Irish English | Confirmed island or exposed western temperate-coast home; Betony and Balfiera are small-sample audition candidates | A light Irish English variety; do not use one accent for every coast or island. |
| South African English | Dry southern/desert home locations, including Alik'r Desert, Antiphyllos, Bergama, and Dak'fron | A specific South African English reference (first audition: Cape English), never a generic “African” voice. This is a creative performance analogy, not a claim about Tamrielic speech. |
| Kenyan English | Tropical/rainforest home locations, including Totambu, Kozanset, Mournoth, Lainlyn, and Satakalaam | A light Kenyan English reference; never attach it to a race. This is a creative performance analogy, not a claim about Tamrielic speech. |

These province names are audition candidates, not blanket province assignments. Resolve the resident's saved home settlement first; its local climate and terrain may refine the broad family, and an origin with no reliable geography stays on the neutral Southern English fallback. In particular, don't infer a tropical voice from a southern province name or a desert voice from Redguard identity. A known migrant keeps their recorded home origin; if a young migrant has no authoritative home origin, use the fallback instead of guessing one.

## What the sources support

The in-game book **Holidays of the Iliac Bay** treats Breton and Redguard traditions as distinct, describes Hammerfell as arid and mountainous, and distinguishes local communities including the Banthan jungle, the Alik'r desert, Sentinel, and Abibon-Gora. The in-game **Etiquette With Rulers** says High Rock's peerage customs vary by region. **The Alik'r** contrasts the Imperial City's urban life with desert life and describes a Redguard poet writing in Old Redguard. These support local identity and geographic variation; none states that any group speaks with a British, African, Irish, or Scottish accent. The Redguard game's Stros M'Kai dialogue also describes a busy port with sailors from many places; treat that as historical port context, not evidence for a Daggerfall-era accent or political assignment.

The local `Daggerfall Unity` source exposes 62 region names and a separate `regionRaces` table used for townsfolk name-bank selection, not an individual's race. `MapsFile.ReadClimatePoliticData()` obtains climate from a location's map coordinates; `GetWorldClimateSettings()` maps raw climate values 223–232. I decoded the climate at the actual named settlement locations in `MAPS.BSA` to check the proposed geography. The resulting examples show why the settlement, not the country label, must drive the choice: Daggerfall settlements are 322 Woodlands and 78 MountainWoods; Wayrest has 253 Woodlands and 47 MountainWoods plus a few other climates; Sentinel has 145 Subtropical and 24 Desert; Lainlyn has 95 Rainforest and 10 Mountain. All 95 Dragontail Mountains settlements and all 37 Ephesus settlements are Mountain; Wrothgarian Mountains has 236 Mountain and one Woodlands settlement. Alik'r Desert has 42 Desert and one Mountain settlement. Totambu, Kozanset, and Mournoth settlements are entirely Rainforest in this dataset. Betony (3 settlement points) and Balfiera (6) are too small for strong statistical claims, so they are only coastal/island audition candidates, not evidence for a universal island accent.

Climate is a game landscape class, not a direct measure of real-world climate, elevation, culture, or accent. `WOODS.WLD` height values are relative byte codes, not metres; the coast flag is an eight-neighbour adjacency hint, not a measured shoreline distance. Some region indexes have no political-map cells, and three High Rock sea-coast map rows are dummy ship/dungeon coordinates; exclude those from home-origin assignment. Bantha appears in the book and region-name table but has no cells in the parsed politics grid, so do not invent a home-settlement climate for it. These are geography checks for this listening proposal, not canon statements about speech.

## References

- Daggerfall in-game text, local source: `../../daggerfall-unity/Assets/StreamingAssets/Text/Books/BOK00033-LOC.txt` (*Holidays of the Iliac Bay*, Theth-i), `BOK00061-LOC.txt` (*A History of Daggerfall*), `BOK00079-LOC.txt` (*Etiquette With Rulers*), and `BOK00085-LOC.txt` (*The Alik'r*).
- [Holidays of the Iliac Bay — Daggerfall book transcription](https://gamefaqs.gamespot.com/pc/29065-the-elder-scrolls-chapter-ii-daggerfall/faqs/81144/holidays-of-the-iliac-bay)
- [Etiquette With Rulers — Daggerfall book transcription](https://gamefaqs.gamespot.com/pc/29065-the-elder-scrolls-chapter-ii-daggerfall/faqs/81144/etiquette-with-rulers)
- [The Alik'r — Daggerfall book transcription](https://gamefaqs.gamespot.com/pc/29065-the-elder-scrolls-chapter-ii-daggerfall/faqs/81144/the-alikr)
- [Favis — UESP transcript of *The Elder Scrolls: Redguard* dialogue](https://en.uesp.net/wiki/Redguard:Favis)
- Local map implementation: `../../daggerfall-unity/Assets/Scripts/API/MapsFile.cs` (`regionNames`, `regionRaces`, climate enum, coordinate lookup, and climate mapping) and `../../daggerfall-unity/Assets/Scripts/API/PakFile.cs` (PAK row decoding); local data: `../../GameFiles/arena2/CLIMATE.PAK` and `POLITIC.PAK`.
- Audited location-level exports (derived from local `MAPS.BSA`, `CLIMATE.PAK`, `POLITIC.PAK`, and `WOODS.WLD`): `../work/tts-regional-geography/dfu-all-location-climate-topography.csv` and `dfu-region-climate-topography.csv`.
- [Daggerfall Unity MapsFile.cs](https://github.com/Interkarma/daggerfall-unity/blob/master/Assets/Scripts/API/MapsFile.cs)
