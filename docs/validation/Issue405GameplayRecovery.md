# Gameplay recovery (#405)

## Behavior and compatibility

Nearby off-axis queue endpoints enter the normal physically checked local approach. Transit projection retains the original requested root for final approach. A zero-horizontal-length route segment can anchor an already accepted physical movement without advancing route ownership. Geometry sweeps, FIFO admission, fuel costs and dock service authority remain in force.

Industrial hauling can grow after the first scout and live minimum supply. Its pending Cargo request remains protected until the vehicle actually exists. Mechanized research stock funding begins after those same live scout/supply prerequisites; research affordability still protects every unpaid production input and the Cargo/Supply replacement reserve.

VehicleRoutingKernel.AlgorithmRevision advances to 3 and authoritative state schema from 17 to 18. Earlier authoritative saves/replays are rejected by the existing compatibility checks. Payload schema 11 and envelope schema 1 are unchanged. Updated checkpoint hashes retain independent capture equality.

## Fixture contracts

Bridge coverage rejects legacy free restoration and verifies exact spending of the finite 120 Steel / 20 Electronics engineer kit before timed repair reopens an identified bridge. Intelligence fixtures compare the copied coarse reported coordinate. Placement uses the current building-selection API.

Construction-policy fixtures explicitly author their required third Cargo vehicle and, immediately before the isolated decision, the live Scout/Tank/Artillery roles required by CanPlanExpansion. Runtime starting vehicles and stocks remain unchanged. The strategic-loop test retains both sides' power/extraction/industry/scout prerequisites and now requires an actual attack from forward industry by either asymmetric opponent, rather than requiring the eventual loser to build a second hub before defeat. Its 40,000-tick limit is unchanged. The paid research test retains 60,000 ticks; the natural terminal gate retains 80,000 ticks, seed 2026 and require-terminal.

Dock and resupply fixtures cover measured physical clearance/arrival. The automatic smelter fixture allows a complete authored processing cycle before asserting OutputFull and retains the authored factory refill target floor.

## Local qualification checkpoint

- 50 focused movement/gameplay cases and 30 affected UI contract cases passed.
- The complete combined local Game suite passed 1,249 of 1,253 cases. Four remaining failures identified the two incomplete construction-policy arrangements, the symmetric-hub assumption, and the normal-stock research funding delay. Their final corrections are being rechecked separately; this checkpoint is not a claim of complete final CI success.
- The natural two-opponent terminal run ended at tick 59,098: Victory, CommandCoreDestroyed, player 1, zero cargo failures and zero route failures. Raw report and executed assembly identities are under issue405/. This run precedes the final earlier research-funding adjustment and is explicitly scoped to that local combined build.

The current PR and issue #405 retain the final exact-source CI result. The required full build-test, including its natural terminal gate, must pass before integration. No native renderer qualification is claimed by these CPU/headless checks.
