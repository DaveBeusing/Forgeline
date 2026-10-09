# RTS visual reference qualification

## Implemented baseline

The gameplay camera uses perspective projection with 45-degree azimuth,
55-degree downward pitch, 45-degree vertical field of view and zero roll.
Close, normal and strategic bookmarks use camera-to-target distances of
120, 420 and 1,000 metres. The authoring reference is 2560 x 1440 at 100%
render/UI scale; review sizes also include 1920 x 1080, 3840 x 2160 and
3440 x 1440. See [Camera and Input](CameraAndInput.md) for engine conventions.

Presentation selects existing unit, building and world mesh chains from
projected size with 12% hysteresis. Construction/destruction assets remain
purpose-specific. Existing strategic overlays remain available; automatic
LOD3/impostor representation is not introduced. Roads and bridges retain their
existing mesh path. Simulation, collision and navigation remain independent.

Semantic mip generation and BC7 compression extend the existing runtime format.
Nineteen shared texture chains occupy 26,448 bytes (previously 103,740); the
surface ORM atlas occupies 21,504 bytes (previously 86,016). These twenty
resources save 138,804 resident bytes. The color atlas retains RGBA8.
See [Asset Pipeline](AssetPipeline.md).

## Reproducible inventory and runtime checks

From the repository root after a Release build:

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release --no-build -- --source assets/source --runtime assets/runtime --clean --qualification-output artifacts/asset-qualification.json
pwsh ./build/Export-VisualAssetInventory.ps1
pwsh ./build/Invoke-RtsReferenceQualification.ps1
```

The inventory resolves stable IDs to source/definition/runtime paths, materials,
textures, dependencies, LODs, collision and sockets. It reads texture source
dimensions and flags masters below 2048 pixels for review. The flag is advisory:
dimensions alone do not prove useful detail; tiny repeated textures can be
deliberate. No source is enlarged to manufacture a numerical quality pass.

Current inventory: 324 assets, 158 meshes, 39 textures and 127 materials.
Compiled container footprint: 4,985,866 bytes, including 790,803 mesh bytes and
4,048,447 texture bytes. Container sizes include metadata and studio splash
assets; they are not GPU allocations or residency.

The matrix runs the production Central Divide smoke scene with 1,000 opt-in
synthetic render instances, isolated settings and all twelve display/zoom
combinations. Reports include actual surface size, draw calls, visible objects,
LOD1/LOD2 counts, texture residency, CPU submission, available GPU timestamps,
frame rate and binding failures. A window clamp means the requested resolution
is unqualified. This workflow validates loading/binding/display behavior; it
does not provide a fixed-tick art-review scene, screenshots or a performance
guarantee.

## Production art and acceptance work still required

Local smoke measurements used an NVIDIA RTX PRO 5000 Blackwell Generation
Laptop GPU with 25,309,478,912 dedicated-memory bytes, native D3D12, VSync and
100% UI scale. At 2560 x 1440 normal zoom, the 1,000-instance smoke reported
48.28 FPS, 17.73 ms CPU render time, 7.22 ms GPU time, 26 measured draw calls,
938 visible instances and 2,004,832 resident texture bytes. This short run
included background validation activity and is exploratory evidence; it does
not establish a controlled 60+ FPS acceptance result. Windowed 3840 x 2160
requests produced 3840 x 2130 surfaces, leaving exact 4K unqualified.

- Terrain sources and original shared material families remain small baseline
  textures. The shared color atlas source is 1254 x 1254. These are not accepted
  4K/8K production masters.
- Produce and visually qualify model/source-master upgrades while preserving
  silhouette, function, faction, scale, pivots, sockets, damage, UVs and tangents.
  Existing mesh families have not been replaced by newly qualified model art.
- Define world coverage and texel density per material from the normal camera;
  choose runtime caps from screen use and memory budgets. Category alone must
  not force 8K sources or runtime allocations.
- Produce a fixed-tick scene with the requested unit/building/resource families,
  bridge/road, props, vegetation, decals, damage and selection/fog. Capture and
  review all required zoom/display combinations under stable lighting.
- Qualify terrain blur/tiling/shimmer, alpha coverage, material response, shadows,
  LOD transitions and unit/background separation. Data tests cannot establish
  these perceptual outcomes.
- Qualify LOD3/strategic representation and infrastructure reduction where
  useful, with per-asset tuning from captured evidence.
- Profile CPU/GPU percentiles, camera stutter, upload spikes, mesh/render-target
  memory and total GPU allocations on named target hardware. Residency counters
  are not a total-VRAM metric.

The full higher-resolution visual upgrade remains incomplete until this art,
capture and performance work is accepted. Engineering checks do not promote
unreviewed source art to production acceptance.
