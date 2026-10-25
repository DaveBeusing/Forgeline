# Traffic integration qualification

Current candidate retains the unchanged gameplay and CI gates.

## Verified locally

- 152 Game tests covering navigation, continuous motion, traffic prediction and leases, real connected travel, original command replacement, frozen physical occupancy, body-envelope containment, docks, queues, serialized traffic continuation and production exits pass (Release).
- New rear-neighbor regression initially failed, then reaches its original goal within the unchanged 600-tick bound after bounded low-speed clearance steering. Every candidate and its actual first step remain independently swept; the final batch arbiter remains authoritative.
- Fine physical crossing cells avoid claiming a coarse pathfinding cell's entire parking area.
- Queue no-passing planes require the predecessor to have entered the lane. Existing FIFO service admission and physical collision vetoes remain in force.
- A vehicle already inside a foreign reserved passage can move only outward; the reservation owner remains retained. Entry and inward movement remain vetoed.
- Production checks at most 25 exit poses against up to 4096 current physical bounds before consuming paid reserved inputs. A fully occupied exit holds production and reserved inputs with OutputBlocked. Both the free alternate-exit and occupied-then-released cases pass.
- StandardOpeningRecordsRealProductionDepotLoadingAndMobileResupply passes after occupancy-aware production exits.

## Outstanding

PlayerCommandsBuildSupplyAndReconThroughNaturalSystems and both NormalOpponentRestoresCriticalFleetBeforeNewResearch variants still fail their unchanged natural-flow limits. They are not represented as passing. Full Game/solution and actual-head CI qualification remain pending. Previous 80000-tick candidates remain Active, failing the unchanged terminal requirement; no newer full-match pass is established.

The diagnostic view and its per-vehicle producer are implemented and published separately in PR #359; its full Client/Presentation modules and actual-head native WARP captures pass. Shared gameplay/CI acceptance remains pending. This traffic candidate is not ready for merge, tag or release.

## Follow-up physical and queue corrections

Circumscribed prediction discs fabricated crossings on separated parallel lanes and safe following gaps. Three red-to-green cases now use conservative swept oriented boxes in a relative translating frame, including rotation padding and a bounded 15-axis separation test. Actual geometry and final motion arbitration remain mandatory.

A later arrival already ahead of its FIFO predecessor may move away only as far as its own assigned tail pose. It receives no dock admission. Before service-lane entry, the fixed final heading no longer disables ordinary avoidance or independently approved straight clearance. A queue owner may preferentially reverse toward its own waiting pose at at most 1 m/s, retaining exact owner/generation checks and actual step validation. Final alignment and entered dock ownership remain with the existing service controller.

A stationary vehicle previously entered the 1 m/s escape branch for a distant predicted obstruction even with a useful verified braking prefix. The stopped-vehicle regression fails before correction. The candidate reserves low-speed clearance for an immediately blocked sweep or a nearby rear body, retaining it through a turn initially facing away from the root destination. The body identity and position come from the same copied collision frame; the bounded lookup is charged to the existing work allowance. Farther forward obstructions use the existing conservative stopping-distance cap. This does not authorize passage through the obstacle or increase authored speed limits.

Recorded follow-up runs before the final local-envelope adjustment include a 70-case run with 69 passing (including the unchanged loaded convoy/manual escort/save/replay/final delivery case) and one rear-neighbor regression. The final rear-body/turn-aware adjustment passes all 70 cases, including the original 600-tick rear-neighbor turn and the unchanged final convoy completion condition. The stopped distant-obstruction case is red before and green after the correction. Current-head341 CI had 14 Game failures (technology, production policy, strategic/raid progression, manual production flow and final convoy delivery); all remain part of final acceptance until rerun. No timeout or assertion was relaxed.
