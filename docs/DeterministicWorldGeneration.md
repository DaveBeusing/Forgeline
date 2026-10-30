# Deterministic world generation contracts

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

ForgeLine.World owns the portable version-1 contracts. They require no renderer,
window, Simulation, Game or compiler dependency. No terrain, climate, geography,
resource, vegetation or prop generator is implemented by these contracts.
Existing BattlefieldTerrainRecipe V1, authored map bytes, ScenarioRegistry
allowlist, save/replay schemas and SimulationRandom behavior are unchanged.

## Recipe and failure boundary

WorldGenerationRecipe records GeneratorVersion (currently 1), the complete ulong
Seed, signed origin chunk, positive width/height in chunks, chunk size in integer
millimeters, height samples per side, region size and immutable terrain, climate,
geography and rules profile IDs. IDs are ordinal lowercase ASCII, 1..96 characters
from a-z, 0-9, period, underscore and hyphen. These are identifiers, not runtime
string hashes. Default IDs are placeholders for future profile registries; validation
checks syntax, not profile availability. Consumers must resolve exact immutable
profiles and reject unavailable IDs. Changing profile semantics requires a new ID
or generator version. Version 1 defines no optional rule bits; Options must be zero.

Dimensions are 1..256 chunks, samples 2..129, region size 1..32 and chunk size
1..16,000,000 mm. Size must be divisible by samples minus one, making the lattice
spacing an exact integer millimeter count. The allocation contract counts every
per-chunk height sample (including duplicated border storage), at most 4,000,000.
The final included chunk must fit signed int X/Z. Validation precedes generation
or fingerprint serialization and performs products in signed 64-bit arithmetic.

GetValidationError returns UnknownGeneratorVersion, InvalidLimits, InvalidProfileId
or UnknownOptions. Validate throws InvalidOperationException with that code and
message. WorldGenerationResult<T> carries either a non-null completed value or one
known diagnostic with a nonempty message of at most 1,024 characters. InvalidContent
is available to producer adapters. Failed results expose no partial completed value.
No cancellation, timing, renderer quality or runtime machine information enters
recipe or fingerprint identity.

WorldDecorationRecipe independently records version 1 and vegetation, props and
decoration rule profile IDs. Render quality, LOD, culling, hardware and animation
state are never gameplay recipe inputs.

## Integer coordinates and random derivation

The global sample lattice uses signed long X/Z:
global = signed chunk * (samples per side - 1) + local sample.
Local samples include both borders (0..samples-1). Neighboring chunks therefore
address the same border coordinate. ToChunk uses floor division and returns
canonical local samples 0..samples-2; a border belongs to the following chunk.
Conversion throws OverflowException when that canonical owner exceeds signed int.
FromChunk supports the complete signed int chunk range. Random At supports the
complete signed long coordinate range without requiring a chunk conversion.

Persistent numeric domain IDs are Terrain=1, Climate=2, Geography=3, Vegetation=4,
Props=5. Unknown IDs fail. Version-1 derivation uses the same SplitMix64 finalizer
constants as SimulationRandom, kept within World to preserve dependency direction.
All arithmetic below is explicitly modulo 2^64; signed coordinates are two's
complement bit patterns:

1. k = Mix(seed XOR 0x464F5247454C494E).
2. In order, k = Mix(k XOR value) for generator version, domain, X, Z, stream ID.
3. output = Mix(k + 0x9E3779B97F4A7C15 * (sample index + 1)).
4. Mix(v): v=(v XOR (v>>30))*0xBF58476D1CE4E5B9;
   v=(v XOR (v>>27))*0x94D049BB133111EB; return v XOR (v>>31).

At is immutable and safe for parallel indexed access. CreateStream returns an
independent WorldGenerationSequence starting at index zero. A sequence has one
owner, advances only its own index and wraps after 2^64 draws. Consumers allocate
stable stream IDs to independent purposes; drawing more values in one sequence
cannot advance another. There is no guarantee of collision-free 64-bit derivation,
statistical independence or cryptographic randomness. Do not use it for secrets.

WorldCoordinateConverter and floating TerrainHeightfield interpolation retain
their existing behavior. They are not the integer lattice serialization contract.
Contract coordinates and lengths are integers. For future floating inputs,
quantize finite physical lengths once to integer millimeters, nearest with ties
to even, rejecting overflow; serialize only the resulting integers and record
any other payload scale/rounding policy in that payload's versioned schema.
No implicit float conversion is supplied. Arbitrary Math/MathF noise, transcendental
functions, fused operations or intermediate floating point evaluation are not
guaranteed bit-identical across platforms merely by using these seed contracts.
A future generator must qualify its own evaluation and quantization boundaries.

