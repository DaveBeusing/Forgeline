# Explicit Frame Passes and Scene Composition

## Status

Accepted as an opt-in graphics/presentation foundation. Direct output remains the default.

## Decision

Keep one render owner, one direct command list and the existing back-buffer fence contract. The frame has an opaque World pass, optional internal scene composition, then an output-space Overlay pass. World cannot resume after Overlay. Repeating the current marker is harmless. Legacy command contexts may ignore markers; enabling scene composition on an unsupported device explicitly fails.

Graphics owns all native targets, views, transitions and the composition pipeline. Presentation selects world pipeline output format at renderer creation. The client configures scene output before constructing world renderers; changing that configuration requires replacing those renderers. Simulation and immutable snapshot contracts do not change.

When enabled, allocate one R16G16B16A16_FLOAT scene target and one SRV per back-buffer index. Reuse follows that index's existing submission fence. Each scene target transitions PixelShaderResource to RenderTarget before World, and back before composition. Resize and shutdown retain the existing all-frame idle/confirmed-removal policy. Partial target creation releases acquired resources and descriptors. A live-GPU shutdown timeout continues to retain the entire device; no target is prematurely released.

World shaders emit linear lighting without exposure, tone mapping or output transfer. A fullscreen triangle performs exposure, the existing optional ACES-fitted curve and linear-to-sRGB exactly once. It uses pixel-coordinate Load, so no resampling/filtering is introduced. Depth remains available for output-space world debug lines. Gameplay interaction geometry, HUD, menus and splash render after composition.

Existing terrain debug colors and the authored clear color require an explicit output-space pixel tag: negative scene alpha encodes `-(1 + alpha)`; nonnegative alpha denotes linear world color. Composition passes tagged color through and restores alpha. Terrain debug outputs use -2. World pipelines are opaque; alpha blending into this tagged intermediate is rejected. Future post-processing must respect the tag or move terrain debug output to a separate output-space submission before adding effects. This is a bounded follow-up, not a generic render graph.

The disabled path has no scene targets, scene SRVs, fullscreen draw, output copy or shader transfer change. Pipeline formats are checked against the active pass. The swap chain remains R8G8B8A8_UNorm. No HDR monitor or 10-bit display capability is implied.

## Measurement and qualification

Four timestamps per reusable frame preserve total GPU frame timing while partitioning World, composition and Overlay. Readback occurs after the submission fence; idle consumes pending measurements before clearing fence values. Diagnostics publish the newest fence and keep CPU submission identity separate from delayed GPU identity. Resize/configuration changes clear measurements from the old target plan. Absent composition/timestamp features have explicit reasons.

The frame plan describes target dimensions, formats, resource counts, reuse lifetimes and logical texel payload bytes. These bytes exclude heap alignment and driver overhead. Composition adds one draw and `width * height * 8 * bufferCount` payload bytes. Qualification-only frame capture adds a copy and an idle wait; ordinary rendering does neither.

Native tests compare actual output pixels on hardware and forced WARP across sRGB Base Color, linear ORM, exposure 0.25/1.15/4, ACES enabled/disabled, clear output, overlays, resize and suspension. Production terrain, procedural objects, debug lines and terrain palette/control output are compared at camera distances 25/90/240. FP16 intermediate precision permits at most one 8-bit step per channel; the tested overlay sample is exact. Required CI runs these functional tests through the existing solution test gate.

Hardware timing uses a separate opt-in fixture with fixed terrain and camera, 64 warmups and 512 samples per mode/zoom. It records adapter, driver, resolution, CPU distribution, completed GPU samples and target payload. It excludes gameplay, object load, real UI and Present from GPU timing and establishes no universal FPS threshold. WARP is functional evidence only. Real device removal, extended soak and physical-monitor readability remain separate acceptance checks.
