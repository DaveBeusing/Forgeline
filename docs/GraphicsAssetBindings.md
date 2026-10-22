# Graphics asset bindings

The source manifest, compiled container and renderer must agree on the identities
of mesh material slots, texture channels, LODs, skins and animation clips. Integrity
hashes alone establish byte consistency; they do not establish these typed bindings.

## Source inventory and actual render path

The implementation baseline is master
`d7c45792cffc5259f65be0cd2e2894fd11431fe8`, version `0.1.450`.
Source discovery on that baseline found 1,825 definitions: 744 meshes, 907 textures,
133 materials, 38 animations, two skins and one UI skin. The four Emissive textures
already declare sRGB. Existing mesh definitions have no unsorted or repeated
material slots; preserving slot order corrects the compiler contract without
changing those authored bindings.

| Binding | Producer | Runtime consumer |
| --- | --- | --- |
| Mesh sections and ordered material IDs | `GltfImporter`, `SkinImporter` | `ResidentAssetStore`, `SimpleInstanceRenderer` |
| Base Color, Normal, ORM, Emissive | `MaterialImporter`, `TextureImporter` | `PreparedAssetSet`, `ResidentAssetStore`, `SimpleInstanceRenderer` |
| LODs, sockets and rigid clips | Source asset definitions, `AnimationImporter` | Runtime catalog, resident mesh/clip resources |
| Skeletal clips and skin | `SkinImporter`, `AnimationImporter` | `RuntimeSkinData.Bind`, resident skin resources |
| UI skin pages | `UiSkinImporter` | UI skin resources and atlas renderers |

Ordinary instanced PBR draws bind Base Color to pixel slot 0, Normal to 1, ORM to 2
and Emissive to 3 in `SimpleInstanceRenderer`. Those are the existing HLSL texture
bindings. Terrain, UI and effects retain their own shader contracts. Shaders are
compiled by `DxcShaderCompiler` at pipeline creation; the runtime asset format has
no Shader asset type or stable shader asset IDs. No shader assets or gameplay
animation state are fabricated to fill that gap.

## Enforced contracts

Compiler 1.12.1 retains material slot order and repeated IDs in manifest metadata.
Its version invalidates the incremental cache without changing asset format,
stable IDs, runtime version or resource budgets. Emissive and Base Color textures
require sRGB at compilation and runtime decoding; Normal/ORM remain linear.
Explicit null material factor/UV arrays produce a controlled compiler diagnostic.

Integrity-bearing catalogs reject typed material, texture, LOD and collision
references unless they are real dependencies with the expected target type.
Legacy unsigned manifests remain inspectable for tooling compatibility.

Runtime qualification now checks actual mesh/skin payload material IDs against
the sealed manifest, resolves their materials, and verifies UVs for textured
materials and tangents for normal maps. Material payload texture IDs must be
registered dependencies with matching usage/color space. Missing authored slots
still deliberately use neutral textures; missing or invalid referenced assets
are qualification errors. Existing rig signatures, rigid ranges and socket/clip
validation remain active. Texture descriptions are cached without retaining
decoded mip arrays across assets.

The existing `--residency-inventory-output` report includes ordered mesh material
slots from payloads, named material texture slots (null means deliberately
unauthored), LODs, skin IDs and animation IDs alongside source paths, runtime hashes,
dependencies, material consumers and texture format/mip data.

Graphics pipeline input validation rejects undefined formats/rates/topologies,
duplicate semantic/index pairs and buffered constants without a declared size.
Root signatures and pipeline states are released if naming, input-layout conversion
or pipeline creation fails before ownership reaches the resource retirement system.
Successful resources continue to use the existing fence-based retirement path.

## Validation and manual handoff

On 22 October 2026, dependency restore and the complete Release solution build
succeeded with SDK 10.0.401, zero warnings and zero errors. The project-reference
validator accepted all 44 projects without cycles or boundary violations.

No automated tests were added or executed, as requested. Native D3D12/WARP and
physical GPU rendering, quality/session transitions, shader execution and disposal
stress, deterministic double compilation, source-less package execution and visual
acceptance remain manual qualification work. No screenshot goldens were changed.
CI workflows and required checks are unchanged; a successful build alone does not
establish these runtime results.

The normal asset compiler emits binding diagnostics during development/build and
source-less validate mode. To export the extended inventory after compiling assets:

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler --configuration Release --no-build -- --mode validate --runtime assets/runtime --qualification-output artifacts/asset-bindings.json --residency-inventory-output artifacts/asset-binding-inventory.json
```

Rebuild runtime assets with compiler 1.12.1 before packaging. Preserve independent
physical GPU and WARP results, and run the canonical package/native checks in
`Development.md` during manual acceptance.
