# Companion successful-use progression

Date: 26 October 2026. Source work only; no Player, Unity, package, deployment, or native acceptance was run by this work package.

## Verified source behavior

The existing `NativeNpcCombatAssist.TrySwing` and `NativeNpcCompanionCast.TryExecute` hooks already use the native weapon/effect skill. Melee awards only after observed target health loss. Helpful magic awards only after `NpcMagicActionContract.MatchesObservedOutcome` confirms the native result and resource debit. Neither hook needed a replacement implementation.

`NativeNpcCompanionExperience.IsBoundToPlayer` had several lifecycle gaps:

- Ordinary and paid Follow accepted experience until `DeadlineAt`, although `FinishAt` ends the agreed service and the deadline can include a return allowance.
- A paid Follow bypassed suspended or stale contract state through the ordinary Follow branch.
- A cancelled paid Follow could leave the active-action index before its active contract settled. The contract fallback then still awarded experience.

The award boundary now rejects terminal, returning, expired, or stale active actions; checks paid Follow against its exact current active contract; checks `LastServiceUpdateAt`; and rejects a contract whose terminal paid Follow is awaiting settlement. The existing sparse progression ledger, native successful-use amount, seeded profiles, and formula remain unchanged.

Normal action admission permits only one current action, so an active Follow cannot simultaneously become Attack, Defend, or CastSpell. The former Follow-only eligibility therefore missed real requested assistance. A live registered traveler whose existing resolved archetype satisfies `TravelerAdventurerPolicy.IsAdventurer` now qualifies while its current Attack, Defend, or exact `CompanionCast` action has explicit conversation admission. Ordinary household residents and ordinary travelers do not qualify through these actions. This uses the actual persisted traveler identity and existing Warrior/Mage/Thief policy; accepting an action does not change anyone's role.

Paid work cannot fall back to this unpaid adventurer rule while a nonterminated player contract exists. Combat additionally requires `NpcServiceCoverage.Combat`; helpful casting retains the existing Follow coverage boundary. No concurrent Follow/action owner or resumption subsystem was introduced.

## Focused verification

Command:

```powershell
dotnet run --project tests/NativeNpcCompanionSkillUse.Regression/NativeNpcCompanionSkillUse.Regression.csproj --artifacts-path outputs/companion-skill-progress/verified
```

Result: **22/22 passed**, exit 0. Thirteen cases exercise the real Core admission and paid-contract APIs, including all three assistance kinds, ordinary household/traveler exclusions, dead/cancelled/expired adventurers, and paid coverage. Nine cases compile the production melee adapter against controlled Unity/formula boundaries and the real Core store. They cover exact weapon skill, a hit, native miss, refused damage, ordinary resident, cancellation before and during damage, expired Follow, a real ledger threshold, application of its gain to the adapter's skill target, and snapshot restoration. The end-to-end assistance case uses actual classified conversation admission, observes arrival into Executing, calls the production swing adapter, and requires the successful hit to cross the ledger threshold and update the body's skill. The CPU fixture does not prove native geometry, DFU's hit probability, or Unity execution.

The initial lifecycle suite was also run against a separate copy of the previous `NativeNpcCompanionExperience.cs` from `HEAD`: **11/17 passed, six expected failures**. The failures cover the five lifecycle gaps and the expired Follow hit. This baseline run did not alter shared production source. An earlier test setup incorrectly used an accrual candidate as a committed contract revision; it was corrected to real suspend/resume transitions before this baseline comparison. The later real-assistance tests produced **19/22 passed** before the assistance fix: accepted adventurer use, paid combat coverage, and the complete admitted-attack skill threshold each failed for the expected missing behavior.

Command:

```powershell
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --artifacts-path outputs/companion-skill-progress/runtime -v:minimal
```

Result: **0 warnings, 0 errors**, exit 0. Two earlier attempts encountered another agent's unfinished `bereavementGreetings` references; the final run passed after that independent source change finished. This is a managed host-reference compile, not a Unity package or native gameplay test.

`git diff --check` passed for the changed tracked source/test files. Git emitted its existing LF-to-CRLF checkout advisory for the Core file.

## Native probe and remaining acceptance

The existing opt-in `NativeNpcCompanionCastProbe` now checks the exact native skill delta, respects `FinishAt`, and rebuilds a native `CivilianEntity` through `NativeNpcVitalsBinding` after the real FullSerializer world round trip. Its skill marker includes `rebuiltNativeSkills=true` only after that binding succeeds. The existing successful self-heal and immediate full-health no-effect replay remain the proof boundaries. No new `NativeNavigationTrial` dispatch is required.

The existing `NativeNpcCombatAssistProbe` already selects an armed generated adventurer and admits a real Attack action through normal action handling. It now also requires weapon XP after observed real target HP loss, the matching derived native skill, and the same native serialization/materialization proof. Polling may observe multiple hits; the native assertion requires positive XP bounded by observed HP loss, not an assumed one-hit Unity frame. The CPU adapter case verifies the exact one-point tally per successful hit. The success marker is `PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_SKILL_PASS`. Existing `combat-assist` dispatch remains unchanged.

Root must run the existing companion-health and combat-assist native probes on disposable characters. The new binding assertions and the admitted-adventurer gameplay path remain unexecuted in Unity. In particular, the direct Follow spell probe is not evidence that ordinary dialogue can issue concurrent Follow and CastSpell. The supported new path is the genuine adventurer's separately accepted assistance action.

Root integration checkpoint: full packageD3CA68C3D919 is installed and Unity
verification passes. Root independently reran22 focused cases and the combined
Core suite (2162/2162 after the wage update). Native acceptance limits above remain.
