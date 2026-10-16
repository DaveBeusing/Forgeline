# Navigation and Movement Quality

## Goal and Baseline

Improve route-search efficiency, precise local arrival, obstacle safety and mechanical visual continuity without replacing hierarchical navigation, changing unit speed balance or coupling simulation to rendering.

The reviewed baseline is `4641a3114b57421a04ce9da449e9a95b41168d6d` on `master`. The existing sector graph, bounded local refinement, versioned request/result handoff, formation routes, fixed-tick locomotion and presentation snapshots remain in place.

## Implemented Changes

### Local route search

`LocalPathSearch` retains the insertion cost with each priority-queue entry. Entries superseded by a cheaper route are skipped before expansion. Equal estimated costs use remaining heuristic distance and stable cell identity as explicit tie-breakers. Current-cell traversal cost is evaluated once per expanded node instead of once for each relaxed neighbor.

Diagonal corner guards, slope costs and the bounded corridor policy are preserved. These changes do not introduce continuous any-angle path smoothing or a full-map fallback.

### Cached regional routes and work accounting

`HierarchicalPathfinder` checks the regional cache before constructing a `Lazy` entry. Closure allocation is isolated on the miss path; competing misses still publish and evaluate one cached regional search.

Local expanded-node diagnostics now include work spent on an unsuccessful narrow-corridor attempt before the one-sector-expanded retry. Cached high-level expanded-node counts continue to describe the stored route's original search, not new high-level work on each cache hit.

### Precise arrival and collision safety

Ground movement shortens a proposed step when that segment would pass through the local arrival region. It does not snap sideways, bypass collision checks, increase speed or change the stop-radius definition. Terrain and obstacle validation precede arrival acceptance.

Mobile and static candidate queries are independent of optional steering weights. Static candidates cover both look-ahead and maximum tick travel. A swept circle against static bounds clips a step at the first contact, including rounded corner caps and a small contact skin. Existing penetration recovery remains explicit. Neighbor buffers are reused; a further static query occurs only when penetration correction expands the travel envelope.

This is swept **static** collision, not a guarantee against fast opposing mobile units or every multi-unit penetration configuration. Movement continues to use preceding-tick spatial occupancy.

### Mechanical presentation continuity

`MechanicalPoseHistory` is idempotent for a repeated completed tick and repeated capture of the same full entity identity. A second extraction reuses the captured wheel/stride distance, machinery time, door position and recoil timestamp instead of resetting the animation phase or losing a one-shot event.

History remains capacity-bounded and is pruned at new tick boundaries. Generation changes, hidden-tick gaps and non-contiguous simulation ticks retain discontinuity protection. Existing render interpolation, signed wheel distance, LOD, material and effect budgets remain unchanged. No extra particle emitters, draw calls or authoritative visual physics are introduced.

## Regression Coverage

| Contract | Coverage |
|---|---|
| Local route cost and obstacle safety | Independent Dijkstra comparison on open, sloped and obstructed single-sector fixtures; both side cells checked for diagonal steps |
| Stable search results | Cold/warm and concurrent searches agree on cells, waypoints and expansion count |
| Bounded simple-route work | An open 16-cell diagonal expands 16 local nodes |
| Arrival precision | Small and zero-radius targets with high incoming speed at 10/20/60 Hz; off-axis near misses remain active |
| Static safety | Thin walls with steering disabled, blocked targets, tangential travel, rounded corner clearance and initial-overlap recovery |
| Mobile endpoint separation | Disabling repulsion steering does not disable candidate penetration correction |
| Visual continuity | Repeat extraction/capture, next-tick progression, hidden reappearance, entity generation reuse, tick gaps and signed interpolated wheel distance |

The fixtures are in `LocalNavigationQualityTests`, `GroundMovementPrecisionTests`, `GroundMovementContactTests` and `MechanicalMotionQualityTests`. Existing movement, formation, navigation handoff and rendering tests remain part of qualification.

## Reproduction

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
./build/Validate-ProjectReferences.ps1

dotnet test --project tests/ForgeLine.Navigation.Tests/ForgeLine.Navigation.Tests.csproj --configuration Release --no-build
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class '*GroundMovement*' '*FormationMovement*' '*HierarchicalNavigation*'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class '*Mechanical*' '*RenderWorld*' '*RenderInterpolation*'

dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/movement-map-qualification.json

./build/Measure-NavigationComparison.ps1 -BaseRef 4641a3114b57421a04ce9da449e9a95b41168d6d
```

`Movement quality` runs the Release build, architecture checks, focused correctness tests and canonical map qualification independently of runtime texture compilation. On pull requests it also compares the PR base with the checked-out candidate on the same Windows runner.

The comparison reuses the existing warmed Central Divide cross-map, expansion and bridge-loss workloads. Three rounds alternate baseline/candidate process order, retaining each report, sample distribution and allocation evidence. It does not add a timing assertion to correctness tests or discard unfavorable measurements. The script removes only its own temporary baseline worktree.

CPU route measurements do not measure the new static sweep, crowded movement, GPU frame time or visual acceptance. The existing resettable 1,000-unit movement benchmark and full-match scenarios remain necessary for locomotion cost qualification.

## Acceptance Boundaries

The required full `build-test` workflow remains unchanged. A passing focused movement job is not equivalent to passing the complete CI, runtime asset budgets, packaged-client checks or terminal full-match validation.

Record executed revision, runner, test totals and benchmark reports in the pull-request evidence. Do not transfer validation from an older source revision to newer movement or rendering changes without rerunning the relevant checks.

Native D3D12 camera-motion captures, target-hardware frame pacing and visual art approval are separate from headless correctness and portable animation tests. No sustained FPS target, whole-match speedup or finished vehicle animation quality follows merely from these source changes.

## Remaining Design Work

### Clearance-aware continuous routes

The current local route still simplifies grid paths at direction changes, and navigation still publishes individual local movement orders. Do not smooth these with unconstrained curves: a visually attractive curve can cross a blocked corner or violate a vehicle footprint.

A future corridor-following change should validate swept clearance for the actual unit class, preserve slope/traversal cost intent, consume intermediate corners without repeated full stops and brake only for the final approach or constrained turns. It must retain order cancellation, route-version guards and formation handoff behavior.

### Vehicle motion character

Current yaw steering is not a vehicle-specific turning-radius model. Wheeled, tracked and infantry profiles should eventually distinguish turn-in-place, minimum curve radius, acceleration under large heading error and controlled reverse movement. Such changes affect gameplay and travel time and require paired trajectory, formation and arrival tests rather than purely cosmetic smoothing.

### Dense formations and traffic

Shared regional routes already exist. Further improvements should focus on narrow passages, opposite-direction encounters, stable slot reassignment and bounded recovery without repeated route-request storms. Predictive mobile collision or parallel movement first needs an explicit snapshot and conflict-resolution contract.

### Visual and command readability

Future native qualification should inspect turn transitions, stationary wheel behavior, mixed-speed groups, camera sweeps and zoom changes. Terrain-aligned chassis, track-specific differential motion and bounded suspension should remain presentation-only unless a separate gameplay requirement changes the simulation contract. Route previews and arrival markers must use actual accepted goals and respect visibility, footprint and UI budgets.

These are follow-up objectives, not features implemented by this correction.

## Related Documentation

- [Hierarchical Navigation](HierarchicalNavigation.md)
- [Ground Movement and Local Steering](GroundMovementAndSteering.md)
- [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md)
- [Combat and Industrial Rendering](CombatIndustrialRendering.md)
