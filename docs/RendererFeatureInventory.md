# Renderer feature inventory

## Scope and evidence

Code baseline: `b726ff9c00b7dc74cf285219fa58440acf663e16` (`master`,
0.1.454), synchronized after the resource cluster integration. This inventory describes consumers present in that revision. It is
not a visual acceptance report. Native evidence below is historical evidence
linked from the owning documents; it does not establish a new native run on
this revision. Builds, headless tests and null-device tests cannot establish
hardware rendering quality, motion stability or sustained frame rate.

Status vocabulary: **wired** has a production consumer; **optional** has a
deliberately conditional consumer; **specified** has no active implementation
of the named technique; **unused** requires evidence of no consumer; **defect**
requires a reproducible failure. A capability probe is not an implementation.
No registration is removed merely because a fixture or preset does not use it.

## End-to-end matrix

Paths are repository-relative and name concrete files, including tests. Shared
client consumer **C** means `src/ForgeLine.Client/ClientRenderHost.cs`, the live
gameplay render owner. Shared extraction **E** means
`src/ForgeLine.Presentation/PresentationExtractor.cs`; it runs after fixed ticks
and publishes copied, faction-filtered presentation data. These aliases apply
only to the two repeated columns below.

| Feature / status | Asset or input | Presentation consumer | Renderer / shader | Client | Tests | Native status / qualification source |
| --- | --- | --- | --- | --- | --- | --- |
| Runtime mesh/material/texture binding — wired | `src/ForgeLine.Assets/RuntimeAssetCatalog.cs`, `src/ForgeLine.Assets/RuntimeAssetBindingValidator.cs` | `src/ForgeLine.Presentation/RuntimeWorldAssetResources.cs`, `src/ForgeLine.Presentation/RenderAssetSession.cs` | `src/ForgeLine.Presentation/InstanceMaterialPipeline.cs`, `src/ForgeLine.Graphics/D3D12GraphicsCommandContext.cs` | C; `src/ForgeLine.Client/RuntimeAssetPathResolver.cs` | `tests/ForgeLine.Assets.Tests/RuntimeAssetQualificationTests.cs`, `tests/ForgeLine.Client.Tests/RuntimeAssetPathResolverTests.cs` | Offline qualification checks payload slots, dependency types, UV/tangent requirements and texture usage/color space. Native binding diagnostics still require a capture; [asset pipeline](AssetPipeline.md). |
| Units, buildings, world props and infrastructure — wired | `src/ForgeLine.Assets/RuntimeMeshData.cs`, `src/ForgeLine.Assets/RuntimeMaterialData.cs` | E; `src/ForgeLine.Presentation/UnitPresentationCatalog.cs`, `src/ForgeLine.Presentation/BuildingPresentationCatalog.cs`, `src/ForgeLine.Presentation/WorldPresentationCatalog.cs`, `src/ForgeLine.Presentation/InfrastructurePresentationCatalog.cs` | `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` | C | `tests/ForgeLine.Presentation.Tests/WorldPresentationTests.cs`, `tests/ForgeLine.Presentation.Tests/SimpleInstanceRendererTests.cs` | Production-reference and package evidence is revision-specific; [graphics settings](GraphicsQualitySettings.md). |
| PBR, normal/ORM/emissive textures and lighting — wired | `src/ForgeLine.Assets/RuntimeTextureData.cs`, `src/ForgeLine.Assets/RuntimeMaterialData.cs` | `src/ForgeLine.Presentation/SceneLightingSettings.cs` | `src/ForgeLine.Presentation/InstanceMaterialPipeline.cs`, `src/ForgeLine.Presentation/PbrMaterialShader.cs`, `src/ForgeLine.Presentation/MaterialQuality/MaterialFilteringShader.cs` | C | `tests/ForgeLine.Presentation.Tests/PbrMaterialShaderTests.cs`, `tests/ForgeLine.Presentation.Tests/MaterialQuality/MaterialNumericsTests.cs` | [PBR qualification](PbrFoundationQualification.md); historical failures and performance limits remain evidence. |
| Terrain geometry, splat surfaces and industrial footprints — wired | `src/ForgeLine.World/TerrainWorld.cs` | `src/ForgeLine.Presentation/TerrainRenderer.cs` | `src/ForgeLine.Presentation/TerrainMaterialPipeline.cs`, `src/ForgeLine.Presentation/TerrainSurfaceShader.cs` | C | `tests/ForgeLine.Presentation.Tests/TerrainSplatMapTests.cs`, `tests/ForgeLine.Presentation.Tests/TerrainIndustrialFootprintTests.cs`, `tests/ForgeLine.Presentation.Tests/TerrainResidency/TerrainGeometryTests.cs` | [World environment](WorldEnvironmentRendering.md); terrain residency is bounded and native traversal must include admissions. |
| Instancing, culling, object LOD and snapshot interpolation — wired | `src/ForgeLine.Presentation/RenderInstance.cs`, compiled LOD mesh IDs | `src/ForgeLine.Presentation/RenderWorld.cs`, `src/ForgeLine.Presentation/RenderSnapshotIndex.cs`, `src/ForgeLine.Presentation/RenderSubmission/SpatialSubmissionIndex.cs` | `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` | C | `tests/ForgeLine.Presentation.Tests/RenderSnapshotIndexTests.cs`, `tests/ForgeLine.Presentation.Tests/RenderSubmission/SubmissionReferenceTests.cs` | [Scalable rendering](ScalableRendering.md) distinguishes isolated CPU savings from unqualified full-frame performance. |
| Rigid mechanical clips — wired when authored | `src/ForgeLine.Assets/RuntimeAnimationData.cs`, `src/ForgeLine.Assets/RigidAnimationBinding.cs` | `src/ForgeLine.Presentation/MechanicalPose.cs`, `src/ForgeLine.Presentation/MechanicalPoseHistory.cs` | `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` | C | `tests/ForgeLine.Assets.Tests/RigidClipPipelineTests.cs`, `tests/ForgeLine.Presentation.Tests/RigidClipPlaybackTests.cs`, `tests/ForgeLine.Presentation.Tests/RigidClipRendererTests.cs` | Per-asset clip and socket validity is separate from continuous-motion acceptance. |
| Infantry skinning — wired when authored | `src/ForgeLine.Assets/RuntimeSkinData.cs`, `src/ForgeLine.Assets/RuntimeAnimationData.cs` | `src/ForgeLine.Presentation/SkinFrameCache.cs`, `src/ForgeLine.Presentation/InfantryAnimationState.cs` | `src/ForgeLine.Presentation/InstanceMaterialPipeline.cs`, `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` | C | `tests/ForgeLine.Assets.Tests/InfantrySkinPipelineTests.cs`, `tests/ForgeLine.Presentation.Tests/InfantrySkinRendererTests.cs` | Static fallback is explicit; fixture success does not establish all live poses or socket motion. |
| Conveyor modules, state surfaces and belt pose — wired | `src/ForgeLine.Assets/RuntimeMeshData.cs`, compiled conveyor IDs selected by E | E; `src/ForgeLine.Presentation/RenderInstance.cs` (`ConveyorFeature`), `src/ForgeLine.Presentation/ConveyorNetworkOverlay.cs` | `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` (conveyor LOD/material branch) | C; `src/ForgeLine.Presentation/RtsConveyorConstructionController.cs` | No conveyor-specific automated test at baseline; manual acceptance: [implementation](ConveyorSimulationAndVehicleInterop.md), [placement](ConveyorConstructionPlacementAndUX.md) | Newer than the original art proposal. Live native build/flow/blocked-state acceptance remains separate from simulation correctness. |
| Directional shadows — optional, wired | Copied terrain and instance caster roots | `src/ForgeLine.Presentation/DirectionalShadowProjection.cs` | `src/ForgeLine.Presentation/DirectionalShadowCasterPipeline.cs`, `src/ForgeLine.Presentation/DirectionalShadowShader.cs`, `src/ForgeLine.Presentation/MaterialQuality/ShadowReceiverShader.cs`, `src/ForgeLine.Graphics/D3D12DirectionalShadows.cs` | C, before world color | `tests/ForgeLine.Graphics.Tests/D3D12DirectionalShadowTests.cs`, `tests/ForgeLine.Presentation.Tests/DirectionalShadowIntegrationTests.cs` | Preset/capability/budget controlled; [graphics settings](GraphicsQualitySettings.md). |
| Combat and industrial particles — optional, wired | `src/ForgeLine.Presentation/IndustrialEffectPresets.cs`, runtime VFX assets | E; `src/ForgeLine.Presentation/ParticleVfxRenderer.cs`, `src/ForgeLine.Presentation/EffectSubmission/ParticleEffectSubmission.cs` | `src/ForgeLine.Graphics/GpuParticleRenderer.cs` (embedded vertex/pixel shaders), `src/ForgeLine.Graphics/D3D12Particles.cs` | C, after instances | `tests/ForgeLine.Graphics.Tests/D3D12ParticleTests.cs`, `tests/ForgeLine.Presentation.Tests/ParticleVfxRendererTests.cs` | [Combat/industrial rendering](CombatIndustrialRendering.md); bounded emission, sorting and fill costs remain. |
| Impact/track/world decals — wired | Runtime decal mesh/material bindings | E; `src/ForgeLine.Presentation/DecalPresentation.cs` | `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` (decal admission, opacity, missing-resource skip) | C via ordinary instances | `tests/ForgeLine.Presentation.Tests/DecalPoolTests.cs`, `tests/ForgeLine.Presentation.Tests/CombatDecalExtractionTests.cs`, `tests/ForgeLine.Presentation.Tests/RuntimeDecalMaterialTests.cs` | [Combat/industrial rendering](CombatIndustrialRendering.md). No independent settings row or separate deferred decal pass. |
| Vegetation visibility and wind — wired; motion optional | World catalog vegetation meshes/materials | `src/ForgeLine.Presentation/VegetationRenderSettings.cs`, E | `src/ForgeLine.Presentation/InstanceMaterialPipeline.cs`, `src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` | C | `tests/ForgeLine.Presentation.Tests/VegetationReferenceSceneTests.cs`, `tests/ForgeLine.Presentation.Tests/RenderQualitySettingsTests.cs` | Wind suppression does not remove vegetation; [world environment](WorldEnvironmentRendering.md). |
| Atmosphere, weather and local haze — optional, wired | `src/ForgeLine.Graphics/GraphicsAtmosphereSettings.cs`, `src/ForgeLine.Graphics/GraphicsLocalHazeSettings.cs` | `src/ForgeLine.Presentation/RenderQualitySettings.cs` | `src/ForgeLine.Graphics/D3D12Atmosphere.cs`, `src/ForgeLine.Graphics/SceneCompositeShader.cs` | C | `tests/ForgeLine.Presentation.Tests/GraphicsQualityPolicyTests.cs`, `tests/ForgeLine.Graphics.Tests/D3D12SceneCompositionTests.cs` | Configuration/development inputs include fields with no frontend row; [world environment](WorldEnvironmentRendering.md). |
| Linear scene, grading, exposure, spatial AA and scaled reconstruction — optional, wired | `src/ForgeLine.Graphics/GraphicsSceneOutputSettings.cs`, `src/ForgeLine.Graphics/GraphicsPostProcessingSettings.cs` | `src/ForgeLine.Presentation/RenderQualitySettings.cs` | `src/ForgeLine.Graphics/D3D12SceneComposition.cs`, `src/ForgeLine.Graphics/SceneCompositeShader.cs` | C; `src/ForgeLine.Client/ClientGraphicsOverrides.cs` | `tests/ForgeLine.Graphics.Tests/D3D12SceneCompositionTests.cs`, `tests/ForgeLine.Graphics.Tests/SceneComposition/CompositionProductNativeTests.cs` | [Graphics settings](GraphicsQualitySettings.md), [spatial reconstruction limits](ScalableRendering.md). Reduced target memory does not imply faster frames. |
| HUD, minimap, selection/order indicators and Operations — wired | `src/ForgeLine.Assets/RuntimeUiSkinData.cs`, copied snapshots | `src/ForgeLine.Presentation/GameplayHudRenderer.cs`, `src/ForgeLine.Presentation/SelectedUnitOrderSnapshot.cs`, `src/ForgeLine.Presentation/OperationsDiagnosisSnapshot.cs` | `src/ForgeLine.Presentation/DebugDrawRenderer.cs`, `src/ForgeLine.Presentation/UiSkinBatch.cs` | C, native-resolution Overlay | `tests/ForgeLine.Presentation.Tests/BattlefieldProductionSkinTests.cs`, `tests/ForgeLine.Presentation.Tests/SelectionOverlayRenderingTests.cs` | [Interface qualification](GraphicalInterfaceQualification.md); physical-display readability remains separate. |
| Frontend and startup media — wired | `src/ForgeLine.Assets/RuntimeUiSkinData.cs`, packaged startup media | `src/ForgeLine.Presentation/FrontendOverlayRenderer.cs`, `src/ForgeLine.Presentation/FrontendSkinnedRenderer.cs` | `src/ForgeLine.Presentation/UiSkinBatch.cs`; embedded frontend shaders | `src/ForgeLine.Client/ClientFrontendRenderHost.cs`, `src/ForgeLine.Client/StudioSplashVideoRenderer.cs` | `tests/ForgeLine.Presentation.Tests/FrontendProductionSkinTests.cs`, `tests/ForgeLine.Client.Tests/UiReferenceScenarioTests.cs` | [Startup](StartupDiagnostics.md), [interface qualification](GraphicalInterfaceQualification.md). Gameplay and frontend have distinct lifecycle owners. |
| Debug overlays and synthetic primitives — optional | `src/ForgeLine.Presentation/DebugDraw.cs`, explicit stress inputs | `src/ForgeLine.Presentation/DevelopmentOverlayRenderer.cs`, `src/ForgeLine.Presentation/DebugOverlayState.cs` | `src/ForgeLine.Presentation/DebugDrawRenderer.cs` | C; `src/ForgeLine.Client/Program.cs` | `tests/ForgeLine.Presentation.Tests/DebugDrawRendererTests.cs`, `tests/ForgeLine.Presentation.Tests/DevelopmentOverlayRendererTests.cs` | Smoke stress can use development geometry; it is not a production no-fallback asset qualification. |
| Presets, overrides and effective fallbacks — wired | `src/ForgeLine.Presentation/RenderQualitySettings.cs` | Same policy resolves requested/active features | `src/ForgeLine.Graphics/GraphicsDeviceInfo.cs`, `src/ForgeLine.Graphics/GraphicsFramePlan.cs` | `src/ForgeLine.Client/ClientUserSettings.cs`, `src/ForgeLine.Client/ClientGraphicsOverrides.cs`, C | `tests/ForgeLine.Presentation.Tests/GraphicsQualityPolicyTests.cs`, `tests/ForgeLine.Presentation.Tests/RenderQualitySettingsTests.cs` | Historical four-tier/WARP evidence: [graphics settings](GraphicsQualitySettings.md). |
| Capture fixtures and GPU telemetry — optional, wired | `src/ForgeLine.Presentation/VisualReferenceScene.cs` | `src/ForgeLine.Presentation/InstanceRenderDiagnostics.cs`, `src/ForgeLine.Presentation/TerrainRenderDiagnostics.cs` | `src/ForgeLine.Graphics/D3D12FrameCapture.cs`, `src/ForgeLine.Graphics/GraphicsDiagnostics.cs` | `src/ForgeLine.Client/VisualReferenceCapture.cs`, `src/ForgeLine.Client/ReferenceCaptureManifest.cs`, `src/ForgeLine.Client/ReferencePerformanceSamples.cs` | `tests/ForgeLine.Client.Tests/VisualReferenceCaptureTests.cs`, `tests/ForgeLine.Client.Tests/ReferencePerformanceSamplesTests.cs` | `build/Invoke-RtsReferenceQualification.ps1` captures selected scenes; results retain adapter, effective policy, dimensions and completed fences. No GPU utilization/bandwidth claim follows from timestamps. |
| DXR / mesh shaders — specified capability only | `src/ForgeLine.Graphics/GraphicsDeviceInfo.cs` | No active rendering consumer of these techniques | `src/ForgeLine.Graphics/D3D12GraphicsDevice.cs` probes support | C reports device diagnostics | Baseline graphics tests exercise fallback independently | [Scalable rendering](ScalableRendering.md): detection does not enable a pass. |
| ExecuteIndirect, GPU occlusion and temporal reconstruction — specified/deferred | No production indirect/history resource path | Existing CPU submission and spatial reconstruction remain selected | No active pass for these techniques | No client enable switch | Existing spatial/submission tests do not qualify these methods | Prerequisites and evidence for deferral: [scalable rendering](ScalableRendering.md). |

