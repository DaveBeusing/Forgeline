# Central Divide environment composition

## Reference and priorities

The starting revision is `9a2b92bc41f06b8319a8d7cada2b5559a5fa388f`.
Nine native 1920x1080 Medium captures cover DaylightTerrain, IndustrialBase and
LogisticsCorridor at 120/420/1000m. Eight readbacks per fixed state agree exactly.
The observed strategic daylight view has a large isolated rectangular industrial
patch, repetitive ground and only a handful of detached props. The tactical
industrial view exposes planar vegetation and little surrounding context.
These are art observations, not accepted golden images.

## Composition and ownership

Twelve authored groves pair irregular canopy positions with lower scrub, grass
and occasional rocks at their margins. Four service yards use the existing
crate, drum, pallet, utility, pipe, concrete, rubble, fence, light and decal
vocabulary. Three fenced edges leave the approach side open. These are bounded
authored groups, not a biome or gameplay generator.

New placements reserve the complete rotated horizontal bounding circle plus
12m around construction rectangles, 24m around resource envelopes, 32m around
crossings and 28m around road centerlines. The conservative rule can omit an
authored candidate; it never moves gameplay data. Existing placements keep their
stable keys. The 444 additions carry `PresentationOnly` in the map artifact and
are cached as render instances when the presentation extractor is constructed.
They do not allocate ECS entities or shift gameplay IDs. The original 27 ECS
decorations and their transforms remain replay-stable. Additions are not
selection targets, collision obstacles, concealment or buildability inputs.
Static public scenery uses the existing extraction/visibility policy. No new
world fog mask or hidden-enemy information is introduced.

The terrain art profile derives rotated gravel and mud regions from the existing
eight road edges, replacing the misleading east-west stripe through the central
barrier. All shoulders precede gravel so junctions preserve their road centers.
Low-strength feathered dirt/rock patches support selected groves; wider yard
feathers and small service aprons soften the large isolated material rectangles.
Rotation and strength are validated construction-time values. The eight material
IDs, four-layer control maps, thirteen GPU samples, shaders and resource owners
remain unchanged.

Initial changed captures exposed a narrow-road dropout at chunk boundaries:
area-only palette selection could discard a locally dominant authored material.
A regression reproduces this with a road spanning two chunks beside broad rock.
Palette selection now reserves locally dominant gravel/concrete/industrial/scorch
surfaces (sample peak at least 0.5), then uses the existing total-area ordering
and stable slot tie-break within exactly four layers. This changes only control
map construction; it adds no shader samples or native resources. Continuous
material-boundary quality still requires inspecting the corrected captures.

## Vegetation and distance

`build/Author-WorldVegetation.py` produces native glTF source geometry using only
the existing foliage atlas rectangles. Stable source paths, IDs and materials
remain intact. Rooted conifers use three overlapping crowns and a narrow stem;
scrub uses two crowns and grass uses intersecting double-sided blades. High and
reduced meshes retain Y=0..1 and a bounded horizontal envelope. Triangle counts
are conifer 80/48, scrub 40/24 and grass 12/6. No third-party material is added.

Production extraction and reference fixtures put rooted vegetation at ground
height without mutating legacy simulation transforms. Centered props retain
half-height placement and decals retain their small ground offset. The fixture
version advances to 3 after integration with mechanical-motion fixture version 2
so changed content cannot be mistaken for an identical acceptance fixture.

Existing projected-size LOD, hysteresis, instancing, conservative vegetation
culling and visual-time motion remain authoritative. There is no new CPU update
loop, texture, material, shader or density service. Existing quality profiles
continue to select object LOD. Continuous live LOD motion still needs visual
review; static screenshots cannot establish absence of popping or flicker.

## Qualification

Evidence is retained under `artifacts/environment-baseline` and subsequent
environment qualification directories. The baseline used eight warmup frames,
16 timed samples and eight repeat readbacks, VSync off, through the existing
native production capture lane. These are short fixture measurements, excluding
readback, not sustained game FPS or portable performance thresholds.

