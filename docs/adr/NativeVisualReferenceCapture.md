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

Named frontend/HUD states reuse this lane and its exact native readback. Baseline
approval is an external reviewer record bound to the immutable manifest and image
hashes, with rationale and explicit rubric decisions; capture never grants art
acceptance. Source/build/runtime/device changes reject automatic inheritance.
Thread-owned execution requirements apply only during explicit capture. Progress
metadata is atomically replaced, and a retained failure marker rejects qualification.
See [UI review](../UiVisualReview.md) and [qualification](../UiVisualQualification.md).

Frame-resource investigation found a High shadow-edge variance with identical CPU upload fingerprints. A diagnostic depth copy changed the observation, but alternate read states, an extra receiver texture and fine derivatives did not establish invariance under broader reuse. Extra renderer resources/copies were rejected because they did not justify their cost. The cause remains unresolved. Optional CPU depth readback and upload hashing stay explicit capture diagnostics, while ordinary rendering retains its existing bounded resources. A capture-only layout change must not hide a difference from ordinary frames.

An explicit collection mode may finish the bounded matrix after a pixel mismatch, preserving every requested scene and diagnostic pair. It still marks failed rows, writes a structurally failed final manifest/failure report, returns nonzero and cannot produce a passing baseline comparison. Strict mode aborts on the first mismatch. No tolerance or golden acceptance is inferred in either mode.

Extend these same contracts if accepted human-reviewed goldens, natural fixed-tick gameplay recordings, sustained camera/LOD sequences or physical display qualification become available. Any automated similarity tolerance requires evidence and an explicit baseline-update policy. See [Native visual reference evidence](../VisualReferenceEvidence.md) for reproduction, rubric and remaining manual acceptance.