## Capture coverage

Resource clusters illustrate the difference between authored and wired features.
`assets/source/world/authoring/resource_clusters/variants.json` declares four
layouts and three sizes per resource family, with LOD0/1/2 and collision proxies.
`src/ForgeLine.Presentation/WorldPresentationCatalog.cs` and
`src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` still select the existing
High/Reduced pair through the canonical IDs. The default medium geometry is
wired; extra layouts, sizes and LOD2 are **authored but not dispatched** in live
gameplay. They are retained assets for a later presentation integration, not
dead registrations to delete. `tests/ForgeLine.Presentation.Tests/ResourceDepositBlenderMasterTests.cs`
and `tests/ForgeLine.Assets.Tests/WorldAssetAuthoringTests.cs` provide source and
catalog contracts; they do not establish variant selection or native acceptance.
See [resource cluster geometry](ResourceDepositClusterGeometry.md) for the
unexecuted acceptance matrix and ownership of the remaining work.

`src/ForgeLine.Presentation/VisualReferenceScene.cs` owns the scene enum and
factory dispatch; `src/ForgeLine.Client/VisualReferenceCapture.cs` owns native
execution. The fixture families include terrain/daylight; industrial/base and
building states; formations/logistics traffic; combat/destruction; mechanical
states; unit, infantry-skin, building and world-prop production/load scenes;
resource deposits; vegetation; road/crossing states; material foundation;
EffectsBarrage/IndustrialEffects; and decal surface/overlap/slope scenes.
`src/ForgeLine.Client/UiReferenceScenario.cs` supplies separate UI scenarios.

