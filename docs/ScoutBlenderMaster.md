# Scout Blender master

## Scope and source audit

Current integration base: a6fe118583be842e00416bcb100d1210f3240a00, master 0.1.431.
The branch is art/scout-blender-master with exactly one PATCH to 0.1.432.
The remote default branch was verified as master before branch creation. No Scout
branch or open Scout PR survived; logistics building PR 273 remains a separate owner.
Integrated PR 186 already provides the 6x6 kit, LODs, damage/wreck and typed motion.
Truck, artillery and operational-building integrations from PRs 274–276 are retained,
including every existing scene enum position. Their earlier native evidence is not
relabelled as current-source qualification. Base CI was still running at audit time.

The demonstrated gap is the missing retained native Scout master. Existing artwork
is preserved rather than replaced without a demonstrated quality gap. The strict
rest project has named source components and the existing mechanical frames. A
separate packed PBR/pose inspection project is not an export input. Seven visual
GLBs and the independent collision export stay under assets/staged/scout; canonical
source ownership remains held by the render residency lane. PR 268 supplies
qualification tooling, not physical/render-product acceptance. No source promotion,
merge, tag, release, golden approval or human acceptance is implied.

## Preserved contracts

The family is unit.directorate.scout_vehicle with LOD1/2, turret, damaged, critical,
wreck and collision identities. Existing instance scale is (3,2.4,5.2), Y-up;
engine (x,y,z) maps to Blender (x,-z,y), with the documented normalized source
frames and physical instance scale. Height offset remains 1.2. Collision, turn
radius, visibility/sensor ranges, ownership and navigation remain authoritative.

LOD0/1/2 contain 2512/736/284 triangles. The separate turret has 188; damaged,
critical and wreck have 24/80/116. The root retains body, six wheel partitions
(rate 2.380952380952381) and sensor partition (rate 0.6). The weapon retains its
existing outer turret frame and cannon range. All eleven root sockets, including
weapon_muzzle and sensor_origin, and all five turret sockets remain unchanged.
Projected 90/28-pixel LOD thresholds and 12% hysteresis remain unchanged; the
legacy 120/340 distance metadata is not substituted for the production selector.

The actual surface material has Base Color (sRGB), Normal and ORM (linear), using
the shared production atlas and existing BC7/runtime caps. It has no Emissive map;
no unsupported sensor lighting or new material/texture identity is fabricated.
Original kit provenance remains ForgeLine repository geometry with no third-party
inputs. Separate family copies of shared runtime textures are not introduced.

## Review fixtures and interpretation

Five appended Scout-only immutable fixtures cover idle/unselected single,
active/unselected single, active/selected single, 100 and 1000 inputs. Repeated
fixtures distribute the four real cardinal placements equally, preserve the
actual height offset and dimensions, and alternate copied still/active cosmetic
inputs. No projectiles, navigation paths or sensor decisions are simulated by
these fixtures. Selection metadata is fixture-only; the capture runner draws its
existing selection indicator. The read-only player context avoids missing-player
data in the new fixtures. The inherited ScoutVehicleProduction gallery retains
its original context and eight state/pose examples.

The planned installed capture matrix has 91 before/after pairs: 18 gallery,
54 single/100/1000 load, eight active/selected state, dark, wet, five LOD0 inspection
and four cardinal camera views. Camera yaw and actual actor placement are distinct.
Eight warmups, 32 timing samples and eight readbacks are short observations, not
sustained FPS, live wheel/sensor motion, thermal or full memory/lifecycle acceptance.
All nonexact comparisons remain visible under zero pixel/channel tolerance.

## Reproduction

Use the repository-pinned Blender 5.2.2 x64 (build d13f752e3b9c), SDK 10.0.401,
C# 14 and PowerShell 7.4+. Executable SHA-256:
f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0.

1. Restore and build ForgeLine.sln in Release.
2. Run build/scout/Qualify-Master.ps1 with a fresh short OutputRoot beneath artifacts
   and optional RenderPreviews. The adapter delegates to the existing strict
   roundtrip/rigid modules. Family-only code preserves component groups, packed
   surfaces and sampled poses. It changes no common importer or runtime schema.
3. Qualification saves two independent masters, exports one reopened master,
   rejects unsupported edits, saves/reopens a real qualification-only vertex edit,
   compiles five scoped catalogs and validates them with an absent source root.
   Independent .NET geometry/metadata and 330 pose-matrix comparisons retain the
   existing tolerances. The edited variant is not a production export. The strict rest exporter is a
   preservation contract: arbitrary geometry or graph edits do not become supported
   runtime data. Intentional art changes require reviewed source-kit/definition
   updates and reimport through the established pipeline; the saved vertex proof
   establishes native editability, not automatic production promotion.
