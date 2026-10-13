# Validated graphics quality and display preview

Status: Accepted

## Context

Persisted profiles, independent effect preferences and graphics construction previously had separate policy paths. A saved effect could override a degraded profile. Display changes were saved before a usable new surface had been confirmed. World resolution was fixed to output resolution.

## Decision

Presentation owns one typed `RenderQualitySettings` resolver. Nullable per-feature overrides inherit preset values; preset selection resets overrides and retains grading. Medium is the explicit default. Ultra increases object retention within the existing High effect budgets. The resolver validates before allocation, uses actual composition format support and available DXGI budget, and caps overrides during degradation. A conservative canonical texture allowance complements scene/depth/shadow/particle payload estimates. Requested and effective settings plus fallback remain distinct diagnostic data.

Schema 3 migrates previous preferences without losing display/input data. Invalid graphics data recovers as its own group. User settings remain outside the installation; no runtime content-format migration is required. Texture detail drops supplied mips while retaining valid BC7 top dimensions and transfer functions.

Graphics changes apply at the next session boundary. Existing render-owner fences retire old GPU resources before construction. Low-level scene-output configuration waits idle, restores previous targets on failure, and escalates failed restoration through the established session-fault contract. No setting enters deterministic simulation.

World scaling uses ceil-sized FP16 color and shader-readable world depth. Output-space HUD/markers remain native. A native overlay depth target exists only when scaled; composition writes nearest source depth under an Always depth comparison so depth-tested overlays retain occlusion. Four display-linear samples interpolate the already fogged/graded/tone-mapped world. Spatial AA can require 20 scene loads; native scale retains its existing path. There is one composition draw and no intermediate/history chain. Shadow pass restoration uses world viewport dimensions. Terrain and instance root-signature budgets remain unchanged.

Display changes use an unsaved, monotonic 15-second trial. Win32 mode/size changes run on the window owner; the render owner applies queued surface intent. Keep requires successful presentation of the requested dimensions. Edits are frozen while pending. Cancel, expiry and disposal restore prior actual windowed dimensions/mode; restore failure attempts windowed safety. Persistence happens only after confirmation and complete candidate validation. The frontend reports failures without trapping navigation in the preview.

## Consequences and validation

Central policy prevents feature overrides from escaping safe caps and makes benchmark workloads explicit. Deferred construction avoids live texture/renderer lifetime mutation. Software and unsupported formats remain qualified fallbacks rather than optimistic high settings. Logical payload estimates and DXGI observations are separate; neither guarantees physical residency.

Scaling reduces target memory but has a measurable composition cost. It remains an explicit user choice, not an automatic performance promise. Existing shader compilation, frame fences, resource retirement, runtime asset formats and headless boundaries remain authoritative. Policy, persistence recovery, native scaling/depth/resize, successful-display confirmation and timeout rollback have focused tests. [Measured settings qualification](../GraphicsQualitySettings.md) records source identities, native evidence and remaining manual acceptance limits.