Each family can exercise selected assets or presentation states without running
the corresponding gameplay lifecycle. In particular, there is no conveyor scene
in `VisualReferenceSceneKind` at this baseline, and no conveyor-specific automated
test under `tests/`. The conveyor implementation document records compile-only
evidence and pending manual scenarios. Existing generic renderer tests must not
be presented as conveyor flow, conservation, replay or native acceptance tests.

`src/ForgeLine.Presentation/MaterialReferenceRenderer.cs` is deliberately used
by the MaterialFoundation capture path in
`src/ForgeLine.Client/VisualReferenceCapture.cs`; it is fixture infrastructure,
not an unused gameplay registration.

## Fallback semantics

`src/ForgeLine.Assets/RuntimeAssetBindingValidator.cs` permits an unauthored
material texture slot to use a neutral texture; an authored reference with the
wrong type, usage or color space fails qualification. These are different cases.
`src/ForgeLine.Presentation/RuntimeWorldAssetResources.cs` and
`src/ForgeLine.Presentation/SimpleInstanceRenderer.cs` retain mesh/material
fallback diagnostics; a production no-fallback capture must check those counters
rather than infer success from a presented frame. Cosmetic decals skip missing
resources. Skinning exposes static fallback counts. Software/capability/memory
fallbacks are resolved separately by `RenderQualitySettings.Resolve`, retaining
requested versus effective settings and a reason.

