# Battlefield Supply

## Purpose

Battlefield supply connects industrial production to operational military capability through physical Fuel and Ammunition inventories.

The system keeps the player-facing model readable while preserving authoritative quantities underneath:

- Fuel is consumed by movement.
- Ammunition is consumed through the combat-facing ammunition hook.
- Supply Depots receive stock through the existing regional logistics network.
- Supply Trucks load only when physically near an operational Supply Depot.
- Units receive resources only from an eligible provider within its resupply range.
- No battlefield resource is granted through a global pool or remote shortcut.

## Resources and Production

`ResourceIds.Ammunition` is the generalized V1 ammunition resource.

The initial ammunition recipe consumes Steel and Electronics and is produced by an Ammunition Plant with the dedicated `AmmunitionProcessing` capability. The abstraction intentionally avoids caliber-specific inventories in V1.

Fuel continues to use the existing refinery production chain.

## Unit Supply State

Supply-capable military units can expose:

- `UnitFuelState`
- `AmmunitionState`
- `UnitSupplyPriority`
- `UnitSupplyState`
- `SupplyMovementConstraint`

Fuel and Ammunition are stored in real `InventoryStore` inventories. Per-resource capacities prevent a shared unit inventory from exceeding the configured Fuel or Ammunition capacity.

`BattlefieldSupplyFactory.AttachUnitSupply` is the canonical helper for attaching initial battlefield supply state to a unit fixture.

## Fuel Consumption

`BattlefieldSupplySystem` observes authoritative unit positions during the `Supply` phase and converts horizontal movement distance into Fuel consumption using the unit's configured Fuel-per-meter rate.

The previous observed position is simulation state. Wall-clock time and rendering state never influence consumption.

Fuel consequences are deliberately simple in the first implementation:

- at or above 50% Fuel: full configured movement speed;
- below 50%: Low Supply mobility scale;
- below 20%: Critical mobility scale;
- zero Fuel: powered movement stops.

A zero-Fuel unit remains selectable and retains its movement order. After Fuel is restored, the movement constraint is lifted and the existing order can continue.

## Ammunition Consumption

`ForgeLine.Combat.AmmunitionConsumption.TryConsume` is the combat-facing V1 hook.

The operation consumes `ResourceIds.Ammunition` from the unit's physical inventory and fails closed when insufficient Ammunition is available. Full weapon firing remains a later combat concern, but future weapons must use this same state rather than introducing a parallel ammunition pool.

## Supply Status

The simplified operational states are:

- `Supplied`
- `LowSupply`
- `Critical`
- `Unsupplied`

For units carrying both Fuel and Ammunition, the displayed status is derived from the more constrained resource. A unit with only one modeled supply category uses that category alone.

Detailed Fuel and Ammunition fractions remain available in the read model for diagnostics and later UI work.

## Supply Depot

The Supply Depot is a storage, distribution, and supply-capable logistics node.

On completion it receives:

- a real inventory;
- a `SupplyDepot` capability;
- a local `SupplyProvider` range;
- an automated Fuel stock policy;
- an automated Ammunition stock policy.

Those stock policies are handled by `AutomatedDistributionSystem`. Regular Cargo Trucks therefore replenish depots using the existing logistics graph, reservations, routes, physical loading, navigation, and unloading.

A Supply Depot does not create resources locally.

## Supply Truck

`SupplyTruckFactory` creates the V1 battlefield supply vehicle.

The vehicle has two distinct inventory responsibilities:

- a dedicated operational Fuel inventory used for its own movement;
- a cargo inventory that carries Fuel and Ammunition for recipients.

Supply Trucks are explicitly excluded from the regional automated-distribution truck pool. They load their battlefield cargo only from an operational friendly Supply Depot within loading range.

This separation prevents carried Fuel from being confused with the truck's own propulsion Fuel.

## Automatic Resupply

`BattlefieldSupplySystem` runs in the canonical `Supply` phase.

Eligible recipients are processed in deterministic order:

1. supply priority;
2. stable entity identity.

Within provider range, Fuel and Ammunition are transferred through `InventoryStore.Transfer`. Source availability and destination capacity are checked before every transfer.

Provider selection is deterministic and prefers:

1. a valid provider explicitly selected by a `ResupplyOrder`;
2. otherwise the nearest eligible provider;
3. stable entity identity as the distance tie-break.

This is intentionally automatic. The player manages provider positioning, depot stocks, transport capacity, and priority rather than manually transferring individual resource units.

## Resupply Command

`ResupplyCommand` gives selected friendly units a normal simulation command for deliberate resupply.

The command:

1. validates ownership and supply eligibility;
2. selects the nearest operational friendly provider;
3. stores a `ResupplyOrder`;
4. issues a normal `MovementOrder` toward the provider.

Navigation and `GroundMovementSystem` remain authoritative for movement. The command never teleports resources or changes transforms.

When the unit returns to `Supplied`, the resupply-specific movement/navigation state is cleared.

## Conservation and Failure Semantics

Battlefield resupply must conserve physical resources.

For every transfer:

`source before + destination before = source after + destination after`

except for explicit consumption such as movement Fuel or weapon Ammunition.

Disabled depots do not provide supply. Missing inventories are treated as unavailable. Insufficient provider stock leaves the recipient partially supplied rather than fabricating the missing quantity.

## Diagnostics

`BattlefieldSupplyMetrics` exposes:

- supplied/low/critical/unsupplied unit counts;
- provider/depot/truck counts;
- Fuel and Ammunition transferred during the current tick;
- cumulative Fuel and Ammunition transfer quantities.

`BattlefieldSupplyDebugSnapshot` exposes per-unit Fuel/Ammunition fractions, supply state, priority, active resupply provider, provider stock, provider type, range, and world position. Snapshot capture is opt-in through `DebugCaptureEnabled`, so normal simulation ticks keep aggregate metrics without allocating debug read-model arrays.

The development debug visualization renders:

- provider resupply ranges;
- provider Fuel/Ammunition stock labels;
- Low/Critical/Unsupplied unit markers;
- active resupply links.

Presentation only renders read models. It never owns or mutates supply simulation state.

## Validation

Regression coverage includes:

- movement Fuel consumption;
- zero-Fuel immobilization and post-refuel resumption;
- Ammunition consumption and insufficient-ammo failure;
- Supply Truck loading;
- provider depletion and recipient priority;
- Supply Depot logistics registration;
- Resupply command generation;
- resource conservation through inventory-backed transfers;
- debug read-model visualization.

All tests execute without requiring a graphics client, preserving headless simulation validation.

## Future Extension

Maintenance is intentionally not part of the first battlefield supply implementation.

The current provider, priority, read-model, and inventory-backed transfer boundaries are designed so Maintenance can be added later without replacing Fuel/Ammunition logistics or introducing a second supply scheduler.
