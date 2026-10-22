# Base Conveyor Port Matrix

**Status: proposed V1 content contract; no ports or conveyor transfer are
currently implemented.** Building names and roles follow
[Central Divide](CentralDivideScenario.md). Per-building placement and port
coordinates must be authored and physically qualified before activation.

| Directorate structure | Belt INPUT | Belt OUTPUT | Transfer role and restrictions |
| --- | --- | --- | --- |
| Command Core | Conditional solids/packages | Conditional solids/packages | Bootstrap inventory and optional local handoff; keep existing building construction stock semantics |
| Mine / Extractor | None | Ferrous Ore, Silicates and other explicitly eligible extracted solids | One-way bulk output; a fluid/gas deposit is never automatically belt-enabled |
| Storage Depot | Eligible solids/packages | Eligible solids/packages | Buffer and truck-to-belt or belt-to-truck handoff; finite shared inventory |
| Smelter | Eligible solid ore | Steel | Consume raw stock and emit produced Steel only after the authoritative recipe completes |
| Refinery | Eligible solid recipe inputs only | None for Fuel | Fuel stays non-belt in V1; fluid output needs future pipeline or road |
| Electronics Fabricator | Eligible solid recipe inputs | Electronics | Protected packaged output |
| Ammunition Plant | Steel, Electronics and other eligible solids per catalog | Ammunition | Sealed-crate output, no individual caliber item simulation |
| Logistics Hub | Eligible solids/packages | Eligible solids/packages | Regional truck exchange and local buffering; no free auto-link |
| Supply Depot | Ammunition | Ammunition where permitted | Cargo Truck/Supply Truck bays remain authoritative; Fuel still delivered by road |
| Barracks | Eligible unit-recipe solids/packages | None | Produced infantry are ECS units, not conveyor cargo; Fuel inputs still use road if required |
| Vehicle Factory | Eligible unit-recipe solids/packages | None | Produced vehicles are not belt cargo; Fuel input continues to need road |
| Power Plant | Explicitly eligible solid recipe inputs if content requires | None | Power is not inventory cargo; no power-by-belt shortcut |
| Radar | None by default | None | Powered consumer, not a routine material transit node |

### Port metadata

Each future authored port must declare: building stable ID and role, port stable
ID, relative world socket, cardinal-facing direction, elevation tier, permitted
resource transport classes, inventory source/sink binding, and maximum throughput
per second. Ports are strictly directed. Input receives from the network;
output offers from the building. A dual-role building requires distinct ports
or an explicitly validated switchable adapter, not an implicit bidirectional
link.

Storage, hub and depot transfer rules must reuse actual shared capacity,
reservations and ownership. A port never implies infinite buffers. A resource
may connect only when both endpoint class constraints and its catalog
eligibility agree. The current resource definitions and production recipes
remain authoritative for which materials a given building truly consumes
or produces; this table does not invent new recipes.

### Compatibility and future expansion

Fuel and Volatiles remain nonconveyable pending a separate pipeline decision.
Other future fluid, gas, unit, construction-unit and oversize resource classes
fail closed. Ingredient changes in recipes require catalog-level validation
but do not automatically make a new resource belt-compatible.

When a production facility requires both belt-compatible and incompatible
inputs, conveyor and Cargo Truck stock may coexist in the **same authoritative
inventory**; production reserves complete recipes as before. Existing regional
logistics policies remain valid, and the dispatcher must not issue duplicate
reservations for the same demand.

### Required content qualification

Before any port is enabled, test its socket geometry, facing, rise, footprint,
ownership, intake/output class, capacity, recipe eligibility, blocked/disabled
status, and lifecycle when the building is damaged, destroyed or removed.
A missing port definition means no conveyor connection, never a guessed
automatic transfer.
