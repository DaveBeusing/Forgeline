# Environment asset variation

Compiler 1.13.0 carries optional version-1 `scatter` metadata on mesh records in
the existing signed runtime manifest and FLAS metadata. Null metadata is omitted,
so source definitions and historical authored-map identities remain valid.
Runtime lookup needs only the compiled catalog and containers.

## Families and form kit

All four existing vegetation and 22 prop roots have adjacent *.scatter.json
recommendations. Their original *.asset.json files remain byte-identical to
the integration base so retained native-source signatures stay valid. The
compiler admits one inline or adjacent scatter declaration, rejects orphan or
duplicate sidecars, caps sidecars at 16 KiB and includes them in the source hash.
Sidecar changes invalidate cached runtime metadata without touching originals.
The original retained-prop/vegetation validators and lineage hashes are unchanged.
The first PR head failed the strict retained-prop roster check; sidecars and the
separate environment namespace correct that conflict without relaxing its gate.
The physical PBR inventory explicitly includes the new environment namespace. Original
mesh IDs, source bindings, materials, LOD distances and collision proxies remain
unchanged. Three additional forms each provide High/Reduced source GLBs:

| Family | Variant | High / Reduced triangles |
| --- | --- | ---: |
| conifer | conifer_swept | 80 / 48 |
| broadleaf | broadleaf_asymmetric | 624 / 152 |
| rock | rock_leaning | 16 / 12 |

All six new IDs use mesh.world.environment.<variant>[_lod1], outside the
sealed original vegetation/prop rosters.

These are original deformations of the corresponding existing ForgeLine source
geometry. `build/environment-variants/variants.json` is the editable recipe;
`assets/staged/environment-variants` retains three editable Blender 5.2.2 rest
masters and source/export hashes. No external model, image or new license is
introduced. Existing shared vegetation/industrial PBR references, UV atlas cells,
opaque sections and High/Reduced topology counts are retained. Tangents are
derived by the established compiler from the updated normals and unchanged UV0.
The kit adds six meshes and zero materials/textures. Instancing can batch equal
mesh/LOD/material identities; additional forms can add batches. No measured
frame-time, draw-call or memory improvement is claimed.

## Placement recommendations

`familyId` resolves to a self-identifying mesh root; variant roots explicitly
depend on that family. Generator version 1 agrees with the current decoration
recipe version. This metadata does not implement scatter or select variants.
Consumers enumerate mesh records, group by ordinal family ID and sort stable IDs.

`naturalSizeMeters` is the nominal instance scale for the existing unit authoring
envelope. Vegetation sizes follow the existing vegetation fixture; prop sizes
follow existing yard/kit vocabulary. Barrier/damage forms use the corresponding
module dimensions. Uniform scale multiplies that complete vector. Natural forms
allow 0.8..1.2 scale, 0.9..1.08 RGB tint multipliers and 0..360 degree yaw.
Constructed modules retain unit scale/tint and zero additional yaw to preserve
their authored modular dimensions. A consumer may orient an assembly separately.
Alpha, material factors and shader features are unchanged.

`groundAnchor` is in local source coordinates: (0,0,0) for rooted plants and
(0,-0.5,0) for centred props. To ground an instance, subtract the scaled and
rotated local anchor from its sampled ground position. Both visual LOD bounds
must contain the anchor. This point does not establish full contact on uneven
ground. Minimum spacing is a recommendation in metres, before optional scale;
consumers must account for their chosen scale and required clearances.
Slope recommendations are 0..30 degrees for natural forms, 0..40 for grass and
0..5 for constructed modules. These are authoring limits, not measured placement
acceptance or terrain/buildability rules.

`biome.dry`, `biome.temperate` and `biome.industrial` are affinity labels in this
library, not registrations of implemented climate profiles. Future biome/scatter
integration must explicitly resolve or map them. Unknown profiles cannot be
silently treated as implemented generators. Affinity IDs are unique and sorted.

The only admitted interaction is `presentationOnly`. Decorative collision
proxies remain compiler/tooling metadata: they grant no blocking, navigation,
cover, concealment, targeting or tactical authority. No placements, map hashes,
save/replay layouts or gameplay dependencies are changed.

## Reproduction and qualification

From the repository root:

```powershell
python build/environment-variants/author.py --output artifacts/environment-variants
python -m unittest discover -s build/environment-variants -p 'test_*.py' -v
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python build/environment-variants/masters.py
```

The producer exports isolated files; retained canonical variants use the same
bytes. Saved masters use engine +Y up/+Z forward mapped to Blender (x,-z,y),
metre units and identity object transforms. They contain editable geometry/UVs
and stable material IDs; they are rest geometry sources rather than packed-PBR
inspection projects. Export an edited saved master for isolated compiler review:

```powershell
blender --background --factory-startup --python build/environment-variants/masters.py -- --export-saved assets/staged/environment-variants/conifer_swept.blend --output artifacts/edited-variant
```

Topology, stable identity, material binding and identity transforms are checked;
unsupported modifiers/animation or split seam changes reject. Compile reviewed
GLBs through the ordinary source definition/compiler path. Do not copy `.blend`
files into a player or load authoring content at runtime.

Exactly one PATCH advances 0.1.520 to 0.1.521. The unchanged Versioning.ps1
Assert-PrVersion helper validates those exact source/head versions; complete
Validate-SemVer.ps1 with Git refs remains part of ordinary CI.

Local validation on source base 37ff3ddf8be2511431366bd4f8f318d55d2fb2d5:

- Pinned SDK 10.0.402 restore and Release build of the affected Core, Assets,
  AssetCompiler and focused test project pass with zero warnings/errors.
- Both focused .NET tests pass: invalid metadata/authority rejection and
  deterministic compile, metadata invalidation, source-less loading/qualification.
- The portable producer test passes for all six GLBs: deterministic bytes,
  unchanged indices/UVs, grounded envelopes and normalized normals.
- Blender 5.2.2 saves/reopens all three masters; all six meshes have zero position
  deviation and UV deviation within 5e-8. Saved-master exports are checked
  separately from recipe generation. The broadleaf saved export has zero
  position/UV deviation and maximum normal-component storage deviation
  0.000228823; canonical GLBs retain the recipe normals. The initial flat-polygon
  normal export failed and the retained masters use explicit smooth custom normals.

The local source snapshot contains affected projects rather than the complete
solution. Full solution, project-reference, canonical catalog, headless,
packaging and terminal-match gates require the unchanged ordinary PR CI.
Local test execution requires normal named-pipe access; the sandboxed runner
handshake failed before executing tests, and the ordinary runner then passed.
Blender's optional thumbnail-cache writes were denied; master saving/readback
succeeded. These environmental failures are not passing test executions.

Real in-game close-camera/PBR review, moving LOD transitions, terrain contact,
draw/residency measurement and explicit visual acceptance remain open. Existing
vegetation/prop reference scenes and rendering gates are retained; no new golden
or threshold is approved. The forms remain review candidates until those checks
are completed. This change does not imply production art approval or integration.
