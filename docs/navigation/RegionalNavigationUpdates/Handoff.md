# Regional navigation integration handoff

## Delivery boundary

Independent internal preparation and canonical revision-1 input mapping are implemented. **Public world-update output adaptation, runtime connection, durable save/replay integration and manual acceptance remain outstanding.** The existing game continues to use its existing navigation world and movement pipeline.

Initial analysis base: `cdb60b1a926e770f25eb1ec350548bb00349ff49`, `master`, `0.1.472-dev`. Prepared current base: `19e237d2e53c9a95abd2f8bb192ca19a4129c683`, `0.1.473-dev`, with one PR PATCH increment to `0.1.474-dev`. PR #354 and PR #350 are integrated; intervening PR #357 retains support-only conveyor blockers and the existing navigation interfaces. Foundation [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356) was integrated during implementation, and its canonical contract revision 1 is now used directly for input mapping. Final candidate SHA, current base synchronization and source-specific CI evidence belong to the implementation PR; no merge, tag or release of this feature is implied.

## Owned files

- `src/ForgeLine.Navigation/Regional/`: canonical input mapping, model/layout, source journal, pending blockers, immutable raster/connectivity preparation, segment dependencies, coordinator and leases.
- `src/ForgeLine.Game/Navigation/WorldUpdates/RegionalObstacleCapture.cs`: read-only physical source observations and canonical change construction with complete entity identity and separate supplied authored infrastructure identity.
- `docs/navigation/RegionalNavigationUpdates.md` and this handoff.
- `Directory.Build.props`: the single PR PATCH increment, reconciled against current master.

Existing Grid/World/Graph, shared contracts, building/conveyor/infrastructure callers, composition, save schemas, tests, CI, map references and reference hashes are outside this feature's changes.

## Ready for the contract owner

The [technical document](../RegionalNavigationUpdates.md) lists exact internal APIs, settings, mutable state, work accounting, call ordering and all eight required central extensions. The immediate blockers are the sealed contiguous legacy grid, concrete legacy graph/snapshot binding, absence of shared latest-pending-blocker enforcement, and missing explicit expected-base publication at a simulation boundary. Copying the entire grid to bridge those APIs would negate the regional storage design.

The integrated canonical inputs are used directly; no shared public signature is duplicated and no separate foundation branch is imported. `RegionalContractAdapter` maps supported obstacle changes/kinds and derives outward-rounded full swept radii from canonical profiles. Extend the canonical contracts/storage centrally, then implement the output adapter against the integrated revision on current master. `INavigationWorldUpdateSession<TCheckpoint>` is deliberately not implemented by this preparation-only change.

## Required integration sequence

1. Establish the permanent-only baseline and complete mutable initial ledger. Mutable start buildings, infrastructure and conveyor supports must not remain embedded in the permanent grid. Preserve stable authored identities and full entity generations; classify actual physical wrecks separately from decorative meshes.
2. Complete the canonical page-backed grid and prepared-graph/snapshot publication contracts, pending blocker query, region/segment dependency proof and explicit regional capacities.
3. Register the public adapter centrally. Feed authoritative old/new footprint events before movement can use stale geometry. Acquire immutable snapshots for jobs, prepare frozen batches under `MaximumWorkPerTick`, and adopt a completed retained batch only at a later `NavigationRequests` boundary with the expected preceding version. Preserve later changes and their pending blockers; incoming edits must not repeatedly restart preparation.
4. Enforce the latest pending generation for result adoption, route reuse and movement. `MaximumSweptRadiusMeters`/`SweptRadiusMeters` include the complete vehicle envelope plus margin; the canonical input helper derives a circumscribed radius covering rotation and terrain pitch. Route proofs require layout, revision and opaque page identity, preventing future-timeline proofs from matching reused scalar revisions after restore. Keep existing physical collision, command ownership, FIFO docking, cargo/fuel and exact service rules authoritative.
5. Release leases at deterministic joined job boundaries. Implement source resynchronization, safe legacy full-build waiting/recovery, retention backpressure, world-reset cleanup and the durable save/replay codec.
6. Compile the two target projects under repository rules, observe normal required CI, then perform the combined manual acceptance. No isolated-module result constitutes delivered gameplay acceptance.

## Capture/restore and budget cautions

Current checkpoints are immutable **in-memory** records tied to the same layout, baseline, content identity and world generation. Journal format revision 3 and coordinator algorithm revision 1 preserve source predecessors, tombstones, the frozen batch and later-change ages, pending/transient blockers, publication ledger identity, tick work and exact preparation cursor. Preparation restores against the frozen ledger; newer current sources remain separately pending. Restore requires joined readers and reconstructs exactly the recorded cursor without extra simulation ticks or early publication. It has no durable codec or independent load-reconstruction budget.

Preparation/publication work is tick-budgeted. Queue admission is separate bounded metadata work; route proofs and blocker queries have separate allowances. Raster preparation scans the full registered obstacle ledger once per dirty region, accounting for each candidate scan. No region-to-obstacle index or measured speedup is claimed. Diagnostic retained cell slots are a conservative capacity estimate, not allocated bytes or measured heap retention.

## Validation record and manual cases

Both exact local Release build commands were attempted and each exited 127 with `dotnet: command not found`. No source compilation occurred:

```powershell
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release
```

Automated tests were not created, changed or manually executed. No benchmark, soak, smoke, headless or visual-reference run was requested. Existing automatically triggered CI remains unchanged; report its actual source-specific status separately. No performance, whole-runtime correctness or manual-acceptance claim is made.

Later manual cases: delayed publication after construction on a route; demolition/destruction with overlapping survivors; real versus decorative wrecks; sector-border edits and both portal directions; crossing close/reopen; same-tick chains/recycled generations; continuing edits during a small-budget frozen batch; capacity/source holds; conveyor supports; unaffected segment reuse with full swept radius; retained readers; world replacement; and saving while later edits, blockers or a completed unpublished generation are pending.
