# Vehicle loading queues

Building loading uses one FIFO roster shared by Cargo Trucks and Supply Trucks
and the existing exclusive `VehicleDockingService`. The roster does not own a
second service bay. The current building catalog provides one compatible bay per
building. Cargo pickup, cargo delivery and supply loading retain distinct roles,
inventories and orders.

A remote order does not reserve the bay. A vehicle registers after reaching the
local approach or a validated holding area. Confirmed arrival tick orders the
roster; equal ticks use the complete stable entity identity. Admission waits until
the following tick so system iteration order cannot decide equal-tick service.
Each registration has its own generation. Stale removals cannot release a newer
registration or a recycled entity.

The queue front connects to the authored dock entry. Waiting targets use actual
instantiated vehicle lengths and the larger adjacent separation radius. The
available row is finite: terrain bounds, sampled footprint slope, navigation,
static structures, authored road corridors and production spawns constrain its
capacity. Twenty is the upper bound, not a promise of twenty physical spaces.
Overflow uses individually validated holding positions outside the row; a truck
stops with a geometry reason if no safe place exists.

Vehicles reach their targets through normal locomotion. Assigned participants
cannot advance through their predecessor in the local row. Ordinary passing
outside that row remains available. Slow following applies near a follower's
assigned place; the head and distant approaches retain authored movement limits
and existing precision braking. A stable target retains its navigation intent,
including the transition to local alignment.

Only the confirmed head can claim a free bay. The bay still requires two distinct
settled observations before finite timed transfer. Ownership includes the entire
entry, service and exit conflict area until the rear has physically cleared it.
There is no timeout that releases an occupied exit. Empty source and full target
use the existing controlled exit and bounded retry, retaining real vehicle cargo.

Manual movement, cancellation and battlefield resupply remove queue assignments
through the docking lifecycle. A cancelled bay owner retains physical exclusion
until clear. Dead, destroyed or recaptured participants lose logical queue
membership; a remaining wreck still obstructs physical clearance. A fresh match
creates fresh ECS state and docking services.

Owned selection snapshots copy registration identity, service role, position,
waiting count, dock state, waiting cause and change tick. Building selection also
copies aggregate waiting and occupied/blocked-exit information while preserving
production work. Foreign selection and multi-selection do not disclose individual
queue details. These values are observations, not delivery-time promises.

An order backlog, graph throughput reservation, stock reservation, physical queue
assignment and exclusive bay occupancy are different resources. Queue membership
creates no inventory and spends no graph capacity by itself.

Save/replay payload schema 4 and authoritative state schema 3 identify these
simulation semantics. Pre-queue authoritative checkpoints are rejected before
reconstruction. Historical fixtures and hashes remain historical evidence.

The reproducible measurement entry point is
`dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks -c Release -- --vehicle-service <report.json>`.
It retains the original 100/500 transport and 1,000 movement workloads and adds
2,000 observed ticks of shared building service at 100/500 transports. Reports carry
the actual Game assembly provenance, time, allocation, path counts and completed
deliveries. They do not invent a new timing or delivery budget. Qualification of
one binary does not qualify a different source head or a later native package.
