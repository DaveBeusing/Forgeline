# Fleet scheduling integration handoff

## Delivery and source

The prepared services from PR #360 are connected to the real Cargo/Supply kernel
introduced by PR #384. This is gameplay composition, not another preparation
branch. The product source is 48af1379ebd02008285636cf232836bddc5e01c6; the final documentation
successor does not change product source.

Work began at master 767e5ebc07e732ec47abfc38a4f0bf6f6a7855b2 and is synchronized with
f8f0e7f37fa98f2abef3d99d18da2d32c3a75115, preserving the intervening policy-template,
Operations documentation and bridge-combat/paid-repair work. The one PR PATCH is 0.1.495.
Authoritative schema 14, shared contract revision 4 and scheduler checkpoint
revision 2 are explicit compatibility changes. Envelope 1/payload 10 remain intact.

## Concrete ownership

- MatchRuntime passes its existing JobScheduler, content/seed identity and actual
  movement clearance to the navigation composition.
- HierarchicalNavigationSystem retains ECS intent and validates exact current
  ownership. It submits through the prepared scheduler and no longer runs its
  former ad hoc rotating continuation list.
- NavigationSearchScheduler reserves sessions, prepares disjoint grants, joins
  existing Jobs and publishes in deterministic binding order.
- VehicleFleetKernel owns the prepared workspace pool and route cache.
  Coordinator Prepare/Complete keep LRU and storage writes out of worker code.
- GroundMovement, precise service/FIFO, cargo/inventory and final Fuel authority
  remain with their existing owners.
- Runtime disposal releases fleet sessions and storage before owned Jobs.

The existing 4,096-component scan, 16-submission/admission, 128-retained,
8-active, 16,384-tick-work and 8-MiB/session bounds are documented in
[Connected scheduling](NavigationSearchScheduling.md). Regional publication is
not connected. Exact pose/scope/cost matching can limit cache reuse.

## Compiler and CI evidence boundary

Only product sources and necessary product dependencies were materialized from
the pinned repository for compiler checks; there are no test projects in that
compiler workspace. SDK 10.0.402, analyzers and warnings-as-errors remain enabled.
After merging the bridge work, its private action collection return type also
needed the concrete ReadOnlyCollection type required by CA1859. That correction
preserves behavior and analyzer enforcement. One concurrent compiler attempt
encountered a locked output DLL; the final variants were checked individually.
Normal NuGet restore first encountered NU1900 because the audit endpoint was
unreachable inside the restricted environment. A normal restore with network
access then succeeded; audit was not disabled or downgraded.

An intermediate Game compile identified the missing IDisposable contract on the
navigation owner. The lifetime contract was corrected, and Game/Navigation then
compiled with zero warnings/errors. The final Public Client Compile target,
including Game, Presentation, UI and other required product dependencies,
completed with zero warnings/errors on the synchronized source.

Both Public and Developer Client Compile targets completed with zero warnings
and zero errors on this source. An intermediate diagnostic edit also exposed an
incorrect session-property spelling, corrected before these passing compiles.
Final automatic PR checks are reported separately in the PR.
The compiler target is not a client launch, package, native interaction,
Save/Replay execution or functional acceptance.

No tests were created, modified, built or manually executed. No smoke,
benchmark, headless/native qualification, workflow dispatch or rerun was started.
Existing automatically triggered CI remains unchanged. Read-only baseline CI at
master 767e5ebc failed on ReadOnlySpan<CongestionMemoryEntry> serialization in
Developer qualification and recovery selections; other Game progression findings
also remain in its logs. Explicit checkpoint DTO projection addresses that
specific metadata boundary. A passing new functional or overall CI result is
not inferred from source inspection or compiler success.

## Manual acceptance and status

Manual user approval remains outstanding. Exercise cold/warm storage, more than
8 active and 128 retained requests, continuing arrivals and age rotation,
identical replacements and cancellation, entity/owner/service changes, global
geometry and independent cost epochs, shutdown, cache expiry/deferrals and
Save/Load/Replay with queued and active work. Confirm current owned counters
without exposing other players' activity. Natural terminal completion and
performance improvement are unverified.

Status: implemented; manual user approval and current-head CI assessment pending.
No merge, tag, release or deployment is included.

Final product compiler commands (Release, SDK 10.0.402):

```powershell
dotnet build src/ForgeLine.Client/ForgeLine.Client.csproj -c Release --no-restore -p:ForgeLineEnableDeveloperTools=false --nologo -m:1 -nr:false -t:Compile
dotnet build src/ForgeLine.Client/ForgeLine.Client.csproj -c Release --no-restore -p:ForgeLineEnableDeveloperTools=true --nologo -m:1 -nr:false -t:Compile
```

Public: 0 warnings / 0 errors (41.78 s). Developer: 0 warnings / 0 errors
(52.93 s). These durations describe compilation, not gameplay performance.
