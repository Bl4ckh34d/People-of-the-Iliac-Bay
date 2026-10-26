# Known bereavement: source-only audit

Date: 26 October 2026. Scope: the bounded grief portion of roadmap LIFE-31/37/54. This change does not implement inheritance, successors, general rumor propagation, or economic simulation.

## Findings and implementation

The former `BereavementFor` derived mourning from a remote partner's corpse record, whether the speaker knew about it or not. `HoldsPartnerGrudge` treated a null hostile-threat ID as proof that the player killed the relative. Greeting suppression existed only in a runtime dictionary, so reload could repeat it. Household context separately exposed global partner/child death state.

The existing alarm participant now carries a small saved death-knowledge receipt: source, learning time, whether grief was recorded, and the last greeting-attempt day. The receipt is admitted only by current native source paths:

- Witnessing the actual death, or a witness sample within the existing one-second native death callback window. An older assault witness does not automatically learn about a subsequent death.
- Actual interior or outdoor corpse discovery, including the existing late-witness discovery path.
- A successful existing `Warn` delivery from a living witness who already knows the confirmed death. Earlier warnings are not retroactively upgraded.

The affected resident must exist and be dead; the observer must exist and be alive. Close relationships are the existing partner, parent, and child links. This does not infer friendship, siblings, or remote family notifications. The old native warning limits and physical-perception callers remain unchanged.

A `Bereavement` scene memory records the relationship, deceased name, and how the observer learned the death. It uses existing negative personal event valuation, the existing 32-entry per-observer bound, and the same deterministic retention hash. Grief retention is 360–720 game days from learning, with declining greeting probability. A persisted receipt prevents repeated sightings from recreating grief after memory eviction or expiry. Cleanup does not erase knowledge. Existing family context uses assigned membership and known deaths rather than revealing a remote life-state count.

The existing conversation `Grieving` input now queries this evidence-backed memory. The typed appraisal pipeline remains the sole owner of persistent appraisal mood deltas; this change adds no direct persistent mood or relationship adjustment. Scene recall already enters the memory tools and context paths, so no parallel journal or new dialogue subsystem is introduced.

Player blame requires explicit `PlayerAttributed` plus actual death-witness knowledge. Discovery and notification never grant witnessed-player retaliation. Adult close relatives retain the existing lasting grudge behavior; children can grieve but do not enter its adult combat path. Neutral mourning greetings are suppressed for an actual player-killing witness.

One greeting attempt per observer/game day is saved, including unsuccessful rolls. It covers multiple losses and survives reload. The greeting still overlays the native talk window and leaves normal topics beneath it. The obsolete runtime dictionary and its two lifecycle clears are removed.

## Verification

The regression first failed against the original source with `Remote corpse must not reveal a partner's death.`

Commands:

```powershell
dotnet run --project tests/KnownBereavement.Regression/KnownBereavement.Regression.csproj
dotnet run --project tests/PeopleOfTheIliacBay.Tests/PeopleOfTheIliacBay.Tests.csproj
```

The focused regression compiles the actual `ResidentBereavement.cs` adapter against minimal UI stand-ins and uses the installed FullSerializer assembly. Cases cover hidden remote death; witnessed, discovered, and delivered death knowledge; earlier assault and warning negatives; player attribution; parent/child grief; child retaliation rejection; malformed receipt rejection; one-to-two-year retention; expiry without restart; corpse cleanup; bounded recall; invalid/dead observer and living-subject negatives; successful and unsuccessful greeting suppression across restore; and native-talk overlay preservation under the UI stand-ins.

Results: focused regression **49 assertions passed**; full core suite **2160/2160 passed**. Logs: `work/known-bereavement-focused.log` and `work/known-bereavement-core.log`. The first full run while the companion agent was adding its tests reported two companion-progression failures; that owner's completed changes pass in the final combined run. No bereavement case failed in that intermediate run.

Deferred explicit player attribution after restore refines the retained grief and eyewitness memories without adding records, renewing their age, or enqueuing duplicate appraisals. Raw household-property usage was searched across `src` and `mod`: the dialogue formatter uses only `HouseholdKnowledge.ToContext`; raw life-state/count consumers are tests, not a second dialogue projection.

## Native acceptance and limits

No Unity build, Player launch, deployment, model request, GPU process, commit, or push was performed by this task. Actual native perception, rendering, and gameplay acceptance remain with the coordinating agent.

The existing dormant `ProbeBereavementGreeting` and its `TownSmokeHarness` call now check the remote-death negative, admit an interior discovery, round-trip through the native serializer, verify the talk-window overlay, round-trip again, and reject a repeated greeting. No `NativeNavigationTrial` dispatch change is required. Success marker: `PEOPLE_OF_THE_ILIAC_BAY_BEREAVEMENT_PASS`.

Old saves are not retrospectively granted death knowledge from global corpse state. General discovery by every possible observer, autonomous long-distance notification, model phrasing, and balance of grief duration are not established by these finite tests. Existing raw structural `NpcKnownRelative.IsAlive` and `LivingHouseholdCount` properties remain available for non-dialogue callers; their `ToContext` projection no longer exposes unknown deaths.

Root integration checkpoint: full packageD3CA68C3D919 is installed and Unity
verification passes. Root independently reran49 focused assertions and the combined
Core suite (2162/2162 after the wage update). Native acceptance limits above remain.
