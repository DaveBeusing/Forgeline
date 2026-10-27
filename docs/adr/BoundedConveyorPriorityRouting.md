# Bounded conveyor priority routing

## Decision

Extend the existing ConveyorSimulationSystem with one optional policy on an owned
Splitter. Default Balanced preserves round-robin distribution. Logistics Coordination
unlocks preference for physical output A or B using a four-turn service cycle
[preferred, preferred, preferred, other]. Do not add a parallel network, priority
resource inventory, future-throughput credit or general logistics scheduler.

A successful turn uses the existing endpoint quantum and all ordinary resource,
power, damage, reservation, retention, truck-commitment and capacity checks.
Only real transfers advance the persistent cursor. Each arbitration scans at most
four service slots over the existing maximum two splitter outputs. A blocked or
unoffered branch yields immediately without accumulating work or credit.
The one-in-four guarantee applies locally while both branches remain eligible and
offered; it is not a universal end-to-end delivery or equal-volume guarantee.

## Authority and interaction

ConveyorRoutingState belongs to the simulation ECS and holds enum preference,
uint policy revision and modulo-four cursor. No component is needed for a default
untouched splitter. The command creates/changes the component at its normal tick
and resets its service cursor. Flow changes only the cursor, retaining the revision.

The player gateway transports session/source/tick ownership. The typed command
validates complete target generation, running match, owner, Splitter type, living
health, preference enum, expected revision and the technology capability. It never
changes stock or movement. uint revision exhaustion fails instead of wrapping.
Balanced is available without research; capability absence also causes fair flow.

Completed snapshots copy scalar state and per-option authoritative failure values.
The object ring reuses Logistics, with exactly three direct choices, full entity and
revision item keys, ordinary pending feedback and A/B port markers. Presentation
only submits commands. No technology gate is duplicated as a separate UI rule.

A/B refer to authored physical output sockets even when one link disappears.
Successful-service cursors and policies survive idle periods and repair, but die
with replacement/removal of the entity. Derived topology/observation caches remain
non-authoritative and bounded by their existing network/label limits.

## Content identity and compatibility

The registered scenario content digest includes catalog descriptions. Preserve its
historical Logistics Coordination text exactly; the copied technology action view
projects the current ConveyorRoutingPolicy.EffectDescription. Catalog IDs, costs,
durations and map/content artifacts are unchanged. Authoritative availability is
the capability/policy check, never the descriptive catalog string.

New captures use authoritative schema 11, payload schema 9 and envelope 1.
The new recorded command contains full target, typed preference and expected policy
revision, plus existing ordered command metadata. The ECS checkpoint includes policy
and cursor. Ordinary reconstruction replays both accepted and rejected command
attempts. Earlier authoritative schemas are explicitly incompatible before runtime
reconstruction; no historical checksum or saved preference is rewritten.

## Delivery boundary

No tests or qualification runs accompany this implementation, per its scope.
Compiler success and manual native/flow/recovery acceptance remain outstanding.
Existing tests and required gates are unchanged. Historical conveyor performance
and correctness results are not attributed to this source. The opponent still has
no authored splitter layout and does not invest in this player-only policy.
