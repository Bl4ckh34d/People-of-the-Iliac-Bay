# Native market commute: source foundation and acceptance boundary

## Implemented source

`StreetPopulation.MarketCommuters.cs` controls the original authored `StaticNPC` GameObject. Its anchored exterior person ID, shared stationary `ResidentActor`, native entity, inventory and household membership remain the same while the body travels. It does not spawn a `MobilePersonNPC` replacement. `NativeMarketCommuter` stores runtime movement state only.

The market catalog supplies the exact stand ID and authored position. Real assigned customer reservations may extend closing from the early half of 17:45–18:15 into the late half. Passersby do not extend closing. The close decision considers customers from 17:30 onward, so a morning sale does not extend an empty evening stand. The exact native talk speaker, personal trade portrait speaker and `NativeVendorTradeAdapter.ActivePersonId` defer departure through active dialogue or trade confirmation.

Morning arrival is deterministic per person and day within 06:00–06:30. The departure calculation includes the exterior route and, when the home is observed, the actual bed-to-exit route. Movement follows surveyed routes with the existing 0.4 m arrival tolerance. A native interior sleeping pose uses the assigned household bed slot. Exterior coordinates retain floating-origin compensation.

The original body attaches to the real loaded exterior or home scene. `OnPreTransition` temporarily reparents it to the persistent population owner before native interior destruction. This preserves ordinary scene-relative damage and witness coordinates. Clear releases shared stationary bindings and restores the original parent, position and activation without replacing the sprite.

Market danger uses the existing native response, `CivilianResponses.Move`, threat position, shelter selection and safety-goal completion. An interior escape uses the existing `CivilianEscape` only for a flee reaction. Work resumes according to the current workday after the native response clears. Routine memories are emitted only when a physical phase changes; initial materialization and clock catch-up reconstruct state without inventing a new witnessed journey.

Market sleep reuses `ObserveHomeSleep` through a transient `DailySchedule.ExteriorWorkday` plan. Medical/Endurance rate, native health update, condition/alarm/task guards and vitals persistence stay owned by the existing ordinary home-sleep implementation. No separate healing formula or persistent clock was added.

Quest resources, native actions, active questors, faction listeners and individual factions are excluded before household assignment.

## Integration hooks owned by the main agent

- Call `TickMarketCommuters(seconds)` after stationary survey and current scene setup.
- Call `ReleaseMarketCommuters()` before clearing population bindings or replacing the world state.
- Use `FindMarketCommuterActor(id)` as the existing `FindActor` fallback.
- Use `TryMarketCommuterState(id, out state)` before the unknown-schedule error.
- Call `ObserveMarketInteriorViolence(alarm, victim, now)` from the ordinary interior stationary violence pass.
- Append `TryMarketCommuterContext(npc, now, out context, buildingName)` to the existing speaker context. The optional building-name function resolves the real home address.
- Invoke `yield return streets.VerifyMarketCommute()` from the serialized native market acceptance runner.

## Verification performed

- The policy regression first failed at the missing morning window, then passed after implementation.
- `dotnet run --project tests/MarketCommute.Regression/MarketCommute.Regression.csproj --no-restore -p:BaseOutputPath=../../outputs/market-commute-policy/` passes 8,006 assertions across 2,000 seeded person/day combinations, clock boundaries and conversation/transaction deferral.
- `dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --no-restore -v:q` compiles the actual complete runtime against Player assemblies with zero warnings and errors.

`VerifyMarketCommute` uses an authored seller with a real household, actual native building door and a 20–180 m surveyed route. It checks evening movement, actual bed pose, sleep context, one game hour of shared free health recovery, morning movement, arrival boundary, exact original body, unchanged item collection and absence of a mobile duplicate.

## Still unverified

The native commute probe has not yet run. The main agent is serializing Unity/Player execution while investigating an acute Inn admission problem. No installed package or native gameplay acceptance is claimed here. All-town route availability, real attack/murder flight, complete native save/reload and longer unattended routines remain native acceptance work. A compilable source foundation and seeded policy tests do not prove those outcomes.

## Owned files

- `src/PeopleOfTheIliacBay.Core/MarketWorkday.cs`
- `mod/Scripts/NativeMarketCommuter.cs`
- `mod/Scripts/StreetPopulation.MarketCommuters.cs`
- `mod/Scripts/StreetPopulation.MarketCommuteProbe.cs`
- `tests/MarketCommute.Regression/MarketCommute.Regression.csproj`
- `tests/MarketCommute.Regression/Program.cs`
- This audit.

Household allocation, static entity bindings, catalog/visitor behavior, vendor trade and shared integration hooks belong to the other explicitly assigned market agents and the main agent.
