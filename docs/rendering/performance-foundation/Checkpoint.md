# Rendering performance foundation

Source checkpoint: `63d63a3d8d2977dfff37a851d30ee4e3428b7843`, product **0.1.529**. Shared reference-history prerequisite: `87a14786cb90bff39b33b7edb6069c6dba8b6efc`. The PR adds exactly one PATCH relative to current master when prepared; measurement identities below are not relabelled as the final documentation/version commit.

This checkpoint defines four bounded implementation/qualification packages over an already implemented renderer. It does not introduce GPU instancing, GPU-local geometry, prepared correspondence or native GPU benchmarking as new foundations.

## Entry state and limits

Current master contains the merged procedural map qualification. Its complete `build-test` fails at the full solution `Test` step; navigation and conveyor checks pass. The preceding PR #438 full suite failed `BattlefieldStatusNativeTests.NativeStatusIsRepeatableAndHiddenOrStaleFactsDoNotPaint(1000,192,2)`, expected 1 status marker, observed 0. Both isolated cases pass on this checkpoint. The failed full suite is not replaced by that subset. The natural terminal-match gate did not run after the failed step. No UI/gameplay fix, test relaxation or protection change is included here.

Repository policy still requires the complete Actions `build-test` before integration. Branch metadata reports protection enabled but an empty required-status context view; detailed administrative enforcement is not established by that limited readback. Historical protection/acceptance documents are not a present success claim.

Remote branches and the latest forty PRs were inspected. No open rendering implementation PR overlaps these scopes at precheck. Closed, unmerged PR #427 is an older work-package proposal at 0.1.519/520; it is not this checkpoint and its closed branch is not revived. Active work must be checked again before each dependent branch starts.

## Deliverables

- [Capability inventory and frozen contracts](Capabilities.md)
- [Exact ownership manifest](Ownership.json), consumable by the existing ownership validator
- [One scaling protocol](Protocol.json), including the distinction between existing workloads and planned scale fixtures
- [Hardware baseline and retained evidence](Baseline.md)
- Four packages: [instancing](Instancing.md), [static geometry](Geometry.md), [correspondence](Correspondence.md), [native scaling](Benchmark.md)

The [existing ownership record](../parallelization/ParallelRenderOwnership.json) remains historical and unchanged. The new manifest narrows/reconciles its boundaries; it does not authorize the eight historical lanes to run alongside the four new packages. Parallel scopes become usable only after this shared prerequisite is integrated and current ownership/check state is revalidated. Acceptance remains separate from path ownership.

## Shared prerequisite

The native reference host previously published only its current fixture snapshot. `VisualReferenceScene.PreviousSnapshot` now optionally supplies immutable history. `ReferenceSnapshotHistory` accepts only an older snapshot in the same session and tick duration, caps paired inputs at 100,000 roots, builds the existing RenderWorld correspondence before timing and includes the complete predecessor instance state in the case fingerprint. Generation changes remain new identities. Existing current-only fixtures preserve their behavior and fingerprints.

No Graphics interface, GPU layout/root signature, queue, resource lifecycle, simulation dependency or default camera/timing policy changes. Existing doubles compile unchanged. The native renderer still uses its existing alpha=1; CPU interpolation cohorts explicitly exercise intermediate alpha. The benchmark owner implements scale fixture registration using this prerequisite rather than constructing a second host or collector.

## Validation and handoff

Both unchanged master and the shared prerequisite build the complete Release solution with zero warnings/errors. Existing collector/manifest contracts pass 4 cases; upload contracts pass 31 with one explicit optional skip; correspondence/submission contracts pass 36. The new history plus existing host/collector selection passes 22 cases. The completed architecture check covers 46 projects, no cycles. The initial sparse checkout omitted an embedded icon; after materializing the exact committed resources the full build passes. Source/commit/tree objects and all materialized files were SHA-verified; qualified captures use clean Git status, exact source stamps and the same sealed runtime payload.

Run `python build/rendering/parallelization/ValidateOwnership.py --manifest docs/rendering/performance-foundation/Ownership.json`. For a package, additionally provide its exact technical name and `--diff-file` containing `git diff --name-only --no-renames` output. This checks paths/dependencies, not semantic acceptance. Shared/frozen requests return to the serial coordinator. Keep one PATCH per PR, individual technical commits and the existing complete validation/terminal gate. No merge, tag or release is authorized by these scopes.
