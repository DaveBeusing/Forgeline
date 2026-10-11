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

Implementation validation and changed-image review are pending. Required gates,
native 1000-instance stress, map/assets, headless behavior, 4K/ultrawide and
hosted `build-test` must be reported separately before acceptance. Existing High
shadow-edge repeatability and slow software-runner smoke limitations are not
resolved by environment art changes.
