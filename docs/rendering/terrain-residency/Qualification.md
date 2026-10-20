# Terrain residency qualification

Status: implementation in progress; no acceptance claimed.

Initial master: bfc16a953d731a844321a0848a2ebadc71c5875f, version 0.1.423.
The checkout was clean. The remote contains only master, no open terrain work;
previous terrain changes and render foundation are integrated. PRs 263/264 are
integrated instance/asset optimizations, not terrain replacements.
The accepted parallel ownership manifest supplies the required frozen contracts.

Master CI 39445686642 fails in Test: five Game cases and
RenderSubmission.SubmissionSafetyTests.BatchEscapeBandUsesBoundedReusablePayloadLeases
(expected zero allocated bytes, observed 8,486,912). These are pre-change results,
not evidence of a particular cause. Full native and final CI remain required.

Functional write scope is TerrainRenderer.cs and the TerrainResidency,
TerrainResidency test, terrain-residency build/documentation prefixes only.
Project files, layouts, shared contracts, shaders and global settings stay frozen.
The serialized one-PATCH exception is finalized against current master later.

Initial custom-artifact build with --no-restore failed NETSDK1004 because that
output directory had no restored assets. The baseline build restores there
before compilation; the failed invocation is recorded here, not a product failure.


The unchanged Release solution builds with zero warnings/errors (4m07.04s).
Four new reference cases pass before product changes. The initial no-test
selection (exit 8) reflected an earlier project file-list evaluation; rebuilding
the test project discovers all four. The initial texture-capability omissions
in the fake context are corrected without changing product assertions.

The bounded CPU preparation/shared-static-index path builds without warnings
or errors and passes 11 reference/preparation cases. These verify unchanged
positions, UVs, control maps and authoritative height samples, bounded CPU LRU,
GPU eviction reuse, late cancellation, static-buffer fallback and failed GPU
publication invalidating readiness. Vertex normals deliberately sample both
sides of immutable chunk boundaries. Native appearance review is still required.


Bounded readiness steps, retained GPU LRU and optional CPU-only neighbour prefetch
build with zero warnings/errors and pass 19 owned cases. The warmed Prepare test
records exactly zero allocated bytes with a positive 4,096-byte control. Tests
cover equal-aspect viewport changes, interrupted plans, independent geometry and
control budgets, one eviction-candidate visit per eviction, and no optional GPU
publication. Native measurement windows are serialized with other render work;
functional results are not presented as performance measurements.

Boundary-preserving geometry selection builds with zero warnings/errors
(6m35.74s) and passes all 31 owned cases (13.237s). Coverage, 32-bit indices,
winding, continuous height-error bounds, boundary positions/normals/UVs,
independent main/light triangle counts, strict pixel-limit hysteresis, protected
road/coast geometry, cancelled worker disposal, frame abort and repeated whole-map
payload plateau are covered. The three required existing classes, full affected
suites, native hardware evidence and final CI are the next gates.
