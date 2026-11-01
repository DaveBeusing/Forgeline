# Conveyor simulation and vehicle interoperability

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Reliability qualification: **0.1.462**, [PR #341](https://github.com/DaveBeusing/Forgeline/pull/341),
based on `master` `b397bd6f19384e4a8a20aea11f9291f918201943`.
The original finite-flow implementation arrived in [PR #302](https://github.com/DaveBeusing/Forgeline/pull/302).
The executable contracts and measured workloads below supersede its compile-only
qualification. Automated behavior, hosted CI and native visual/hardware acceptance
are recorded separately; an unreached required gate is not a pass.

<details>
<summary>On this page</summary>

- [Flow authority](#flow-authority)
- [Activated building ports](#activated-building-ports)
- [Trucks, stock policy and recovery](#trucks-stock-policy-and-recovery)
- [Vehicle clearance and presentation](#vehicle-clearance-and-presentation)
- [Persistence and reconstruction](#persistence-and-reconstruction)
- [Material accounting and production chains](#material-accounting-and-production-chains)
- [Historical implementation evidence](#historical-implementation-evidence)
- [Active-network measurements](#active-network-measurements)
  - [Allocation correction and equivalent workloads](#allocation-correction-and-equivalent-workloads)
- [Historical integration dependencies](#historical-integration-dependencies)
- [Synchronized integration checkpoint](#synchronized-integration-checkpoint)
- [Reproduction and acceptance boundaries](#reproduction-and-acceptance-boundaries)
- [Bounded player priority routing](#bounded-player-priority-routing)
  - [Arbitration and starvation bound](#arbitration-and-starvation-bound)
  - [Commands, snapshots and recovery](#commands-snapshots-and-recovery)
  - [Acceptance still required](#acceptance-still-required)

</details>

## Flow authority

`ConveyorSimulationSystem` runs in Economy after extraction, with no presentation,
Windows or rendering dependency. Constructed modules retain their entity ID,
generation, owner, geometry, health and explicit building attachment. Each active
module owns an InventoryStore inventory, restricted to seven stable resource IDs:
Ferrous Ore, Silicates, Steel, Electronics, Ammunition, Volatiles and Fuel.
Volatiles and Fuel are an interim conveyor exception for the existing Refinery
until pipelines are implemented. Units and unknown resources remain excluded. Supports and caps have no inventory or drive.

| Module | Total buffer | Maximum ingress and egress |
| --- | ---: | ---: |
| 4m short belt, either building adapter | 100 | 100 units/s |
| 8m standard belt, either curve, either ramp | 200 | 100 units/s |
| Merger, splitter | 100 shared across resources and ports | 100 units/s total |

Rate is multiplied by the actual fixed tick duration, allocated power fraction and
health fraction below 70% integrity. Every carrier draws 2 power, a junction 4,
on the construction payer's existing player power network, at Industrial priority.
Missing membership or zero allocation stops flow; brownout reduces throughput.
A destroyed foot stops carriers attached at that socket until repaired. Current
balance constants are provisional, not a claim of runtime tuning.

Every move uses atomic InventoryStore.Transfer: source availability excludes all
reservations and the sink enforces its real remaining shared/resource capacity.
The tick snapshots available stock per inventory before transfers. Incoming goods
cannot depart again during that tick, preventing entity-order-dependent traversal
of an entire line. Continuous quantities preserve fractional remainders, without
discarding small cargo or accumulating offline throughput credit.

Inputs, outputs and resources have persistent round-robin cursors. Each arbitration
round selects one eligible sender per receiver and one requesting output per sender;
a successful transfer advances both port cursors and the resource cursor. Quanta are
at most one unit. Each node divides its actual budget into `ceil(budget)` equal
quanta; a transfer uses the smaller endpoint quantum. This avoids a recurring
60/40 split from the fractional tail of a 30 Hz tick. Competing merger inputs
alternate; splitter outputs alternate; blocked/rejecting/offline branches are
skipped and the remaining branch can use the shared 100/s budget. A deterministic
bounded number of rounds covers the tick budget, two-port contention and fractional
tails of the seven eligible resources. Cursors
advance only for actual transfers, surviving idle periods and replay.

Connections require the same owner, complementary direction, opposite facing and
position/height within 0.02m. A building connection additionally requires the correct
adapter's exact building entity/generation attachment. Ambiguous coincident sockets
fail closed. Connection lookup is spatially bucketed; the graph and resource reachability
are rebuilt from authority each tick, including removal, repair and building death.
A branch receives a resource only when a matching building input is reachable.
Full/offline destinations retain that compatibility, allowing bounded backpressure.
Cycles without a compatible sink admit no new cargo. Network IDs are derived from
the minimum full entity identity in each connected component; there are no road edges.

## Activated building ports

Authored tier-0 sockets are relative to the building's ground origin and rotate
with its actual pose. Fixed-role buildings retain INPUT at `(-width/2, 5.8, 0)`
facing west and OUTPUT at `(width/2, 5.8, 0)` facing east where supported.
**Command Core and Storage Depot have four keyed physical positions each:**
`west.primary` at `(-width/2, 5.8, 0)`, `east.primary` at
`(width/2, 5.8, 0)`, plus `west.secondary` and `east.secondary`
at the same X coordinates with local Z -6m for Command Core or -4m for
Storage Depot. The primary positions preserve the former exact attachment
coordinates. Each position accepts **either** a building-input adapter or a
building-output adapter, never two simultaneously. The connected adapter
resolves and locks the direction until it is validly removed/reconnected.
The +Z loading lanes remain reserved. Only completed, living, owned buildings
with exact authored dimensions expose these ports; unsupported variants do not.

| Building | Expected width,height,depth | Input | Output |
| --- | --- | --- | --- |
| Command Core | 20,12,20 | Eligible solids/packages | Same, respecting retained stock |
| Extractor | 12,8,12 | None | Its actual eligible extracted resource |
| Storage Depot | 14,8,14 | Eligible solids/packages | Same |
| Smelter | 18,10,16 | Actual supported recipe ingredients: Ferrous Ore | Actual completed recipe outputs: Steel |
| Refinery | 18,10,16 | Volatiles, real production input inventory | Fuel, real production output inventory |
| Electronics Fabricator | 18,10,18 | Actual recipe: Silicates | Actual recipe: Electronics |
| Ammunition Plant | 18,10,18 | Actual recipe: Steel, Electronics | Actual recipe: Ammunition |
| Logistics Hub | 18,9,18 | Eligible solids/packages | Same |
| Supply Depot | 16,8,16 | Ammunition | Ammunition |
| Barracks | 18,8,16 | Eligible costs of supported unit recipes | None |
| Vehicle Factory | 26,12,24 | Eligible costs of supported unit recipes | None |

Production input and output ports bind distinct ProductionFacility inventories;
unit facilities bind their real input inventory. Extractors bind their actual output
InventoryStorage and require the matching extractor owner/resource. Storage, hub and
supply ports use the existing authoritative shared inventories. No proxy stock,
recipe completion shortcut, invisible external shipment or synthetic unit cargo exists.
The Refinery exposes exactly one fixed west INPUT and one fixed east OUTPUT at
5.8m above local ground, rotated with its authored footprint. Its actual
powered recipe consumes 10 Volatiles and produces 10 Fuel over 30 ticks at
full power. These are authoritative inventory transfers, not conversion by
conveyor. The +Z truck dock remains open. Power Plant and Radar have no ports.

## Trucks, stock policy and recovery

The explicit hybrid handoff is the existing Storage Depot, Logistics Hub or Supply
Depot inventory. Cargo Trucks bring regional goods through normal docks; local
conveyors consume available unreserved stock. Ammunition delivered by belt can be
loaded into Supply Trucks through the normal depot system. Volatiles and
Fuel may travel between compatible owned storage and the Refinery via powered
local belts, but no Fuel enters the ammunition-only Supply Depot conveyor port.
Regional/front routes and existing Fuel/Volatiles Cargo Trucks continue through
their existing vehicle queues, reservations, fuel consumption and road rules.

Enabled stock policies retain their DesiredTarget in shared source inventories.
At a destination with a policy, conveyor receipt is limited to DesiredTarget minus
current stock minus already committed Assigned/InTransit truck quantities. Pending
truck demand is reconciled against subsequent actual receipts by the existing
scheduler. The belt creates no second logistics request. In-flight belt stock is a
finite upstream buffer, not a promise of destination delivery; a truck may cover a
shortage while the belt waits. A request for 80 may result in an accepted 40-unit
truckload: only those 40 reserve destination demand. Once 20 have actually unloaded,
only the remaining 20 are committed; the first 20 are already destination inventory.
The accepted CargoTransportOrder destination remains authoritative if its stock policy
is retargeted. Full physical loading transitions Assigned to InTransit even when the
reservation observer has already reduced the source-reservation quantity to zero.
Without a stock policy, actual capacity is the limit. Production's separate output
inventory does not retain its input targets.

A full/rejecting/downstream-offline sink stalls the immediate buffer, then bounded
upstream buffers. Freeing stock or repairing/reconnecting the route resumes service
without an offline catch-up burst. Replace continues to refuse occupied parts.
Authorized player Remove and selected Delete demolition may remove a loaded owned
carrier or junction: the current InventoryStore quantities (not delayed visual
buffer values) determine an exact per-resource 50% salvage and 50% destruction.
Any outstanding cargo reservation or unsupported resource rejects that particular
removal without mutation. There is no new construction Steel refund.
The recoverable half first enters one bounded owner-scoped 4,096-unit inventory;
the Economy phase transfers it through InventoryStore to a valid owned Command Core,
then storage depots in stable entity order. When no recipient has room, the half
remains in that persistent recovery inventory; when even the recovery inventory
cannot accept the complete half, removal fails closed instead of discarding it.
Existing replacement, placement, power and support rules remain unchanged.
Empty buffer inventories are destroyed with their removed module. Replacing the
same adapter without new endpoint arguments preserves its explicit full EntityId
attachment in both preview and execution. An explicit new endpoint is validated
normally; reusing an entity index does not make an old attachment valid.

Combat retains a destroyed conveyor entity, health-zero wreck, buffer and footprint.
A wreck stops transfer and its power consumer is disabled. Repair works with cargo
present: nonfatal repairs cost 4 Steel; a wreck costs its original paid module Steel.
It restores health and drive, retaining the same inventory and entity. Deliberate
demolition of an owned occupied wreck uses the same half-cargo recovery rules;
combat destruction itself never triggers salvage. Destroyed support feet remain real ground
obstacles until repaired or safely removed, avoiding lost stock or invisible clearance.

Demolition preview exposes the held/recovered/lost amounts by resource. Accepted
Delete/Remove results report the authoritative recovered and lost amounts separately
from construction refunds. Salvaged inventory can still be waiting for capacity.
The owner's bounded recovery inventory is captured as ordinary ECS/inventory
state; cargo transfers and repeated command rejection remain replayable under
authoritative schema 19. Older authoritative checkpoints are not reinterpreted.

## Vehicle clearance and presentation

The existing StrategicInfrastructureSystem navigation rebuild excludes carrier AABBs
and includes actual support footprints. Current intact standard spans and ramps have
a minimum 5.2m underside. Placement samples terrain and requires 5m clearance; an
insufficiently high ramp/transition is rejected, rather than projected onto the ground
or allowed to clip a vehicle. Feet remain excluded from road corridors and loading
lanes. Ground movement also checks the actual vehicle upper bound against the raised
carrier lower bound during obstacle steering, static penetration and swept movement.
Dock reservation applies the same clearance condition to the planned vehicle poses.
Only typed non-support conveyor pieces receive this clearance treatment; real feet,
ordinary obstacles and vehicles too tall for the span remain blocking. No low ramp
or fallen span is introduced.

Owned completed-tick snapshots copy buffer resources, ingress/egress, capacity,
connection entity IDs, network identity, active time, health/power status and causes.
Flowing, idle/no-input, disconnected, blocked, congested, offline, damaged, critical
and destroyed select existing kit state meshes/materials. Conveyor mechanical clips
use simulation ActiveSeconds; stopped ticks do not advance them. Foreign topology,
buffers, throughput and private animation phase are not published.

Hover shows rates, cargo, connections and causes; Operations exposes buffers, power,
nominal capacity, measured utilization and local links within existing list bounds.
Selecting or hovering an owned carrier adds a bounded box, output links and rate/
buffer label from the snapshot. Headless match report schema 3 includes the same
conveyor facts for all match participants for offline diagnosis.

## Persistence and reconstruction

The historical reliability qualification used authoritative state schema 5 and payload 6.
Current captures use authoritative schema 11, payload 9 and envelope 1 for the
routing policy below. That qualification did not introduce a schema change. Buffer inventory IDs,
stock/reservations, cursors, active time, measured state and attachments are included
in the existing authoritative ECS/inventory snapshot and hash. Existing recorded
ConveyorConstruction commands rebuild that state through deterministic replay;
derived topology and UI caches carry no authority. Older authoritative checkpoints
and replays are explicitly rejected as incompatible; there is no guessed migration
of pre-flow cargo.

`ConveyorPersistenceTests` builds paid networks in a complete MatchRuntime with the
original catalogs and Standard starting stock on a fixed 512 m terrain. Recorded
commands create the network, apply actual artillery damage, repair the occupied wreck
and supports, pause receipt through a stock policy, drain, replace an adapter and
remove/rebuild a connection. Tests serialize, deserialize and reconstruct both save
and replay at the cargo, wreck, repair and edit checkpoints. Complete authoritative
SHA256 hashes agree immediately and after another 1, 7 and 24 ticks. The controlled
composition is explicit and recreated by its resolver; the normal Gameplay-start
qualification is a separate production-chain case.

## Material accounting and production chains

The chain contracts use the existing ResourceExtractionSystem, ProductionSystem,
InventoryStore, CargoTransportSystem and BattlefieldSupplySystem. Recipes and normal
starting stock are unchanged. Every conveyor and support is built through a paid
ConveyorConstructionCommand. Controlled fixtures author small terminal layouts and
power allocations; one full Central Divide Gameplay case uses the original terrain,
real BuildCommands and unmodified construction costs.

For each resource independently, the ledger checks:

`closing inventory = opening inventory + extracted quantity - committed recipe inputs

+ committed recipe outputs - construction costs - recorded unit consumption`.

Inventory totals include source stock, production input and output inventories,
every carrier buffer, truck cargo, depot stock and unit supply inventories. Remaining
deposit stock is measured separately. Reservation quantities remain a subset of
inventory, not an additional source of material. Completed recipe cycles identify
actual input/output commits; an active input reservation is not consumption.

| Chain | Transactions and delivery assertions |
| --- | --- |
| Ferrous Ore → Smelter → Steel storage | Finite deposit 40; two original 10 Ore → 10 Steel cycles; 20 Steel reaches storage and 20 Ore remains at the production input. |
| Silicates → Electronics Fabricator → storage | Finite deposit 40; two original 10 Silicates → 10 Electronics cycles; 20 Electronics reaches storage and 20 Silicates remains at the production input. |
| Steel/Electronics → Ammunition Plant → Supply Depot → Supply Truck → Rifle Squad | Four original 6 Steel + 2 Electronics → 10 Ammunition cycles. The depot and physical Supply Truck carry 40 manufactured ammunition to a field recipient; loaded and delivered quantities are checked separately. Fuel reaches the depot by a real Cargo Truck and remains absent from all conveyor buffers. |
| Normal Gameplay startup | Standard starting resources pay for a Power Plant, two Storage Depots, a Smelter, bands and supports. An existing Cargo Truck supplies 20 Ore to the source depot; removing its retention policy releases stock to two original recipes and 20 Steel reaches the final storage. Catalog construction costs are subtracted per resource. |

The extraction and ammunition fixtures use seeds 17/917 and 20/30 Hz. Recovery also
covers 20/7 Hz, and persistence uses two independent seeds plus the artillery case.
The aggregate chain tolerance is `1e-7`, including the fixture's 200,000 Steel payer
stock and repeated binary arithmetic; focused transfer contracts use `1e-8` and test
a positive `1e-10` shipment with exact per-hop equality. No epsilon deletes cargo.
Fuel consumption is accounted from real vehicle displacement in the field-supply
chain; the Gameplay-start invariant covers the five belt-compatible resources.

## Historical implementation evidence

The results in this section and the measurements below precede synchronization
with master `4f1126dc`. They retain their original source identities and do not
qualify the synchronized branch. See the integration checkpoint below.

| Scenario | Implementation commit | Executed result | Remaining native acceptance |
| --- | --- | --- | --- |
| Mixed cargo, shared budgets, fractional quantities, one-hop progression, merger/splitter fairness, blocked branches, reservations and actual truck commitments | `92e13874c11a097f3992f2c6f86b3a0c046d9d19` | ConveyorFlowTests: 21 passed, 0 failed, 0 skipped. Fairness, tiny-tail capacity and committed-load regressions were first reproduced red. | Native flow indicators and animation readability. |
| Outage/brownout, bounded backpressure, support damage, occupied wreck/repair, remove/replace, ownership, generations, ambiguous sockets and incompatible/sinkless networks | `e1c763082a8c4053ad61e5e6cb81bd635c6ba214` | ConveyorRecoveryTests: 23 passed, 0 failed, 0 skipped, including actual spatial movement/docking. | Physical D3D12 driving, visual clearance and support readability. |
| Paid cargo checkpoints, actual artillery wreck, repair and live edits through Save/Restore/Replay, followed by more ticks | `e1c763082a8c4053ad61e5e6cb81bd635c6ba214` | ConveyorPersistenceTests: 3 passed, 0 failed, 0 skipped. Included in a 142/142 passing recovery/movement/docking run. | Native save/load presentation and damaged/repaired asset state. |
| Original Ore/Steel and Silicates/Electronics recipes, manufactured Ammunition through physical field supply, and paid normal Gameplay startup | `9e177b0a364108eef332ca7d86cd2e1d868fd5e2` | ConveyorProductionChainTests: 7 passed, 0 failed, 0 skipped. Separate resource ledgers include reservations, recipe commits, construction and actual unit consumption. | Native construction and the complete industrial/supply interaction. |
| Active and stopped network measurements at 100/1,000/4,096 modules, physical deliveries and finite recipe balances | `6408a4270726a2c1faeb0ffb916b9a5c7b4090e8` | The driver qualified 24/24 cases in each paired source configuration. Hosted CI produces its own current-head report. | Reference hardware and native full-match performance. |
| Per-tick allocation reduction, duplicate enabled policy bounds and policy disabling | `4d8f1c38431d3e25d3407bc403e90df92d49770d` | Flow plus policy contracts: 23 passed, 0 failed, 0 skipped. Both new policy cases also passed before the refactor. Paired workload digests and observations match exactly; detailed source provenance and results are below. | Native/full-match and reference-hardware performance. |

The individual test counts overlap with the combined suites; they must not be added
to those totals. Commit identifiers identify the implementation under test, not a
claim that later documentation changes affect execution.

Local Release builds of the integrated Game tests and simulation benchmarks use SDK
10.0.401 with zero warnings and zero errors. The complete unfiltered Game suite
executes 707 tests: 706 pass, one fails and none are skipped. Its only failure is the
independent baseline convoy defect #334 described below. The focused combined conveyor,
distribution, battlefield-supply and MatchPersistence run passes 117 tests, with no
failures or skips. The complete Simulation test project
passes 86 tests with no failures or skips. Project-reference validation covers all
44 projects and reports no cycles or architecture-boundary violations. The PR version
script advances 0.1.461 to 0.1.462 once; running it again against the unchanged master
leaves the prepared version unchanged.

## Active-network measurements

The dedicated `--conveyor-measurements` mode measures 100, 1,000 and 4,096 carrier
modules at 20 Hz, with linear and splitter/two-branch/merger topologies. Each case
warms for 256 ticks and measures 512. Powered cases include original Steel production
and 1/4/16 physical Cargo Trucks; the 5/50/205 production lanes use finite starting
Ore with no refill, teleport or in-window reset. Unpowered, incompatible-endpoint and
full-buffer cases deliberately stop conveyor flow while production remains active.
They are reported as stopped-state costs, not active logistics throughput.

Each report retains raw tick samples, allocations, GC counts, process CPU, module and
truck counts, recipe commits, delivered/lost cargo, real truck displacement and
qualification failures. The full tick and ConveyorSimulationSystem.Execute call
have separate timing/allocation regions. Observations and authority digests are
outside those regions; process CPU per observed tick includes their overhead.
Qualification rejects missing sustained powered flow, inactive production, absent
completed physical deliveries, incorrect stopped states, lost cargo or a failed
per-resource recipe balance. Hosted wall-time values are advisory.

The original `b397bd6f` baseline qualified all 24 cases on Ubuntu 24.04.3 x64,
SDK 10.0.401 / .NET 10.0.12, with 8 runtime-visible processors and
`DOTNET_TieredCompilation=0`. The host reports AMD EPYC 9V74; frequency/power settings
and reference-machine status are unknown. At 4,096 powered modules the original
linear case averaged 45.24 ms per complete tick (p95 60.12 ms), including 43.08 ms
inside conveyors and 16.85 MB conveyor allocation per tick. The split/merge case
averaged 43.45 ms per tick (p95 56.72 ms) and 17.02 MB conveyor allocation. These
measurements establish the allocation investigation in #308, not a hardware budget
acceptance. Baseline report SHA256:
`0a96576963dd6c68249baac9c955f597407d3f280c1aa0a879a17dcaa797d6ca`.

### Allocation correction and equivalent workloads

Diagnostic profiling attributed the largest per-tick allocations to node/port/socket
construction, connection matching and policy/snapshot limits. The bounded correction
computes sockets and compatible resource masks once per module/port in the tick,
aggregates enabled policy minima/maxima once per full entity/resource identity, and
uses unique-candidate counts instead of temporary matching lists and GroupBy arrays.
The graph and policy data are still rebuilt every tick. There is no persistent cache
to invalidate, and no transfer-order, quantum, budget, cursor or persistence change.
Duplicate policy tests verify the minimum destination target and maximum source
retention, including disabling either bound and observing the revised receipt or
retained stock.

The paired comparison isolates that allocation correction on top of the already
corrected flow/distribution semantics. It does not compare the old unfair arbitration
against the corrected behavior. Both builds used identical benchmark sources and
the baseline's 0.1.461 build metadata; combined functional validation uses 0.1.462.
These are controlled source measurements, not a binary measurement of the final PR
commit. Source and binary hashes, report hashes, configuration, all 24 case metrics,
GC counts and equality summaries are retained in
[ConveyorReliabilityMeasurements.json](ConveyorReliabilityMeasurements.json).

| Modules | Topology, powered | Complete tick mean before → after (ms) | Complete tick p95 before → after (ms) | Conveyor allocation before → after (bytes/tick) |
| ---: | --- | ---: | ---: | ---: |
| 100 | Linear | 1.0507 → 0.6993 | 1.3767 → 0.6235 | 412,596 → 227,873 |
| 100 | Split/merge | 0.4836 → 0.4428 | 0.7862 → 0.5603 | 416,726 → 230,244 |
| 1,000 | Linear | 5.3166 → 4.1668 | 7.2671 → 5.6244 | 4,103,406 → 2,283,731 |
| 1,000 | Split/merge | 5.3533 → 3.8216 | 7.3134 → 5.5477 | 4,142,787 → 2,305,513 |
| 4,096 | Linear | 50.8974 → 27.5176 | 74.0662 → 45.5681 | 16,892,658 → 9,414,322 |
| 4,096 | Split/merge | 47.6745 → 27.7626 | 66.0700 → 44.4829 | 17,060,382 → 9,505,319 |

All 24 cases qualify before and after. All 408 sampled authority digests and 12,288
per-tick module-output/truck-distance pairs agree exactly, as do counters and final
truck states. Powered measurement windows complete 65/650/2,665 original recipe
cycles and 1/4/16 physical deliveries at the three scales. At 4,096 powered modules,
Linear delivers 80 Steel and SplitMerge delivers 160 Steel; both retain belt flow
and truck movement in both halves.
No cargo is lost. Conveyor allocation falls 43.56–47.14% across all 24 cases.

The worst observed after-correction p95 complete tick is 45.5681 ms, but 28 of 12,288
ticks still exceed 50 ms (792 before). Shared-host wall timings remain advisory.
These results do not establish a universal 20 Hz budget, a graphical frame budget,
Windows/WARP performance or physical reference-hardware acceptance.

The [hosted Conveyor reliability run 39988636659](https://github.com/DaveBeusing/Forgeline/actions/runs/39988636659)
also qualified all 24 cases at exact source revision
`e972ccba181dbf31ef28bda15ea5a37f76de82cb`, after passing 117 focused Game and 11
Inventory tests. Its uploaded `conveyor-reliability` artifact records
`CompleteMatrix=true`, `Qualified=true` and no case failures. Raw JSON SHA256:
`a53c53ea99f09f1f3a13bfd16035dc4e81e121228e9a376af36f264d3051cedd`.
All compared authority samples, per-tick output/distance samples, counters and final
truck states also match the local after-correction report. This hosted run uses
Ubuntu 24.04.5, .NET 10.0.13 and four visible processors; its timings are a separate
host measurement and are not substituted into the paired comparison above.

## Historical integration dependencies

The unchanged baseline has a convoy command-precedence defect tracked by
[#334](https://github.com/DaveBeusing/Forgeline/issues/334). In
`ManualEscortMovementReplacesConvoyFollowWithoutCancelingCargo`, the prior pending
navigation intent wins over a newly accepted manual Move in the convoy phase. The
escort detaches one tick later than required; cargo orders remain intact. Reproduction
against the unmodified `b397bd6f` Game binary establishes that this is independent of
conveyor/distribution changes. The repair belongs to the existing parallel
[PR #340](https://github.com/DaveBeusing/Forgeline/pull/340).

At the `e1c76308` implementation checkpoint, the
[Frame hot paths run 39985936299](https://github.com/DaveBeusing/Forgeline/actions/runs/39985936299)
builds successfully and then reports 97 passing / 4 failing Operations tests in
Integrated interaction/display. The positive fixtures supply no render instances,
so the owned/visible full-identity focus guard correctly returns Invalid. The exact
failures are `ShortClicksSelectAndOpen`, both `ConfirmedServiceBlock` cases and
`OperationsClickRequiresRelease`. This existing fixture dependency is tracked by
[#339](https://github.com/DaveBeusing/Forgeline/issues/339); weakening the production
privacy guard is not part of conveyor qualification. The same checkpoint's
[Movement run 39985932736](https://github.com/DaveBeusing/Forgeline/actions/runs/39985932736)
passes. These checkpoint results do not substitute for checks on the final PR head.

At `e972ccba`, the [main CI run 39988636450](https://github.com/DaveBeusing/Forgeline/actions/runs/39988636450)
passes the complete Release solution build and early conveyor/inventory contracts,
then stops at step 27, UI audio reliability and industrial source contracts.
`UiInteractionFeedbackTests.WarmCopiedFeedbackRetainsZeroAllocation` expects zero
current-thread allocation bytes and observes 1,240; that invocation has 59 passing,
one failing and two explicitly native-skipped tests. The tested source and test
files are byte-identical to master, and the fixture invokes no conveyor, distribution
or vehicle simulation. The reason for that allocation is unresolved; this is not
attributed to a particular runtime mechanism. Later asset, WARP, full-solution-test,
package, recovery and natural-terminal steps in that job are unreached. The same
source's [Frame run 39988636471](https://github.com/DaveBeusing/Forgeline/actions/runs/39988636471)
independently reproduces the four #339 Operations failures. Subsequent PR checks are
reported in the PR; a checkpoint failure or an unreached gate is never recorded as a pass.

## Synchronized integration checkpoint

PR #340 is integrated into master `4f1126dcbeef9f41d3ffae0be6c0fc6951f71d46`.
Conveyor branch source `006dcd83300685ef51a7f663dc035cb65c160194` merges that exact
master while preserving the seven existing conveyor commits. The existing Operations
fixture and convoy command-precedence corrections are now present; the historical
#334/#339 failures above are not assertions about the synchronized source.

The shared construction path inherits a live adapter's full-generation attachment
when no endpoint override is supplied. Explicit replacement of a dead endpoint with
a matching live owned port preserves the occupied adapter entity, cargo, damage and
supports. A missing override cannot silently bind a reused building index. Both the
attachment-inheritance and occupied-reconnection regression groups remain in the
focused conveyor workflow.

Distribution retains accepted-load/destination accounting together with building
destruction cleanup. Both industrial/radar and conveyor/inventory focused CI gates
are retained. No required Windows/WARP, complete solution, package, recovery or natural
terminal gate is removed. The synchronized branch uses master's authoritative schema
6 and explicit schema-5 rejection; conveyor integration adds no new persisted fields
or schema transition.

The repository version-preparation script advances current master `0.1.462-dev` to
`0.1.463-dev`, one PATCH for this PR after synchronization. Current-source CI must
complete again. Earlier 117-test and 24-case successes above remain historical
evidence and cannot substitute for those runs. Physical D3D12 driving, visual review
and reference-hardware performance remain open under the acceptance boundary below.

## Reproduction and acceptance boundaries

Use the SDK and Microsoft.Testing.Platform runner selected by `global.json`:

```powershell
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class '*Conveyor*' ForgeLine.Game.Tests.AutomatedDistributionSystemTests ForgeLine.Game.Tests.BattlefieldSupplySystemTests ForgeLine.Game.Tests.MatchPersistenceTests --parallel none
dotnet test --project tests/ForgeLine.Simulation.Tests/ForgeLine.Simulation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Simulation.Tests.InventoryStoreTests
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release --no-build -- --conveyor-measurements artifacts/conveyor-measurements.json
```

Restore and build the projects first. For paired allocation/timing measurements, set
`DOTNET_TieredCompilation=0` for both hosts. The main `build-test` workflow retains all
existing full-solution, WARP, asset, package, recovery and natural terminal-match gates.
The independent headless Conveyor reliability workflow exposes the focused contracts
and benchmark even when an unrelated earlier graphical gate stops main CI. It does
not replace the required main workflow.

Native acceptance still requires physical D3D12 execution and human inspection of
Cargo/Supply Trucks and combat vehicles beneath spans/ramps, beside real supports and
through docks before and after edits. Inspect owner/fog isolation, selected links,
Operations bounds, flowing/stalled clips, state materials, LODs, reduced motion,
terrain contact and camera readability. Headless movement, save/replay identity and
hosted WARP are separate evidence; none establishes native visual quality or reference
hardware performance.

## Bounded player priority routing

The new routing change starts from master `c2ac8dcde03c3b06c553fd7bf4518fb7f88c712a`,
after core-progression PR #381. It extends the existing flow arbitration; it adds
no transport graph, currency, recipe, fluid support or resource authority.

One owned, living Splitter has three choices: Balanced (the unchanged default),
prefer Output A, or prefer Output B. Prefer A/B requires the player's real Logistics
Coordination capability. The Logistics category of the selected object's action
ring offers these choices, marks the configured one SET and submits one typed
ConveyorRoutingCommand. The existing shortcut uses the same selection. Before
unlock, either preference is disabled with the authoritative
LOGISTICS COORDINATION REQUIRED reason. Balanced/reset is unrestricted by research.

A and B are physical socket ordinals, not the compact index of currently connected
links. In local splitter space, A is the output at (-4, 5.8, 4), B at (4, 5.8, 4);
the placed orientation rotates both. Existing owned/focused socket markers label
OUT A/OUT B with FAIR or the configured 3/1 weight. Disconnection of A cannot rename B.

### Arbitration and starvation bound

Balanced retains the original successful-transfer round robin. An explicit
preference uses a four-turn cycle [preferred, preferred, preferred, other].
At most one ordinary quantum transfers on a turn. The cursor advances only after
InventoryStore.Transfer succeeds and is retained across ticks and idle periods.
An ineligible/unoffered output is skipped within the same bounded four-slot scan;
unused slots accumulate no credit. If only the other branch is eligible, it can
use all otherwise available source capacity.

When both branches remain eligible and offered, the lower-weight branch receives
one service turn in each four successful transfers. This is a local service bound,
not a guarantee of end-to-end timing or a fixed volume ratio through differently
damaged, powered, full or resource-constrained receivers. Existing receiver/input
arbitration, per-resource rotation and all physical constraints still apply.

All inventories, stock retention, committed truck quantities, reservation exclusion,
power/health scaling, one-hop tick snapshots, throughput and finite buffers remain
authoritative. Preference cannot make a blocked destination accept stock, create
goods or reserve resources for future ticks. Topology remains derived.

### Commands, snapshots and recovery

ConveyorRoutingState stores the enum, monotonically increasing uint policy revision
and modulo-four successful-service cursor on the full splitter EntityId.
Commands carry issuer, complete target identity, preference, expected revision and
observed tick through the existing session-bound player gateway. Execution checks
running match, owner, actual Splitter, living health, capability and revision.
Revision exhaustion fails without wrapping. Repair keeps the policy/cursor;
replacement/removal destroys it with the original entity. Reused entity indices
inherit no policy. The effective flow falls back to fair if the capability is absent.

The completed snapshot copies configured preference, revision, physical output
identities and per-choice failure values. Ring item keys include full target
identity and policy revision; a stale displayed choice cannot silently become a
different selection. Presentation issues requests and never writes ECS.

ReplayCommandKind.ConveyorRouting records the target, typed preference and expected
revision with normal ordering/source/tick metadata, including commands later rejected.
Payload 9 adds those command fields. Authoritative schema 11 includes policy and
cursor and rejects schema 10 and earlier before reconstruction. No migration invents
a prior preference or rewrites historical hashes. Envelope 1 and registered scenario
content identity are unchanged. See [the routing ADR](adr/BoundedConveyorPriorityRouting.md).

### Acceptance still required

No tests were created, changed, built or manually executed for this implementation.
No smoke, benchmark, headless/native qualification, workflow dispatch or CI rerun
was started. Existing automatically triggered repository CI is separate evidence.
The previous reliability measurements above qualify their named historical sources,
not this routing change; no new timing or allocation improvement is claimed.

Product compilation remains open: the local source mirror does not contain the
current master tree, and the connected content API provides no compiler. The product
projects to compile are Game and Presentation with their product dependencies.
No older checkout or test-project build is substituted for that result.

For manual review, use finite stock and a paid splitter with two compatible sinks:
observe default fairness, verify locked choices, complete paid Logistics Coordination,
apply A/B preference and inspect actual output rates/material conservation. Block,
disconnect, damage or unpower the preferred branch and verify the other can flow;
restore both and inspect the persistent service ordering. Check partial stock,
reservations, different resources, truck commitments, rotated A/B markers, ownership,
stale revision, repair/replacement and active save/load/replay followed by more ticks.
These are manual scenarios, not executed evidence or changed gates.
