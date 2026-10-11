# Directorate Command Core production brief

The existing identity is building.directorate.command_core; gameplay footprint
is 20 x 20 metres with 12-metre visual scale, centre pivot and +Y up/+Z access.
LOD0 previously had 300 triangles and LOD1/2 share the same 36-triangle source.
Its six box masses and three fittings lack recessed access, a readable
communications array, independent strategic reduction and authored construction.
Primary metadata has no sockets. The shared collision box remains unchanged.

Produce a low, wide reinforced headquarters, split command blocks surrounding
an access court, a broad communications spine, roof heat exchangers, tapered
blast walls, stepped foundation, recessed loading portal and serviced roof.
Use the current sixteen-cell PBR atlas with concrete, painted steel, structural
steel, glass, warning and equipment cells; no new texture or material family.
Keep one material section per mesh. Large roof/access landmarks survive LOD2.

Retain X [-.505,.505], Z [-.34,.48] and top Y .56 (20.2 x 16.4 x 12.72m
relative to the current transform). Deliberately extend only the foundation
bottom from Y -.34 to -.5: existing placement centres at half the 12m height,
so the old base floats 1.92m above flat ground. The new bottom seats at ground
without changing centre pivot, gameplay footprint, placement or navigation.
This is a cosmetic foundation change; collision/spatial authority is unchanged.
LOD1/2 will retain these extents to avoid silhouette/bounds jumps. No alternate
building definition, simulation changes or cosmetic collision detail.

Editable component recipes and a deterministic mesh exporter are the source.
Export standalone modular glTF pieces and dedicated assembled LOD0/1/2.
Construction and operational attachments are Command-Core-specific variants
of the existing state vocabulary; authoritative priority remains unchanged.
Destroyed geometry is a collapsed command/array wreck, not a physics object.
Socket additions are named authoring anchors only, with no new gameplay logic.

Visual acceptance requires production D3D12 renders at 120/420/1000m, normal
role read and state distinction. Compilation is not final art approval.

## Editable sources and reproduction

Edit `assets/source/buildings/directorate/authoring/command_core.kit.json`:
component dimensions, bevels, atlas cells, explicit LOD membership and state
recipes are independent of compiled runtime files. With Python and numpy,
run `python build/Author-CommandCore.py`. Use `--output <empty-directory>`
for an isolated reproducibility check. No DCC installation is required;
assembled and individual component glTFs are directly importable into glTF
editors. Each export embeds its own buffer and names the mesh. JSON recipes
remain the parametric source of truth; editing an exported mesh is an
explicit fork and is not silently imported back into the kit.

The `sources/command_core/kit` directory exports foundation, command blocks,
access portal, roof systems, communications, structural frame, partial shell,
isolation shutters, damaged plates, critical exposed ribs and wreck modules.
These are editable reusable authoring pieces, not eleven new runtime types.
Other Directorate families retain their existing source and state modules.
Existing bevel/detail batch scripts must not replace this family's new paths;
use this dedicated exporter when regenerating the Command Core.

| Runtime identity suffix | glTF filename | Triangles |
| --- | --- | ---: |
| primary | command_core_assembled_lod0.gltf | 4244 |
| .lod1 | command_core_assembled_lod1.gltf | 952 |
| .lod2 | command_core_assembled_lod2.gltf | 348 |
| .foundation | command_core_foundation.gltf | 108 |
| .structural_frame | command_core_structural_frame.gltf | 240 |
| .partial_shell | command_core_partial_shell.gltf | 288 |
| .unpowered | command_core_unpowered.gltf | 36 |
| .damaged | command_core_damaged.gltf | 48 |
| .critical | command_core_critical.gltf | 180 |
| .destroyed | command_core_destroyed.gltf | 204 |

All runtime identities have explicit asset metadata and one material section.
The three new LOD silhouettes retain the complete foundation, armoured wing
masses, open service mouth, communications spine, broad signal panel and dish.
LOD0 adds manufactured bevel faces, roof louvres, hollow exhaust collars,
windows, joints, hatches, hinges and antenna feed. LOD1 removes subpixel
fittings/bevels and reduces curved tessellation. LOD2 keeps the role landmarks
and removes most roof fittings. Projected-size selection and hysteresis retain
the existing engine policy. The old LOD1/2 shared 36-triangle representation
is retained as historical source, not referenced by the new metadata.

One section reuses structural atlas PBR; authored local face charts stay inside
an intended cell with the existing .018 inset. Faces have explicit outward
normals; the compiler generates tangent bases from UVs. Weathering and normal
relief come from matched existing production atlas maps, with no painted light,
AO in Base Color or new unique textures. Unpowered shutters obscure glazing
and close the broad signal panel. Damaged/critical add displaced armour,
exposed ribs and a hanging plate; existing material/VFX state remains active.
Destroyed substitutes low collapsed mass and a fallen communications mast.
State priority, construction progress thresholds and authority are unchanged.

Primary socket anchors are communications (0,.395,.06), service_access
(0,-.414,.25), roof_maintenance (.29,.19,.05). They are normalized in the
retained visual coordinate system and do not drive gameplay. The unchanged
shared collision proxy and 20 x 20 x 12m gameplay definition remain authoritative.
The new bottom -.5 seats at the centre sample; the existing terrain slope and
placement validity policies still govern permitted sites, not cosmetic detail.

Original ForgeLine component geometry has no external mesh, image or DCC
library input. Provenance is in the kit; repository ownership/terms apply.
No external license grant or final art approval is implied. All PBR sources
retain their existing provenance. `command_core.exports.json` records source
SHA256, vertex/triangle counts and extents for every generated file.

## Qualification method

Compile through the existing Asset Compiler; the catalog gains seven meshes:
348 assets, 165 meshes, 56 textures and 127 materials. Compiler outputs are
generated only. Run the visual inventory exporter after compiling. A fresh
export to an isolated directory matched all source hashes; every triangle in
the kit, assembled LODs and states was finite, nondegenerate, correctly wound
and paired with unit-length normals. Compiled tests check UV/tangent/material
sections, full LOD extents, descending index counts, an open loading mouth,
anchors, collision resolution and state references.

`CommandCoreProduction` is an opt-in immutable Central Divide terrain fixture
with three construction stages, operational, unpowered, damaged, critical and
wreck instances at the real gameplay scale. It derives all states through ECS
extraction. Existing family galleries remain available. Capture it through
`Invoke-RtsReferenceQualification.ps1 -Capture -Scenes CommandCoreProduction`
at CloseTactical/NormalGameplay/Strategic and native review displays. This
fixture checks fixed states; it does not prove continuous lifecycle motion,
selection interaction, physical-monitor readability or gameplay acceptance.

Build, runtime and capture results are recorded below after qualification.

The raised foundation has a real sloped service ramp and a cut-through lane.
It rises from a six-centimetre front lip to the 1.032m loading floor across
4.6m, within the retained bounds. The entry anchor follows that threshold.
The ramp is cosmetic; vehicle pathing does not sample its triangles.
