# Family households implementation plan

> Use executing-plans to implement the following tasks and requesting-code-review before the checkpoint. Existing implementation authorization covers this refinement.

**Goal:** Generate persistent family households of up to five, influenced weakly by real housing size/beds, while preserving existing residents and preventing children from joining adult visits.

**Architecture:** Keep demographic allocation and relationships in the Unity-independent core. Read vanilla interior bed counts and exterior size hints in a DFU adapter. Migrate old saves without rerolling names, appearance, deaths or household membership; legacy adults remain adults and housemates unless an actual relationship already exists. Newly generated families explicitly identify parents, partners and children. Runtime visuals use existing classic child flats where available.

**Tech stack:** DFU 1.1.1, Unity 2019.4.40f1, C# 7.3/netstandard2.0 core, net8 behavior harness.

**Spec:** `docs/RELATIONSHIPS-AND-FUTURE-INVENTORIES.md`, `docs/MILESTONES.md` and latest user amendments.

## Constraints

- Maximum five: up to two adult parents and three children; most households smaller.
- House size and beds are preferences, never one-person-per-bed limits. Sharing is allowed.
- Shared household surname; names/roles persist. Existing saved appearances and identities are preserved.
- Children have no shopping, tavern, job or inter-settlement travel routines. Local play is a later extension of this demographic slice, not adult visits disguised as play.
- Existing quest/service bindings and physical test-town scope remain unchanged by demographic generation.
- Functional milestones take priority over curved paths and visual polish.
- Directional child movement and appearance-preserving civilian movement/combat are now essential functional requirements; see `docs/DIRECTIONAL-CIVILIAN-ANIMATION.md`. Static child flats in this demographic slice are an incomplete home-only prototype, not completion of mobile children.

## Tasks

1. [x] Core allocation and tests. Add `HomeProfile`, persistent sex/age-group/parent/partner fields and household surname seed. Generate weighted deterministic families with test coverage for five-member bounds, mostly smaller homes, bed sharing, larger-house preference, order independence and valid relationships.
2. [x] Save migration and validation. Schema 3 annotates legacy adults using their existing appearance-seed gender and historical surname seed. Preserve all identities, deaths and routes. Validate parent/partner references, household membership, reciprocal partners and child schedule restrictions. Core tests and actual Unity FullSerializer passed, including raw legacy field shapes. Actual-player fixture migration remains in task 5.
3. [ ] DFU housing adapter and settings. Read actual interior bed models using stable building keys and an exterior model size hint, without opening every interior. Increase the bounded household menu limit to five for new allocations only. Record that ownership/residence suitability is still a separate task.
4. [ ] Runtime child distinction. Use saved sex for adult graphics/names; child schedules stay home until local play is implemented. Render existing child flats with a persistent identity and limited interaction rather than adult bodies. Preserve adult fixture behavior.
5. [ ] Actual-player validation. Generate a new disposable family fixture, inspect child sprites and family counts, verify no adult visit plans for children, then save/load. Retain old fixtures to verify migration. Review changes, fix confirmed findings, update docs and Obsidian, commit, and leave the full goal active.

## Foundation checkpoint

86/86 core behavior tests pass. `LivingCitiesBuild.VerifySaveState` passed in Unity with exit 0, including FullSerializer family round trips, v1/v2 migration and adapter restore isolation. Read-only Luna/high review found no confirmed critical/important issues. The installed portable 0.2.0 bundle remains unchanged; family runtime integration has not shipped. The latest animation amendment also requires every stationary and mobile adult human appearance to have directional walking/running/weapon combat/spellcasting plus sleeping in bed; children need directional walking/running and sleeping, without battle sets.
