# Infantry skeletal assets

The two existing Directorate infantry roles use editable four-figure Blender masters in `assets/source/units/directorate/authoring/infantry`. Their LOD0 mesh IDs now reference separate `skin.directorate.*` assets and nine `animation.directorate.*` assets per role. LOD1/2, materials, collision, sockets, footprints, selection and gameplay identities retain their existing contracts. Each squad remains one simulation entity.

## Rig and compiler

Compiler 1.12.0 supports the bounded optional skin subset described in [the decision](adr/BoundedInfantrySkinning.md). Each figure has pelvis, spine, head, left/right upper arms, forearms, thighs and shins. Four figures share one 44-joint armature. Joint TR uses metre coordinates and unit scale. glTF inverse binds must be finite, invertible and match the rest hierarchy; weights must be nonnegative and sum to one, with at most four influences. Float `WEIGHTS_0` and unsigned byte/short `JOINTS_0` are supported. Morphs, sparse/normalized accessors, second influence sets, non-unit scale animation, required extensions and retargeting are rejected. Constant unit-scale channels exported by Blender are validated then omitted.

Skin container type 6 carries an independent version-1 bounded payload including bind geometry, joint hierarchy, inverse binds, weights and sockets. Mesh payload version 2 remains unchanged. Existing rigid Animation version 1 remains byte-compatible; skeletal Animation version 2 adds the complete bind-rig SHA-256 signature before the common clip data. A clip selects one named action from the source GLB through `skeletalAnimation: true` and `animationClip`. References participate in normal dependency hashing, incremental builds, sealing and source-less validation. Rebuild/reinstall a complete matching catalog/player when a version or dependency fails; the player never compiles sources.

The reader limits each skin to 48 joints, 32768 vertices, 98304 indices, 32 sockets and 8 MiB payload/source input. Clips allow 64 nodes, 128 resulting TR channels, 1024 keys/channel, 8192 total keys and 120 seconds; the raw skeletal source allows 192 channels including discarded unit-scale keys. The rigid subset retains 32 nodes/64 channels. Conservative swept bounds cover interpolated rotations and weighted positions within two model metres. Render culling expands for skeletal poses without changing collision or selection bounds.

## Gameplay mapping and rendering

Completed-tick snapshots copy movement speed, target, reload timing, accepted shot events, visible hit events, actual suppression and wreck lifecycle. Idle/walk/run/aim/suppression loop; fire/hit/reload/death have bounded phases. Existing event visibility gates prevent hidden events from selecting actions. A cosmetic source-entity identity links a newly generated wreck to its destruction event without depending on the impact position or changing wreck behavior. Death lasts one second before the existing static discarded-equipment mesh resumes. Old/revealed wrecks do not replay death. Session changes, discontinuous ticks, hidden/reappearing units and generation changes reset visual history. Repeated completed ticks freeze poses. No wall-clock value drives gameplay.

CPU skinning uses the existing channel sampler and instanced renderer. A shared `(mesh, action, 24 Hz phase, detail)` key produces one deformed vertex upload for all matching squads. Existing material/index resources and shadow/color pose buffers are shared. Each fence-owned frame slot retains at most sixteen variants and 16 MiB GPU upload vertices; at most four slots are accepted. Overflow reports a static fallback. Renderer skin CPU resources are session-cached at most eight assets/32 MiB payload, alongside the existing 128-clip/16 MiB cache. CPU vertex scratch is bounded by those upload pools; two 48-matrix stack spans serve evaluation. Deformation and sampling allocate no managed memory after storage is supplied. The separate shot-socket reader caches at most eight bounded skins and fire clips; the two production roles reuse their immutable CPU skin data without per-entity resource creation. Resource and submission diagnostics expose renderer skin bytes, evaluated poses and fallback counts.

Detail fades through three weights to bind geometry near the existing 90-pixel projected threshold (LOD1/2 retain the existing 90/28-pixel selection with hysteresis). Actual reduced meshes remain static. The `weapon_muzzle` socket follows the right forearm palette at the completed-tick fire pose; authoritative hit trajectories remain independent. There is no foot IK, cover/prone mechanic, ragdoll or per-soldier controller.

## Author, edit and qualify

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/infantry_skin.py -InputRoot assets/source -OutputRoot artifacts/infantry-export
./tools/blender/Test-BlenderInfantry.ps1 -OutputRoot artifacts/infantry-authoring
./build/Build-RuntimeAssets.ps1 -Mode development
```

The first command reconstructs rigs from the retained component geometry/recipes. The committed `.blend` files own production poses and can be edited directly. The authoring gate reopens both masters twice, exports all actions, compares every compiled skin/clip payload to the committed GLB, edits a real key and proves its clip changes, then validates each compiled catalog with its source directory unavailable. `tools/fixtures/infantry-skin/samples.json` contains evaluated Blender joint reference positions for unit tests. The legacy static recipe exporter preserves a production `skinReference` and cannot overwrite its LOD0 metadata.

```powershell
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj -c Release --no-build -- --filter-class ForgeLine.Assets.Tests.InfantrySkinPipelineTests ForgeLine.Assets.Tests.RigidClipPipelineTests --parallel none
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.InfantryAnimationTests ForgeLine.Presentation.Tests.InfantrySkinRendererTests --parallel none
./build/Prepare-InfantrySkinQualification.ps1 -OutputRoot artifacts/infantry-review
```

The preparation produces identical bind geometry/materials with optional skin playback enabled/disabled in isolated source-less catalogs. Capture `InfantrySkinning` and `InfantrySkinningEngineer` for the nine actions, and `InfantrySkinningRepeated` for 1000 squads/4000 figures. `-MechanicalSeconds .375` selects a fixed bounded phase and enters capture identity. Use close/normal/strategic views and the unchanged performance comparison gate; commit and rebuild first. Fixed pose references do not prove smooth live motion or human acceptance. Package only compiled assets through the normal publisher; Blender and source assets never ship. See [qualification](InfantrySkinQualification.md) for measured status.
