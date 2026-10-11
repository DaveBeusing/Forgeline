# Final image quality review

## Visual targets and baseline

Integration baseline is master a4eb694 (0.1.343), including the material,
world dressing, mechanical motion and battlefield-effect refinements.
Low/Medium/High retain their existing shadow, atmosphere, LOD and particle
budgets. Directorate targets are neutral steel/concrete, muted industrial
paint, warm directional light with cool ambient fill, restrained atmosphere
and distinct silhouettes. Critical state uses the existing text/symbol/state
contracts as well as color. There is no authored night scenario to qualify.

Native baseline review of BattlefieldCombat CloseTactical and IndustrialBase
NormalGameplay shows legible full-resolution HUD/resource symbols and muted
world colors. Terrain repetition and saturated fallback bursts are existing
content limitations. These observations do not justify changing shared
exposure, white balance, fog density or introducing bloom/sharpening. Neutral
grade and 1.15 exposure remain the reproducible controls.

The current five-sample AA uses luminance alone to detect/select edges.
An equal-luminance red/green boundary can consequently evade filtering;
a single-pixel diagonal blends the full 30% at High. Native synthetic
regressions isolate both cases without relying on scene-shadow variation.
The proposed refinement keeps partial edge coverage while preserving more
of a local single-pixel peak. Full-resolution overlays remain outside the
world composite.

## Evidence policy

Reference capture now permits explicit raster DPI 96/120/144/192 at any
supported native output size. Zero retains the established height-derived
DPI. Explicit DPI is recorded in the capture key and row; it changes the
production HUD raster/layout input only. Camera, native output size, gameplay
input/hit testing and physical Windows display scaling are separate facts.
Native 3840x2160 and 3440x1440 readback can prove pixel dimensions and raster
clarity; physical-monitor readability still requires display inspection.

Repeated fixed states must remain pixel-identical; failed High shadow cases
retain failure status. Adjacent camera poses do not establish temporal
stability, sustained animation or frame pacing. No accepted golden or human
sign-off is inferred from successful structural checks. TAA requires motion
vectors, reset/disocclusion/alpha evidence and a separate justified budget;
no such evidence currently warrants temporal resources here.
