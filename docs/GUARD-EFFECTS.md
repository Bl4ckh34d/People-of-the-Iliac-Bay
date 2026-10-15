# Guard effect persistence

Verified development checkpoint, 15 October 2026. Installed development0.3.34 SHA-256: `ED3A6FB3B446EC51F8245ADA067F8E7790755F86A41FD0BD3F784FAA2FF7CC6B`.

Guard records now retain DFU's serialized effect bundles and the last processed native magic minute. Actor restoration uses the native effect manager, including effect-specific state and caster identity. Elapsed magic rounds are replayed using the same two-day cap as native DFU. The processed broker minute avoids counting a pending global tick twice. Capture occurs on save, scene replacement and before native scene transitions; no engine source changes are required.

The pre-transition capture is essential: native `EnableExteriorParent()` schedules destruction of the interior before the next controller update. Capturing only when clearing views lost effects on actual shop exit, despite passing isolated recreation and save/load tests. `OnPreTransition` captures while the source actors still exist.

## Evidence

- 289/289 core tests: `work/GuardEffects-core-green.log`. Optional snapshots deep-copy, reject blank/oversized payloads and leave old guards without invented effects.
- Native player-origin continuous health damage survives native JSON and body recreation with exact remaining rounds. Three unloaded minutes reduce duration appropriately; expired effects remain absent. `work/GuardEffects-elapsed-pass.log`.
- Actual game-slot reload restores continuous fatigue damage on the wounded indoor guard. Actual shop exit preserves that effect on the same guard, which physically pursues outside. `work/GuardEffects-door-pass.log`.
- Doorway failure reproduced before pre-transition capture: `work/GuardEffects-door-red.log`.
- Unity build succeeds: `work/GuardEffects-final-build.log`.

The actual slot reload checks the bundle/key; exact remaining-round assertions are in the isolated native serialization/recreation test. The doorway check rejects absent, expired or restarted duration. Test saving is confined to the dedicated LivingCitiesTest character.

## Related save correction

The elapsed-time test exposed historical entry-messenger routes being rejected after guard post relocation. Historical routes now validate guard identity rather than requiring the guard to retain its former post forever. Planning and delivery still check the current post; an undelivered route to a relocated guard is cancelled without informing it. Delivered history survives. Regression: `work/EntryPostMigration-red.log` and the passing core suite.

## Limits

The native guard fixture is immune to paralysis; that immunity is unchanged. Tests use continuous damage instead. Poison, disease, custom effects, caster relinking from non-player enemies, and every transition layout are not exhaustively verified. The adapter uses pinned DFU private timing methods and needs revalidation when upgrading DFU. Timed roster travel does not yet explicitly model movement restrictions from every effect. Automatic indoor witness-to-enemy defense remains unfinished; its actor adapter is tested separately.