4. Set FORGELINE_SCOUT_RUNTIME_SEED to a fully qualified canonical runtime and
   rerun ScoutBlenderMasterTests with FORGELINE_SCOUT_MASTER pointing to the same
   native qualification root to produce the complete sealed overlay.
5. build/scout/retained.py accepts a passing native qualification root, saves the
   projects/eight exports and lineage, and then verifies current canonical inputs.
   With no argument it checks the retained candidate without Blender.
6. Publish-PreAlpha.ps1 -VerifyReproducible -RuntimeAssetRoot uses the complete
   qualified overlay. Extract into a fresh directory outside the checkout and
   verify every package file checksum before startup/native captures.
7. build/scout/Capture-Review.ps1 receives explicit BaselineRuntime, CandidateRuntime,
   ClientPath and fresh OutputRoot. Pass the actual installed executable for the
   installed matrix. The comparator records pixels; no image gate or golden is
   approved by this command.

## Observed checkpoint

Release build passes with zero warnings/errors after correcting nullable test
access. All 22 affected Scout, truck, artillery and operational-building scene
cases and 117 shared presentation/rigid-motion regressions pass. All 102 portable
Blender contracts and canonical compiler process contracts pass. The current canonical cache
qualifies all 1363 assets and 74844434 payload bytes. First native corrected master
has all 57 original visual component selection groups and three packed source
maps with verified hashes/color spaces. The complete native qualification passes: five 12-asset catalogs (eight meshes,
three textures, one material) compile and validate without sources. Independent
and reopened exports have identical source/runtime bytes. All ten real native
negative cases pass; 330 independent matrices have maximum error 0.0000038146973
against the unchanged 0.00002 limit. A saved/reopened qualification-only edit
changes source Y by 0.0009999871253967276. All canonical source hashes remain
unchanged. The full sealed overlay, installed package, image matrix and final
full-suite CI remain pending at this checkpoint.

The first Release attempt failed a new test's nullable value-type member access;
its corrected build/tests pass. The first Blender attempt completed project and
preview generation but failed its report with an uninitialized component-list
variable. That original failure/log and outputs remain retained, and a fresh root
is used after the adapter correction. No failed attempt is relabelled as passing.

Authoring checkpoint: d54e8376b551f5c68a0287e86132dc47c5044d80. Original reports
are retained under [authoring evidence](scout/evidence/authoring/qualification.json).
The original component recipe is additionally bound by a portable source hash.
All 72 pre-existing scene enum IDs are retained; five Scout IDs are appended.
The first staging copy encountered the pre-created Git-attributes-only directory.
The corrected guard accepts only this empty metadata target and still rejects
overwriting any saved master/export candidate.

## Exact source-channel preservation

The first installed matrix (source 20b00ace) completed 91 pairs: 69 exact and 22
nonexact, 179 changed pixels total with maximum channel delta 44. Its eight-repeat
readbacks were exact, all counters matched, and no fallback/binding/submission
fault occurred. These are historical observations, not an accepted image result.
The strongest 4K/Low/1000 case becomes pixel-exact in a controlled diagnostic when
canonical source vertex values are serialized without Blender storage noise.

The family adapter still validates the real native scene through the shared strict
exporter. It then rejects channel deviations beyond 0.00000005, retains the original
readback GLBs and report separately, and serializes the unchanged source floats.
The final eight GLBs have exactly matching source channels. No common exporter,
renderer, material, texture, gameplay field or tolerance is changed. The .NET
comparison now additionally requires exact RuntimeMeshVertex sequences, including
compiled tangents. The old rounded outputs fail this assertion; the corrected
three actual native exports pass. All 330 pose matrices retain maximum error
0.0000038146973, and eleven unsupported edits reject, including a 0.000001 source-Y
geometry edit that would exceed the narrower preservation contract.

[Exact native qualification](scout/evidence/exact-authoring/qualification.json) is
bound to fa503a2b62b15fc26f08a7bbff9b472f117abed1. Its optional runtime seed reuses
previously qualified material/texture containers, recompiles changed mesh records
through development mode, and revalidates every scoped payload without sources.
The default provisioned workflow still performs fresh cold builds. Updated native
masters/exports and a complete new installed matrix remain separate checkpoints.
