# Gameplay recovery (#405)

The queue controller now hands nearby off-axis endpoints to the normal physically checked local approach. Transit projection retains the original requested root for final approach, and a zero-horizontal-length route segment can anchor an already accepted physical movement without advancing route ownership. Geometry sweeps, FIFO admission, fuel costs and dock service authority remain in force.

VehicleRoutingKernel.AlgorithmRevision advances to 3. MatchAuthoritativeSnapshot state schema advances from 17 to 18 because deterministic movement/AI decisions changed. Previous authoritative saves/replays are rejected by the existing compatibility checks; payload schema 11 and envelope schema 1 are unchanged. The updated initial/tick-20/tick-4 checkpoints come from independent matching captures rather than weakening hash equality.

Bridge coverage now rejects legacy free restoration and verifies that an owned combat engineer spends exactly the finite 120 Steel / 20 Electronics kit before the timed repair reopens an identified bridge. Intelligence fixtures compare the copied coarse reported coordinate, without exposing the exact hostile coordinate. Placement fixtures use the currently supported building-selection API.

Local evidence recorded during remediation:

- 50 focused gameplay cases passed after the minimal queue/projected-goal/accepted-motion corrections.
- NormalOpponentPaysForResearchAndReconstructsWhileProductionContinues passed its original 60,000-tick budget after core funding prioritization. Research and combat production remain paid, and save/replay hashes remain equal.
- 30 affected UI contract cases passed together.
- Physical waiting-scout resupply completed in the measured 2,400-tick diagnostic, after remaining in motion at the former 1,200-tick cutoff. The production fixture now allows a complete authored processing cycle before asserting OutputFull.

The full Game suite, remaining bootstrap policy cases and the unchanged seed-2026 80,000-tick require-terminal gate still need final successful qualification. This document does not claim CI success while any of those checks remain failing or incomplete.

The construction-policy fixtures now explicitly author their required third cargo truck and, immediately before their isolated decision, the live scout/tank/artillery roles required by CanPlanExpansion. These test-only arrangements do not change runtime starting vehicles, stocks, paid technology/production tests, the 40,000-tick strategic-loop limit, or the 80,000-tick natural terminal gate.
