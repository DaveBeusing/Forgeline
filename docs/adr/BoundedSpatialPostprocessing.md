# Bounded spatial postprocessing

## Decision

Use the existing frame-indexed FP16 scene targets and one final fullscreen triangle for atmospheric extinction, manual exposure, bounded white balance, ACES, contrast/saturation, spatial edge filtering and sRGB output. World albedo SRVs decode sRGB before linear lighting; normal/ORM stay linear. Transparent particle drawing precedes the final image. Overlay selection, warning, command and HUD colors are drawn afterward at the full output resolution. Authored clear/debug sentinel colors bypass the world transform.

The composite has one 40-DWORD vertex constant block and one scene descriptor, plus one shared depth descriptor when fog is active. It explicitly reuses the center scene sample and reads four clamped neighbors only when spatial AA is enabled. Fog uses the corresponding opaque depth at each sampled location. Five scene reads and at most five depth reads are bounded; there are no intermediate targets, history textures, jitter, motion vectors or reconstruction.

The edge filter chooses the neighbor direction with lower luminance gradient and retains at least 70% of the original display-linear color. The low/high AA settings blend 20%/30%. Clear/debug centers are preserved exactly. The result receives one output transfer. Exposure is manual; conservative grade bounds limit contrast to 0.95–1.08, saturation to 0.95–1.05 and white-balance/tint factors to ±0.1. Default grading is neutral.

## Quality ownership and fallback

One presentation-owned settings resolver supplies construction-time Low/Medium/High defaults for existing shadow, atmosphere, particle, terrain-detail/object-LOD and final-image contracts. The client owns persistence and the quality menu. Native graphics owns targets, descriptors, barriers and completed-fence timings. No simulation domain, save/replay format or authoritative visibility rule changes.

Software devices select Low. Known available local-memory budget is checked against logical scene, shadow texture/constant-arena and particle payload before configuring the session; the resolver degrades High→Medium→Low, then selects direct output without those optional effects when the linear payload will not fit. Unsupported postprocessing selects the same direct fallback. Budget queries use the existing graphics budget contract; unavailable information stays unknown and does not invent an available-memory value. Native allocation failures, device removal and later changes in budget retain the existing fail-closed device/session fault path; this is not a promise of live memory-pressure recovery.

The profile remains stable throughout the session. Existing camera-based LOD hysteresis, particle distance/readability rules and atmospheric zoom compensation remain. Non-default saved per-effect settings and explicit development overrides remain available; diagnostics report the actual configured values. Quality changes take effect in the next match. They do not add another per-frame image-quality controller.

## Tradeoffs and validation

Spatial filtering cannot reconstruct subpixel geometry and can still shimmer under camera movement. Thin-silhouette tests enforce retained center contrast; color-transfer tests preserve direct/composed equivalence with neutral settings, and overlays remain byte-identical. Native tests cover WARP, fog integration, configuration guards, minimize/restore and resource retirement. Separate qualification records whole profiles and isolates AA costs on actual hardware.

The scene and UI remain at resolution scale 1. No measured requirement justified sharpening, upscaling or additional temporal resources. Vendor reconstruction, HDR monitor output, temporal AA and general post chains remain deferred. Long moving scenes, monitor/DPI legibility and actual device-removal recovery remain manual acceptance. See [Postprocessing qualification](../PostProcessingQualification.md).
