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
3. [x] DFU housing adapter and settings. Read actual interior bed models using stable building keys and an exterior model size hint, without opening every interior. Increase the bounded household menu limit to five for new allocations only. Ownership/residence suitability remains a separate task. Native housing checks and actual-player allocation passed.
4. [x] Runtime child distinction. Use saved sex for adult graphics/names; child schedules stay home until local play is implemented. Render existing child flats with a persistent identity and brief greeting rather than adult bodies. Adult fixture behavior passes. This completes only the home-only prototype, not required directional child movement.
5. [x] Actual-player validation. New family fixture and legacy fixture passed. Child sprites were inspected; children have no adult visits and family fields survive reload. Read-only Luna/high review found no confirmed critical/important issues. Documentation records evidence and limits; the full goal remains active.

## Foundation checkpoint

Subsequent runtime checkpoint: 0.3.0 is built and installed, with 87/87 core tests and passing actual-player family/legacy checks. Character `LivingCitiesTest-126494eb`, SAVE11–SAVE15, has 402 residents including 118 children and a verified five-person home. The earlier source-only checkpoint below is retained as history. Sleeping sprites are the first art production task when actual home/tavern sleeping is implemented; directional movement, child play and full appearance coverage remain open.

86/86 core behavior tests pass. `LivingCitiesBuild.VerifySaveState` passed in Unity with exit 0, including FullSerializer family round trips, v1/v2 migration and adapter restore isolation. Read-only Luna/high review found no confirmed critical/important issues. The installed portable 0.2.0 bundle remains unchanged; family runtime integration has not shipped. The latest animation amendment also requires every stationary and mobile adult human appearance to have directional walking/running/weapon combat/spellcasting plus sleeping in bed; children need directional walking/running and sleeping, without battle sets.
