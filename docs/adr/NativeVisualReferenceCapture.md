# Native visual reference capture

## Decision

Extend the existing RTS qualification script, D3D12 readback, immutable presentation snapshots and production terrain/instance/particle/HUD renderers. Explicit capture is available through the graphics-device contract on its creating thread; normal frames retain their allocation/copy behavior. Native resources, surface state, barriers, fences and retirement stay in Graphics. Request parsing, build/runtime attestation, PNG export and evidence orchestration stay in Client/tooling. Presentation owns authored fixture states and their sensor/intelligence/extraction boundary. Simulation and Headless gain no dependencies or behavior changes.

One bounded request selects up to six scenes, four displays, three zooms and three quality profiles. No renderer or asset source is replaced. A small native host window owns an explicitly sized swap chain, permitting exact native 4K readback independently of desktop/window clamps. Reports preserve both actual surface and host-window size; physical-display qualification remains separate. Output roots are new and baselines read-only.

Fixture simulation setup is completed before snapshots reach Graphics. Static acceptance layouts use shipping asset identities and canonical terrain/world/resource placements; they do not claim natural economic or combat execution. Known visible combat effects and HUD values are explicitly authored. A real sensor/intelligence system and the production extractor exclude hidden foreign units. No mutable simulation object is copied into rendering.

Structural checks cover source revision versus built assembly, runtime hashes before/after, requested versus actual surface, native PNG dimensions and checksums, repeated fixed-state pixels, bindings and fallback meshes. Camera-pose sequences inspect spatial change without claiming sustained frame pacing. CPU/GPU distributions exclude readback/encoding and distinguish WARP from hardware. Human rubric results remain pending until actual inspection; no automatic golden update or quality score exists.

## Alternatives and tradeoffs

Desktop screenshots were rejected as the primary evidence because occlusion, window clamps and display scaling can hide the native framebuffer dimensions. Upscaling an old screenshot would not prove output fidelity. A separate renderer or generic screenshot framework would duplicate established ownership. Freezing a live game by sharing mutable ECS objects would violate the snapshot boundary. Natural full matches alone cannot guarantee all six requested visual states at repeatable viewpoints.

The authored fixtures are reproducible but omit live simulation cost, natural event distribution and animation progression. Per-frame readback introduces stalls; it is never used during timed samples or ordinary gameplay. Image equality is required only for repeated fixed states in the same run. Driver/build/content differences require human review and equivalent-workload checks rather than an invented tolerance.

## Re-evaluation

Extend these same contracts if accepted human-reviewed goldens, natural fixed-tick gameplay recordings, sustained camera/LOD sequences or physical display qualification become available. Any automated similarity tolerance requires evidence and an explicit baseline-update policy. See [Native visual reference evidence](../VisualReferenceEvidence.md) for reproduction, rubric and remaining manual acceptance.
