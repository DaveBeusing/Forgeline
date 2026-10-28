# Operational alert qualification

Source/test commit: f60caac6343e2b9da2eb1b6d9d10f1d311212f6d. Base master:
201af6aad7ace07df21608c1d058d23b0828b070. Version 0.1.504-dev, one PATCH per PR.
Subsequent documentation/evidence changes do not alter these product sources.

## Executed checks

- Complete Release solution build, pinned SDK 10.0.402: zero warnings/errors.
- Alert, notification, fleet-shortage, cause, camera-focus and transient-input cases: 98 passed.
- Settings, frontend, binding and native operational-alert cases: 119 passed.
- Complete Input module: 25 passed.
- Total selected cases: 242 passed, zero failed/skipped. Earlier narrower runs are subsets.
- Project reference graph: 46 projects, no cycles or architecture violations.
- Existing alert/map benchmark: all 56 cases retain zero warm HUD/input allocation.

Lifecycle tests cover 5,000 captures with concurrent categories, changed causes, tick-based
recovery/cooldown, critical recurrence, actual current damage events, ignored foreign/stale/
nonfinite events, completion baseline/batching, session reset, priority/overflow, short-tap
cycling behind MORE, retained Ctrl/Shift, blocked input and older snapshots. Existing focus
checks preserve ownership, full entity generation and current authorized visibility.

Settings tests retain current schema 4, old-file defaults, invalid preference validation,
Apply persistence, unsaved draft isolation, live application and unrelated preferences.
Two old expectations were reproduced without the alert changes: a hard-coded schema 3
and Reduced Motion as the final General row. The migration assertion now requires the
actual current schema plus default notification preferences; traversal expects the new
Informational Alerts row. Product graphics migration behavior is unchanged.

## Native UI compatibility

D3D12/WARP (Microsoft Basic Render Driver, feature level 12_1, debug layer unavailable)
rendered a concurrent core-threat/supply/industry fixture at 1280×720 and raster DPI 96.
All-mode repeat readbacks match exactly. Quiet-mode output keeps the complete first critical
row pixel-identical while removing lower-priority rows and retaining bounded overflow.
Both outputs have zero missing/dropped glyphs and skin assets/quads. Captures All.png and
Quiet.png and their source/assembly/image identities are retained locally; they are not
remote screenshot references or approved terrain goldens.

The unchanged compatible cache has 1,363 assets, compiler version 1.12.0 and signature
b1ddf984833ad10ef043b12bdd90a0e0730eda199f322548c5419b10eac2f480.
It was copied for isolated UI compatibility checks. This is not fresh compilation of the
current production cache or package, a physical-GPU result, or a full gameplay playthrough.
The fixture supplies completed-tick read-model facts; it does not claim a full combat match.

## Hot-path evidence

[Complete CPU/null-graphics results](qualification/operational-alerts/hotpaths.json) use
128 warmups and 256 samples per case across 1/10/100/1000 entities, seven map layers and
both action-menu states. DOTNET_TieredCompilation=0 and DOTNET_gcConcurrent=0 are process-local,
as required by the existing benchmark host. The first attempt omitted the latter and
correctly refused qualification; the guard and zero-byte assertion were not relaxed.

These measurements include full HUD/minimap rebuilding with active alerts and idle alert
input. They exclude extraction, world overlays, GPU transfers, waits and Present. CPU
percentiles are observations on a shared workstation, not a speedup, GPU or FPS guarantee.
Dense lifecycle storage is bounded independently by the eight-record contract tests.

## Reproduction and remaining gates

Use the normal restore and Release solution build. Run the Presentation class filters
Alert*, Notification*, CargoFleet*, OperationsCause*, CameraFocus* and TransientHud*;
Client filters NotificationPreference*, OperationalAlertNative*, Settings*, Frontend* and
GameplayBinding*; and the complete Input project. Use Microsoft.Testing.Platform with
--parallel none for the selected suites. Runtime-dependent cases require a qualified
assets/runtime catalog. FORGELINE_OPERATIONAL_ALERT_OUTPUT selects a fresh capture directory;
existing PNG output is never overwritten.

Run the Rendering benchmark host with --alert-map-hotpaths and both required process
options above. No benchmark source, visual golden, allocation threshold, timeout, branch
protection or simulation terminal gate was weakened.

Required final-head CI, fresh current asset/package qualification and human bright/dark
terrain, live mouse/keyboard/focus, physical display/DPI and save/load journeys remain open.
Baseline master CI 40597373272 already fails its technology/gameplay step; logistics-navigation
qualification also fails. These baseline observations are not feature-head acceptance.
Keep the PR draft until required checks and remaining acceptance are resolved. No merge,
auto-merge, tag or release is requested.
