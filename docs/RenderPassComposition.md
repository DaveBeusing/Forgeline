# Client render pass composition

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Implementation and ownership](#implementation-and-ownership)
- [Creation, frame and disposal trace](#creation-frame-and-disposal-trace)
- [Capability and quality gates](#capability-and-quality-gates)
- [Lifecycle and recovery boundaries](#lifecycle-and-recovery-boundaries)
- [Verification — NOT EXECUTED](#verification--not-executed)

</details>

## Implementation and ownership

The baseline already renders directional shadows, terrain, instances, optional
particles and scene composition before native-resolution overlays. This change
unifies their production and reference-capture configuration/execution; it does
not introduce new visual techniques or claim native acceptance.

`ClientRenderConfiguration` resolves effective quality against actual device
capabilities, available-budget policy and swapchain buffer count. Both
`ClientRenderHost` and `VisualReferenceCapture` use it before constructing their
terrain, instance and particle renderers. Gameplay continues to apply validated
saved settings plus development environment overrides; captures use their typed
request, independent of the environment. Requested and effective settings remain
separate in existing qualification diagnostics.

`ClientWorldPassSequence` executes a fixed synchronous sequence through a stack
value `ClientWorldPassRecorder`. This is an actual consumer of the existing
renderers, not a feature-registration list or a new rendering backend. Terrain
and instance asset preparation remains before command recording. Snapshot
interpolation, viewer filtering, settings persistence, simulation ownership and
gameplay commands are unchanged.

## Creation, frame and disposal trace

| Stage | Inputs and concrete consumer | Target/dependency contract | Lifetime / failure behavior |
| --- | --- | --- | --- |
| Quality resolution | `ClientRenderConfiguration.Resolve`, `RenderQualitySettings.Resolve` | Actual buffer count, FP16 scene support, R32 depth support, optional effect capabilities and existing budget estimate | Resolves once per renderer generation or capture profile; no adaptive budget polling |
| Configuration | `ClientRenderConfiguration.Apply` | Neutralize postprocessing, atmosphere, shadows and particle-depth demand before scene format/scale; apply scene, atmosphere, shadow settings and postprocessing | Outside frame recording and between renderer lifetimes; restore previous settings on failure or raise a controlled session fault |
| Renderer creation | Existing `TerrainRenderer`, `SimpleInstanceRenderer`, `ParticleVfxRenderer` constructors | Compiled runtime assets, resolved lighting, texture/detail/environment settings | Existing render-owner leases and bounded residency; no runtime authoring-source reads |
| Preparation | Existing terrain/instance `Prepare` calls | Copied snapshot, world dimensions, camera, shadow coverage and unchanged caster limits | Synchronous before the device frame callback; existing asset readiness/submit/retire state machine remains authoritative |
| World marker and atmosphere view | `ClientWorldPassSequence.Record` | Scaled world dimensions; finite copied camera matrices when atmosphere is enabled | Missing shadow inputs or invalid view/frame values fail before pass submission |
| Directional shadow | Existing terrain and instance shadow renderers | D32 depth targets, R32 shader-readable views; shadow map becomes writable for casters and returns readable for world receivers | At most 128 terrain draws within existing limits; remaining caster/draw budgets go to instances; shadow scope ends on managed recorder failure |
| Opaque terrain | `TerrainRenderer.Render` | FP16 scene color when enabled, otherwise RGBA8 output; writable world depth | Existing terrain material/control slots, sample limits and residency remain fixed |
| Particle preparation and opaque instances | `ParticleVfxRenderer.Prepare`, `SimpleInstanceRenderer.Render` | Copied authorized effects; shared instance mesh/material batches; world depth produced before transparent sprites | No particle-position GPU readback or simulation mutation; bounded existing mesh fallback remains |
| Transparent particles | `ParticleVfxRenderer.Render` / `GpuParticleRenderer` | Existing particle pass unbinds writable depth, transitions it to shader-readable and restores depth-write afterward | One bounded particle submission; an empty/unavailable emitter set is not evidence of an active GPU effect |
| Composition / Overlay marker | `BeginPass(Overlay)` -> existing D3D12 `ComposeScene` | Scene RT becomes shader-readable; optional sampled world depth feeds atmosphere/reconstruction; output is RGBA8 at native dimensions | Existing tone mapping, grading and spatial AA; scaled reconstruction writes native overlay depth and restores world-depth state |
| Overlay | Existing debug, docking, selected-order, gameplay/HUD and frontend renderers | Native dimensions/DPI; native reconstructed depth where appropriate | No world submission may follow Overlay; no HUD redesign or new pass is introduced |
| Submit / retire / Present | Existing D3D12 device and instance asset session | Existing command queue, per-backbuffer fences and bounded timing query rings | No per-frame readback is added; unavailable GPU timing remains unavailable |
| Shutdown | Existing reverse-order renderer `using` scopes and device owner | Existing fence-safe resource leases, upload retirement and target ownership | Wait/retirement rules and confirmed-removal handling are retained; no premature release to make shutdown appear successful |

Frontend-only capture and startup/menu rendering deliberately enter Overlay
without world drawing. The material-reference fixture explicitly enters World
and retains its specialized material renderer; it is not presented as a full
battlefield pass trace. These cases still use the native device's existing final
composition/target transition rules.

## Capability and quality gates

`IGraphicsDevice` now exposes explicit atmosphere and shadow support, plus the
current particle-depth demand for configuration restoration. Default backends
report optional capabilities as unavailable unless they implement them.
D3D12 caches R32 texture/shader-load support; particles no longer advertise
support unconditionally. Shadows additionally require D32 depth-stencil support.
Their existing shader uses explicit R32 loads and manual comparisons, so no
comparison-sampler capability is invented. Atmosphere requires the existing
supported FP16 composition plus sampled-depth path.

Unsupported shadows or atmosphere are disabled independently in the resolved
profile, preserving other available features and the original request. Existing
particle-capability fallback, software Low caps, budget degradation and direct
native-output fallback remain. Disabled native effect configuration is accepted;
an attempt to enable an unsupported effect fails with a specific error before
resource allocation. No advanced-capability probe enables an unimplemented path.

No settings row is added for bloom, temporal AA/history, SSAO, SSR, volumetric
lighting, deferred rendering, indirect submission, mesh shaders or ray tracing.
The existing [feature inventory](RendererFeatureInventory.md) remains the source
for implemented versus specified techniques. Existing graphics controls continue
to edit requested settings for the next session; effective disabled effects and
fallback reasons are reported through the existing quality diagnostics.

## Lifecycle and recovery boundaries

`ClientRenderSurface` is shared by gameplay and frontend render owners. A
minimized flag or either non-positive dimension suspends submission and queues
one zero-size device request. Repeated suspension does not churn resize requests.
Resume or a changed size queues the latest positive dimensions. Requested state
is committed only after the device accepts the request; actual target rebuilding
and successful-presentation confirmation remain device-owned.

Gameplay resets frame-rate smoothing, rate sampling and the particle delta-time
baseline after resize/resume. Time spent suspended therefore does not enter the
next measured frame interval or particle delta. Native timing is still reset by
the existing device target/resize lifecycle, not replaced with invented samples.
Alt-Tab occlusion probing, window-mode trials and their presented-size confirmation
retain their existing owners and semantics.

Configuration reset includes particle-depth state and its query resources so a
previous capture/profile cannot leave stale optional depth demand behind. The
device's setters retain all submitted-frame fence waits and target/descriptor
rebuild rules. A recoverable configuration exception restores previous scene,
atmosphere, shadows, particle-depth and postprocessing settings. If native failure
or restoration prevents that, a `GraphicsDeviceException` retains both errors and
requires a fresh session. This is controlled fault handling, not automatic
device-loss recreation or a promise of recovery on a removed GPU.

No retention, texture, descriptor, shadow, caster, particle, upload or timing budget
is raised. No additional render target, shader family, synchronization worker,
history buffer or per-frame diagnostic collection is added.

## Verification — NOT EXECUTED

Prepared `ClientRenderCompositionTests` cover independent capability fallback,
requested/effective settings, actual swapchain buffer-count budget admission,
reset ordering, particle-depth cleanup, rollback/fatal restoration, recording
guards, repeated suspension, resize and failed-request state preservation.
`ClientWorldPassSequenceTests` cover real dispatch ordering, scaled/native
dimensions, optional inputs, existing budgets and shadow scope closure on faults.
`ClientRenderCompositionNativeTests` adds a WARP target/configuration lifecycle
case across scale changes, particle depth, suspension and resize. Fake-device
results would not establish GPU correctness; the native case has not been run.

The user should execute the canonical restore/reference/build/asset/full-suite
commands in [Development](Development.md), then focused Microsoft.Testing.Platform
classes as needed:

```powershell
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Client.Tests.ClientRenderCompositionTests ForgeLine.Client.Tests.ClientWorldPassSequenceTests ForgeLine.Client.Tests.ClientRenderCompositionNativeTests
```

Keep the existing Graphics frame-plan, scene composition, particle, shadow,
surface lifecycle, resource retirement, fault injection and Client owner/display
tests. Required `build-test`, asset pipeline, Windows smoke, headless and natural
terminal-match CI remain unchanged. No workflow was manually dispatched; GitHub
may start PR checks automatically, which are separate from local execution.

Native manual matrix remains OPEN: 1280x720, 1920x1080 and 3840x2160; standard and
HiDPI; Low/Medium/High/Ultra plus supported independent overrides; rapid resizing,
windowed/borderless switches, Alt-Tab/occlusion, minimize/resume, repeated session
restart, missing inputs and injected/real device faults. Compare gameplay and
typed reference captures for pass order, active features, target dimensions,
shadow/particle depth, native overlay sharpness and output color. Record bounded
CPU/GPU/frame metrics with the real adapter/driver and submission fence, including
unavailable measurements and existing performance limits. No golden image was
updated and no software success is extrapolated to physical hardware quality.

**NOT EXECUTED:** restore, builds, automated tests, manual CI dispatches, native
WARP/hardware runs, reference captures, device-loss tests and performance soak.
Only repository/source and Git diff inspection were performed during this work.
