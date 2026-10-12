# Windows application branding

The canonical FORGELINE application mark is
`assets/source/branding/forgeline/application/forgeline.ico`. It was integrated by
PR #133; the directory repair in PR #137 preserved its original image payloads.
The current valid file contains 16, 32 and 48 px PNG frames. This change reuses
that existing product mark unchanged. The studio cinematic and HUD/faction marks
are separate assets and are not sources for this application icon.

## Executable and native window contract

`ForgeLine.Client.csproj` embeds the canonical ICO through `ApplicationIcon`.
The Windows apphost therefore carries the same icon as the client assembly.
`ForgeLine.Platform.Windows` embeds the same file as a managed resource named
`ForgeLine.ApplicationIcon`; no runtime file, source checkout, asset manifest,
image decoder package or D3D12 device is needed to set a window icon.

Each WindowsWindow assigns ICON_SMALL and ICON_BIG with WM_SETICON before its
first ShowWindow. Native system metrics for the window DPI determine the requested
sizes. The smallest sufficiently large frame is preferred; if the source is
smaller, the native loader uses the largest available frame. This fallback does
not provide additional authored high-resolution detail.

The bootstrap, menu, gameplay and restart windows all use the existing platform
window abstraction. Bootstrap-to-menu handoff retains the same HWND and icons.
A DPI transition creates a replacement pair, assigns both handles, then releases
the previous pair. Each window owns its handles through SafeHandle. WM_NCDESTROY,
normal disposal and failed construction release them; the class registration
never keeps an icon handle alive. Immutable source bytes are cached once.
No new top-level window, shell process or taskbar grouping identity is introduced.
Headless composition and simulation do not acquire icon or window dependencies.

## Build and packaged qualification

```powershell
pwsh ./build/Test-WindowsApplicationIcon.ps1 -ClientPath src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64/ForgeLine.Client.exe
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
pwsh ./build/Test-WindowsApplicationIcon.ps1 -ClientPath artifacts/prealpha/win-x64/publish/ForgeLine.Client.exe
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/prealpha/win-x64/publish/ForgeLine.Client.exe -Samples 2 -RequireApplicationIcons
```

Publish-PreAlpha runs the resource assertion for every generated publication tree,
so the existing CI reproducible-package gate also checks branding. It maps the
published EXE only as data, requires a valid RT_GROUP_ICON directory and checks
each RT_ICON payload against the canonical frame by SHA-256. Missing/truncated
source frames, missing resources, changed pixels and mismatched frame metadata
fail qualification. A standalone ICO in the output directory is not proof of
resource embedding and is not required at runtime.

Native integration tests query both actual HWND icons, verify their bitmap sizes
at 96/144/192/288 DPI, retain handles through handoff and mode switches, and check
that replaced and closed-window handles no longer resolve. Repeated creation and
closing exercises ownership without accumulating icons.

The existing packaged startup qualification additionally queries both WM_GETICON
values while the process runs. RequireApplicationIcons requires at least one valid
observation per launch and rejects HWND replacement. Probes have bounded native
message timeouts; unanswered probes are counted separately and retried, rather
than being interpreted as missing artwork. Ordinary process closure is tolerated without disguising a
launch with no successful observation. Its interactive-menu and native-first-frame
gates remain in force. This is also enabled in the existing CI menu step.

## Artwork updates

There is no larger source artwork or editable master for this FG mark in the
repository. The existing ICO is retained as the application identity for this change. Additional
24/64/128/256 px authored variants require a freely usable, approved larger or vector source. Do not stretch the 48 px image
and claim new high-resolution artwork, invent a replacement logo, or infer usage
rights from a catalog placeholder. Keep the existing mark until a source is
supplied and its product approval and usage provenance are recorded.

When that source is available, retain it beside the canonical ICO with approval
and licensing provenance. Rasterize independently at 16, 24, 32, 48, 64, 128 and
256 px, inspect 16/32 px legibility on light/dark Windows surfaces, and assemble
an ICO directory containing complete RGBA/PNG frames with actual lengths and
offsets. Preserve aspect ratio and transparency. Rebuild both client and platform
and rerun resource/native/package assertions. The current native loader and PE
assertion accept the extended frame set without new gameplay changes.

## Manual Windows acceptance and cache diagnosis

Use a freshly published package copied outside the source checkout. Check Explorer
file properties and executable icon, an unpinned launch, the initial native
bootstrap, studio/menu/gameplay, Alt+Tab, title-bar icon and taskbar. Close the app,
pin and relaunch the current executable; confirm the shortcut target and icon
location refer to that installation. Exercise mixed-DPI monitor movement and
minimize/restore. Record Windows version, monitor scaling and installation path.
These shell interactions are manual acceptance and are not established by a
passing binary resource assertion.

If the EXE assertion and native WM_GETICON checks pass but an older shortcut or
Explorer view still shows a stale icon, first verify its target and icon location,
unpin/re-pin that shortcut and reopen Explorer. Do not delete user caches,
shortcuts or restart Explorer automatically. Cache staleness does not prove that
the new binary lacks its resource. Final high-DPI art and manual pin/unpin/shell
acceptance remain distinct from the verified native assignment.
## Recorded qualification

Validated on 2026-10-12 from master `36a45381f3237dac94223f338c69c3390833fb71`.
Version advances once from 0.1.345 to 0.1.346. The canonical ICO is unchanged:
SHA-256 `A6E3EFF1A7DCD861FC2E2BE2AA899BCEFAD8713F6F6B91AD7264554635D2E329`.

- Release solution build: zero warnings/errors; 42-project reference graph passed.
- Full serial Release suite: 1741 tests, 1732 passed, 9 optional skips, no failures;
  duration 6m02.078s. All 33 native platform tests passed.
- Version grammar, allocation contracts and one-PATCH SemVer gate passed.
- Release and Debug packages each passed two-tree reproducibility verification
  and exact published icon payload assertions.
- Published Release, published Debug and a fresh Release installation outside
  the source checkout each completed three menu smoke launches: one warmup plus
  two observations. Every launch retained its observed HWND, returned nonzero
  small/large icons, reached a presented menu, reported application Ready,
  session NotRequested and zero dropped startup events.
- Successful paired native icon observations per launch: Release 108/52/49,
  Debug 116/65/57, fresh installation 64/43/44. Debug also recorded 1/0/1
  unanswered bounded probes; those were retried, not counted as successes.
- A client with an EXE resource but no native window icons failed the live gate.
  Truncated/altered ICO data and the headless EXE without an icon group failed
  the resource gate. Failed qualification stops its owned child and retains logs.

Release package provenance is `73031e39aa7961f496752b4ea80fc46e0ad801ff`; Debug is
`2d9c55ec375d2f7eacc92606827ff10f0282680e`. Production icon code is identical between
these revisions; later changes refine the external probe and docs. Both trees
were clean and use the current 348-asset compiled catalog. The fresh installation
resolved its own adjacent runtime assets, not the checkout. Native runs used
Windows build 26200, .NET 10.0.12, NVIDIA RTX PRO 5000 Blackwell Generation Laptop
GPU (driver 32.0.15.9653), 1600 x 900 with VSync.

Evidence and packages are under `artifacts/window-branding`. Required remote
build-test still gates integration. Explorer/pinned-shortcut visuals, actual
Alt+Tab/taskbar appearance and physical mixed-DPI movement remain manual
acceptance; native resource/handle tests do not establish those visual checks.