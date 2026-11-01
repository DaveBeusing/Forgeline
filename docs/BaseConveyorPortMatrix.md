# Base Conveyor Port Matrix

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

**Status: implemented finite-flow port subset with headless qualification; native acceptance pending.**
[Conveyor simulation and vehicle interop](ConveyorSimulationAndVehicleInterop.md)
lists exact activated sockets, dimensions, resource filters and real inventory bindings.
Command Core, eligible Extractors, Storage Depot, Smelter, Electronics Fabricator,
Ammunition Plant, Logistics Hub, Supply Depot, Barracks and Vehicle Factory are active.
The current Refinery processes only Volatiles/Fuel and has no conveyor port; Power Plant
and Radar also remain excluded. Rows below describe roles including future eligible
content, not permission to invent sockets for an unsupported definition or scale.

In Draw route, clicking an eligible source and destination selects compatible
available ports and places their adapters automatically. Command Core and
Storage Depot each expose four physical adaptable positions (not eight sockets):
west/east primary at local Z 0, plus west/east secondary at local Z -6m
(Command Core) or -4m (Storage Depot). One validated adapter determines
whether a free position is an input or output, and occupies it exclusively. During construction,
IN/OUT arrows identify the real sockets; selected or hovered buildings retain
these markers outside construction. See [construction and routing](ConveyorConstructionPlacementAndUX.md)
for length adjustment, visibility and placement limits.

| Directorate structure | Belt INPUT | Belt OUTPUT | Transfer role and restrictions |
| --- | --- | --- | --- |
| Command Core | Conditional solids/packages | Conditional solids/packages | Bootstrap inventory and optional local handoff; keep existing building construction stock semantics |
| Mine / Extractor | None | Ferrous Ore, Silicates and other explicitly eligible extracted solids | One-way bulk output; a fluid/gas deposit is never automatically belt-enabled |
| Storage Depot | Eligible solids/packages | Eligible solids/packages | Buffer and truck-to-belt or belt-to-truck handoff; finite shared inventory |
| Smelter | Eligible solid ore | Steel | Consume raw stock and emit produced Steel only after the authoritative recipe completes |
| Refinery | None for the current Volatiles recipe | None for Fuel | Current recipe is entirely non-belt; future content requires explicit eligible-port qualification |
| Electronics Fabricator | Eligible solid recipe inputs | Electronics | Protected packaged output |
| Ammunition Plant | Steel, Electronics and other eligible solids per catalog | Ammunition | Sealed-crate output, no individual caliber item simulation |
| Logistics Hub | Eligible solids/packages | Eligible solids/packages | Regional truck exchange and local buffering; no free auto-link |
| Supply Depot | Ammunition | Ammunition where permitted | Cargo Truck/Supply Truck bays remain authoritative; Fuel still delivered by road |
| Barracks | Eligible unit-recipe solids/packages | None | Produced infantry are ECS units, not conveyor cargo; Fuel inputs still use road if required |
| Vehicle Factory | Eligible unit-recipe solids/packages | None | Produced vehicles are not belt cargo; Fuel input continues to need road |
| Power Plant | None in the current catalog | None | Power is not inventory cargo; future eligible inputs require an explicit content change |
| Radar | None by default | None | Powered consumer, not a routine material transit node |

The [raised conveyor visual kit](ConveyorVisualLanguage.md) defines the
standard belt end frames, tier increments and explicit input/output adapters.
This matrix retains ownership of future *building-specific* physical sockets;
none are generated automatically by the standalone kit.

## Port metadata

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

## Compatibility and future expansion

Fuel and Volatiles remain nonconveyable pending a separate pipeline decision.
Other future fluid, gas, unit, construction-unit and oversize resource classes
fail closed. Ingredient changes in recipes require catalog-level validation
but do not automatically make a new resource belt-compatible.

When a production facility requires both belt-compatible and incompatible
inputs, conveyor and Cargo Truck stock may coexist in the **same authoritative
inventory**; production reserves complete recipes as before. Existing regional
logistics policies remain valid, and the dispatcher must not issue duplicate
reservations for the same demand.

## Required content qualification

Before any port is enabled, test its socket geometry, facing, rise, footprint,
ownership, intake/output class, capacity, recipe eligibility, blocked/disabled
status, and lifecycle when the building is damaged, destroyed or removed.
A missing port definition means no conveyor connection, never a guessed
automatic transfer.
