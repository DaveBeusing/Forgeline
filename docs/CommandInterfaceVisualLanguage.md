# FORGELINE Command Interface Visual Language

## Scope and Current Audit

The Windows frontend is rendered by `FrontendOverlayRenderer` over the existing Direct3D 12 graphics device. The UI is intentionally independent of game authority, and `ForgeLine.UI` owns navigation/focus and pointer hit testing. The gameplay HUD uses distinct, modular surfaces composed by `GameplayHudRenderer`.

The frontend uses the bundled antialiased DejaVu font atlas and adaptive physical-client-pixel regions. See [UI typography](UiTypography.md) for font resources, metrics, fallback and ownership. `FrontendGeometry` supplies named regions to both drawing and input; see [layout contract](FrontendLayout.md). The battlefield HUD has a separate DPI-aware layout with named regions. These constraints are relevant to every visual change: presentation must not drift away from actual input geometry.

### Verified design weaknesses

- Five-by-seven glyphs are retained only for bootstrap/failure and explicit debug backends. Normal frontend/HUD text uses measured glyph quads; full bidi and complex-script shaping remain future work.
- Flat background and only-selected-row panels made the frontend resemble a technical prototype rather than a deliberately composed game menu.
- Frontend amber accents and battlefield HUD cool-tinted panels were defined independently instead of using shared semantic colors.
- Main-menu feedback previously occupied the vertical area of the final menu entry. The revised feedback position prevents that overlap.
- Major screens are implemented and tested, but a finished scene-backed title treatment, rich content transitions are not yet provided by this visual pass. The bundled typography layer is now implemented separately.

### Existing strengths to retain

- Focus and hover affordances, keyboard navigation, disabled command states, explicit feedback and error states.
- The input action contract; mouse hit testing consumes the same published geometry as drawn controls.
- DPI-aware gameplay HUD with noncolor selection patterns and separate resource, selection, action, minimap and operational surfaces.
- Read-only presentation snapshots and no simulation mutations originating from visual controls.
- A compact, industrial military presentation appropriate to the Directorate.

## Visual Direction

The visual identity is **industrial command / tactical operations**: layered carbon and gunmetal backgrounds, legible off-white hierarchy, restrained warm-metal attention accents, readable alert colors, and strong rectilinear geometry.

Colors are centralized in `ForgeLineUiPalette`. Prefer semantic tokens over copied RGBA literals:

| Semantic role | Token | Use |
| --- | --- | --- |
| Deep canvas | `BackgroundDeep` | Main frontend backdrop |
| Primary surface | `Surface` | Panels, paused surfaces, HUD base |
| Raised surface | `SurfaceRaised` | Default selectable cards/rows |
| Active/hover/press | `SurfaceActive`, `SurfaceHover`, `SurfacePressed` | Input-state feedback |
| Attention accent | `Accent` | Selected rails, primary operations, focus |
| Foreground | `TextPrimary`, `TextSecondary`, `TextMuted` | Information hierarchy |
| Status | `Warning`, `Critical` | Explicit alert context, always with text or shape |

Alpha is meaningful only when the graphics pipeline supports blending. Functional controls must retain distinct structural cues (rails, borders, underlines, disabled treatment) in addition to color. Never encode an essential combat or resource state only in a subtle color change.

## Frontend Composition

- Use subtle vertical tonal variation rather than a flat color fill.
- Keep a consistent logo/header, tagline, separator and low-contrast version/footer placement.
- Make every menu row's visual boundary discoverable; focus adds a thick warm rail and explicit SELECT text while disabled actions receive a visibly different treatment.
- Use `Navigation` / `MenuRow` for both visible bounds and input. The 96-DPI 1080p default retains X 82–642 and Y 338 + 72×row; compact layouts change these through the shared resolver.
- Use a right-side **abstract industrial schematic**, not a false gameplay render or fictional runtime health indicator. It must not intercept pointer input or imply functional options.
- Do not obscure the primary commands, loading errors, or menu feedback with ornament.
- Detail pages reserve a separate decorative region; rows, values and adjustment controls use the shared geometry.
- Pause screens preserve the battlefield context rather than replacing it with the full frontend backdrop.

Geometry now adapts to actual viewport, per-monitor DPI and user scale. Details and controls scroll deliberately; action buttons and feedback remain outside the list. [Qualification](FrontendLayoutQualification.md) records the display matrix and native evidence.

## Performance and Validation

Frontend composition uses bounded static geometric detail and the existing overlay buffer/pipeline; it must not load source assets, initialize a second graphics device, or allocate new graphic resources per frame. Normal text batches generate six vertices per glyph, share one atlas, and reuse bounded frame buffers. Qualification records the extra text draws and measured CPU/upload trade-off.

Targeted validation:

```powershell
dotnet test tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --filter "FullyQualifiedName~FrontendOverlayRendererTests|FullyQualifiedName~ForgeLineUiVisualLanguageTests|FullyQualifiedName~GameplayHudVisualQualificationTests"
dotnet test tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --filter "FullyQualifiedName~FrontendLayoutHardeningTests|FullyQualifiedName~FrontendPointerInteractionTests|FullyQualifiedName~FrontendControlAffordanceTests"
```

Visually qualify main menu, loading, settings, controls, pause, and the battlefield HUD at 1280×720, 1920×1080, 2560×1440, 3440×1440, with 96/144/192 DPI where practical. Confirm that SELECT and disabled states remain unambiguous, every visible control matches its click target, and critical information stays legible in motion over bright and dark battlefields. Measure GPU time and vertex count before approving additional adornment.

## Remaining Production-Quality Work

This pass is **visual-foundation work**, not a claim of final AAA visual quality. A complete title-quality interface additionally needs:

1. Extend the bundled typography contract when additional locales require complex-script shaping or bidi; current language coverage, kerning, metrics and visible replacement are documented in [UI typography](UiTypography.md).
2. Professionally authored title/menu environment imagery or animated real-time background scenes; integrate with the compiled runtime asset pipeline and quality presets.
3. Motion language: short deliberate transitions, input feedback, panel reveals and audio cues, with reduced-motion/skip behavior and no artificial loading delays.
4. A coherent HUD iconography and information-density pass for selection, build/production, logistics, alerts and minimap with semantic states at multiple zoom levels.
5. Screenshot/golden-image qualification on reference machines to assess visual hierarchy, not just geometry and input tests.

Implement those as independently reviewable changes while preserving the game's RTS-first readability and simulation separation.
