# Native UI review handbook

Use the existing [reference evidence lane](VisualReferenceEvidence.md). Automated
structure, GPU performance and human art/accessibility decisions are separate.
No matching checksum grants typography, AAA quality or physical-display acceptance.

## Reproduce a candidate

Commit and build the revision, compile/qualify canonical runtime assets, and use
a new output root. Run these commands in PowerShell from the repository root:

```powershell
dotnet build ForgeLine.sln --configuration Release
./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes BattlefieldCombat -Profiles Medium -Zooms NormalGameplay -Displays 1920x1080 -RasterDpi 96 -UiScales 1 -UiScenarios Menu,MenuFocus,MenuDisabled,Loading,LoadingError,NewGame,LoadGame,LoadEmpty,LoadBroken,SettingsLong,ControlsLong,Pause,Credits -CinematicFrontend -OutputRoot artifacts/ui-frontend
./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes BattlefieldCombat -Profiles Medium -Zooms NormalGameplay -Displays 1280x720,1920x1080,2560x1440,3440x1440,3840x2160 -RasterDpi 192 -UiScales .75,1,2 -UiScenarios ResourcePressure,LowPower,SupplyCritical,MixedSelection,Targeting,QueueBlocked,Technology,Contacts,DockFocus,DockHover,DockPressed,DockPending,DockAccepted,DockRejected,MinimapStrategic -OutputRoot artifacts/ui-hud-dpi192
```

Repeat at DPI 96 and 144 with distinct output roots. Run frontend states at the same
five displays, DPI 96/144/192 and scales .75/1/2 for complete coverage. `MenuReveal`
adds a frozen decorative half-reveal. Use the existing UI feedback capture for
injected-time frame sequences and inspect live motion separately. Reduced-motion,
Low/software and lifecycle tests remain required. Without `-CinematicFrontend`,
the production schematic fallback is intentional. Requested cinematic art must
become Ready or capture fails; missing required runtime assets reject capture.

The default World request and original fixture files remain unchanged. Named
states are authored copied facts, not natural matches. Contacts retains the existing
identified/hidden enemy fixture and extractor boundary. MinimapStrategic changes
the actual camera to strategic zoom. Pressure/low-power/supply cases intentionally
combine warnings to inspect priority/density without changing simulation rules.

## Compare and review

```powershell
./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot artifacts/after -BaselineRoot artifacts/before -ComparePixels -ReportPath artifacts/before-after.json
./build/Export-VisualReferenceReview.ps1 -EvidenceRoot artifacts/after -OutputPath artifacts/review.pending.json
```

The report records source/runtime/device comparability, per-region changed hashes,
CPU/GPU observations and exact pixel deltas with `-ComparePixels` or the gate. Cross-source
pixel deltas are diagnostic and never inherit baseline acceptance. No native image is resized,
normalized or recolored. Nonuniform region pixels do not establish legibility.

The exporter creates an entirely **pending** review. A human inspects every exact
PNG at 1:1 pixel zoom and fills a review copy with reviewer, UTC decision time,
change rationale, per-image decision, notes and every rubric decision. Record the
reviewer and decision through ordinary repository review. JSON is an attributable
record, not a cryptographic identity service. Automation must not complete real
acceptance decisions. Keep the manifest, native images, review and settings with
their checksums in retained artifacts. Never edit immutable capture metadata to
set human acceptance or overwrite an approved image set.

| Rubric field | Inspect at native size |
| --- | --- |
| typography | Antialiasing, glyph edges/kerning, ellipses, font sizes, ÄÖÜ/ß and long labels. Unsupported scripts are coverage gaps. |
| hierarchy | Primary task, resource totals, details and decoration remain distinct. |
| contrast | Bright/dark terrain and cinematic/fallback content; shape plus text under protan/deutan/tritan and grayscale inspection. Record viewing method separately from original evidence. |
| focus | Keyboard-only focus is discoverable; disabled reasons and all scrollable actions are reachable. |
| hitAlignment | Click centers and edges; visible regions agree with input after scale/DPI transitions. |
| feedbackSeverity | Pending intent is neutral; copied acceptance/rejection and critical warnings have explicit labels/shapes. |
| occlusion | No unintended clipping/overlap; alerts, actions, pause context and minimap remain readable. |
| intelligence | Hidden enemy facts stay absent; contacts and minimap zoom respect permitted knowledge. |

An accepted new golden requires exact image hashes/manifest, change rationale and
reviewer decision. A changed manifest needs a fresh review. Missing/pending/rejected
reviews fail `-GateImages` even when native pixels match:

```powershell
./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot artifacts/repeated -BaselineRoot artifacts/after -GateImages -ApprovalPath artifacts/review.accepted.json -ReportPath artifacts/repeated-review.json
```

The gate requires equal source/build/runtime, fixture, adapter/driver, output,
camera, DPI/scale, effective quality and lighting. Later revisions can produce
before/after reports but cannot inherit old-source acceptance automatically: review
the new candidate and preserve both chains. Defaults are exact pixels. Legacy
explicit tolerance flags are diagnostic and cannot bypass review/identity checks.

## Hardware and accessibility session

Inspect physical 1080p, 1440p and 4K displays at recorded OS DPI, plus ultrawide and
compact layouts. Requested raster DPI is a layout input; host-window DPI is recorded
separately. A small host with a 4K backbuffer does not qualify a physical 4K monitor.
Record monitor, refresh, OS scaling, adapter/driver, preset and source/runtime hashes.

Walk menu/new/load/settings/controls/pause/credits by keyboard, then pointer. Include
broken/empty saves, text expansion, disabled actions, loading failure, missing art,
reduced motion, sound off/on, focus loss, minimize/restore, resizing and monitor
transitions. Inspect populated RTS backgrounds, queue blockage, technology, contacts
and every dock result. Test below 30 FPS and long stalls. Capture readback interrupts
pacing and cannot qualify live motion or listening quality.

Collect warm CPU p50/p95/p99, completed-fence GPU samples, allocations, vertices,
upload bytes and resource faults without competing workloads. Frontend samples
measure complete clear/UI frames; HUD samples include frozen world and terrain
preparation. They are not isolated UI shader costs or simulation time. Use matched
workloads and existing performance/full-match benchmarks for budget decisions;
short WARP/native observations do not prove sustained 60/144 Hz.

CI runs a small software frontend subset plus functional geometry/input/identity
tests. It cannot grant hardware/art acceptance. Native resource tests, packaged
startup and world smoke remain independent. Review fields `physicalMonitor`,
`liveMotion` and `gpuPerformance` start pending and require their own evidence;
the static image gate never approves these scopes.
