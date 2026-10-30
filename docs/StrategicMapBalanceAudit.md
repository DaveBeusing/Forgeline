# Strategic map balance audit

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The offline audit extends existing compiled-map qualification. It reads authored
geometry and the registered, digest-checked artifacts; it never starts a client,
allocates a match, advances ticks or changes navigation, resources or rules.

## Run

After the canonical Release build:

```powershell
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --audit-all --qualification-output artifacts/strategic-map-audit.json --preview-directory artifacts/map-audit-previews
./build/Test-StrategicMapAudit.ps1
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.StrategicMapAuditTests --parallel none
```

The command enumerates every descriptor in ScenarioRegistry, resolves its exact
map/sidecar/content/rule identity, and writes JSON plus a Markdown summary.
Optional SVG previews add the map key, revision, verdict and labelled baseline
problem locations to the existing terrain/geography preview. They are developer
artifacts, not player intelligence or an in-game overlay. Hover a problem symbol
for its diagnostic ID and cause. Full crossing-state failures remain in JSON.

The ordinary Central Divide and Split Basin compiler commands also append
StrategicBalance to their existing qualification JSON, write a companion Markdown
summary and return failure for hard audit errors. Existing compile/load,
operational geography and digest gates remain intact. The early CI audit runs
the new regression class and the same command-line qualification script.

## Reproduction identity and schema 1

JSON records scenario key, map SHA-256, content SHA-256, terrain recipe version,
the SHA-256 of all little-endian height samples in chunk/Z/X order, map revision,
audit schema, rules, grid settings and the complete ordered observations.
There is no timestamp, host path, cache-hit counter or wall-clock timing in the
report. Both current maps use deterministic authored terrain recipes; geometry
consumes no random stream or match seed. A match seed is therefore not an audit
input. A future seeded map must be compiled and identified before auditing.

The report contains all starts, Infantry/Wheeled/Tracked, all resource approaches,
all strategic sites and opposing starts. It measures the baseline, every single
crossing loss and all crossings lost, preserving initially unavailable crossings.
Declarations are sorted by stable IDs/ordinal keys. Distances come from the
existing HierarchicalPathfinder on the actual NavigationWorld, using 16 m cells,
8-cell sectors and 0.5 m obstacle clearance. Endpoint projection is disabled.
The fallback search bound is the grid cell count; query exhaustion aborts with
no completed verdict. SearchBudgetExhausted is explicitly incomplete.

A bounded four-neighbor flood uses NavigationGrid.IsTraversable for connected
components and reachable yard samples. These components match the existing
eight-neighbor topology with diagonal corner cutting prohibited. It does not
replace route measurement. Resource approaches reuse the existing geography
radius (largest horizontal deposit half extent plus 128 m), selecting the
nearest grid center in the start's connected component, with stable Z/X ties.
The report measures the route to that approach, not extractor placement,
loading, finite vehicle turning geometry or final docking feasibility.

Opening rows choose the nearest reachable non-contested deposit of each
configured vital type. Defaults are Ore, Volatiles and Silicates; caller-supplied
criteria can support other factions without map-name branches. Rows preserve
quantity and extraction rate. Expansion choices record reachable contested
deposit count and nearest route by resource type. Site comparisons use the
nearest reachable site of the same kind. This compares equivalent choices
rather than treating every side-specific mirrored site as a symmetry violation.
The complete per-target matrix remains available for deliberate contestability
review.

Yard area counts reachable 16 m cells whose centers admit a 2 m footprint through
BattlefieldBuildableAreaQuery inside the owning start yard, multiplied by 256 m².
It excludes shared sites from the opening comparison. This is a sampled
permission/access metric, not a guarantee that a full building fits, meets its
slope/collision/dock policy or has construction materials.

Road capacity is the maximum, over existing bidirectional road paths, of the
minimum authored CapacityPerSecond along each path. Crossings disable their
actual referenced road edge. End nodes are the nearest authored road nodes to
the corresponding starts, with ordinal tie breaking; their IDs are reported.
This is a widest-path bottleneck, not aggregate max flow, delivered material,
vehicle throughput or proof of navigable road approaches. Missing/same-node
anchors yield zero and a RoadCut warning. In both current maps, the 160/s bridge
is limited to 120/s by its connecting road edges; its loss leaves 90/s over the
ford, and losing both crossings yields zero.

## Criteria and exceptions

Hard errors: blocked baseline spawn (no projection), disconnected baseline
starts, absent/unreachable vital non-contested reserve within 450 m of measured
route distance, zero reachable yard samples, or incomplete path search.
Malformed geometry/rules, cancellation, query-budget exhaustion, invalid
scenario identity and output failures terminate the command without success.

Warnings: inaccessible baseline sites/deposits, absent contested choices,
road cuts or disconnected starts under disruption, and an across-start ratio
greater than 1.25 for local route distance/quantity/extraction/yard area or
equivalent expansion/site choices. Combined crossing losses are deliberately
reported even when the cut is expected. Their complete per-target loss matrix
remains in Routes without generating a warning for every severed target.

Verdicts are Invalid, ReviewRequired or NoThresholdViolation. None means
balanced, fair, symmetric, an arrival-time advantage or a win-rate guarantee.
Warnings do not fail the process; hard errors do. The fixed rules are recorded
in every result. No stock, map content, runtime pathfinding or acceptance
threshold was changed to produce these results.

Deliberate asymmetry can be recorded with --exceptions path/to/exceptions.json:

```json
{
  "battlefield.split_basin": {
    "RoadCut|AllCrossingsClosed|Road|1|start:2": "Both crossings deliberately sever the basin road network."
  }
}
```

Each exact current warning ID needs a non-empty rationale of at most 1,024
characters. The original severity, message and ID remain in the report with
AcceptedReason; counts distinguish unaccepted and accepted warnings. Hard
errors, unknown/stale IDs, unknown map keys, duplicates and oversized files
are rejected. The input is bounded to 64 KiB. No current map warnings are
accepted automatically; review and rationale are explicit.

## Qualification

The focused suite covers both deployed compiled maps, byte-equivalent repeated
reports, declaration ordering, blocked spawn, unreachable vital deposits,
one-sided quantity/extraction, bridge detours and end-to-end bottlenecks,
retained warning exceptions, non-waivable errors, cancellation and budgets.
The CLI script checks repeat JSON/Markdown hashes, registry coverage, explicit
exceptions, stale/duplicate rejection, invalid arguments and failed output.

See [executed qualification](StrategicMapAuditQualification.md) for exact source,
results and remaining gates. Existing scenario, navigation, save/replay,
package and natural terminal gates remain required.
