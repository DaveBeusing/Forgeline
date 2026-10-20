# Instance submission qualification

Status: implementation in progress; acceptance open.

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

Product source: `cfab93d1c12c691c2f3fd6404d24d680fde99876`.
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

Game, logistics, simulation, economy and their existing tests are unchanged.
These failures remain outside this change's write ownership. Native checks,
final CI and human review remain open. No acceptance is inferred from subsets.
