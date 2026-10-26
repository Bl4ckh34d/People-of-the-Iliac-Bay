# Companion spell costs after body reconstruction — 26 October 2026

## Verified defect and bounded repair

Shield casting and native Shield persistence already exist in the current source. This change repairs a remaining resource constraint rather than introducing another protection system.

DFU's `EntityEffectManager.UsePlayerCharacterSkillsForEnemyMagicCost` defaults to `true`. `NativeNpcStartingSpells.EnsureKnownSpells` sets it to `false` when it generates a managed magical adventurer's starting book. A rebuilt resident restores its saved book through `NativeNpcKnownSpellBinding`, however, and `NativeNpcVitalsBinding` then skips starting-book initialization. The rebuilt manager therefore previously retained player-based spell costs. This could change Shield affordability and magicka costs merely by reconstructing the same resident.

`NativeNpcKnownSpellBinding.BindTo` now establishes the resident's own native skill cost source before binding or restoring the exact book. It requires a matching `ResidentActor`, the actor's own entity manager and a non-player manager. Existing alive/adult/quest eligibility remains in force. Player bodies, unrelated native enemies without a resident component, mismatched entities and quest-protected residents are excluded. The change does not teach spells, replace authored books, add effects, spend magicka or bypass native spell formulas.

## Offline evidence

Command:

```powershell
dotnet run --project tests/NativeNpcKnownSpellBinding.Regression --configuration Release
```

Before the production repair, the linked real binding fails all three cases:

- A saved Shield book restored onto a fresh body switches to player skill costs.
- An existing managed spellbook retains player skill costs.
- A player body can enter the ordinary resident binding.

After the repair, all three cases pass. The exclusion case checks player, mismatched manager, mismatched resident, quest-protected and unmanaged bodies. These tests run the production binding and real `WorldStateStore`; small engine-host, native book-storage and JSON serialization doubles replace Unity-only boundaries. They establish binding and cost-source behavior, not native Shield damage or exact native serializer behavior.

`dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release --no-restore` also passes with zero warnings and zero errors against the installed DFU/Unity assemblies. Scoped `git diff --check` reports no whitespace errors.

## Native acceptance still required

The existing `NativeNpcStartingSpellsProbe` now resets the manager to DFU's fresh default, re-enters the production binding and asserts that its own-skill cost mode returns before restoring the book. The probe preserves the existing native template, exact book and resource assertions. Its success marker gains `ownSkillCostRestored=true`.

No Unity session, Player session, deployment or native probe was run for this slice. The native probe simulates the manager default on the existing actor; the offline regression separately supplies an entirely new component host after a store snapshot/restore. A normal scene transition or save reload with a real magical companion remains the final gameplay check. Existing Shield effect, damage, duration and persistence acceptance must not be inferred from these cost-source tests.

## Source references

- `mod/Scripts/NativeNpcKnownSpellBinding.cs`
- `mod/Scripts/NativeNpcStartingSpells.cs`
- `mod/Scripts/NativeNpcVitalsBinding.cs`
- `mod/Scripts/NativeNpcStartingSpellsProbe.cs`
- `tests/NativeNpcKnownSpellBinding.Regression/`
- Native DFU `Assets/Scripts/Game/MagicAndEffects/EntityEffectManager.cs`: default cost flag and `SetReadySpell` cost calculation.
