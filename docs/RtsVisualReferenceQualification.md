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
resources originally saved 138,804 resident bytes. The subsequent production
atlas now derives three BC7 maps from native 4096-pixel sources. Terrain has
seven complete source-master families. See [Production surfaces](ProductionSurfaceMasters.md).
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

Current inventory: 413 assets, 230 meshes, 56 textures and 127 materials.
Production texture budgets are documented in [Production surfaces](ProductionSurfaceMasters.md).
Container sizes include metadata and studio splash assets; they are not GPU
allocations or residency. The original 324-asset baseline occupied 4,985,866
container bytes; do not use that historical figure for the new production set.

The matrix runs the production Central Divide smoke scene with 1,000 opt-in
synthetic render instances, isolated settings and all twelve display/zoom
combinations. Reports include actual surface size, draw calls, visible objects,
LOD1/LOD2 counts, texture residency, CPU submission, available GPU timestamps,
frame rate and binding failures. A window clamp means the requested resolution
is unqualified. This workflow validates loading/binding/display behavior; it
does not provide a fixed-tick art-review scene, screenshots or a performance
guarantee.

## Fixed-state native evidence

The five industrial production galleries retain nine real presentation states
and unchanged gameplay footprint scale per family. Their before/after captures
use fixed 120/420/1000m reference distances. See
[Industrial building production](IndustrialBuildingProduction.md) for model scope,
reproduction and qualification limits; compilation alone is not visual acceptance.

The optional `Invoke-RtsReferenceQualification.ps1 -Capture` lane extends the original loading smoke with six immutable reference states, all four native resolutions, three zooms and Low/Medium/High profiles. PNGs, source/runtime hashes, actual backbuffer dimensions, repeated pixels, CPU/GPU distributions and resource diagnostics form a metadata manifest. A comparison report protects baselines and distinguishes structural qualification from pending human art review. See [Native visual reference evidence](VisualReferenceEvidence.md) for fixtures, methodology, review rubric, failure handling and WARP/hardware separation.

Exact 3840×2160 backbuffer output is verified independently of the host window. This does not certify a physical 4K monitor or monitor-DPI transitions. The original smoke remains available without image readback.

## Production art and acceptance work still required

Local smoke measurements used an NVIDIA RTX PRO 5000 Blackwell Generation
Laptop GPU with 25,309,478,912 dedicated-memory bytes, native D3D12, VSync and
100% UI scale. At 2560 x 1440 normal zoom, the 1,000-instance smoke reported
48.28 FPS, 17.73 ms CPU render time, 7.22 ms GPU time, 26 measured draw calls,
938 visible instances and 2,004,832 resident texture bytes. This short run
included background validation activity and is exploratory evidence; it does
not establish a controlled 60+ FPS acceptance result. Windowed 3840 x 2160
requests produced 3840 x 2130 surfaces, leaving exact 4K unqualified.

- Native 4096-pixel terrain and shared surface masters are implemented, with
  matched PBR channels. Manufactured LOD0 components and seven prop silhouettes
  have new sources. Visually qualify those upgrades at the reference camera;
  compiler validation does not substitute for final art acceptance.
- Define world coverage and texel density per material from the normal camera;
  choose runtime caps from screen use and memory budgets. Category alone must
  not force 8K sources or runtime allocations.
- Review the fixed-state unit/building/resource, bridge/road, prop, vegetation,
  decal, damage, selection and intelligence captures under the evidence rubric;
  none is automatically promoted to an accepted visual golden.
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

## Mechanical state gallery

The native capture lane accepts `MechanicalIdle`, `MechanicalMoving`,
`MechanicalFiring`, `MechanicalLoading`, `MechanicalUnpowered` and
`MechanicalBroken`. Each authored immutable scene places the Main Battle Tank,
Scout Vehicle, Cargo Truck, Supply Truck and Vehicle Factory at the same camera
reference. Fixture version 2 includes explicit copied mechanical poses. These
are reproducible presentation states, not claims of natural loading or combat.
Use the existing three zoom references and record actual profile/display,
CPU/GPU distributions, submission ranges and residency. Repeated still captures
do not establish sustained motion, wheel contact or LOD-transition acceptance.

### Battlefield VFX capture result

The 0.1.343 hardware matrix for BattlefieldCombat and DamageDestruction
contains 18 native 1920x1080 captures across three profiles and zooms.
Seventeen are pixel-stable; BattlefieldCombat High CloseTactical differs
in six channels (maximum delta 25), leaving the matrix failed. The cause
is not isolated. Fixed-age captures also miss the short muzzle phase and
cannot establish sustained motion acceptance. See the
[full qualification and unresolved gates](BattlefieldEffectCatalog.md#qualification-at-51219a4-01343).

### Final image matrix

The 0.1.344 matrix covers all six fixtures, Low/Medium/High, three RTS
zooms and all four native displays. It records 214 stable rows out of 216;
two High Full-HD tactical states remain unequal, so no golden is accepted.
Eight explicit-DPI 4K/Ultrawide rows pass structurally. See
[Final Image Quality](FinalImageQuality.md) for before/after, timing and
physical/motion/borderless limits.

The opt-in `CommandCoreProduction` gallery adds all eight construction,
operational, power, health and wreck states using the Command Core's real
20 x 20 x 12m gameplay scale. Its dedicated modular/LOD sources and qualification
are documented in [Command Core production](CommandCoreProduction.md).

The dedicated [Main Battle Tank](MainBattleTankProduction.md) records its native
state gallery, 1,000-tank formation, measured close-view performance blocker
and pending live/art acceptance. Technical image equality does not clear those gates.

## Modular environment galleries

Opt-in `EnvironmentKitYard` / `EnvironmentKitBridge` extract Central Divide at
tick 32, seed 2026. `EnvironmentKitRepeated` adds 1,000 read-only copied kit
instances. The [modular kit qualification](ModularEnvironmentKit.md) records
72 profile/zoom/display rows, six repeated-kit rows, exact backbuffers, compiled
source identity and the open 60-FPS/borderless-budget/human-art gates.


[Industrial building production](IndustrialBuildingProduction.md) records five
authoritative nine-state scenes, fifteen matched High/1080 baseline cases and
180 final native cases across three profiles, three zooms and four backbuffer
sizes. Fourteen original PNGs support visual review; complete portable timing,
resource, camera and hash metadata are included. Structural equality, source
identity and zero binding/fallback failures do not establish human art signoff,
sustained FPS, moving LOD quality or physical-monitor DPI acceptance.