The implicit legacy rendering lifecycle in
`src/ForgeLine.Presentation/RenderAssetSession.cs` is an explicit compatibility
path alongside prepared frames. Its existence does not demonstrate a dead
registration or justify removing ownership/fence checks.

## Pass and shader registration

`src/ForgeLine.Graphics/GraphicsFramePlan.cs` exposes World and Overlay passes.
The directional depth pass is nested before terrain/instance color submission;
particles follow instances. Entering Overlay triggers the configured scene
composition before native-resolution indicators and UI. Direct output bypasses
linear composition. This is not a general deferred pass graph.

Shader sources are compiled through `src/ForgeLine.Graphics/DxcShaderCompiler.cs`.
The matrix names the shared terrain, instance/PBR, shadow and composition shader
owners. Particle shaders are embedded in `GpuParticleRenderer.cs`; debug/HUD/UI
shaders are embedded in their renderer/batch owners. Shader filenames used in
compiler diagnostics need not correspond to standalone `.hlsl` files. Neither a
diagnostic filename nor an adapter capability is a dead registration.

## Limits retained after classification

| Class | Concrete boundary and owner | Decision |
| --- | --- | --- |
| Safety/correctness | `src/ForgeLine.Graphics/GraphicsFramePlan.cs`: checked target sizes and ordered World → Overlay; `src/ForgeLine.Presentation/RenderAssetSession.cs`: render-thread ownership and prepare/record/submit lifecycle | Retain; prevents invalid resource/recording use. |
| Safety/correctness | `src/ForgeLine.Assets/RuntimeAssetBindingValidator.cs`: manifest/payload/type/UV/tangent/transfer-function checks | Retain; malformed content is not an optional quality tier. |
| Safety/correctness | `src/ForgeLine.Presentation/PresentationSnapshotBuffer.cs`, `src/ForgeLine.Presentation/RenderWorld.cs`: atomic copied handoff, session/tick acceptance and bounded history ownership | Retain; no simulation writes or extra snapshot buffers. |
| Performance budget | `src/ForgeLine.Presentation/PresentationExtractor.cs`: 4,096 snapshot effects; retained capture scratch threshold 16,384 | Retain. Scratch retention threshold is not a maximum entity count. |
| Performance budget | `src/ForgeLine.Presentation/TerrainResidencySettings.cs`: default 128 resident chunks | Retain; native traversal and fence retirement remain relevant. |
| Performance budget | `src/ForgeLine.Graphics/ResourceScheduling/UploadArenaLimits.cs`: four slots, 4 MiB normal slot, 128 MiB maximum transfer, 32 transfers/batch | Retain; bounds transfer memory and submission work. |
| Performance budget | `src/ForgeLine.Presentation/RenderQualitySettings.cs`: High/Ultra share bounded shadow/particle resources; software and insufficient-budget caps | Retain. Ultra's extra object detail is not evidence that resource caps are obsolete. |
| Performance budget | `src/ForgeLine.Graphics/GraphicsDirectionalShadowSettings.cs`: one cascade, 1,024/2,048/4,096 resolution, 1,024/4,096/8,192 casters, 256 draws and four PCF samples | Retain; changing these requires native cost and regression evidence. |
| Safety/correctness and budget | `src/ForgeLine.Assets/RuntimeSkinData.cs`: 48 joints, 32,768 vertices and 98,304 indices | Retain; bounded payload and shader contracts are not phase labels. |
| Safety/correctness and budget | `src/ForgeLine.Assets/RuntimeAnimationData.cs`: 64 nodes, 128 channels, 1,024 keys/channel, 8,192 total keys, 1 MiB payload and 120-second duration | Retain; animation format versions 1/2 remain compatibility identities. |
| Configuration range | `src/ForgeLine.Presentation/RenderQualitySettings.cs`: finite world scale 0.5–1; `src/ForgeLine.Presentation/TerrainResidencySettings.cs`: 1–4,096 chunks | Retain; validated ranges are supported contracts. |
| Product/compatibility | `docs/BaseConveyorLogistics.md`, `docs/BaseConveyorPortMatrix.md`, `assets/source/infrastructure/directorate/conveyors/conveyor-kit.v1.json`: solid-resource eligibility, 90° turns, authored sockets and versioned art contract | Retain. These describe scope/format decisions, not a renderer-wide feature lock. |
| Unnecessary feature lock | No reproducible renderer-wide V1 gate identified in the audited production consumers | No runtime limit removed; no blanket increase. |

