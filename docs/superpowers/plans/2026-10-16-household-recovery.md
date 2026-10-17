# Persistent household recovery

Latest checkpoint, 16 October: history, migration, persisted reservations, runtime activation and native bank listing/purchase adapters are implemented and pass the combined 573-test core suite and Unity build. Earlier foundation-only status below is historical. Native gameplay acceptance, release of deceased native bed/worksite claims and smooth crowd refresh during replacement remain open.

This implements the approved vacancy requirements in `../../VACANCY-AND-RECOVERY.md`. It does not redefine an empty rendered interior as an extinct household. Latest user rules override the old goal summary: one occupant per bed; player-caused deaths may break quests; no artificial random enemy spawner.

## Contract

Every permanent household member, including a native keeper whose home is elsewhere than their work, must be confirmed dead before recovery is scheduled. Living travelers and absent residents retain ownership. Preserve old names, relationships, corpses, quest references and journey history. A replacement is a new family with new persistent IDs, never resurrection or silent reassignment.

Choose a fixed deterministic deadline once: dense cities 3–7 game days; ordinary settlements 7–30; isolated hamlets bias late. Initial isolated policy is 24–30 days, an implementation default within the user's approximately 30-day requirement. Runtime classification must use actual settlement/isolation evidence. Settings or reloading must not reroll an existing decision.

## Implementation sequence

1. Add and test a pure recovery policy with explicit eligibility facts, fixed deadline calculation, and activation gates. This is a foundation only; it must not spawn families until identity history and persistence are ready.
2. Add generation-aware IDs and a historical household ledger. Keep exactly one current household per building. Identity resolution for alarms, relationships, quest bindings and trips must continue finding retired people. Preserve generation-zero IDs and legacy saves unchanged.
3. Persist extinction/recovery decisions and native bed reservation lifetime. Dead native keepers retain historical identity but no longer reserve a bed for current occupants. Missing legacy death timestamps leave recovery dormant rather than guessing.
4. Integrate DFU property eligibility: surveyed residential buildings only, positive known bed count, complete population, no player ownership, faction claim, active quest placement or native keeper worksite. Recheck at activation; do not overwrite newly purchased/protected property.
5. Atomically archive the old family and create one new generation after the deadline, with the player outside and corpses cleared. Refresh schedules and identity caches without touching historical quest contracts. Retain vacancy when blocked.
6. Verify save/load during delay and after replacement, no duplicate generations, new IDs/surnames, one resident per bed, living absent-owner exclusion, corpse/player/quest/property gates and different settlement delays in core and native tests.

## Related work and limits

16 October generation factory checkpoint: `HouseholdGeneration.Create` preserves generation-zero identities and appearance exactly. Positive generations use deterministic fresh appearance/surname seeds and generation-qualified resident IDs, with partner/parent links remapped within the new family. It requires surveyed beds and respects one resident per bed. Core tests pass 542/542 (`work/HouseholdGeneration-core.log`). This is not the historical ledger or replacement transaction; no runtime caller activates replacements yet. Next implement the historical ledger and validated save migration before connecting the factory to vacancy recovery.

Bank listings must eventually use real DFU ownership APIs; a fake listing is not an acceptable substitute. This initial implementation sequence covers family replacement and safe vacancy only. Player-caused quest deaths versus ambient protection need their own verified policy change; they are not a reason to rebind an active quest to a replacement family.

Current runtime has individual deaths and corpse/crime-scene cleanup. It does not yet have household generations, replacement immigration or bank listings. Core-policy tests alone will not establish runtime completion.
