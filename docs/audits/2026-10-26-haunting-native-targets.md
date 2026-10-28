# Haunting targets: source integration and acceptance boundary

The explicit gameplay request is that Lysandus's haunting Ghost/Wraith actors can attack living outdoor residents and guards, alongside the player. This change applies only to genuine `S0000977` actors during the unresolved curse, from 19:00 until 05:00.

## Cause and implementation

Native `EnemySenses.GetTargets()` enumerates enemies and the player, excluding `CivilianEntity`. It also filters non-player targets through the global infighting setting and the quest actor's `IsAttackableByAI` flag. Native senses has no replaceable target enumeration delegate.

`HauntingLifetime.Track()` now installs `NativeHauntingTargets` only after its existing real quest/resource and Ghost/Wraith checks. The adapter checks the current quest, native mobile type, curse, time, actor health and enabled state again before selecting a target. It accepts only active, living `ResidentActor` or `GuardActor` bodies with the same loaded exterior `DefenseScene`; protected residents and protected quest resources are excluded. Interior ownership is checked independently of visibility.

The adapter surveys local physical colliders every 0.25 seconds. Admission uses the same eye positions, field of view, range and first-hit ray as native `EnemySenses.CanSeeTarget`. The native public sight delegate cannot survey arbitrary candidates because it uses private distance and direction fields belonging to the current target. A disabled controller matching an existing resident capsule supplies the geometry required by native senses and motor, using the existing `NativeNpcCombatRoleDecision` boundary. Crowd movement retains its capsule.

Selected victims are pinned before native physics and after rendered frames, following the existing guard-defense integration pattern. Native senses, motor, attack timing, animations and damage remain the owners. A victim that dies, becomes inactive, enters an interior or loses line of sight is released together with this adapter's target memory. A nearer player already seen by native senses remains eligible when acquiring a new NPC target.

The existing `DefenseCombatAdapter.AttackCivilian` and `NativeDefenseHooks.EnemyHitNonPlayer` already adapt real `CivilianEntity` targets to native attack formulas and `DamageHealthFromSource`. They also supply the existing null-safe formula patches and source attribution. No additional damage callback, proxy entity or animation-frame interception was added.

No global `EnemyInfighting` or quest attackability flag is changed. Other quests and unowned ghosts are not bound. Existing dawn/pending-placement cleanup remains in `HauntingLifetime`.

## Verification

- `dotnet run --project tests/NativeHauntingTargets.Regression/NativeHauntingTargets.Regression.csproj`: ten scope cases pass. The initial implementation returned false and failed at `night ghost` before implementing the policy.
- `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -o work/haunting-targets-compile`: succeeds with zero warnings and errors.
- Native probe added through the existing `-HauntingDawnProbe` entry. It creates real quest resource Ghost/Wraith actors, a real resident civilian body and a `GuardActor.Create` CityWatch body. With infighting disabled, it requires natural attack/HP loss for all four combinations. It also checks a blocking wall, independent interior ownership and an unowned ghost control. Existing dawn, inactive exterior and pending placement checks run afterward.
- Native run `haunting-targets-native-01` passes on installed package SHA256 `77342C1BDF1B7BF9B37291218487DC6021FE40898E3DF67DB640DABEECCDAEE0`, with matching repository/deployed target source SHA256 `225D99F7EE17B7E4ED5F3DFEACF1642049AB2BC9B5C6181FFB86685EB41D15E8`. The exact owned `PeopleOfTheIliacBayTest-9a5f197b` / `People - Daggerfall day 2` save was used. Four naturally scheduled attacks reduce HP from 2000 to 1974 (Ghost/resident), 1967 (Ghost/CityWatch), 1977 (Wraith/resident) and 1975 (Wraith/CityWatch). All target controls, inactive exterior inn expiry, pending placement and dawn/control markers pass. TownTest exits zero and owned player PID8076 exits. Evidence: `work/haunting-targets-native-01-launch.log` and `work/haunting-targets-native-01-player.log`.
- The runner's explicit fixture save allowlist now admits `HauntingDawnProbe`, and it requires the new `HAUNTING_TARGETS_PASS` marker alongside existing dawn markers. No other fixture checks were relaxed.

## Open acceptance

The controlled native run establishes real damage, target exclusion and cleanup on disposable bodies positioned above the loaded town. Ordinary street encounters, every modded body layout and user gameplay acceptance remain separate. Native target reacquisition remains owned by unmodified `EnemySenses`; its periodic player-only enumeration can briefly replace a pinned victim, as in the existing guard-defense path. The focused run confirms that all four normal combat combinations proceed despite this periodic native enumeration.

Files: `mod/Scripts/NativeHauntingTargets.cs`, `mod/Scripts/NativeHauntingTargetsProbe.cs`, `mod/Scripts/HauntingLifetime.cs`, `mod/Scripts/HauntingDawnProbe.cs`, and `tests/NativeHauntingTargets.Regression/`.