## Documentation corrections and remaining work

The graphics settings page now distinguishes implemented cosmetic decals from
the absence of an independent setting. General prototype wording in architecture
and current performance targets is replaced with lasting contract terminology.
Historical qualification results, asset IDs, schema/version labels, conveyor
scope decisions and unresolved Product Owner decisions remain intact.

No proven unused production registration or runtime rendering defect was found
by this static audit. This is not proof that every asset variant or live state is
visually correct. The conveyor art proposal remains the source contract for its
versioned preproduction JSON; the linked implementation documents govern current
live behavior. Its initial proposal label must not be read as the status of all
conveyor rendering.

The source terminology scan across Graphics, Presentation and Client finds
historical `prototypeBattlefield` identifiers, schema/fixture version 1 values
and UV-coordinate variables named `v1`. These are not general feature gates;
renaming them would not unlock rendering and is outside this documentation edit.

Follow-up ownership: asset/tooling work owns missing authored channels, clips and
binding qualification; Presentation owns feature metadata, faction filtering,
state/LOD selection and overlays; Graphics owns passes, native resources and
shader execution; Client owns composition, settings, capture and lifecycle.
New passes, temporal history, indirect submission and visual redesign require
their own implementation and regression evidence. Native capture on the final
revision, continuous camera/match motion, physical-monitor acceptance, device
removal/resize soak and sustained frame-rate qualification remain independent
gates; old screenshots and successful CPU tests cannot close them.

