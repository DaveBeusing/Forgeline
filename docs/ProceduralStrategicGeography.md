# Procedural strategic geography

`ProceduralStrategicLayoutGenerator.Generate` is an offline, headless Game API.
It consumes a completed `TerrainWorld` and the World generation recipe; footprint
admission uses `ITerrainQuery`. It returns a complete qualified layout or ordered
attempt diagnostics, with no partial battlefield on failure. Cancellation throws.
It does not register a scenario, compile terrain, instantiate scenery, change
match rules, or replace either authored battlefield.

## Version 1 profile

Use `geography.strategic.two-player.v1` and
`rules.directorate.strategic.v1`. Terrain/climate profile IDs identify the supplied
producer's terrain; this stage does not resolve or generate their contents.
The complete heightfields are independently bound into output identity.
The recipe must match chunk dimensions, sample count and zero origin exactly.
The existing validators require a zero-origin rectangle. This profile supports
2048..4096 meters per axis and chunks divisible by the 16 m navigation lattice.
Unsupported extents, profiles, versions and options return InvalidInput before
navigation allocation. These limits do not alter the broader World contracts.

Two starts receive 256 m square yards and Command Core positions inside them.
Each side receives finite local Ferrous Ore, Volatiles and Silicates, using the
current Central Divide opening quantities/rates. Six richer contested reserves
and paired Expansion, MiningOutpost and ForwardOperatingBase sites provide
expansion choices. Placement samples every 16 m across the complete reserved
footprint, requiring available finite heights and normals at most six degrees
from vertical. This is a placement admission policy; normal construction rules
still validate each actual building, footprint, material cost and access.

Candidate centers lie on navigation cell centers. Each target examines exactly
289 or fewer offsets within +/-128 m, ordered by squared distance, then Z, then X.
Previously accepted footprints cannot overlap. The Geography domain, stream 1,
indexed by attempt number determines a shared opening shift in 16 m increments.
No shared mutable random sequence or simulation state participates.
At most eight complete candidates are attempted; callers may request 1..8.
Diagnostics identify placement, route, corridor, topology or qualification
rejection. Every retry starts a new candidate without modifying the terrain,
rules, validator thresholds or an already produced layout.

## Navigation and logistics

Deposits contribute conservative 44 m half-extent navigation obstacles. All road
segments are adjacent refined cells returned by the existing HierarchicalPathfinder;
endpoint projection is disabled. All three existing movement classes must reach
the road endpoints. The retained road geometry uses the Wheeled route. Shared
cells have one keyed node, so intersections are actual graph connections rather
than geometric crossings of unrelated long edges. The bounded graph has at most
2048 nodes and 8192 edges, with fixed 120/s authored capacity. This is a topology
capacity, not measured delivered throughput or vehicle turning qualification.

Northern and southern site routes connect both starts. Their selected central
edges must be distinct and separated by at least one quarter of the map height.
For each, the generator removes the real edge and checks graph connectivity,
then blocks its source cell and requires an ordinary alternate navigation route
for Infantry, Wheeled and Tracked. Site access joins the same graph. This stage
does not infer bridges/fords from arbitrary heightfields or create restorable
strategic infrastructure without a terrain-authored passage contract.

Every candidate runs the unchanged BattlefieldValidator,
BattlefieldOperationalGeographyValidator and StrategicMapAudit. Existing elevation
and restricted-build-geography checks remain required; completely flat terrain
is therefore rejected. Audit hard errors and warnings both reject this profile's
candidate; no warning exceptions are supplied. The successful report still means
NoThresholdViolation, not measured competitive balance or a win-rate guarantee.

## Reservations and identity

The immutable 16 m raster covers the entire world, in Z/X order. Flags may combine:
Construction for start/site yards and existing 114 m mining zones; Resource for
those mining zones; Traffic for every road cell plus one-cell clearance; Obstacle
for unavailable, steep or statically blocked Wheeled cells. Bounds overlap is
conservative. `IsReserved` checks every touched cell and excludes out-of-world
footprints. None is available for subsequent scatter, subject to its own terrain
and placement policy. No renderer or visual instance is created.

Payload type 0x534c0001, schema 1, uses exact finite IEEE-754 bit patterns as
little-endian integers. The ordered payload includes map revision/dimensions,
starts/core positions/yards, resources including rates/quantities, sites, sorted
road nodes/edges including costs/capacities, obstacles and the reservation raster.
Objectives are derived exactly from starts; empty world-object/crossing arrays
and terrain visual bindings are fixed by the immutable geography profile.
An appended SHA-256 binds all supplied terrain height samples, including duplicated
chunk borders, in sorted chunk Z/X, then local Z/X order. Each chunk prefixes its
signed coordinates and sample count. The recipe binds chunk size. Payload blocks
are at most 65536 bytes, with sequential stable IDs, wrapped by the existing
World gameplay fingerprint. This identity covers this stage plus its terrain,
not a future full compiler output or registered map artifact. Decoration remains
outside gameplay identity. Floating-point evaluation still requires separate
cross-platform qualification.

Returned BattlefieldDefinitions are defensive copies. Caller mutation cannot
change the retained layout or its fingerprint. Existing runtime artifact formats,
terrain recipes, registries, savegames and replays are unchanged. Map-compiler
integration with a procedural landform producer remains a separate boundary.

## Reproduction

```powershell
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.ProceduralStrategicLayoutTests --parallel none
```

The suite covers several seeds, repeat canonical bytes, terrain-bound identity,
defensive copies, real road-cell geometry, build/resource reservations, unknown
profiles and mismatched extents, bounded flat-terrain rejection, a dividing wall,
unusable slopes and cancellation. Authored map/audit, navigation parity and
save/replay regression remain separate unchanged tests. Full required build-test,
normal terminal-match qualification and native graphics acceptance remain
separate from these headless layout checks.
