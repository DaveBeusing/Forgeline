# Cargo Convoy and Escort Operations

## Scope and ownership

Cargo convoys are optional fixed-tick regional movement coordination. They do not replace `CargoTransportOrder`, `CargoTransportRouteState`, inventories, throughput reservations, Cargo Truck fuel, vehicle docking or the physical FIFO queue. A convoy is its own generation-aware ECS entity; the group carries owner, route anchors, network version, leader, status and tick identity. Trucks retain their individual cargo order and every resource transfer remains owned by the existing loading/unloading lifecycle.

The current functionality uses the existing regional GroundRoad route selected for each truck. Formation planning and the hierarchical pathfinder remain authoritative for their normal callers. Convoy coordination does **not** submit competing `MovementOrder` instances to trucks, insert artificial cargo stops or transfer/teleport materials.

## Player controls

With at least two owned cargo vehicles selected, open the existing **Group** radial page and choose **Create Convoy** (the final group action normally used to focus the selection). Other selected owned combat-capable units become optional escorts. The offer comes from current owner-filtered order and combat read models; simulation revalidates every entity, its full identity, route and control rights at dispatch.

All cargo members must have current `ToDestination` orders with exactly matching source, destination, graph version, route edges and active segment. No truck may already belong to a convoy or be in a dock, fuel service or rescue. Duplicate selections, foreign/stale IDs, an insufficient number of trucks, occupied escort combat orders and incompatible routes reject the entire request without partial membership or inventory mutation. The existing logistics command feedback reports whether the authoritative command succeeded.

Select a member truck and open Logistics to see the copied **CVY** count and Rendezvous/Holding/Moving/Disrupted status. **DISBAND** is the existing Cargo Logistics Cancel control while a convoy is selected; disbanding removes only convoy membership and convoy-owned locomotion constraints, never cargo orders or physical bookings.

## Movement and escorts

A convoy initially rendezvouses under individual pathfinding and normal road steering. The lowest full entity identity is the leader. Members are pace-limited to the slowest participating truck. When the formation stretches beyond its safety spacing, the leader may hold briefly, with movement-state reset to avoid false stuck detection; followers continue on their existing movement targets. Convoy motion does not overrule fuel, suppression or other higher-authority movement restrictions.

Escorts are validated owned combat-capable ground units, not trucks or foreign contacts. Escort following issues normal owned movement commands toward a position behind the convoy leader only when the target has materially changed and after a bounded tick interval. It does not create combat targets or infer hidden enemies. An explicit subsequent combat/hold/retreat command or independently replaced movement order removes that escort from follow control. Disbanding cancels only an escort movement intent that still has the exact convoy-issued tick identity, not newer player orders.

## Recovery and dock safety

Membership is revalidated every tick. Destroyed, foreign, failed or unavailable cargo members are removed with their physical cargo fate remaining with `CargoTransportSystem`. If fewer than two trucks remain, the temporary group is retired and survivors follow their individual orders. A route graph version change, member fuel-service/rescue, loss of propulsion authorization, wide route-segment divergence or approaching an actual building dock immediately dissolves the convoy. This makes the **existing** dock geometry, FIFO queue, loading order and exit service entirely responsible for each truck and prevents a group from reserving or obstructing a service bay as a unit. No member is teleported or reordered to recover.

A group status is informational and must not become a material-transfer trigger. No synthetic resource or transport capacity is granted. No new conveyor or economic recipe behavior is introduced.

## Contracts and verification

- Core lifecycle: `CargoConvoyGroup`, `CargoConvoyMember`, `CargoConvoyEscort`, `CargoConvoyMotionConstraint`, `CargoConvoySystem`
- Authoritative input: `PlayerLogisticsActionCommand` via `PlayerCommandGateway` and `PlayerActionRequestDispatcher`
- Presentation: completed-tick, generation- and ownership-filtered `CargoConvoyReadModel` in the existing stock/cargo selection; no independent simulation truth
- Tests: regional same-route acceptance, duplicate/foreign/stale rejection, membership cleanup and cargo conservation, network invalidation, leader/escort interruption, gateway feedback, existing physical dock FIFO and ordinary transport regressions
- Validation: focused Game and Presentation tests before full Release build, Microsoft.Testing.Platform solution test suite and required `build-test`, Movement quality, scalability, save/replay and natural terminal-match gates

The system's goal is deliberate, physically believable pacing; it does not promise that two independent cargo trucks share a single hierarchical-navigation computation. Performance measurements must distinguish convoy coordination overhead from existing per-truck pathfinding and may not count route reuse that did not occur.


## Reproducible 10/50/100 performance qualification

`CargoConvoyBenchmarks` isolates one temporary convoy containing equal numbers of 10, 50 or 100 synthetic pre-routed trucks and ground escorts. It benchmarks twenty 20 Hz coordination ticks and a bounded owner-filtered capture. Each source fixture uses a real generation-aware entity, a real LogisticsNetwork route and the production command/convoy systems. Physical transfer, navigation path search, fuel consumption and bay occupancy are **not executed** in this isolated fixture; the existing CargoTransport, Formation and navigation benchmarks cover those domains independently.

Run on the configured SDK and Release build:

```powershell
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release -- --filter '*CargoConvoyBenchmarks*'
```

Record wall time, allocated bytes, hardware, scenario parameters and comparative cargo/movement baselines before proposing a performance threshold. No measured throughput, frame-time target or shared-path speedup is claimed solely because the benchmark source compiles.