The integration base is `b25f003` (mechanical motion, PR #172). Final images use
`9cc66de`; `59d593c` subsequently fixes only the default capture request matrix
and its tests (the explicit captured matrices are unchanged). Evidence is retained
locally, with native manifests recording their exact source/build identity:

| Evidence | Scope and result |
| --- | --- |
| `environment-matrix-fix-build.log` | Final Release build, zero warnings/errors. Version 0.1.341 is one PATCH above the integration base; the 42-project architecture graph passes. |
| `environment-final-*-tests.log`, `environment-matrix-fix-client-tests.log` | All 14 test modules pass when run separately: 1,690 succeeded, nine optional hardware measurements skipped, no remaining failures. The game module is recorded separately below. |
| `environment-integrated-game-tests.log` | All 390 game tests pass, including the recorded schema-1 replay. |
| `environment-replay-test.log` | Six world-presentation tests pass; extraction preserves the historical `848FAB...1549B` checkpoint and emits non-interactive rooted dressing. |
| `environment-replay-parity.json` | Seed 2026, 64 ticks: all 23 deterministic diagnostic fields match the starting revision, including 103 entities. Timing/allocation/GC fields are excluded. |
| `environment-integrated-map.json` | 71 reachability, two alternate-navigation and two alternate-road checks pass. |
| `environment-integrated-assets.json` | Clean compilation: 341 assets (158 meshes, 56 textures, 127 materials), 56,729,669 container bytes, no qualification issues. All 80 asset tests pass separately. |
| `environment-final-medium` | 36 native images: three environment scenes, three zooms, 1080p/QHD/4K/ultrawide; 24 additional QHD camera poses. Eight identical readbacks per fixed state. No fallback meshes or material-binding failures. |
| `environment-final-profiles` | All 18 Low/High 1080p images for the same three scenes/zooms pass eight repeated readbacks. |
| `environment-corrected` | Nine Medium 1080p native images at `623c83f`, eight identical readbacks each; corrected narrow-road dropout. |
| `environment-large-displays` | 27 QHD/4K/ultrawide images and QHD camera poses at `1c40c6a`; preceding palette correction and mechanical integration, not final-head acceptance. |
| `environment-final-terminal.json` | Final implementation completed at tick 42,922, winner 1, Command Core destroyed; 314 simulation entities. |
| `environment-final-windowed-validated.json` | Final 1,000-instance smoke passes the canonical Windowed assertions: 1600x900, 49 presented frames, 75 total draws, 186 peak SRVs and zero binding failures. The unchanged assertion function was invoked separately with its build-directory path resolved; this is not a pass of the combined borderless script. |
| `environment-windowed.json` | Earlier 1,000-instance smoke: 1600x900, 117 presented frames, 75 total measured draws, zero texture/material binding failures. |

The final native captures retain 52,707,564 resident texture bytes. The earlier
windowed smoke measured 46.45 FPS, 19.75ms CPU render and 6.55ms GPU on the RTX PRO
5000 Blackwell laptop; the final short startup run measured 7.10 FPS, 138.36ms CPU
render and 6.80ms GPU (completed active window 7.44 FPS / 19.84 TPS). This large
CPU variance is not a stable performance qualification or a 60-FPS acceptance.
Controlled sustained profiling remains required. The final IndustrialBase normal
1080p fixed-state image has CPU/GPU medians of 14.16/13.46ms, 29 instance draws,
102 visible instances and 82 reduced instances; that is a different workload.
Concurrent CPU work and short windows prevent speedup conclusions. The environment mesh payload adds
31,558 bytes; textures are unchanged by this PR.

The first full-suite attempt correctly exposed a replay regression from adding
decorative ECS entities. The final implementation removes that allocation and
roots legacy vegetation during extraction; neither stored checkpoints nor their
verification were relaxed. That first run also had a native client handoff
timeout and a D3D12 `0xC0000005` while native modules ran concurrently. Its asset
test was stopped before the clean integration build; it is not a passing suite.
Isolated final Graphics, Presentation and Client runs pass. The integrated
mechanical gallery also exposed an invalid default request (all twelve enum
values against a six-scene limit). The six original defaults are now explicit;
optional galleries remain selectable and the existing failure/evidence-retention
tests pass. No acceptance guard or test expectation was relaxed.

The borderless 5120x2160 smoke rendered but remained unfocused. Completion requires
an active FPS/TPS measurement window; the run never produced one and was stopped.
No borderless, focus-recovery or physical-display acceptance is claimed. The
debug layer was requested but unavailable on this machine. The 18 Low/High images
above pass for these three environment scenes; broader High-profile shadow-edge
qualification and slow software-runner smoke limitations remain separate.

Hosted runs for `7c258cd` passed Frame hot paths and Scalability qualification.
The main CI passed build, map, assets, evidence contracts and native WARP capture,
then remained in the window-mode smoke. Final-head hosted completion is still
required; local passes do not replace it.

Visual review found clearer conifer silhouettes and industrial groupings with
open approaches. Repetitive natural ground and some broad material boundaries
remain visible. Continuous motion/LOD transitions and final art acceptance remain
human review items; repeatable screenshots are structural evidence, not approved
goldens. The PR remains a draft until required hosted gates and these acceptance
items are resolved.

## Bounded modular kit

`CentralDivideKitDressing` appends 34 PresentationOnly `kit.*` records without
changing legacy keys, navigation obstacles, construction sites or simulation
allocation. Yard/natural modules keep the existing exclusions; thin road drains
and bridge approaches use documented corridor exceptions. Aprons follow actual
bridge mesh ends rather than its passage envelope. See the
[kit catalog, reproducibility and measured acceptance limits](ModularEnvironmentKit.md).

## Procedural decoration sets

[Deterministic environment scatter](EnvironmentScatter.md) adds a separate bounded
headless generator over biome fields and frozen gameplay clearances. Its compact
chunk sets, reference plots and fingerprints do not change authored dressing,
scenario artifacts, ECS allocation, rendering or persistence.
