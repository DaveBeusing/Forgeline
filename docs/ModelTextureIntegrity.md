# Production model and texture integrity

## Baseline and scope

Review date: 23 October 2026. Integration base:
`78fe033802595dd838713132527f161e9d3b7a17` (0.1.459).
The isolated `fix/model-texture-integrity` branch prepares one PATCH increment to
0.1.460 in [PR #337](https://github.com/DaveBeusing/Forgeline/pull/337).

The production census covers 2,049 asset definitions, 1,389 glTF/GLB mesh files,
20 animation-only glTF files and 975 PNG/TGA images under `assets/source`.
Counts include LODs, state models, authoring kit components, proxies and UI images;
they are not counts of unique playable units or buildings. All images were fully
decoded. Material/dependency IDs, color spaces, accessor ranges, indices, finite
attributes, normals, tangents, UV diagnostics and animation channels were checked.

A separate native Blender 5.2.2 LTS census opened all 127 tracked `.blend` files,
including the 50 production-tree masters and staged preservation masters. It
reported zero open failures, structural errors, validation warnings or unresolved
image references. No master was saved or changed. This is structural authoring
qualification, not physical-monitor or live-game visual acceptance.

## Repairs

### Smelter exhaust frame continuity

The swept-pipe exporter recomputed a circular ring basis from a fixed reference.
At the final vertical segment, the reference became parallel to the tangent and
the fallback reversed the ring phase. This twisted the connecting sidewalls.

`source_geometry.pipe_frames` parallel-transports the frame through reference
singularities. Regular nonsingular authored frames retain their original roll.
The exhaust kit, LOD0 and LOD1 exports were repaired. Their disagreeing triangle
counts change from 12, 12 and 6 to zero. LOD2 and unrelated state meshes remain
unchanged. A separate old/new generator comparison over 127 industrial/tank
outputs changed only those three smelter files.

### Self-propelled artillery track insets

The sign-based inner contour placed some inner track vertices outside the outer
convex profile, producing concave or crossing sidewall quads. Merely reversing
triangle winding would not repair the geometry.

`source_geometry.inset_profile` retains valid legacy profiles exactly and uses
inward supporting-line intersections only when the original inset is invalid.
Both track kit parts and all three main LODs were repaired. The two kits had
three disagreeing triangles each; LOD0/1/2 had six/two/two. All now report zero.
The existing Main Battle Tank profile remains bit-for-bit unchanged.

Across the eight repaired glTF files, vertex counts, triangle counts, outside
bounds, source IDs, material/atlas identity, mechanical ranges and anchors are
preserved. Only the changed 32-bit source components were transferred from the
corrected exporter outputs; unrelated original bytes were retained. Baseline
cross-platform roundoff was measured at approximately 1.26e-16 and was not treated
as an authored geometry change. The two affected export reports were synchronized.

### Cross-platform export checksums

Of 500 retained export checksums, 492 still described CRLF text rather than the
LF bytes stored by Git. Every replacement was justified by an exact SHA-256 match
against the same source converted to CRLF; unknown mismatches were not repaired
blindly. All 500 records now identify current source bytes. Eight authoring scripts
write explicit LF text to prevent recurrence. This metadata repair changed no
mesh, texture or Blender master bytes.

No proven pixel-data, normal-map or material-binding defect was found by the
executed source checks, so texture images were not recompressed, repainted or
changed merely to create a diff. All 975 texture SHA-256 values remain unchanged.

## Executed evidence

- [Geometry repair run 39932586975](https://github.com/DaveBeusing/Forgeline/actions/runs/39932586975): original/corrected exporter comparison, exact changed-file allowlist, 19 regression fixtures and complete before/after production census. Eight geometry warnings became zero; structural errors remained zero. Repairs committed as `4937dad440e4ae00f8c2e86b95ca52956d41fa14`.
- [Checksum repair run 39933493799](https://github.com/DaveBeusing/Forgeline/actions/runs/39933493799): proved all 492 line-ending mismatches, refreshed reports, verified source meshes unchanged, passed all 22 regression tests including production regeneration and reran the complete source census. Committed as `ad985a8872b3a1bf4043de68596847ea3653fcf6`.
- [Native master census run 39932001628](https://github.com/DaveBeusing/Forgeline/actions/runs/39932001628): all 127 masters opened with the official checksum-verified pinned Blender, embedded execution disabled and source hashes unchanged. The native-master job passed; this statement does not classify the separate runtime-assets job as passed.
- The 22 Python tests also passed independently using the downloaded repository source/repair artifacts. They are not substitutes for .NET or native rendering tests.

Reports contain full per-file hashes and findings; the run artifacts preserve
before/after geometry counts and checksum mappings. One-time source-writing
migration scripts/workflows were removed after verified commits. Retained audit
and manual qualification workflows have read-only repository permissions.

## Acceptance boundaries

The source census and Blender open/structure tests are green. They do not establish
complete C# asset-suite, packaging, native D3D12 shading/animation, GPU lifetime,
measured frame-time or human visual acceptance. Current run-specific .NET and CI
results are recorded separately in PR #337; never infer success for skipped,
interrupted or still-running steps.

Existing test-contract defects remain separately tracked in #306 (construction
menu count), #315 (global production count assertions) and #316 (selection proxy
material classification). Do not change real proxy materials or remove conveyor
assets merely to satisfy stale tests. Existing required checks were not disabled.

Staged industrial/artillery Blender snapshots retain their historical source and
qualification identity. They were inspected, not silently rewritten or promoted;
current procedural source/exports remain authoritative under their existing
family documents. Human comparison in the game should inspect smelter pipe bends
and artillery tracks at close, ordinary and strategic zoom, including LOD changes,
lighting, material response and mechanical motion.
