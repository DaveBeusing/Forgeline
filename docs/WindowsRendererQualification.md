# Manual Windows renderer qualification

Status: **execution and product acceptance pending**. This runbook wires existing
build, content, reference and package commands; it records no new native, WARP,
headless or manual success. Historical evidence applies only to its recorded SHA.
The integrated gameplay overlays require live interaction as well as authored UI
fixtures. See [renderer inventory](RendererFeatureInventory.md),
[overlay acceptance](GameplayOperationsWorldOverlays.md) and the existing
[paired performance collector](rendering/qualification/Qualification.md).

## Machine and source prerequisites

Use a Windows x64 desktop with an interactive physical D3D12 adapter, its current
vendor driver and enough display capability for the requested cells. Install Git,
PowerShell 7.4 or later and the exact SDK in `global.json` (currently 10.0.401,
C# 14, Microsoft.Testing.Platform). `dotnet --version` must match from the checkout.
The SDK restores native/shader dependencies; do not substitute a standalone HLSL
compiler or change shader/golden tolerances. Internet access to configured NuGet
feeds and space for source assets, Release outputs, a complete runtime cache,
captures and two reproducible publish directories are required. Keep the Windows
page file enabled. Record free space and OS/driver versions before a long run.

The checked-in source assets are inputs to AssetCompiler. Blender is required
only when reauthoring/re-exporting those inputs, not for ordinary build/publish
from existing sources. Missing authored inputs or compiler dependencies block
the Assets stage; do not replace them with placeholders. Windows Graphics Tools
and a working debug layer are required for debug-layer-specific checks; an
unavailable layer is a missing prerequisite, not a successful resource gate.

Start from fresh integrated `master`, or the exact clean PR HEAD under review:

```powershell
git fetch origin
git switch master
git pull --ff-only
git status --short
git rev-parse HEAD
dotnet --version
```

Do not pull, edit or switch commits during a run. After another rendering change
is integrated, rebuild and qualify that new HEAD in fresh evidence directories.
Do not combine captures from different SHAs, SDKs, runtime signatures or adapters.
Existing `build-test` CI must succeed at the actual candidate SHA; a previous
green run or a local Release build cannot replace it.

## Staged commands and evidence

From the repository root, choose a unique attempt name. Each stage requires a
fresh child directory in ignored `artifacts/`; existing attempts are refused.
**Plan is the default and executes no builds, tests or captures.** It writes the
exact argument arrays and script parameter files to `session.json` and creates
`manual-review.json` with every case marked `not-executed`.

```powershell
$attempt = 'artifacts/windows-renderer-20261023-a'
pwsh ./build/Invoke-WindowsRendererQualification.ps1 -OutputRoot "$attempt-plan"
pwsh ./build/Invoke-WindowsRendererQualification.ps1 -Stage Build -OutputRoot "$attempt-build"
pwsh ./build/Invoke-WindowsRendererQualification.ps1 -Stage Assets -OutputRoot "$attempt-assets" -BuildEvidenceRoot "$attempt-build"
$inputs = @{ BuildEvidenceRoot = "$attempt-build"; AssetEvidenceRoot = "$attempt-assets" }
./build/Invoke-WindowsRendererQualification.ps1 -Stage Contracts -OutputRoot "$attempt-contracts" @inputs
./build/Invoke-WindowsRendererQualification.ps1 -Stage Warp -OutputRoot "$attempt-warp" @inputs
./build/Invoke-WindowsRendererQualification.ps1 -Stage Native -OutputRoot "$attempt-native" @inputs
./build/Invoke-WindowsRendererQualification.ps1 -Stage Package -OutputRoot "$attempt-package" @inputs
```

Run only the explicitly chosen stage. Build restores, validates project references
and builds the full Release solution. Assets force-compiles the runtime cache
without rebuilding tools. Contracts invokes runtime pipeline contracts, the full
solution tests, headless smoke and the natural terminal match (seed 2026, 80,000
ticks, `--require-terminal`). Package invokes `Publish-PreAlpha.ps1` with
`-VerifyReproducible -RuntimeAssetRoot assets/runtime`. These remain the
[canonical development commands](Development.md); the wrapper does not replace
the complete CI workflow, window-mode checks or other required gates.

All execution stages require clean Windows x64 source and the pinned SDK. Later
stages verify completed Build evidence at the same SHA and hash both Client and
AssetCompiler output trees before using `--no-build`. Contracts/capture/package
also require completed Assets evidence and an unchanged runtime payload tree.
SDK, adapter inventory and before/after runtime manifest hashes are recorded.
Asset qualification/progress reports retain compiler version/content signature;
package `build-provenance.json` retains source, SDK and runtime provenance. An
observed old runtime manifest in a Plan is explicitly an unqualified cache.

Each child process has `commands/<id>.json`, original stdout/stderr byte logs,
exit code and timeout/competition observations. Default timeout is four hours
**per command**, adjustable through `-CommandTimeoutSeconds`; a timeout fails the
stage. Native commands use the existing competition monitor. Run no benchmark,
second client or GPU-heavy application concurrently. The summary remains
`acceptance: open`, even after all selected commands return zero. Review and
hardware acceptance require separate evidence; no wrapper stage grants them.

## Capture matrix and visible acceptance

Native and Warp both route through the real `Invoke-RtsReferenceQualification.ps1
-Capture` interface. Native omits `-Software`; Warp explicitly sets it. Both use
64 warmups, 512 samples and 8 repeated captures. The world matrix has 27 scenes,
four requested profiles, three zooms and four displays (1,296 cells per lane).
The authored UI matrix has 11 scenarios, four profiles, four displays and three
UI scales (528 cells per lane), fixed BattlefieldCombat/NormalGameplay camera as
required by the client. The session descriptor lists every selected name.

| Cases | Existing fixture coverage | Required review |
| --- | --- | --- |
| Terrain, formations, combat/logistics | DaylightTerrain, IndustrialBase, FormationConvoy, LogisticsCorridor, DamageDestruction, BattlefieldCombat | Grounding, silhouette, fog/visibility, sorting and continuity across zooms |
| Materials/shaders/effects | MaterialFoundation, EffectsBarrage, IndustrialEffects | PBR response, shadows, transparent order, tone mapping, no missing bindings |
| Animation | InfantrySkinning/Engineer and MechanicalIdle/Moving/Firing/Loading/Unpowered/Broken | Pose and authored state correctness; manually inspect moving playback for popping |
| Resource variants | ResourceDepositVariantsGrass/Rock/Mud, ResourceDepositVariantLoad1000 | Shared-instance variant identity, terrain grounding, variation, LOD and stress budgets |
| Environment and repeated assets | VegetationGrove, WorldPropsGallery, DecalsOverlap, InfrastructureBridgeStates, OperationalBuildingsRepeated, CombatSupportRepeated | Placement, overlap, material/LOD continuity and bounded submission |
| HUD fixture states | Operations/Empty/Missing, LogisticsBlocked, QueueBlocked, SelectionUnit/Building, SelectedGroup, RingEdge/Pending, NotificationsExpanded | Legibility, disabled/missing states, panel/radial clipping and native UI sharpness |

Requested profiles are Low/Medium/High/Ultra; displays are 1920x1080,
2560x1440, 3840x2160 and 3440x1440; zooms are CloseTactical,
NormalGameplay and Strategic. UI scales are .75/1/2. Compare requested **and
effective** settings, adapter identity, actual dimensions, render scale, raw
capture image/hash and frame sample distributions in each case's native manifest.
A display-limited cell remains unqualified at its requested size. WARP may cap
features/quality; it establishes software contracts only. Adapter inventory alone
does not identify the adapter actually used: inspect the capture manifest.
No headless result establishes graphical correctness or a physical GPU budget.

The plan is representative feature coverage, not an assertion that every asset
or animation clip is accepted. Family-specific scenes/checklists in the inventory
remain additional gates where an asset changed. These captures use static authored
snapshots. Operations row navigation, live transport observations, current
selection/session identity, conveyor visibility and click capture require the
LIVE-OPS, LIVE-CONVEYOR and LIVE-ORDERS cases in `manual-review.json` and
[overlay checklist](GameplayOperationsWorldOverlays.md). They are not silently
accepted by an Operations screenshot. Motion, Windows DPI, world resolution
scaling, idle/stress and lifecycle cases also remain manual.

For physical performance comparisons, retain the existing
`build/rendering/qualification/Invoke-RenderQualification.ps1` collector with
immutable baseline/candidate/analysis SHAs, packages and reviewed hardware
evidence. Its five repetitions, hardware admission, complete-frame/resource
gates and `--require-accepted` report are unchanged. This single-source runbook
does not establish a speedup or waive currently unavailable measurements.

## Package install and live desktop checks

In the Package output, `package/` contains `FORGELINE-<version>-win-x64.zip`,
its `.sha256` and `.provenance.json`; `package/win-x64/publish/` is the unpacked
publish tree. Verify the archive hash and source SHA, then extract into a **new
directory outside every checkout**, for example a uniquely named directory below
`$env:TEMP`. Do not copy `assets/source` into it. Check that no `assets/source`
directory exists and that `assets/runtime/manifest.json` matches package
provenance. Change to that install directory before launch:

```powershell
Get-FileHash -Algorithm SHA256 '<archive path>'
Expand-Archive -LiteralPath '<archive path>' -DestinationPath '<new external install directory>'
Set-Location '<external install directory>'
./ForgeLine.Client.exe --settings-root '<new isolated settings directory>' --startup-diagnostics-output '<external evidence directory>/startup.json'
```

Create the external evidence directory first. Use an isolated settings directory
for each install attempt; preserve personal settings. Observe the branded startup,
menu, new game, save/load, gameplay and normal close, then relaunch. Apply the
DISPLAY and LIFECYCLE matrix, recording each actual resolution/DPI/profile cell
separately. Include ReducedMotion, overlay overlap and changes of match/session.
Default startup logs are under `%LOCALAPPDATA%/FORGELINE/diagnostics/`; an explicit
startup output avoids mixing attempts. See [startup diagnostics](StartupDiagnostics.md)
for milestone meanings and in-process launch suffixes.

## Failure, recovery and sign-off

Stop on a nonzero exit, timeout, `failure.json`, binding failure, device removal,
stale/foreign marker, missing payload, capture mismatch or source/runtime change.
Retain the entire attempt, command journal, request JSON, images, manifests,
startup logs and relevant Windows event/device-removal details. Classify as build,
asset/compiler, runtime integration, hardware/driver, UI/overlay or environment;
record observed versus expected behavior and the responsible subsystem/issue.
Do not refresh goldens, lower budgets or retry into the same directory.

After fixing a source problem, commit, rebuild and start new stages for the new
SHA. After an environment failure, document the changed prerequisite and use a
new attempt. A failed or interrupted stage cannot supply prerequisite evidence.
Never count a skipped check as passed. If the monitor cannot support 4K/ultrawide,
the debug layer is missing, or physical D3D12 is unavailable, list the exact
blocked cells/prerequisite and leave native acceptance open.

Keep original `manual-review.json` as the template; copy it to a reviewed report
and expand DISPLAY/lifecycle rows into individual case IDs. Record source SHA,
operator/date, pass/fail/blocked status, adapter actually used, driver, requested
and effective dimensions/profile/UI scale/DPI, before/after counters, p50/p95/p99,
screenshots/video/log paths and issue references. Hash the final evidence bundle.
Sign-off requires exact-source successful full CI, Build/Assets/Contracts,
native images and motion review, physical resource/performance gates, all manual
cases, reproducible package and external install. Until then report tooling
complete and qualification pending, with failed/blocked cases individually listed.
