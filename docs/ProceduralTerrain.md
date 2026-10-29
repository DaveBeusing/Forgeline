# Seeded terrain landforms

`ProceduralTerrainFactory` creates a separate headless `TerrainWorld` from a
`ProceduralTerrainRecipe`. It does not participate in scenario loading or change
the version 1 authored Central Divide and Split Basin recipes, map artifacts,
savegames or replays. Callers consume the existing `ITerrainQuery` interface.

## Inputs and evaluation

The nested `WorldGenerationRecipe` supplies the versioned 64-bit seed, signed
chunk origin and rectangular extent. Its terrain profile must be
`terrain.landforms.v1`. This evaluator's own version is 1. Unknown versions,
profiles, option bits and invalid parameters fail before heightfield allocation.
The recipe and all nested inputs are immutable records without mutable buffers.

Version 1 uses 256 m chunks and 65 samples per side, including both boundaries.
`WorldSampleCoordinate.FromChunk` supplies the global integer lattice. Each
sample is converted to integer millimeters at exactly 4,000 mm spacing. Negative
noise cells use mathematical floor division. The same position has the same
height regardless of map extent, chunk order, worker scheduling or visual LOD.

Five independent Terrain domain stream IDs feed value-noise lattices:

| Stream | Lattice size | Purpose |
|---|---:|---|
| 1 | 2,048 m | Signed broad hills and basins |
| 2 | 1,024 m | `1 - abs(noise)` ridge lines |
| 3 | 128 m | Bounded surface detail |
| 4 | 2,048 m | Low-relief/flat-zone mask |
| 5 | 2,048 m | Potential drainage corridors |

Noise values and cubic smoothstep interpolation use signed 64-bit Q20 integer
arithmetic, with integer division truncating toward zero after each multiplication.
Lattice values take the upper 21 random bits minus 2^20. Heights are evaluated
as integer millimeters and converted once by division by `1000f` into the existing
float heightfield. There are no transcendental noise functions or mutable random
sequences. These operations define the version 1 evaluation order; changing
rounding, stream IDs or interpolation requires a new terrain recipe version.

Relief is hills plus nonnegative ridges plus detail. The flat mask attenuates
that relief by `1 - mask * FlatStrengthPermille / 1000`; drainage subtracts
`DrainageDepthMillimeters * max(0, 1 - 4 * abs(noise))`. Flat zones soften relief;
they do not promise level building pads. Basins and ridge contours are geometric
approximations, not an erosion or geological simulation.

`ProceduralTerrainEvaluator.Evaluate` also returns flat and drainage masks in
0..1000 permille. Drainage denotes potential topology only: it creates no water,
flow simulation, crossings, navigability, resource or biome classification.

## Parameter and allocation limits

| Parameter | Default | Accepted range |
|---|---:|---:|
| Base height, mm | 20,000 | -100,000..100,000 |
| Hill height, mm | 18,000 | 0..40,000 |
| Ridge height, mm | 24,000 | 0..40,000 |
| Detail height, mm | 500 | 0..1,000 |
| Drainage depth, mm | 4,000 | 0..8,000 |
| Flat strength, permille | 600 | 0..1,000 |

Parameter combinations must additionally have `MaximumGradient <= 0.35`.
The bound sums conservative per-axis smoothstep-noise, ridge, attenuation and
drainage derivatives, multiplies by sqrt(2), and includes 0.02 for fixed-point,
millimeter and float rounding on the fixed 4 m sample grid. The diagnostic
gradient is rise/run, not degrees. It bounds each bilinear cell using the largest
opposing-edge partials; it is not a surface-normal seam guarantee. Existing
`TerrainHeightfield.SampleNormal` retains its one-sided chunk-boundary behavior.

Useful parameter combinations (hill/ridge/detail/drainage in millimeters):

| Use | Hill | Ridge | Detail | Drainage | Flat permille |
|---|---:|---:|---:|---:|---:|
| Open RTS terrain | 10,000 | 10,000 | 250 | 3,000 | 800 |
| Default mixed landforms | 18,000 | 24,000 | 500 | 4,000 | 600 |
| Stronger ridges | 10,000 | 32,000 | 250 | 3,000 | 400 |

The common 4,000,000 stored-sample budget and 256-chunk per-axis limits apply.
All outer map bounds must lie within +/-1,048,576 m, keeping 4 m samples
representable by the existing float-based spatial queries. A requested chunk
must belong to the recipe rectangle. No hidden clamping or partial map is used.
Height storage is four bytes per stored sample; the existing heightfield
constructor defensively copies each chunk buffer. This is a payload bound,
not a measured process working-set guarantee. Shared border samples are stored
in both chunks, with identical float bits. Outer-boundary query ownership follows
the existing next-chunk-at-local-zero convention.

