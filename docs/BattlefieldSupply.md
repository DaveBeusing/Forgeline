# Battlefield Supply

## Purpose

Battlefield supply connects industrial production to operational military capability through physical Fuel and Ammunition inventories.

The system keeps the player-facing model readable while preserving authoritative quantities underneath:

- Fuel is consumed by movement.
- Ammunition is consumed through the combat-facing ammunition hook.
- Supply Depots receive stock through the existing regional logistics network.
- Supply Trucks normally load when physically near an operational Supply Depot; a friendly Command Core is a deterministic fallback loading source when needed battlefield stock is unavailable from depots.
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

The operation consumes `ResourceIds.Ammunition` from the unit's physical inventory and fails closed when insufficient Ammunition is available. Weapons use this same inventory-backed state rather than introducing a parallel ammunition pool.

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

Supply Trucks are explicitly excluded from the regional automated-distribution truck pool. Their normal battlefield loading source is an operational friendly Supply Depot within loading range. A friendly Command Core may also act as a fallback loading source when it is physically within the same loading range and holds Fuel or Ammunition the truck currently needs. This recovery path transfers real inventory stock; it does not create resources or bypass movement.

The fallback prevents battlefield recovery from deadlocking when regional depot stock is temporarily exhausted while real reserve stock still exists at the base. Supply Depots remain the preferred forward logistics layer, and Supply Trucks still have to travel to a valid loading source before their cargo can be replenished.

This separation prevents carried Fuel from being confused with the truck's own propulsion Fuel. Available propulsion Fuel excludes quantities already reserved in the inventory. A stocked cargo compartment cannot fund a trip whose propulsion inventory is empty, missing, or insufficient.

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

The command validates ownership and supply eligibility, uses `BattlefieldResupplyPlanner` to select a provider, and stores a real `ResupplyOrder`. A mobile recipient approaches the provider. When the recipient cannot afford that trip or is immobilized, an eligible Supply Truck may instead approach the recipient.

Navigation and `GroundMovementSystem` remain authoritative for movement. The command never teleports resources or changes transforms. Already-in-range supply does not require either party to fund a journey.

When the requested supply is satisfied, the resupply-specific movement/navigation state is cleared. Rescue movement owned by the provider is released at the next automatic-decision boundary when its recipient order no longer exists.

## Rescue Feasibility and Order Ownership

Rescue admission has two stages. The planner first rejects trips whose direct horizontal approach already exceeds available propulsion Fuel. This is a rejection bound, not a claim that a route around obstacles is affordable. Providers refueling themselves, serving another assigned recipient, or executing unrelated movement are not redirected into a new rescue.

`SupplyRescueAssignment` identifies the recipient and the exact provider movement order owned by that rescue. `AutomaticResupplyDecisionSystem` refreshes the provider's available propulsion Fuel and consumption rate before navigation. This also services deliberate rescue assignments whose provider does not itself have an automatic policy.

`HierarchicalNavigationSystem` reuses its completed hierarchical path to check the full horizontal waypoint distance and final local approach before issuing the first waypoint. There is no second global pathfinder. A missing current budget defers the rescue rather than certifying unknown travel. During automatic recovery, the remaining path is rechecked against current Fuel. Topology-driven replacement paths pass through the same admission check. Local steering can change actual travel cost, so passing admission is not an unconditional guarantee of arrival.

`SupplyRescueRejection` records the last affected recipient, reason, positions, required and available Fuel, and simulation tick. Route rejection suppresses immediate reselection of the same stationary provider/recipient pair for 20 ticks; changed positions or enough newly available Fuel permit an earlier retry. The record is bounded to one per provider. Another eligible provider may be selected through the normal deterministic planner.

Cancellation, recipient loss, replacement, and completion release only movement that still belongs to the rescue. A newer unrelated movement order must survive, including when it overlaps an older pending or active route. Rescue planning creates no inventory reservations and changes no resource quantities. It therefore has no separate stock reservation to leak or refund.

## Conservation and Failure Semantics

Battlefield resupply must conserve physical resources.

For every transfer:

`source before + destination before = source after + destination after`

except for explicit consumption such as movement Fuel or weapon Ammunition.

Disabled depots do not provide supply. Missing inventories are treated as unavailable. Insufficient provider stock leaves the recipient partially supplied rather than fabricating the missing quantity. Route failures and rescue-budget rejection do not consume cargo or propulsion Fuel by themselves.

## Diagnostics

`BattlefieldSupplyMetrics` exposes:

