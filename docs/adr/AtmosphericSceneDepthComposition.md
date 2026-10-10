# Atmospheric Scene Depth Composition

## Decision

Atmosphere is copied visual state with no intelligence authority. Compose distance/height extinction into linear scene color before the existing exposure, ACES and sRGB transfer. A single point depth read reconstructs world position from the inverse view-projection and viewport pixel center. There is no depth blur, neighborhood search, temporal history, volume texture or second composition draw.

Disabled is the default and preserves the original direct-output contract. Distance uses exponential extinction after a bounded start distance. Height approximates exponential height density integrated along the camera-to-surface segment; the start-distance deduction uses that segment's mean density. The small-height-span limit avoids cancellation. Exponents are bounded, opacity is capped at 65% and the default cap is 50%. Zoom compensation scales density toward a 420 metre reference with a bounded 0.35–2 multiplier. This is visual haze, not a weather or detection model.

## Ownership and lifecycle

Graphics owns configuration, one shader-resource view of the existing surface depth, resource barriers and the fullscreen pipeline. Enabled atmosphere requires linear scene composition; the client automatically selects it before renderer construction. Existing D32 depth becomes an R32 typeless resource with D32 depth/R32 float sampling views. Depth payload is unchanged. The FP16 scene targets cost width × height × 8 × buffer count bytes when the direct path was previously selected; diagnostics report that requirement separately from zero additional depth-target bytes.

The queue records depth writes, unbinds the DSV, transitions depth to pixel sampling, composes, returns depth to writes and restores the DSV before Overlay. The surface depth is shared safely by ordered queue submissions; per-buffer scene targets retain their existing frame-fence reuse. Configuration waits for idle outside recording, recreates the surface-owned targets and resets measurements. Resize rebuilds and retires the depth SRV together with the surface targets. Partial target acquisition failure faults the graphics session and releases acquired ownership. Device removal and shutdown follow the existing fatal-session retirement policy.

The camera contract is finite/invertible and must be submitted before world draws. Missing camera data gives unchanged scene output and an explicit unavailable reason; a depth-clear/background pixel receives no extinction. Output-space clear/debug pixels and the later HUD, minimap, warning colors and tactical overlays are preserved. A recording failure cannot publish an applied atmosphere or an unsubmitted CPU identity. CPU and delayed GPU identities are separate; composition GPU timing includes output transfer and fog rather than pretending to isolate only extinction arithmetic.

## Intelligence and content limits

World depth comes only from the existing authorized immutable world render stream. Hidden entities cannot supply depth/color or cast shadows; radar/last-known markers remain copied tactical overlay state. Atmosphere never reads ECS, faction intelligence, or targetability and is absent from saves/replays and authoritative hashes.

Opaque terrain, units/buildings and existing opaque VFX share the same depth ordering. General transparent volumetric smoke, soft particles and a dedicated sky pass remain outside this opaque scene contract. The existing linear path rejects alpha-blended world pipelines explicitly. Physical-monitor readability, bridge/unit silhouettes under arbitrary motion, smoke art-direction and sustained full-client throughput require manual visual acceptance; synthetic overlay pixels and raster scaling do not establish those claims.

## Validation

Native hardware/WARP pixel tests compare extinction to an analytic distance reference and height extinction to independent numerical integration; exposure 0.25/1.15/4, clear and exact overlay preservation, zero density, missing camera, disable/release, resize/suspend and recording faults are covered. Production terrain/object captures cover actual 120/420/1000 metre cameras at 100/150/200% raster scale, unchanged static frames, hidden-object invariance and shadow compatibility. Settings round-trip all color/density/height controls and preserve disabled defaults in older files.

An opt-in hardware fixture compares Direct/Linear/Distance/Height on identical nine-chunk terrain, resolution, exposure and camera. The 25/90/240 metre cases match a pre-change baseline; 120/420/1000 metres extend coverage. Target bytes, descriptors and completed-fence composition/total GPU samples accompany CPU observations. WARP provides structural evidence only. See Graphics.md for commands and measured results.
