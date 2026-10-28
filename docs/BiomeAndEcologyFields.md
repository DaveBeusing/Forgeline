# Biome and ecology fields

ForgeLine.World provides static environment queries over an immutable ITerrainQuery
and the versioned world-generation recipe. No ECS, window, graphics, weather,
hydrology, erosion, vegetation instances or material/shader integration is added.
Existing terrain queries, authored map bytes, scenario loading, navigation and
save/replay schemas retain their existing behavior.

## Inputs and ownership

Construct BiomeField with a valid WorldGenerationRecipe, an immutable terrain query
and a BiomeFieldProfile. The recipe's ClimateProfileId must exactly match the
supplied profile ID; the provided default is climate.temperate.v1. Profile version 1
validates all numeric parameters. Every supplied parameter and ID participates in
the raster fingerprint, including parameters whose effect is clamped at a point.
Resolve immutable profiles explicitly; do not silently change a registry entry.
Changes to evaluation rules require a new profile version and supported evaluator.

The physical recipe rectangle must lie within +/-1,000,000,000 mm on each axis.
TrySample accepts signed global integer millimeters inside that rectangle,
including its outer geometric boundary. Outside queries return false. Terrain
height and normal must both be available and finite. Heights beyond +/-1,000,000 m,
zero-length or downward normals are unavailable. Invalid versions, profile/grid
limits, surface enums and raster coordinates fail explicitly.

Only the existing ITerrainQuery is consumed. WorldBounds is not treated as proof
that all chunks exist. A missing sample is never filled with flat or random ground.
In particular, TerrainWorld can reject an outer boundary whose canonical owning
chunk is absent. Rasters retain those samples as unavailable.

The terrain source must be a stable, thread-safe snapshot for concurrent queries.
Construct a new field/cache after terrain replacement or streaming availability
changes; the interface carries no revision token and this cache cannot discover
external mutation. No build/road/water permission is inferred from presentation.

## Continuous climate and soil

Climate noise uses WorldGenerationRandom in the Climate domain, with independent
stable stream IDs 0x42494F4D0001 (temperature), 0002 (moisture) and 0003 (soil).
Noise nodes use globally floor-divided integer coordinates, including negative
coordinates. The top 16 random bits supply node values. Bilinear interpolation
uses a cubic smoothstep on both axes. There are no per-chunk noise origins or
random sequence cursors. The default wavelengths are 1024 m, 768 m and 256 m.

Normalized values use unsigned 16-bit integers with U=65535. Fractional cell
coordinates are rounded to the nearest U lattice value; positive ties round up.
Smoothstep computes t=floor((v-a)*U/(b-a)), then
floor(t*t*(3*U-2*t)/(U*U)), with clamped endpoints. Products and interpolation
round with a U/2 integer bias. All intermediate products fit signed 64-bit.
Quantized changes are bounded numerical steps, not independently visible chunk
boundaries. This is value noise, not a hydrological or meteorological model.

Terrain height is rounded once to integer millimeters with ties to even. Slope
is round((1-normal.Y/length(normal))*U), ties to even, clamped to 0..U.
It is a normalized steepness, not an angle. The unchanged float ITerrainQuery
limits terrain precision; at large coordinates its float inputs can represent
less than millimeter precision. Climate coordinate arithmetic remains integer.
Given identical quantized terrain inputs, subsequent field rules are integer.
Cross-platform identity of arbitrary upstream float terrain/normal calculations
is not promised. Discontinuous or inconsistent terrain normals can still cause
local ecological discontinuities; this field does not repair upstream geometry.

Temperature is stored in milli-degrees Celsius. Defaults are 16 C +/-12 C of
large-scale noise, with a 6.5 C/km lapse above the configured water level, clamped
to -50..50 C. Signed integer divisions truncate toward zero. Water level defaults
to 0 mm. A smooth lowland factor falls from U at that level to zero 30 m above it.
Rock exposure rises smoothly with slope from 5000 to 30000 normalized units.
Moisture is clamp(3*noise/4 + lowland/4 - slope/3). Soil fertility is its independent
soil noise multiplied by the remaining non-rock fraction. These are suitability
indices, not resource inventory, actual soil depth or dynamic water quantities.

## Biome rules and surface constraints

Five persistent classes are TemperateMixedForest=0, OpenMeadow=1, DrySlope=2,
RockyGround=3 and WetDepression=4. BiomeWeights stores all five UNorm16 weights.
The evaluator allocates an exact budget of U, in this order:

1. Rock receives the smooth rock exposure.
2. Wet depression receives remaining budget times lowland and moisture suitability
   (smooth moisture 36000..60000).
