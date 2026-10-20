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
before compilation; the failed invocation is retained, not a product failure.
