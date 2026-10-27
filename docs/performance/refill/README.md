# Automatic refill CPU investigation (#402)

The matrix uses 16/64 owned destination depots, three materials per depot,
4/16 explicit sources and finite physical cargo trucks. Open cases use four
trucks; the large blocked cases use sixteen. Seed 403 and the registered
Validation composition remain fixed. Sources, policies and fleet are explicitly
arranged before the first tick; ordinary dispatch, loading, routing and unloading
run afterward. Open cases must observe a real arrival. No-surplus, no-truck,
empty-drive-tank, disconnected and 1-unit route-window cohorts are retained.

Run on Windows with the repository .NET SDK:
```powershell
dotnet run --project benchmarks/ForgeLine.Logistics.Benchmarks -c Release -- report.json 128
```

Each cohort warms eight operations then records 128 complete ticks. UI action
extraction is measured separately at the completed tick, including construction,
technology and selected logistics facts. It allocates copied observations; it is
not a renderer or GPU measurement. Setup, reporting and the extra open-arrival
check are outside the timed window. Raw reports contain sample distributions,
GC/memory windows, request age/failure counters, delivered quantities, environment
and measured assembly SHA-256 identities.

Sources: base PR head 8a518d9ff67925d3999b66304c9c63831035570a plus the staged
#397–#401 corrections and the identical measurement harness. The pre-index and
final-index assembly hashes differ; the harness hash is unchanged. This is an
explicit staged-source comparison, not a claim that those assemblies are an
unmodified master build. The reports retain their original version metadata.
Host: .NET 10.0.13, Windows x64, 24 logical processors (Intel family 6/model 198).
Clock/power and target-hardware qualification were not supplied.

The first index-only trial reduced CPU and UI allocation but increased tick
allocation. The final change also captures stable graph nodes/truck identities
once per dispatch phase, builds retention only for actual source inventories and
skips the phase scratch when no request is eligible. Tables/lists are phase-local
and proportional to live nodes, trucks and at most four priority entries per
applicable policy. No high-water cache, lifecycle invalidation queue, stale
simulation authority or truncated demand list is retained. Eligibility and Fuel
are still checked live per route; successful assignments remain immediately
visible to subsequent candidates. Stable ordering and tie breaking are unchanged.

| Depots | Case | Tick p95 ms, before → after | Bytes/tick, before → after | Bytes/UI extraction, before → after |
|---:|---|---:|---:|---:|
| 16 | open | 2.858 → 1.461 | 62296 → 56131 | 49634 → 26282 |
| 16 | no-surplus | 2.143 → 1.062 | 59437 → 52419 | 49722 → 26370 |
| 16 | no-truck | 2.942 → 0.997 | 61723 → 54700 | 49722 → 26370 |
| 16 | no-fuel | 4.450 → 1.998 | 62987 → 56750 | 49722 → 26370 |
| 16 | disconnected | 3.328 → 1.138 | 51077 → 44059 | 49722 → 26370 |
| 16 | congested | 3.669 → 1.633 | 63324 → 56723 | 49722 → 26370 |
| 64 | open | 13.722 → 11.199 | 619469 → 584126 | 118842 → 26370 |
| 64 | no-surplus | 5.894 → 1.490 | 441424 → 440848 | 118842 → 26370 |
| 64 | no-truck | 9.470 → 1.771 | 574751 → 490839 | 118842 → 26370 |
| 64 | no-fuel | 15.434 → 3.669 | 580474 → 509200 | 118842 → 26370 |
| 64 | disconnected | 6.946 → 1.373 | 307538 → 223656 | 118842 → 26370 |
| 64 | congested | 10.572 → 2.776 | 573032 → 501490 | 118842 → 26370 |

All twelve final authoritative checkpoint hashes match their baseline counterparts,
including normal real delivered quantities, request histories and physical waits.
The large open case has zero arrival within its 136 measured ticks and reaches a
real 50-unit arrival by tick 188 in both cohorts; that extra check is not included
in timing. The narrow route case moves real 1-unit loads. The empty-tank case can
transition to the existing fuel-service ownership and report NoTruckAvailable;
its name describes the arranged empty physical drive tanks, not a substituted
diagnostic expectation.

Timing is observational: two baseline runs show variance, and the full tick still
includes navigation/other simulation systems. No 20-Hz, frame-rate or native
graphics qualification is inferred. Remaining allocations and route-search work
are visible in the raw reports. No diagnostic or terminal-match gate is weakened.

- [Baseline](refill-before.json)
- [Second baseline (same assembly identities)](refill-before-repeat.json)
- [Intermediate index-only trial](refill-after-index-only.json)
- [Final phase views](refill-after.json)
