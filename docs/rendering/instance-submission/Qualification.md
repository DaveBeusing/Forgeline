# Instance submission qualification

Status: implemented, not yet accepted (implementiert, noch nicht abgenommen).

Base: `47f4230f487e37f699b6572354e89f800476c513`, tracked version 0.1.420.
The remote had only master and no open PR at pre-check. Snapshot publication
and resource scheduling are integrated through PRs 258 and 260. The accepted
parallel ownership manifest supplies the required frozen boundaries.

Master CI 39421043011 fails before build at version validation: expected
0.1.421, received 0.1.420. This is a pre-existing integration issue. This
change will carry one isolated PATCH delta relative to its current base.

Only SimpleInstanceRenderer, InstanceVisibilityIndex and the assigned
RenderSubmission, build/rendering/instance-submission,
tests/ForgeLine.Presentation.Tests/RenderSubmission and this directory are
functional write scope. Shared contracts, resource-session ownership, graphics
layouts, workflows and existing tests remain frozen. Project references stay
acyclic. Native qualification and human image review remain required.

## Executed functional checks

Measured corrected source: `63fada7afaf610ea91af3a8c6d283dd1d4093975`.
Final functional binaries: `60724f1204cffbc850cbbfbc0a381cd62b0f23a1`.
The later public-API fix changes only the standalone measurement adapter.
The complete solution Release build passes, zero warnings/errors. The reference
master build also passes. The project-reference validator and functional
ownership check pass; the serialized version diff is only 420 to 421.

42 focused cases pass before the allocation isolation adjustment. The full
Presentation project initially reports 1,442 total, 1,427 passed, three failed,
12 optional skips. The three new large-workload allocation cases report
480/12,960/480 bytes under concurrent test classes, despite isolated focus
passes. Those original failures remain in the local product-presentation log;
no individual event is attributed to a runtime counter defect without a trace.

The allocation cases share global ArrayPool buckets and now run in their own
nonparallel test collection. This isolates the promised stable workload from
competing tests without disabling a test or changing its zero-byte assertion.
A positive control still requires at least 4,096 real allocated bytes. The full
Presentation rerun passes: **1,443 total, 1,431 passed, 12 optional skips**, exit 0,
51 seconds. It uses the same product sources; only test isolation/control changes.

The earlier complete solution run on ac03012c reports 3,036 total, 3,009 passed,
nine failed, 18 skipped, exit 2, 10m57s. Three failures are the newly isolated
allocation cases. The remaining six are reproduced against unchanged master
47f4230f in seven selected theory cases (six failures, one pass, exit 1):

- BattlefieldSupplySystemTests.SupplyTruckFinishesRefuelingBeforeRescuingImmobileUnit
- TacticalCombatSystemTests.ActiveCargoTransportRefuelsWithoutDroppingDeliveryOrder
- VehicleDockingIntegrationTests.DockedSupplyKeepsCargoSeparateFromOptionalDriveTopupsButAllowsRequiredRecovery (initialDriveFuel 0)
- SkirmishSupplyPolicyTests.ExpansionFundingKeepsPartialHubCostsAheadOfOptionalExtractors
- SkirmishOpponentTests.EastOffensiveObjectiveAdvancesIntoOpponentHalfAgainstStationaryBase
- SkirmishOpponentTests.DirectCombatTargetsRequireCurrentIdentifiedIntelligence

After candidate enumeration and endpoint corrections, the complete Release
solution build passes with zero warnings/errors. The complete Presentation
project passes **1,452 total, 1,440 passed, 12 optional skips**, exit 0, 50.3s;
52 focused renderer/extraction/index/owned cases pass. These checks refer to
63fada7a. On final source 60724f12, Release solution build again passes with
zero warnings/errors (98.98s), and full Presentation passes **1,453 total,
1,441 passed, 12 optional skips**, exit 0 (65.3s). The 21 safety cases, including
large-to-small pose-cache reclamation, pass separately. Original log hashes
and commands are in [functional checks](evidence/functional-checks.json).

Portable raw CPU evidence, exact adapter/controller source and hashes are in
[evidence](evidence/). [Measurements](Measurements.md) separates reproducible
CPU reductions from mixed mechanical timings and pending GPU/image results.

Game, logistics, simulation, economy and their existing tests are unchanged.
These failures remain outside this change's write ownership. No complete
solution green result is claimed.

Native capture runs complete on hardware against the same source/assets:
18 cases per side; all six capture exits zero. Fifteen image pairs are exact,
three tactical vehicle views exceed zero pixel tolerance and require human
shadow review. The unchanged 15% performance gates fail 31/7/7 metric comparisons
for fixed/sweep/Low; details and raw frames are in [Measurements](Measurements.md).
Final CI, the complete-suite baseline failures, native performance, sweep
memory plateau and human image acceptance remain open. No acceptance is inferred
from successful subsets or structural image exits.


The longer ABBA repeat completes with 1,024 warmup / 512 measured frames,
eight zero capture exits, 16 stable product resource plateaus and no detected
foreign workloads. Tactical repeated-vehicle CPU medians improve in both pairs;
the earlier slowdown is not stable across cohorts. Nevertheless the unchanged
performance gate still fails 7/2/12/1 metric comparisons (A High/A Low/B High/B
Low), so performance acceptance stays open. Twelve of 16 image pairs are exact;
the same tactical shadow selection differences require human review.

The final project-reference validator passes 46 projects with no cycles. Final
source ownership and one-PATCH validation are required before publishing the
final PR head. All product paths are unchanged after the measured 60724f12;
subsequent commits contain measurement helpers and evidence/documentation only.
