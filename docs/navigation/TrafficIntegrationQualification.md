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

## Reserved-dock controller handoff

An exact owned Reserved dock admission removes VehicleQueueMotion from the FIFO head while it still approaches its queue target. Requiring that removed component disabled legitimate reverse corrections. The legacy proposal now accepts either the current queue controller or the exact current owned dock slot, matching building/generation/pose, first queue position and approach target. A managed waypoint must retain the matching owned root target. Entered lanes and unrelated service targets remain excluded; actual geometry/staging approval is unchanged. The caller delegates this authority check to the existing owner-aware helper.

A new no-QueueMotion reserved-head regression fails before and passes after the correction. StandardOpeningRecordsRealProductionDepotLoadingAndMobileResupply again passes without changing its limits. The expanded follow-up run has 72 passed / 3 failed out of 75: PlayerCommandsBuildSupplyAndReconThroughNaturalSystems and both TechnologyFleetRecovery variants remain failures. The preceding combined bce388d8 complete Game run was 1,013 passed / 15 failed. Traffic0736 remains Active at 80,000 ticks (66 completed deliveries, 7,424.5 cargo, zero combat damage), failing the unchanged terminal gate. No full acceptance is claimed.

## Static flank geometry and admission

A static building touching the abstract admission envelope was treated as a traffic occupant and could block an otherwise physically clear lane forever. Static Structure bodies now remain in collision capture and every actual geometry sweep, but cannot acquire dynamic traffic admission. An existing occupied vehicle lease still retains the complete physical rear-clear condition. A real NavigationVehicleGeometry regression independently proves both clear flank travel and obstruction when the same building is moved onto the path; the admission case is red before and green after. The follow-up set has 73 passed / 3 failed out of 76; the same three natural player/fleet cases remain unresolved.

## Service wait versus physical escape

A long external dock-reservation wait alone previously triggered a reverse Recovery directive. The red-to-green case now retains Hold/ReservedCorridor without inventing a physical blocker. Proactive escape outside the coarse participant graph requires an observed MovingVehicle or StaticObstacle reason. Explicit detected wait cycles and already verified retained escapes keep their separate bounded handling. All original turn/convoy cases remain green; the expanded set is 74 passed / 3 failed out of 77, with the same natural player/fleet cases outstanding.

## Rear-clear passage contact

Circumscribed-circle admission could recapture a vehicle after its complete footprint had already cleared a foreign passage, leaving it blocked just outside the region. Touches now projects the complete conservative swept physical enclosure (translation, rotation and clearance) into the horizontal passage frame. The outward-occupant check uses the same contact representation, including numerical padding. Geometry remains authoritative and the original owner/Entered lease is retained. The new outward-continuation case fails before and passes after, while inward re-entry remains rejected. Follow-up set:75 passed /3 same natural-flow failures out of78.

## Service admission before passage capture

A vehicle waiting for a foreign service reservation could nevertheless acquire an unentered crossing lease against that service owner. Service applicability/contact is now evaluated once per intent before passage selection, with bounded work charged to the unchanged allowance. A blocked approach cannot acquire or retain an unentered lease. Occupied/entered ownership remains retained until the complete footprint clears. The test covers blocked initial admission, free admission, later withdrawal of an unentered claim, and mandatory retention of an occupied claim. The expanded set is76 passed /3 failed out of79. The formerly frozen KI dock exit now clears and the recorded vehicles continue moving at tick12000, but the same natural-flow limits still fail and are not waived.
