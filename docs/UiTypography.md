# UI typography

## Typeface and precompiled resources

The Windows frontend and all normal gameplay HUD text use DejaVu Sans Regular/Bold, font version 2.35. Exact upstream revisions, font hashes and complete redistribution terms are in `assets/source/ui/fonts/README.md`, `metrics.json` and `LICENSE.txt`. The publisher copies the full notice to `ThirdPartyNotices/DejaVuFonts.txt`. Original TTF files, Python and font-authoring libraries do not ship and are never discovered from system fonts.

`build/Build-UiFontAtlas.py` is an offline authoring tool; dependencies are pinned in `build/ui-font-requirements.txt`. It generates committed metrics, PNG and a gzip alpha payload. Run it with `--verify` after installing those dependencies in an isolated authoring environment. Verification checks metrics, compressed alpha and decoded PNG pixels. `build/Validate-UiFonts.ps1` checks source provenance hashes, atlas integrity and coverage without Python; CI and publishing call this gate. Generated metrics have fixed LF endings for byte-level verification.

The version-1 resource `font.ui.dejavu_sans` contains 1,448 glyphs and 3,524 kerning pairs across two faces. The 2048×4096 distance field uses 48px source glyphs and a six-pixel spread. Its immutable R8 linear GPU texture occupies 8 MiB and is shared by all text surfaces on the creating graphics thread. Linear sampling and derivative-based coverage preserve antialiasing across output sizes. Texture quality and scene resolution scale do not reduce text resolution.

Metrics and compressed alpha are embedded in Presentation, available before the runtime world catalog. This is a deliberately narrow UI resource contract: it introduces no new general asset payload version or runtime font importer. The PNG remains an offline inspection asset. It is not exported as an unused generic RGBA catalog texture: doing so would add 32 MiB and violate the existing production catalog budget. The mesh/texture/material catalog has no combined font-metrics type, so the embedded contract is validated directly by CI/publishing. Runtime UI ownership remains in Presentation/Graphics; UI owns focus, input and view models. Headless simulation remains independent.

## Styles and layout

| Role | Reference size | Face |
| --- | ---: | --- |
| Command title | 32 | Bold |
| Menu item | 22 | Regular |
| Section heading | 16 | Bold |
| Telemetry | 12 | Regular |
| Numeric | 12 | Bold |
| Warning | 14 | Bold |
| Tooltip | 12 | Regular |

Use `UiTypography.Style` for shared role defaults and the existing semantic palette for colors. Text size is in physical output pixels after the caller's scale. Metrics include advances, bearings, ascent, line height and kerning; numbers have equal advances. `UiFontMetrics.Measure`, `Fit` and `Wrap` are the common contracts. Fit uses a measured ellipsis and Unicode scalar boundaries. Wrapping is bounded to 256 lines; a scalar wider than its available line has no drawable content. Draw alignment and optional baseline positioning use the same advances.

ASCII, Latin-1/Extended-A, selected Greek/Cyrillic, German diacritics, punctuation, arrows, percentages, digits and a visible replacement glyph are bundled. Common decomposed Latin accents compose during scalar traversal. Unsupported characters, including emoji, display U+FFFD and increment rendering diagnostics. This is left-to-right layout with kerning and common accent composition; Arabic/Indic shaping, bidi and full global-script coverage require a later explicit shaping contract.

Frontend rendering and pointer mapping consume the same published adaptive physical-pixel geometry, including actual window DPI and user scale. Density fit limits preserve menu/actions and scroll long lists; see [frontend layout](FrontendLayout.md). Gameplay retains its separate DPI-aware region layout. Detail labels and values use measured width budgets, and the renderer clips glyph quads to the viewport and supplied right edge. Full descriptions remain available through their existing detail/tooltip models. No gameplay or input authority moved into rendering.

## Ownership, performance and failures

Each surface reuses bounded geometry and per-frame upload buffers. A batch emits six 32-byte vertices per visible glyph, up to 8,192 glyphs, with at most four frame slots. Buffers grow within that bound and replacement succeeds before retiring the old allocation. One additional draw per active text surface follows its panel/icon draw; atlas lifetime ends after its final surface lease, with native retirement following existing fences. Existing icon bitmap geometry is retained.

Font failure reports an actionable reinstall diagnostic; bitmap text remains only for that failure/bootstrap case and explicitly unsupported debug backends. Normal D3D12 devices always select the atlas. Text diagnostics expose readiness, missing/dropped glyphs and requested upload bytes. Graphics/device failures remain failures rather than being silently hidden by fallback. The development overlay retains its diagnostic bitmap renderer.

The measured CPU trade-off is recorded in [Typography qualification](UiTypographyQualification.md). Text batching greatly reduces vertices and requested upload bytes; it adds draws and does not promise a dense-HUD CPU speedup. Layout generation reuses metrics and vertex storage without warm managed allocation. It does not retain an unbounded string/layout cache.

## Reproduction

```powershell
pwsh ./build/Validate-UiFonts.ps1
dotnet test --project tests/ForgeLine.Presentation.Tests -c Release -- --filter-class '*UiFontMetricsTests' '*UiTextBatchTests' '*UiTypographyNativeTests'
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks -c Release -- --typography-hotpaths artifacts/frontend-typography.json
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks -c Release -- --gameplay-ui-hotpaths artifacts/gameplay-ui-typography.json
# Run against an unpacked, published Windows client and its compiled runtime root:
./ForgeLine.Client.exe --ui-typography-capture <empty-output-directory> <runtime-root>
```

The native capture command renders menu/HUD over the complete resolution × DPI-layout × UI-scale matrix, checks repeated pixels and missing/dropped glyphs, and exports representative PNGs plus settings/loading/controls/pause. DPI values are requested frontend/HUD layout sampling values; the report separately records actual window DPI. This is not physical-monitor DPI certification. Capture/readback duration includes waits and is not isolated text GPU timing.
