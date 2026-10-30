# Saved terrain PBR sources

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The eight Central Divide terrain material IDs already resolve seven complete,
original periodic 4K sets. Preserve this integrated artwork in seven genuine
Blender 5.2.2 material masters and derive the compiler inputs by reopening and
baking those files. This closes the saved-source/edit/export gap. It does not
claim newly accepted art. Native review also exposed oversized industrial ground
rectangles; a separate profile-only correction concentrates those two areas into
smaller feathered working patches. Texture fidelity and the intentional profile
appearance change have separate evidence.

## Verified starting point

Ordered audit fetched `master` at
`a524b0a8164390abe1e9cb38170e690f46147574`, tracked version **0.1.436**.
The integration/default branch is `master`. No related remote terrain/surface
branch or open PR remained. PR #100 is explicitly superseded by merged #101;
#111, #149 and #166 provide the existing palette, physical maps and bounded
surface refinement. Their reviewed changes are reused. Related review/comment
collections were empty. Terrain residency #265 and qualification #268 are
integrated, but their physical/moving-camera/thermal/full-memory and human gates
remain open. Main CI on the fetched master was still running during the audit.

Branch `art/terrain-pbr-master` starts from that fetched master and advances
PATCH once to **0.1.437**. No release, tag, merge or golden approval is performed.

## Persistent bindings and physical scale

| Slot | Material suffix | Saved source family | Effective repeat |
| --- | --- | --- | --- |
| 0 | grass_ground | rocky_scrub | 32 m |
| 1 | dirt | dry_dirt | 32 m |
| 2 | mud | cracked_earth | 16 m |
| 3 | rock | bedrock | 32 m |
| 4 | gravel | crushed_gravel | 12 m |
| 5 | industrial_ground | dark_ash | 21.333 m |
| 6 | concrete | concrete_base | 16 m |
| 7 | scorched | dark_ash | 25.6 m |

Material IDs retain prefix `material.world.terrain.`. Industrial/scorched share
the ash maps; scorched retains its existing `[0.65,0.60,0.55,1]` reflectance factor
and separate UV scale. There are still **21 texture IDs and eight materials**.
The profile's tile, source UV scale, layer ordering, cubic feathers, slope
projection, macro modulation and shader detail remain authoritative and unchanged.

Current master explicitly compiles all 21 maps with deterministic balanced BC7:
Base Color is sRGB at a 1024 maximum; Normal and ORM are linear at 512. Full
semantic mip chains remain enabled. The historical RGBA terrain allocation in
the original production guide predates the current BC7 policy; source metadata
and current compiled observations govern this work. The 56 MiB compiled gameplay
texture-container and 64 MiB resident-texture limits are not increased. Neither
metric is complete physical GPU memory.

## Authoring and saved export

Sources live in `assets/source/world/authoring/terrain/<family>.blend`. Each
contains the original three packed 4096-square RGB(A)8 images, a metre-scale
Blender +Z plane, UV0, Principled inspection graph and frozen slot/compiler
bindings. Original PNGs and `build/Author-ProductionSurfaces.py` remain retained.
They are repository-authored mathematical sources under the repository license.
No third-party inputs are introduced.

The supported saved controls are linear RGB `Tint` (0.8–1.2, alpha one),
`RoughnessBias` (-0.05–0.05) and `DetailAmplitude` (0–0.04). Bounds compare in the
same float32 domain as Blender sockets. Detail embeds UV into a 4D torus before
sampling noise, so both texture directions are periodic. Defaults 1/0/0 preserve
the original surfaces. Changing controls requires a new reviewed export and
native comparison; it does not change gameplay or create new material slots.

Arbitrary graph rewiring, altered source images, color spaces, channel meanings,
UVs, unit scale, swatch transforms or unsupported animation/shapes are rejected.
The original graph and source hashes are checked after reopening. Packed image
paths are relative; masters are self-contained. ORM remains R=AO, G=roughness,
B=metallic. All terrain sets are dielectric. Principled has no direct AO input;
the independent AO export and game material remain authoritative.

```powershell
./tools/blender/Invoke-TerrainPbr.ps1 -Action Author -Family dry_dirt -OutputRoot artifacts/terrain-author
./tools/blender/Invoke-TerrainPbr.ps1 -Action Export -Family dry_dirt -OutputRoot artifacts/terrain-export
./tools/blender/Test-TerrainPbr.ps1 -OutputRoot artifacts/terrain-qualified
```

Use a fresh output root for every invocation. Export opens the real saved master
and runs CPU Cycles EMIT bakes at 4096 square, one sample and one thread. Base
reflectance writes sRGB; tangent Normal and ORM write linear data. Emission
isolates channel data from lighting, AO shading and display/view transforms.
Existing tangent normal pixels are preserved rather than regenerated from the
flat inspection plane. A deterministic lossless encoder writes the final PNGs.
The exporter never saves temporary bake nodes or stripped previews over a master.

Outputs are isolated under `artifacts/` until reviewed. Canonical texture
definitions then select `world/textures/terrain/blender/<family>/` baked PNGs,
with existing production names and stable IDs. `surface.gltf` in each folder
is an editor-only flat material swatch referencing those maps. Its engine/glTF
+Y plane is not a replacement for gameplay terrain, has no runtime mesh Asset ID,
and is not shipped. The two ash appearances retain their distinct repeat scales.

## Qualification and integration limits

Portable tests reject persistent-ID, cap, format, mip, color-space, source-path,
shared-slot and bounded-control mutations. Provisioned native qualification
reopens each saved master for independent bakes, compares complete output bytes,
exercises real rejected edits, and verifies a saved tint edit changes Base Color
while preserving Normal/ORM. Its scoped compiler closure is exactly 29 assets:
21 textures plus eight materials, all validated without an authoring root.
Ordinary CI runs portable contracts. The manual `saved-terrain` job requires a
provisioned Windows x64 Blender runner and is not a simulated native pass.

Complete source adoption also requires the full canonical compiler, existing
terrain/material/heightfield/splat tests, native source-bound before/after scenes,
resource observations and reproducible installed-player checks. No shader,
heightfield, navigation, road, control-map semantics or simulation rule changes
are authorized by this source adapter. Eight slots, four active layers, the
33x33 control grid and the thirteen-binding/sample ceiling remain unchanged.

Static repeatability and preserved inputs establish functional source continuity.
They do not close live camera LOD/shimmer/seam review, sustained client performance,
complete physical memory/lifecycle, thermal stability or explicit human art gates.
Keep integration in draft while required checks or those acceptance gates remain
open. See [qualification](TerrainPbrBlenderQualification.md) for exact source and execution results.
