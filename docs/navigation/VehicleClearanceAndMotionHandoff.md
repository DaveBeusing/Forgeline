# Vehicle clearance and motion integration handoff

## Status and owned files

Contract revision: 1. Implementation is confined to Navigation/Geometry,
Game/Navigation/Motion, this documentation and one PR version increment.
Existing movement, spatial-index, docking, queue, conveyor, persistence and
composition sources are unchanged. Algorithms are not registered in a match.

Read [Vehicle clearance and continuous motion](VehicleClearanceAndMotion.md)
before connecting these services. The pull request records the current base/head,
Release compilation and automatically observed CI. Automated tests have not been
created, changed or manually executed. Manual acceptance is outstanding.

## Recorded compilation blocker

Both required project compilations were attempted against master
78e889d471435477c6b03ad866a572c64a3f262c plus these source changes, with SDK
10.0.401 and the proposed one-PATCH version 0.1.475-dev. Restore succeeded.

~~~text
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --verbosity minimal
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --verbosity minimal
~~~

Both commands failed with the same three CA1512 diagnostics in unchanged regional
sources inherited from that master revision:

- RegionalChangeJournal.cs, AdvanceTick, line 247.
- RegionalChangeJournal.cs, MarkPublished, line 291.
- RegionalUpdateCoordinator.cs, TouchTick, line 318.

Each diagnostic requests ArgumentOutOfRangeException.ThrowIfLessThan in place of
an explicit range-check throw. No analyzer, warning rule, test target or source
file outside this implementation's ownership was changed to bypass the failure.
The Game target cannot finish because its Navigation dependency does not build;
successful compilation of the complete motion implementation is not claimed.

Next build step: the regional owner corrects those range-check forms while
preserving their comparisons, then the same two Release commands are run on the
integrated source. This blocker is separate from runtime wiring and manual
acceptance.

## Required integration sequence

1. Publish the matching immutable world/terrain source and applicable full
   capability profile. Construct NavigationVehicleGeometry once for that pair
   with explicit interval/work limits. Recreate it after replacement publication.
2. At actual command acceptance, assign and persist a checked monotonic command
   generation. Revalidate it before applying motion; the legacy read adapter
   cannot distinguish two otherwise identical replacement commands in one tick.
3. Capture authoritative vehicle observations and complete physical coverage
   before any participant moves. Use NavigationMotionCapture; include full
   entities/generations, true conveyor bounds, structures, wrecks, mobile bodies
   and valid reservations. Cover at least max(PlanningHorizonSeconds, DeltaSeconds).
   Incomplete capture must remain explicitly incomplete.
4. Combine current fuel, tactics, suppression, formation, convoy, queue and dock
   restrictions through their existing owners. Produce current generation-bound
   traffic directives. Neither missing traffic nor a projected endpoint grants
   permission to enter a dock or bypass a hold.
5. Pass ordinary routes to ContinuousNavigationMotionPlanner with the real fixed
   delta and authored goal tolerance. Retain the internal used-target/cursor
   observations for diagnostics and post-approval progression. Use the existing
   precise controller to propose headed local, dock and queue movement.
6. Pass every actual mover to NavigationMotionArbitrator.Constrain against the
   same physical frame. Nonparticipants must really hold or have a conservative
   full-tick swept-volume certificate already represented in that same frame;
   a logical dock/queue lease is insufficient. The normal integration rule is
   that every actual mover participates and all nonparticipants hold.
   Retimed or clipped paths require full actual-duration clearance.
   Rejected or unfinished participants keep their physical occupancy.
7. GroundMovementSystem alone rechecks current binding/generations and commits
   final accepted transforms and actual velocity. Before that commit, call
   NavigationAcceptedMotionProgress.TryGetNextWaypoint for ordinary routes. Hold
   unresolved or budget-limited progress; commit transform and cursor coherently.
   Do not advance directly to the lookahead target or create another movement loop.
8. Charge existing propulsion fuel from the applied motion and update the spatial
   index only at the authoritative phase boundary. Preserve original destination,
   final tolerance and existing precise docking/settling/service checks.
9. Copy and filter diagnostic data for the authorized viewer. Preserve existing
   route, command, queue and dock state through the central capture/restore path;
   rebuild transient geometry services and per-tick proposals after restore.

## Concrete integration requirements still owned by composition

- The frozen observation lacks a route cursor and authoritative command counter.
  The existing route/order owner supplies and persists these; this change does
  not fabricate a tick-based command generation.
- The snapshot does not expose native heightfield cells. Geometry construction
  requires the same TerrainWorld source used by the snapshot publisher.
- Body observations contain no angular/acceleration prediction or future-coverage
  certificate. Conservative prediction and full-batch final arbitration are
  required; instantaneous neighborhood capture alone is insufficient.
- StopRadius, authored HeightOffset, precise queue speed and settling rules remain
  with existing components/controllers. Goal constraints and effective limits
  must be mapped from those sources, not guessed from truck size.
- Shared runtime callers, save/replay assessment and UI registration belong to
  central integration. An isolated compiling module is not delivered gameplay.

## Manual acceptance after combined integration

| Scenario | Observe and accept only when |
| --- | --- |
| Long truck at a building corner | Entire turning body clears the structure; a clear center/end pose cannot hide a clipped corner. |
| Diagonal narrow opening | Direction and vehicle dimensions determine passage; no raster-center shortcut. |
| Conveyor crossing | Sufficiently high spans allow passage; supports and low spans remain obstacles. |
| Slope, ridge, valley and chunk seam | Whole-footprint slope/support and the intervening grounded trajectory remain valid. |
| Two fast vehicles choosing the same space | Same-frame proposals and final arbitration prevent overlap independent of task completion order. |
| Long straight route | Speed builds normally without repeated intermediate full stops or permanent precision creeping. |
| Several tight turns and a looping route | Lookahead remains bounded, skips only cleared geometry and never jumps to an ambiguous later route segment. |
| Arrival, parking and departure | Original goal accuracy, heading, settled speed, FIFO ownership and physical lane occupancy remain unchanged. |
| Fuel/tactical stop or reduced traffic cap | Existing authority wins; no extra speed, free fuel or post-veto movement. |
| Incomplete neighborhood or exhausted work | Only a complete safe prefix is usable; unresolved vehicles retain cargo, orders and physical occupancy. |
| Replacement command, destruction and restore | Old identities/proposals cannot resume ownership; route progress and real command/dock/queue generations survive correctly. |

Inspect the diagnostic hull, used target, limiting cause, braking distance and
approved horizon together with the real vehicle. No runtime smoothness or
performance measurement is claimed before this acceptance.