## Canonical bytes and fingerprints

WorldCanonicalWriter writes uint/int32 and ulong/int64 little endian, signed
integers as two's complement, without padding. Byte strings have a uint32 length
prefix. Profile identifiers use those length-prefixed ASCII bytes. There are no
floating-point, culture-sensitive text, native-layout or dictionary writes.
A writer emits at most 16 MiB. SHA-256 is over its complete byte sequence and is
reported as 64 lowercase hexadecimal characters. Fingerprints are content
identity, not seeds, authored map hashes, signatures or trusted-content proofs.

All fingerprints begin with uint32 0x31475746 (ASCII FWG1, canonical format 1)
and uint32 kind (Gameplay=1, Decoration=2).

Gameplay then writes, in order:
GeneratorVersion uint32; Seed uint64; Origin X/Z int32; WidthInChunks,
HeightInChunks, ChunkSizeMillimeters, SamplesPerSide, RegionSizeInChunks int32;
TerrainProfileId, ClimateProfileId, GeographyProfileId, RulesProfileId identifiers;
Options uint32; content record count uint32; ordered records.

Decoration writes the referenced gameplay SHA-256 as a length-prefixed 32-byte
string, decoration Version uint32, VegetationProfileId, PropsProfileId,
RulesProfileId identifiers, record count uint32 and ordered records. It requires
a Gameplay fingerprint. Decoration changes do not alter gameplay identity;
gameplay changes invalidate the dependent decoration identity.

A WorldGenerationContent record writes X/Z int64, TypeId uint32,
SchemaVersion uint32, StableId uint64 and length-prefixed canonical payload.
Type and schema IDs must be nonzero. Payloads are copied on construction and
limited to 65,536 bytes each. At most 262,144 records are accepted, with the same
16 MiB total canonical limit. Collections are materialized within that count
bound, then sorted by signed Z, signed X, unsigned TypeId, unsigned StableId.
Duplicate keys fail even when their schema or payload differs. Schema version
belongs to content identity, not to the uniqueness key. No arrival-order tie
breaking is permitted. Canonical bytes returned to consumers are defensive copies.

Producer adapters own stable type/instance IDs, the complete versioned payload
schema and inclusion of all relevant output. The container does not infer terrain
truth from scenery or check payload semantics/coordinate extent. Repeated border
samples must be emitted once by their canonical owner. Future gameplay producers
include all authoritative generated contents in Gameplay; decoration producers
include their own immutable output in Decoration. Runtime display settings remain
outside both output recipes. Include source/rule identities in immutable profiles
or versioned payloads; never use an unversioned external file as an implicit input.

## Reference vectors and qualification

WorldGenerationTests fixes a symbolic three-record miniworld. Seed is ulong.MaxValue,
origin (-1,-1), size 2x2 chunks, 2000 mm chunks, three samples per side, one-chunk
regions, and profile IDs t/c/g/r. Records use type/schema 1, coordinates/IDs
(-2,-2)/10, (-1,0)/20 and (0,-1)/30. Each payload is the uint64 Terrain At value
at sample index 7 and stream ID 3. The canonical output is 208 bytes:
SHA-256 97a719d04077730ffb70fef6ccbcfd84cda5e7c4603adbd555c37c6dc5da8c12.
The test pins every byte as well as the digest; it is not a generated terrain map.

At (-2,-2), index 7, stream 3, that seed produces:
Terrain c6fbb8fd21370445; Climate 5ecf103b027ad788;
Geography 54e357d80194d562; Vegetation 98100e8c21cb65d5;
Props 8361b5582407d9b5.
Seed zero, Terrain at (0,0), index/stream zero yields 1002a55d1b2ef512.
Seed ulong.MaxValue, Props at (long.MinValue,long.MaxValue), index/stream
ulong.MaxValue yields b2a81e3a2261f47b.

The suite covers repeated and parallel permuted calls, separate domain/coordinate/
stream identities, shared edges, negative and extreme coordinates, unknown
versions/options, limits, schema identity, duplicate rejection, immutable copies,
decoration dependency, byte budgets and defined result failures.

On base 7716007a8a084578a865b8164f616b6ce58affae, the connector-derived source
snapshot with these changes passed the SDK 10.0.402 Release World test-project
build with zero warnings/errors and all 47 World tests, plus the unchanged
46-project reference validation. Full solution restore/build, authored-map and
save/replay regression, map/asset qualification and the natural terminal gate
remain the complete PR build-test job's responsibility. Local focused evidence
does not establish those gates or physical graphics acceptance.