3. Dry slope receives remaining budget times dry-moisture suitability (smooth
   U-moisture 20000..50000) and slope suitability (smooth 1000..18000).
4. Mixed forest receives remaining budget times soil, moisture suitability
   (smooth 20000..50000) and temperature comfort: rising -5..10 C, falling 24..40 C.
5. Meadow receives the exact remaining budget, preventing normalization drift.

Natural ground therefore sums exactly to 65535. DominantBiome uses the largest
weight; ties choose the lowest stable class ID. Soft weights, not the discrete
dominant label, are the integration contract for smooth material/scatter choices.

Natural samples at or below water level become Water. An explicit surface argument
can instead identify Water, Rock or Built, supplied from authoritative caller
data. These arguments do not change height or temperature. Water sets moisture
to U and soil/weights to zero. Built sets soil/weights to zero. Explicit Rock sets
soil to zero and rock weight to U. Explicit constraints take precedence over the
natural water-level classification. They do not grant build permission or invent
land above water. Undefined enums fail. Point queries can supply individual masks;
the raster/cache surface argument is one uniform constraint for that raster.
Mixed authored constraints should query the appropriate per-point context.

## Compact rasters, cache and identity

BiomeFieldRaster.Create samples the recipe's existing height lattice, including
shared borders, using exact integer spacing. One private packed sample uses 26
bytes: availability byte, resolved surface byte, height int32, temperature int32,
slope/moisture/soil uint16, then five uint16 weights in class order. Integers are
little endian. Missing records are entirely zero. Public reads return value types;
no mutable backing bytes escape. A 129x129 raster occupies 432666 sample bytes.

BiomeFieldCache retains 1..32 immutable rasters (default 8), keyed by chunk and
explicit surface. Its locked FIFO policy serializes cold builds too, limiting
transient allocation to one additional raster per cache. Eviction/order changes
work and retained memory only. Repeated direct and cached samples remain identical.
Render quality is absent from field inputs. A renderer may sample fewer points
or display a lower resolution; it must not redefine authoritative field values.

ComputeFingerprint uses the existing Gameplay canonical fingerprint contract.
Type 0x42494F50/schema 1 stores the complete profile parameters, chunk X/Z,
sample count per side and requested surface; type 0x42494F52/schema 1 stores each
packed row. Stable IDs are zero and coordinates are global sample-lattice row
origins. The outer contract includes every world recipe field, generator version,
seed and rules. Canonical row ordering and SHA-256 are inherited unchanged.
These are per-raster identities: do not concatenate independently duplicated
border rows into a whole-world identity. Future world assembly must assign one
canonical owner to shared samples and retain the complete terrain/rule identity.

CaptureDiagnostics returns a headless report with format version 1, complete
recipe/profile, chunk/surface, canonical SHA-256 and distribution statistics.
ToJson exports stable indented JSON without timestamps, paths, cache counters or
hardware/quality values. Available, ecological, water and built sample counts are
separate. Mean temperature uses available samples; mean moisture/soil are normalized
to 0..1. Mean biome weights use ecological samples only; dominant counts retain
deterministic ties. Empty denominators produce zero, never NaN. Statistics count
lattice samples including borders, not physical biome area or global population.

Example in any headless host after composing an immutable terrain query:

```csharp
var profile = new BiomeFieldProfile();
var recipe = new WorldGenerationRecipe { Seed = 42, ClimateProfileId = profile.Id };
var field = new BiomeField(recipe, terrainQuery, profile);
var cache = new BiomeFieldCache(field);
string json = cache.GetRaster(new ChunkCoordinate(0, 0)).CaptureDiagnostics().ToJson();
```

## Reference and validation

BiomeFieldTests fixes a 3x3 raster at chunk (-1,-1), seed zero, origin (-1,-1),
2x2 chunks of 2000 mm, three samples per side, region size 1, terrain/geography/
rules IDs t/g/r, the default climate profile and constant 1 m height/up normal.
Canonical length is 549 bytes; SHA-256 is
ec6346db297d4d10e12e73805eed053cbebe2a25d7bb2ba740b5008f77bb3b14.
The first sample is temperature 22576, moisture 36535, soil 19842 and weights
(11421,54018,0,0,96). The last has moisture 36534, soil 19841 and weights
(11420,54020,0,0,95); temperature remains 22576.

Tests cover negative/shared edges, tiny cross-boundary differences, parallel
permutations and FIFO eviction, display subsampling, seed/coordinate sensitivity,
height/slope/lowland gradients, water/rock/built constraints, unavailable terrain,
numeric limits, complete profile identity, maximum packed size, normalized
distributions, JSON identity and integration with existing TerrainWorld. The
existing early World CI step includes these tests. No renderer hookup or physical
graphics acceptance follows from headless field qualification.
