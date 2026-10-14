# FORGELINE Command Interface Visual Language

## Scope and Current Audit

The Windows frontend is rendered by `FrontendOverlayRenderer` over the existing Direct3D 12 graphics device. The UI is intentionally independent of game authority, and `ForgeLine.UI` owns navigation/focus and pointer hit testing. The gameplay HUD uses distinct, modular surfaces composed by `GameplayHudRenderer`.

The existing frontend uses a custom five-by-seven bitmap glyph renderer and a 1920×1080 reference layout, scaled and centered into the viewport. Main-menu interaction rectangles, settings adjustment hit targets, and primary/secondary buttons are intentionally fixed in reference coordinates. The battlefield HUD has a separate DPI-aware layout with named regions. These constraints are relevant to every visual change: presentation must not drift away from actual input geometry.

### Verified design weaknesses

- Low-fidelity five-by-seven glyphs remain the strongest typography limitation. The approach is functional for diagnostics, but it does not meet finished-game typography, internationalization, or accessibility goals.
- Flat background and only-selected-row panels made the frontend resemble a technical prototype rather than a deliberately composed game menu.
- Frontend amber accents and battlefield HUD cool-tinted panels were defined independently instead of using shared semantic colors.
- Main-menu feedback previously occupied the vertical area of the final menu entry. The revised feedback position prevents that overlap.
- Major screens are implemented and tested, but a finished scene-backed title treatment, rich content transitions, and game-quality UI font/atlas pipeline are not yet provided by this visual pass.

### Existing strengths to retain

- Focus and hover affordances, keyboard navigation, disabled command states, explicit feedback and error states.
- The input contract and 1920×1080 frontend reference geometry; mouse hit testing must remain synchronized with the drawn controls.
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
- Keep the exact current menu interaction boxes: X 82–642; rows start at Y 338, advance by 72 and are 52 high, in reference pixels.
- Use a right-side **abstract industrial schematic**, not a false gameplay render or fictional runtime health indicator. It must not intercept pointer input or imply functional options.
- Do not obscure the primary commands, loading errors, or menu feedback with ornament.
- Detail pages can carry a quiet second-plane technical grid, but must preserve the settings controls and their hit rectangles.
- Pause screens preserve the battlefield context rather than replacing it with the full frontend backdrop.

The current geometry remains reference-space based. If the UI eventually adopts a responsive constraint layout, migrate hit testing and rendering **together** and qualify ultrawide, 16:9, low resolution and multiple DPI/UI scale combinations.

## Performance and Validation

Frontend composition uses bounded static geometric detail and the existing overlay buffer/pipeline; it must not load source assets, initialize a second graphics device, or allocate new graphic resources per frame. Large collections of text will still generate many vertices until a glyph-atlas solution replaces the prototype renderer.

Targeted validation:

```powershell
dotnet test tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --filter "FullyQualifiedName~FrontendOverlayRendererTests|FullyQualifiedName~ForgeLineUiVisualLanguageTests|FullyQualifiedName~GameplayHudVisualQualificationTests"
dotnet test tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --filter "FullyQualifiedName~FrontendLayoutHardeningTests|FullyQualifiedName~FrontendPointerInteractionTests|FullyQualifiedName~FrontendControlAffordanceTests"
```

Visually qualify main menu, loading, settings, controls, pause, and the battlefield HUD at 1280×720, 1920×1080, 2560×1440, 3440×1440, with 96/144/192 DPI where practical. Confirm that SELECT and disabled states remain unambiguous, every visible control matches its click target, and critical information stays legible in motion over bright and dark battlefields. Measure GPU time and vertex count before approving additional adornment.

## Remaining Production-Quality Work

This pass is **visual-foundation work**, not a claim of final AAA visual quality. A complete title-quality interface additionally needs:

1. A licensed, bundled high-quality UI typography pipeline (e.g., multi-channel signed-distance-field glyph atlas), kerning, tracking, Unicode coverage, locale fallback, and text measurement. Replace the bitmap font in a separate, measured implementation, including gameplay HUD usage.
2. Professionally authored title/menu environment imagery or animated real-time background scenes; integrate with the compiled runtime asset pipeline and quality presets.
3. Motion language: short deliberate transitions, input feedback, panel reveals and audio cues, with reduced-motion/skip behavior and no artificial loading delays.
4. A coherent HUD iconography and information-density pass for selection, build/production, logistics, alerts and minimap with semantic states at multiple zoom levels.
5. Screenshot/golden-image qualification on reference machines to assess visual hierarchy, not just geometry and input tests.

Implement those as independently reviewable changes while preserving the game's RTS-first readability and simulation separation.
