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

The diagnostic view and its per-vehicle producer are separate unfinished work in PR #359. This traffic candidate is not ready for merge, tag or release.
