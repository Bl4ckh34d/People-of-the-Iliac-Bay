# Directional civilian animation

User amendments, 14 October 2026, including the expanded all-human coverage requirement. This is an essential part of the current mod, not only future visual polish. No generated sets are implemented or validated yet.

## Required behavior

- Every stationary and mobile adult human NPC appearance needs walking, running, weapon-fighting and spellcasting animations from all viewing angles, plus a sleeping sprite lying in bed. This includes appearances that already have walking animations: walking faster does not satisfy running. Asset coverage is not limited to shopkeepers, noncombatants or the first pilot appearances.
- Children need walking and running from all viewing angles and a sleeping sprite lying in bed, but no battle or spellcasting sets. They move between home and local play groups; adult shopping, work, tavern and inter-settlement travel restrictions still apply.
- Additional child diversity is required: at least three female and three male peasant appearances, plus at least three female and three male noble appearances (at least 12 distinct child appearances total). Each needs its standard flat, idle animations from every viewing angle, directional walking/running and the previously required sleeping pose. Match Daggerfall's pixel resolution, palette, proportions and style across the complete set. These are distinct source appearances, not merely different frames of the same child.
- Any NPC mobilized or made hostile by the simulation keeps the same recognizable appearance. A shopkeeper fleeing a murder visibly runs toward the exit; a civilian fighting the player uses its own weapon or spellcasting animations. Having spellcasting assets does not by itself grant every civilian spellcasting abilities; behavior selects an action consistent with the NPC's capabilities.
- Preserve identity, clothing, face, proportions, scale and palette across idle, movement and combat, indoor/outdoor transitions, hostility changes and save/load. Do not replace the person with a generic fighter or render a moving static flat as the completed behavior.
- Animation changes must not sever service, relationship, inventory-extension or quest bindings. Child mobility does not imply adult combat or adult activities.

## Delivery sequence

User-selected production order: start with sleeping sprites for all NPC flats when implementing actual sleeping in homes and taverns. Validate bed alignment and waking transitions at that milestone, then expand into the full directional sets below. This changes sequencing, not the required movement/combat/child-diversity coverage. Do not delay the current family foundation to generate all animation sets upfront.

1. Export and inspect actual source flats from the installed game. Record archive/record, dimensions, pivot and palette references in an asset manifest.
2. Produce one child standard-flat/directional-idle/walk/run/sleep pilot and one adult walking/running/weapon-combat/spellcasting/sleep pilot from these references. Use generation followed by visual review and frame repair; generation alone does not establish usable animation. Low source resolution is not evidence of fast generation or consistent output. Validate native pixel scale and in-engine appearance before scaling to the 12 child variants and all existing human appearances.
3. Package frames behind an appearance-specific animation mapping. The standard DFU mobile billboard convention uses five drawn views with mirroring for eight directions; check asymmetric clothing/equipment before allowing mirrored views.
4. Verify frame order, transparency, feet/pivot alignment, speed, direction selection and state transitions in the actual player. Review facing-away and diagonal motion as well as frontal motion.
5. Exercise the actual behavior: child goes to a local play group and returns; shopkeeper flees through a real door; a fighting civilian retains its own visual identity. Save/reload and streaming must preserve the chosen appearance.
6. Inventory every stationary and mobile human appearance in the installed game. Track source archive/record or mobile variant, identity, age group, each action's required views/frames, sleeping pose, generation status, visual review and actual-player validation. Expand until every applicable appearance has the required sets. The full requirement is not complete after the pilots or only the appearances currently spawned by the prototype.

Home-only static child prototypes may support family/schema development but must remain labeled incomplete. Spellcasting and sleeping are now explicitly required; the earlier wording treating casting as optional is superseded. Additional corpse sprites and the later inventory mod remain separate recorded scope. Curved path polish remains deferred; functional animated movement is required. Sleeping acceptance requires alignment with a real bed, including a persistent appearance when a sleeper wakes, flees or fights.
