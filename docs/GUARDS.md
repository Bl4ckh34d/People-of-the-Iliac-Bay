# Finite guards

This is the deterministic core foundation for M4 under the existing approved gameplay requirements. The installed 0.3.3 preview still uses native spawning until the replacement adapter is validated.

## State and behavior

A settlement owns a saved roster. Each guard has a permanent identity, a post, current duty, location/route and life state. Creating a settlement twice returns the same roster. Population changes and menu changes never silently reroll existing guards.

The initial policy uses five percent of residents, at least four guards in towns and eight in cities. Very small hamlets (fewer than forty residents) have no garrison. Larger hamlets have a small contingent. These defaults are a starting policy, not a claim that settlement-size tuning is final. Later settings must expose supported controls.

Reports dispatch available living guards by actual route length, with up to four responders. A guard cannot answer two incidents simultaneously. Repeated reports reuse the same dispatch rather than replenishing it. Travel uses game time and measured route length; actor visibility and player door transitions do not control elapsed travel. No reachable route or no available guard means no responder.

A report with no assigned responders remains pending and can retry when a route or guard becomes available. Once any responders are assigned, the same report never recruits additional responders. Invalid route requests leave the entire roster unchanged, including other guards whose travel would otherwise advance.

Arrival starts a bounded search at the reported location. Fresh sightings may update pursuit; loss of sight leaves only a last-seen position. Search expiry returns guards to their posts. Recruitment preserves dead identities and introduces a new identity only after a delay, with at most one replacement per day. It never happens while loading a save simply because an actor is absent.

## Implementation sequence

The production `NativeGuardHooks` bridge now intercepts both native spawn entry points and suppresses automatic pooled-guard conversion while the roster owns a town. Its owner supplies a supported-town predicate, a report callback and a real roster-actor resolver. Native Unity tests exercise immediate/deferred calls and actor returns, unsupported-town fallthrough, conversion gating, disposal and rejection of null actors. The hook is deliberately not installed by `LivingCitiesMod` yet: there is no physical roster controller to satisfy its actor contract.

The controller must also take ownership of native mobile guards. `WeaponManager` immediately dereferences a guard returned while attacking a mobile guard, and `PlayerEntity` can convert additional pooled guards after a response. Leaving native pooled guards active alongside the roster would duplicate the population. Entry from an interior must retain exterior route/door information; current saved citizen routes do not provide a complete navigation cache. These are integration requirements before activation, not reasons to keep instant native reinforcements in the final mod.

1. Test and implement roster identities, settlement counts, finite dispatch, distance-based arrival, search/return, death and bounded recruitment in the deterministic core.
2. Persist and validate the roster in schema 6, including native DFU serialization and migration from schemas 1–5.
3. Replace supported-town native batch/single spawn paths with roster ownership, preserving the return contract when vanilla hits an already visible guard.
4. Materialize patrol/post/response actors at their real locations. Preserve native combat/arrest integration while controlling sight/search and door arrival.
5. Test repeated reports, exhausted/dead roster, nearby/far reinforcements, escape, shop entry, save/load mid-response and old-save compatibility in the actual player.

Paired torch patrols, walls/headquarters, creature defense and wider settlements remain part of M4 after the first dispatch adapter. They are not optional replacements for the above requirements.