Validation commands and complete CI requirements remain in
[Development](Development.md). The required `build-test` job includes assets,
Windows smoke, headless execution, solution tests, packaging and the natural
terminal match with seed 2026. This inventory does not weaken those gates.

## Observed validation gaps

The first local Release solution run used source baseline `8e2935ab`, before
the resource-cluster synchronization. It was interrupted after multiple failures;
it is not a successful full-suite result. The following findings belong to
unchanged source/test contracts and must not be hidden by a documentation PR:

| Finding / status | Evidence and next owner |
| --- | --- |
| Construction-action count contract — failing | `tests/ForgeLine.Presentation.Tests/PlayerActionReadModelTests.cs`: expected 13 building entries, actual 27 including expanded actions. Also fails on synchronized master in [CI run 39859499327](https://github.com/DaveBeusing/Forgeline/actions/runs/39859499327). Presentation/action-model integration must reconcile catalog coverage without dropping conveyor actions. |
| Production asset count contracts — failing | `tests/ForgeLine.Assets.Tests/WorldAssetAuthoringTests.cs`, `tests/ForgeLine.Assets.Tests/DirectorateUnitAssetAuthoringTests.cs`, `tests/ForgeLine.Assets.Tests/DirectorateBuildingAssetAuthoringTests.cs`, `tests/ForgeLine.Assets.Tests/VfxAssetAuthoringTests.cs`, `tests/ForgeLine.Assets.Tests/RtsUiAssetAuthoringTests.cs`: expected 1,365, compiled 1,825 on the original baseline. Asset/tooling owners must reconcile the expanded catalog; these values do not describe the later resource-cluster catalog. |
| Selection-proxy material classification — failing qualification contract | `tests/ForgeLine.Assets.Tests/ProductionAssetMaterialCompletenessTests.cs` treats conveyor `.selection` IDs as physical production meshes and reports missing materials. For example, `assets/source/infrastructure/directorate/conveyors/models/straight_standard/selection.asset.json` points to `collision.glb` without material references. Asset/tooling owners must distinguish proxy purpose from visible meshes; this failure alone does not prove a rendered surface is missing its material. |
| Semantic artwork coverage — failing | `tests/ForgeLine.Client.Tests/UiArtworkCoverageTests.cs` reports an enum/catalog coverage mismatch. UI asset owners must reconcile explicit bindings or documented exceptions. A visible fallback does not close the semantic artwork contract. |
| Convoy command interaction — failing | `tests/ForgeLine.Game.Tests/CargoTransportSystemTests.cs`, `ManualEscortMovementReplacesConvoyFollowWithoutCancelingCargo`, fails its boolean assertion. Game/logistics owners must investigate authoritative escort/order semantics; renderer documentation cannot close this regression. |

The initial native cursor/menu failures also lacked the generated runtime cache
in the fresh worktree. Those are setup failures, not established renderer defects;
native tests require canonical runtime assets before execution. Graphics tests
passed in that run with explicit native lifetime/debug-layer skips. Missing debug
layer and opt-in lifetime coverage remain unqualified. Neither interrupted tests
nor skipped checks establish a successful required `build-test` job.