- supplied/low/critical/unsupplied unit counts;
- Fuel and Ammunition transferred during the current tick;
- cumulative Fuel and Ammunition transfer quantities.

`BattlefieldSupplyDebugSnapshot` exposes per-unit Fuel/Ammunition fractions, supply state, priority, active resupply provider, provider stock, provider type, range, and world position. Snapshot capture is opt-in through `DebugCaptureEnabled`, so normal simulation ticks keep aggregate metrics without allocating debug read-model arrays.

The development debug visualization renders provider ranges, stock labels, Low/Critical/Unsupplied unit markers, and active resupply links. Presentation only renders read models. It never owns or mutates supply simulation state.

The headless progression observer distinguishes a truck's carried Fuel from its propulsion Fuel, records factory-local input deficits and industrial production/extraction state, and retains the first loss of minimum offensive eligibility. Inspect `SupplyRescueRejection` for route admission failures rather than interpreting a completed geometric path or a successful Cargo Transport counter as proof of battlefield rescue completion.

## Validation

Regression coverage includes movement Fuel consumption, zero-Fuel immobilization and post-refuel resumption, Ammunition consumption and insufficient-ammo failure, Supply Truck loading from depots and Command Core fallback stock, provider depletion and recipient priority, Supply Depot logistics registration, Resupply commands, resource conservation, and debug read models.

`SupplyRescueFeasibilityTests` covers unaffordable propulsion, a feasible alternative, reserved propulsion stock, missing propulsion inventories, cargo/propulsion separation, already-in-range transfer, and competing recipients. `SupplyRescueRoutingTests` covers an unaffordable real detour, alternative-provider selection, topology-driven replanning, cancellation and recipient/provider loss, changed propulsion stock, and preservation of a newer movement order. Route-admission tests intentionally omit ground movement to prove that rejection happens before any travel or consumption.

All tests execute without requiring a graphics client. Passing focused supply tests is not evidence that a complete skirmish naturally reaches victory; the independent terminal-match gate remains required.

## Future Extension

Maintenance is intentionally not part of the first battlefield supply implementation.

The current provider, priority, read-model, and inventory-backed transfer boundaries are designed so Maintenance can be added later without replacing Fuel/Ammunition logistics or introducing a second supply scheduler.

## Artillery Resupply

Artillery uses the existing `AmmunitionState` inventory and is therefore a normal Battlefield Supply recipient. When an active fire mission cannot remove its configured Ammunition cost, the mission enters `NoAmmo` but remains valid. `BattlefieldSupplySystem` may replenish the artillery inventory in the normal Supply phase through a `SupplyProvider` or Supply Truck. The artillery system observes the replenished inventory on a subsequent Combat tick and continues the same mission until its requested round count completes. No artillery-specific ammunition pool or transfer path exists.

## Tactical Automatic Resupply

`AutomaticResupplyPolicy` defines Fuel and Ammunition fraction thresholds for units that may autonomously request supply.

`AutomaticResupplyDecisionSystem` never grants resources. It uses the shared `BattlefieldResupplyPlanner` to select an enabled friendly provider deterministically and creates the same real `ResupplyOrder` plus normal `MovementOrder` used by deliberate resupply.

`ResupplyCommand` uses that same planner, keeping manual and tactical provider-selection semantics consistent. Actual quantities remain transferred only by `BattlefieldSupplySystem`. Tactical combat yields to an active resupply order, and stored AttackMove/Retreat intent can continue after supply completion.

See [Combat Orders, Tactical Behavior, and Readiness](CombatOrdersAndReadiness.md).

## Human Player Supply Controls

The shared action surface exposes battlefield supply for one selected owned supply-capable unit.

- `Y` opens the supply view.
- `Tab` selects automatic Fuel threshold, automatic Ammunition threshold, or explicit Resupply.
- Left / Right changes the selected automatic threshold in bounded steps.
- `M` toggles automatic resupply and submits the policy through the player command boundary.
- `Enter` applies the edited automatic policy on a threshold row or submits the existing explicit `ResupplyCommand` on the Resupply row.

The read model reports current supply state, Fuel/Ammunition fractions, selected provider, provider stock, provider state, and `ResupplyProviderRejection` flags. Provider states are descriptive only: Assigned/Traveling does not promise successful transfer. Empty or destroyed providers, route retry, provider Fuel infeasibility, busy providers, and unavailable stock remain normal authoritative outcomes.

Actual Fuel and Ammunition move only in `BattlefieldSupplySystem` at legitimate transfer range. Automatic and explicit requests use the same `BattlefieldResupplyPlanner`; the player surface does not create a second rescue or transfer implementation.
