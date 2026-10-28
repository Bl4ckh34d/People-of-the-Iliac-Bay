# Companion Shield: actual protection and damage acceptance

## Existing flow and remaining defect

The existing native adapter already discovers exact affordable Shield spellbook
entries, binds either self or the actual player touch target, spends the caster's
native magicka and verifies the native outcome through the existing action
contract. Conversation admission and willingness remain with the existing
semantic action system. The change does not add a protection flag, teach spells,
grant concurrent Follow/CastSpell actions or train ordinary residents.

Both magic adapters previously measured Shield only by remaining rounds. Native
`BaseEntityEffect.GetMagnitude()` in DFU's `EntityEffect.cs` applies
`FormulaHelper.ModifyEffectAmount()` for touch delivery. A saving throw can reduce
the magnitude to zero. `Shield.Start()` then retains that zero strength while the
effect still has rounds. Native `Shield.DamageShield()` passes damage through in
this state. Remaining rounds therefore do not alone establish protection.

`NativeNpcShieldState.ProtectionRounds()` now reads only the exact native Shield
type, requires an unended effect with remaining rounds and positive native
`Shield.SaveData_v1.shieldRemaining`, and supplies both existing adapter readers.
Consequently a spent cast with zero actual strength no longer satisfies the
timed-effect success contract or its successful-use skill award.

## Offline evidence

- `dotnet run --project tests/NativeNpcShieldState.Regression --configuration Release`:
  9/9. The extracted previous duration-only reader first failed the zero-strength
  case. Positive and partly depleted Shield, zero/negative strength, expired and
  ended effects, an unsupported subclass, an absent effect and mixed bundles are
  covered by the production reader. The test replaces only native effect storage;
  it does not prove native casting, damage or serialization.
- `dotnet run --project tests/NativeNpcKnownSpellBinding.Regression --configuration Release`:
  3/3, including restored own-skill cost mode.
- `dotnet run --project tests/NpcMagicNativeGate.Regression --configuration Release`:
  12/12.
- `dotnet run --project tests/PeopleOfTheIliacBay.Tests --configuration Release`:
  2179/2179, zero failed, on the concurrent working-tree snapshot.
- `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --configuration Release --no-restore`:
  zero warnings, zero errors against installed native assemblies.

## Native acceptance prepared, not executed

`NativeNpcCompanionCastProbe.VerifyShield()` now calls the actual recipient's
`Entity.DecreaseHealth()` rather than calling `Shield.DamageShield()` directly.
It requires unchanged HP and exact strength loss for an absorbed hit, then breaks
the restored NPC Shield with an extra one-point overflow. It requires only that
overflow to reach HP and requires the broken Shield to disappear from the saved
condition packet. The real player-touch recipient receives a separate native
damage check. Existing exact magicka, native round/strength restoration,
independent snapshot timestamp, offscreen aging and expiry checks remain.

Use the existing isolated `companion-health` roadmap fixture with
`POTIB_COMPANION_CAST=1`. A successful Shield marker now includes
`nativeEntityDamage=true`, `nativeOverflow=true`, `brokenClear=true` and
`playerEntityDamage=true`. Touch casts retain native saving throws: a native
zero-effect result is not evidence of successful protection and must not be
relabeled as a successful Shield cast.

No Unity session, Player session, package build or deployment was run for this
slice. Root owns the serialized native test slot. This source check does not
close free-dialogue, scene-transition or actual save-file gameplay acceptance.
