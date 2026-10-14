# Combat and industrial rendering

## Runtime contract

Completed combat events and copied gameplay state feed presentation only. Existing muzzle, impact, explosion, smoke, dust, supply/refuel/rearm and wreck mappings remain in place. Steam and welding add two typed presets without changing compiled formats or adding assets: 38 concrete effect kinds reuse 36 effect meshes and ten VFX material families in the 492-asset runtime catalog.

Running ProductionFacility or UnitProductionFacility state must also resolve an Operational building. Smelters use slag_outlet, refineries process_tower, ammunition factories exhaust and vehicle factories motion_loading_door. Steam uses six smoke particles per descriptor, welding six sparks; existing validated lifetime/radius/speed bounds apply. Compiled sockets are transformed through copied scale/rotation; unavailable sockets use bounded height offsets. Stable full entity identity maintains emitter continuity. Idle, unpowered, hidden or removed factories stop publishing effects.

Existing mechanical presentation owns wheel movement, turrets, recoil, loading doors, damage/wreck transforms and render interpolation/LOD. Effects reuse those copied actors and compiled anchors. No skeletal system, authoritative machinery state or physics animation is introduced.

## Budgets and readability

Transient VFX retains 2,048 pooled slots. Snapshot publication is capped at 4,096 requests, with discarded requests diagnosed separately. Analytic GPU particles keep the existing 512/2,048/4,096 Low/Medium/High particle caps and 0.5/1/2 frame-area fill limits. Supported mappings rejected by admission are suppressed; they cannot bypass fill limits with a mesh explosion flood. Disabled, unsupported or unavailable mappings may draw at most 32/64/128 mesh effects by tier, or 128 for Disabled/no backend. Hidden copies consume neither fallback admission nor visible effect identity.

Explosion radius remains bounded at eight metres; maximum projected particle diameter is 96 pixels. Smoke uses existing bounded alpha and soft intersection fade. Particle sorting remains approximate by emitter anchor. Production perspective reconstruction uses -Projection.M33 and Projection.M43. Opaque depth rejects particles, particles do not write depth, and UI/selection overlays follow scene output. The overlay remains the source of critical feedback.

DecalPool accepts TireTrack, TrackedVehicle, Crater, Scorch and Hit on Ground or Receiver. Default limits are 512 active, 16 per 256m anchor cell and 128 published marks. Ground marks require visible center/corners; receiver marks retire immediately on hidden/dead/reused identity. Marks are not selectable, do not cast shadows, cap strength at 0.65, and fade over the final fifth of their lifetime. Missing compiled resources skip the mark instead of drawing an opaque cube.

Artillery impacts request eight-metre craters; tank-cannon impacts four-metre scorch marks, lasting 1,200 ticks. Other armored/structure hits request 1.2m receiver marks lasting 400 ticks. Receiver anchoring uses a dominant-axis proxy face; hidden source positions are not read. Owned moving vehicles request tracks once per eight ticks with lifetime 200 ticks and strength 0.28. Existing movement history rejects idle frames and large teleports. Ground bias/normal alignment is cosmetic and never changes height, terrain control maps, navigation, collision or combat.

Settings retain particles.quality and FORGELINE_PARTICLE_QUALITY; profile changes apply on renderer recreation. No new decal preference or schema migration is added. Diagnostics include PublishedEffects/DiscardedSnapshotEffects, DecalMetrics (active/capacity/published/cells/spawn/drop/expire/retire/migrate), particle SuppressedEffects/FallbackEffects and instance SkippedDecalResources. Reference exports include decal counts and completed-fence particle pass distributions; a stale raw graphics timestamp is not a sample for a frame that submitted no particles.

## Verification and evidence

Portable results are in [combat-industrial-effects.json](qualification/combat-industrial-effects.json). The measured rendering source is 949659b0c7629a95023f80d003a25868497ff5eb; the packaged source 246305c8f6376d66313ce4a70c246a2cd3d1eda9 differs only in the corrected existing fallback test expectation. Full Release tests: 1,909 passed, nine skipped, zero failed. Native perspective-depth tests, production parity, mechanical interpolation, visibility, missing-resource handling, 10,000-operation decal churn and fallback saturation are included. Release build had zero warnings/errors. Architecture checks covered 42 projects.

Native qualification contains 18 hardware captures (two scenes, three profiles, three zooms), four Disabled-particle fallback captures, two software captures and two extracted-package captures. Fixed hardware states pass eight exact readbacks; software states pass two. Package images match the equivalent native Medium/NormalGameplay pixel and PNG hashes. The reproducible development package contains 492 qualified assets and passes a 1,000-extra-instance client smoke check.

EffectsBarrage deliberately injects 1,024 copied effects over the existing IndustrialBase fixture. IndustrialEffects uses frozen working presets, while separate tests run actual production and compare its results with presentation absent. Both request 64 marks, of which the default cell cap admits 32. These are synthetic load fixtures, not natural battle placement or art goldens. Inspection of Medium frames found selection rings and HUD still visible; an earlier overflow cohort was rejected because fallback explosions blanketed actors. The corrected depth input also changes visible particle output, so old pixels cannot qualify the final rendering source.

Earlier paired 128-warmup/512-sample moving-camera measurements retained three baseline variance trends and three regressions above the existing 15% threshold. They are recorded without retuning or discarding the threshold. Final static source measurements establish bounded functional cost, not a controlled speedup, sustained 60 FPS or performance acceptance. The D3D12 debug layer was unavailable in hardware captures; zero reported messages do not establish debug-clean execution. Long battles, natural overlapping smoke, object-hit art quality, continuous camera/frame pacing and full-match readability remain unqualified.

## Reproduction

Build Release, run the solution tests and existing architecture/version checks before capturing. Use a clean committed tree and a new output directory:

```powershell
./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes EffectsBarrage,IndustrialEffects -Profiles Low,Medium,High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -Warmups 32 -Samples 128 -RepeatCaptures 8 -OutputRoot artifacts/effect-reference
```

Repeat with -Profiles Medium -ParticleQuality Disabled for the fallback path; use -Software -Profiles Ultra -Displays 640x360 to verify capability fallback. Use -CameraPath Sweep and paired runs with -Warmups 128 -Samples 512 for moving-camera performance comparisons on matched hardware. Retain variance and failed comparisons.

```powershell
./build/Publish-PreAlpha.ps1 -OutputRoot artifacts/effect-package -VerifyReproducible -RuntimeAssetRoot assets/runtime
```

Extract the development archive into a fresh directory, run its client with --smoke-test --render-stress 1000, then capture with -ClientPath and -RuntimeAssetRoot pointing exclusively to the extracted package. Structural checks, exact fixed-state pixels, human art acceptance and performance gates remain separate results.

Final paired Sweep captures at 56ff7fed43530b9fceff06297ff5433e0f85a5aa (same rendering code as 949659b0c7629a95023f80d003a25868497ff5eb) use 128 warmups, 512 samples and eight exact readbacks per case: two scenes and tactical/strategic zooms in each of two runs. All eight captures are structurally valid; all four paired comparisons are comparable, with zero trends beyond 15% and zero unqualified cases. This establishes repeatability for the final moving stress workload, while the earlier master comparison remains historical and does not prove a final-source before/after improvement.