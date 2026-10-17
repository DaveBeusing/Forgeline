# Rigid clip assets

[Decision and bounds](adr/BoundedRigidClips.md).

Compiler 1.11.0 adds optional `animation` assets from glTF 2.0 `.gltf` or `.glb` files. Each definition supplies a stable ID, for example `animation.test.arm_cycle`, type `animation` and source `rigid_clip.glb`. A mesh may list one ID in `animationReferences`; it becomes a typed dependency and participates in source hashes, external-buffer hashes, incremental invalidation, manifest sealing and deployment integrity. Existing production assets retain their original mechanical behavior and need no migration.

Initial support is intentionally narrow: one looping clip, named rigid nodes with identity rest TRS, canonical meter scale, translation and unit-quaternion rotation, STEP/LINEAR interpolation, strictly increasing finite timestamps starting at zero. glTF nodes may appear in any order; compilation puts parents first and rejects cycles/multiple parents. Each node name and its complete parent chain must match static `mechanicalParts` and an explicit socket. Animated scale, skinning, morph channels, cubic interpolation, sparse/normalized animation accessors and required extensions fail with diagnostics. Bake rest transforms into mesh coordinates; export identity-rest animation control nodes. Do not reference inspection-only actions from the existing mechanical authoring projects.

Clips use the normal glTF row-equivalent rotation then translation before the instance transform, around the authored socket. They cannot coexist with code-driven parts in the same mesh. Legacy metric wheel/turret transforms keep their existing semantics. Compiler/load-time swept-volume checks conservatively cover every interpolated orientation and translation, including parent motion, inside the unchanged static mesh bounds. Clip meshes must also fit the renderer's canonical `[-0.5,0.5]` unit box. This may reject a safe but tightly packed rotating part: retain a containing static frame or reduce motion rather than bypassing validation. Gameplay collision, selection and simulation authority do not change.

Limits: 32 nodes, 64 channels, 1024 keys/channel, 8192 keys/clip, 120 seconds, 2-meter translation vectors, 8 MiB aggregate source/container and external buffer input, 1 MiB compiled clip payload and 64 KiB container metadata. Existing mesh/header/manifest versions remain readable; the appended Animation type is 5 and the independent binary version is 1. Older players cannot load clip catalogs. An unsupported payload version requires recompilation/reinstallation of the complete matching player and catalog.

The renderer caches immutable clips per asset ID/session (at most 128 clips/16 MiB of payload; overflow fails controlled loading), binds channels once on mesh load and uses existing part geometry equivalence, instancing, frame buffers and fence-owned disposal. Evaluation uses binary search, no per-entity animation controller, no per-frame managed allocation and at most the existing 32-matrix/2-KiB scratch span. Loaded clip count and payload bytes appear in material/resource diagnostics. There are no animation GPU buffers/textures/descriptors. Repeated geometry stays one instanced draw per distinct part/material, not per entity. A copied active clock from mechanical history supplies render interpolation; it freezes on inactive/power-loss ticks and paused repeated snapshots. Session changes, hidden/reappearing and reused entity identities retain existing reset behavior. Damage uses its authored state mesh; wrecks/destroyed buildings suppress motion. Screen-space weight fades to neutral; reduced assets with no reference stay static. This clock describes active operation, not arbitrary cinematic wall time or a gameplay event-state machine.

## Reproduce the fixture

`tools/fixtures/rigid-clips` contains the editable `.blend`, actual Blender-exported GLB, seven evaluated Blender sample poses and three minimal source definitions. The inner arm translates/rotates inside four static corner posts. The fixture is outside production discovery and player packaging.

```powershell
dotnet build ForgeLine.sln --configuration Release
./tools/blender/Test-BlenderRigidClips.ps1 -OutputRoot artifacts/blender-rigid-clips-review
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj -c Release --no-build -- --filter-class ForgeLine.Assets.Tests.RigidClipPipelineTests --parallel none
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.RigidClipPlaybackTests ForgeLine.Presentation.Tests.RigidClipRendererTests --parallel none
```

The Blender gate compiles the committed export and two fresh pinned-Blender exports, compares compiled clip payload hashes and validates each with its source tree temporarily unavailable. Normal CI runs parser, payload, dependency, source-less and playback/batching tests without Blender. The manual authoring workflow has a separate bounded clip job.

## Native review

Build/qualify production assets first, then prepare fresh isolated source/runtime clones:

```powershell
./build/Build-RuntimeAssets.ps1 -Mode development
./build/Prepare-RigidClipQualification.ps1 -OutputRoot artifacts/rigid-clip-review
```

The preparation replaces only the tank LOD0 in the isolated review catalog with the fixture mesh and clip. It preserves the game's production source tree and validates clip/neutral source-less catalogs. Compare the same fixture mesh with and without clip playback, not the production tank geometry. Commit changes and rebuild before capture so the normal clean-tree/source-SHA rule holds.

```powershell
./build/Invoke-RtsReferenceQualification.ps1 -Capture -OutputRoot artifacts/clip-frame-1 -RuntimeAssetRoot artifacts/rigid-clip-review/clip-runtime -Scenes MainBattleTankProduction -Profiles High -Zooms CloseTactical -Displays 1920x1080 -InspectionDistanceMeters 20 -MechanicalSeconds 1 -Warmups 8 -Samples 8 -RepeatCaptures 2
```

`MechanicalSeconds` is a bounded optional fixed pose time (0..120), accepted only for the tank production/formation fixtures. It enters the fixture fingerprint, capture key and metadata; omission preserves original reference poses. Capture 0, .5, 1, 1.5, 2 and 3 seconds for motion/loop review. Capture `MainBattleTankFormation` against both isolated catalogs for repeated-instance cost. Use the existing performance comparison gate without changing budgets. Fixed frames prove exact phases; they do not by themselves prove smooth video playback or human visual acceptance. Qualification results and any remaining acceptance limits belong in the associated evidence report.

Package only compiled manifests/containers through the existing publisher; no Blender, `.blend`, glTF sources or authoring scripts enter the player. Missing references fail catalog/mesh qualification and controlled session loading; rebuild the complete catalog instead of silently ignoring motion.

Recorded native frames, paired cost failures and installed-player results: [qualification](RigidClipQualification.md).