## Diagnostics and command

After building the ordinary Release MapCompiler, run:

```powershell
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --generate-terrain --qualification-output artifacts/terrain.json
```

The default CLI recipe uses seed 17 and an 8x8 rectangle at (-4,-4). An optional
`--recipe-input recipe.json` accepts the complete procedural recipe, with normal
record defaults for omitted properties; input is limited to 64 KiB and unknown
properties fail. For example, a small open-terrain input is:

```json
{
  "Generation": {
    "TerrainProfileId": "terrain.landforms.v1",
    "Seed": 17,
    "Origin": { "X": -2, "Z": -2 },
    "WidthInChunks": 4,
    "HeightInChunks": 4
  },
  "HillHeightMillimeters": 10000,
  "RidgeHeightMillimeters": 10000,
  "DetailHeightMillimeters": 250,
  "DrainageDepthMillimeters": 3000,
  "FlatStrengthPermille": 800
}
```

The JSON report contains the resolved recipe, chunk/sample counts, height range,
maximum bilinear gradient bound, bitwise seam mismatches, total generation plus
diagnostic duration and `TerrainSha256`. Timing is observational and excluded
from identity. The command fails on invalid inputs, seam mismatches or a sampled
gradient beyond the recipe bound. It exports diagnostics, not a registered
playable scenario or terrain asset.

Terrain SHA-256 covers the little-endian uint32 marker `0x314e544c`, terrain
version uint32, seed uint64, the six parameter int32 values in table order, then
chunks sorted by signed Z/X. Each chunk contributes X/Z int32 followed by 65x65
row-major IEEE float32 height bits. This is a terrain diagnostic identity;
it is separate from `WorldGenerationFingerprint.Gameplay`, climate/geography
profiles and decoration. A future composed world producer must include its
complete authoritative content and deduplicate shared borders in that contract.

## Qualification

Against master `37ff3ddf8be2511431366bd4f8f318d55d2fb2d5`, SDK 10.0.402,
the Release World test run passes 58 cases, zero failed/skipped. Coverage includes
fixed sample/mask vectors, a pinned terrain SHA-256, all signed shared edges,
parallel reversed chunk construction, independently generated submaps, finite
height/gradient limits, zero relief, extreme supported origins and invalid inputs.
The new compiler command also compiles in an isolated World-only harness; this
is not a full MapCompiler or solution build.

The default parameter recipe at seed 17 and origin (-2,-2), size 4x4 has digest
`13253f1ba5c44650be7c6e02af3f6ba4fe4768b3509badd0f6beef05cb66a1e3`.
One local run observed heights 24.488..30.134 m, maximum cell gradient
0.0174591085, zero seam mismatches and 234.9225 ms including diagnostics.
This one cold/shared-host observation is not a performance threshold or claim.

`build/Test-ProceduralTerrain.ps1` checks repeated compiler exports, flat custom
input, unknown properties/options/versions and allocation rejection in normal
CI after the Release build. Existing authored-map hashes, full tests, headless,
asset, package and natural terminal-match gates remain unchanged. Full required
`build-test` and physical graphics acceptance are separate from local World
qualification. There is no graphics, gameplay-map or balance acceptance claim.

## Current integration and required CI

The branch also integrates master `fd551f265375c73f75f131b96d289de0dbc6c106`,
preserving biome fields, environment variants, strategic geography and their
existing CI gates. The combined World suite passes all 75 Release cases locally.
The final PR version is one PATCH above this master's 0.1.522.

On terrain head `33724a46d2a78db8c68e9a517e2c9b5b9e6668b9`, the
[complete CI run](https://github.com/DaveBeusing/Forgeline/actions/runs/40787933032/job/123165829010)
passed the full Release build, terrain export, both authored map qualifications,
compiled scenario recovery, runtime assets, WARP/headless and save/replay smoke
steps. Its solution test step finished with 4,616 passed, one failed and 76
conditional skips. The failure was
`BattlefieldStatusNativeTests.NativeStatusIsRepeatableAndHiddenOrStaleFactsDoNotPaint`
at distance 1000, DPI 192, scale 2: expected one status marker, actual zero,
line 26 of the unchanged presentation test.

The [unchanged master CI](https://github.com/DaveBeusing/Forgeline/actions/runs/40833193453/job/123311852436)
on `fd551f2` reproduces that exact test, parameter set, line and assertion
(4,613 passed, one failed, 76 skipped). This establishes an independent native
HUD baseline failure. The new terrain factory is not called by that fixture.
Keep the PR unmerged while the required check is red. Subsequent package and
natural terminal-match steps did not execute after the failed solution test;
their success is not claimed. Fresh CI on the final synchronized head is still
required, and physical graphics qualification remains separate.
