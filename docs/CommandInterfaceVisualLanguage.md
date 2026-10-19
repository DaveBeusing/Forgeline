# FORGELINE Command Interface Visual Language

## Ring hover and contextual object detail hierarchy

Radial hover is indicated by an isolated darker wedge and thin bright outer arc, not by fading its text/icon or changing gameplay hit geometry. The adjoining read-only object panel uses a lightly translucent backing, an emphasized object name, a full empty row after the name and structured fact labels. Its right-hand placement is prioritized inside the existing safe-area and obstacle layout. It does not add a second inventory, simulation, or command system; valid current single-object and group read models are the only data sources. DPI, narrow layouts, screenshot contrast and actual D3D12 review require current-source acceptance separate from historical UI evidence.

## Scope and Current Audit

The Windows frontend is rendered by `FrontendOverlayRenderer` over the existing Direct3D 12 graphics device. The UI is intentionally independent of game authority, and `ForgeLine.UI` owns navigation/focus and pointer hit testing. The gameplay HUD uses distinct, modular surfaces composed by `GameplayHudRenderer`.

The frontend uses the bundled antialiased DejaVu font atlas and adaptive physical-client-pixel regions. See [UI typography](UiTypography.md) for font resources, metrics, fallback and ownership. `FrontendGeometry` supplies named regions to both drawing and input; see [layout contract](FrontendLayout.md). The battlefield HUD has a separate DPI-aware layout with named regions. Its
version-2 contract groups resources and actions on the right, reserves onboarding
on the upper left and publishes notification bounds below the visible dock.
Normal HUD counters are removed; diagnostics remain available through Shift+F1. These constraints are relevant to every visual change: presentation must not drift away from actual input geometry.

### Verified design weaknesses

- Five-by-seven glyphs are retained only for bootstrap/failure and explicit debug backends. Normal frontend/HUD text uses measured glyph quads; full bidi and complex-script shaping remain future work.
- Flat background and only-selected-row panels made the frontend resemble a technical prototype rather than a deliberately composed game menu.
- Frontend amber accents and battlefield HUD cool-tinted panels were defined independently instead of using shared semantic colors.
- Main-menu feedback previously occupied the vertical area of the final menu entry. The revised feedback position prevents that overlap.
- The authored static Directorate title treatment supplements this foundation. Bounded decorative motion and optional interface cues follow the [UI motion/audio contract](UiMotionAndAudio.md); higher-detail content and human motion acceptance remain separate. See [cinematic menu](CinematicFrontend.md).

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
- Use the authored Directorate still in the visual-only `MenuArtwork` region. The industrial schematic remains the missing/unsupported-art fallback. Neither presentation is a live match preview or runtime health indicator, and neither captures input.
- Do not obscure the primary commands, loading errors, or menu feedback with ornament.
- Detail pages reserve a separate decorative region; rows, values and adjustment controls use the shared geometry.
- Pause screens preserve the battlefield context rather than replacing it with the full frontend backdrop.
- The pause navigation column has one continuous quiet backplate from header through footer, preventing underlying HUD text from entering gaps between menu surfaces. The world remains visible beside it.

Geometry now adapts to actual viewport, per-monitor DPI and user scale. Details and controls scroll deliberately; action buttons and feedback remain outside the list. [Qualification](FrontendLayoutQualification.md) records the display matrix and native evidence.

## Performance and Validation

Use the [native UI review handbook](UiVisualReview.md) for named frontend/HUD
captures, attributable comparisons and explicit human baseline review. Structural
checks, GPU performance and subjective readability remain separate.

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
2. Further high-detail title content and optional motion review; the compiled static title composition uses existing Directorate production meshes and does not claim new film-quality models.
3. Human acceptance and refinement of the bounded motion/cue language, including subjective listening, physical-monitor transitions and sustained live-match pacing. Current reduced-motion/input rules are documented in [UI motion/audio](UiMotionAndAudio.md).
4. A coherent HUD iconography and information-density pass for selection, build/production, logistics, alerts and minimap with semantic states at multiple zoom levels.
5. Screenshot/golden-image qualification on reference machines to assess visual hierarchy, not just geometry and input tests.

Implement those as independently reviewable changes while preserving the game's RTS-first readability and simulation separation.

## Battlefield HUD hierarchy

The status strip keeps all seven authoritative resources visible. Each cell has a quiet
caption and a bold, right-aligned quantity; power has a separate labelled state and
generation/demand block. Narrow, tall strips use two resource rows instead of hiding
currencies behind an overflow count. This is presentation of captured totals, not net flow.

Selection identity uses a raised header, while health, explicit supply/fuel/ammunition,
readiness and copied work remain in the inspector. Local scale and compact height fit
the available width. The established command-dock band and its frozen geometry fixtures
remain independent. Unavailable private facts are omitted; aggregate selection does not
synthesize individual component values.

Alert cards use bounded width, severity labels and rails (double marks for critical
conditions). Core and supply-critical conditions precede transient command feedback.
Only populated rows capture input; gaps, empty rows and space to their right remain
world space. Operations retains existing navigation/causes and gives selected or
blocked facility rows structural emphasis. The minimap selector and two-line legend
use its fitted size, with the frame inside its interaction bounds. Fog/knowledge colors
and semantic marker sources are preserved. Tooltips fit available width and height.

No reviewed command fixture or accepted pixel golden is regenerated. New captures are
review evidence, not automatic golden approval. See [HUD qualification](BattlefieldHudQualification.md).

## Adaptive command dock

Dock hierarchy: mode tabs, selected context, card title, availability/work status, captured quantities and progress, six rebound-key controls. Explicit operation labels replace ACT. LAST accepted/rejected feedback never promises production/delivery/research completion. Compact dock content fits its existing region without changing other HUD panels.

See [dock qualification](CommandDockQualification.md) for before/after native images, reproducible matrix evidence and the distinction between reviewed candidates and human-accepted goldens.

## Compiled skin foundation

The [compiled skin path](UiAtlasRendering.md) prepares core pages. The
[graphical frontend](GraphicalFrontend.md) now migrates every normal menu/settings
surface with separate state art and existing live text. Battlefield HUD migration
and human visual acceptance remain separate work.

## Battlefield selection markers and drag marquee

The player-facing selection vocabulary is grounded in `RtsSelectionVisualStyle.Selected` (RGB 0.25/1.0/0.35), shared by owned unit/building L-corners and the drag box. Four terrain-conforming, rotated footprint corners replace the former 24-segment selection ellipses; selected groups retain a separate marker per visible member. Hover uses diagonal corner notches; permitted foreign objects use the existing contrast tint plus outward ticks. The drag rectangle is intentionally light: 0.12-alpha green fill, 0.92-alpha green border and 1.25 physical pixels of single-stroke thickness. These geometry changes do not change picking, drag thresholds, HUD capture, F2 technical circles, collision/build placement previews or ordered player commands. Physical-monitor readability under bright and dark terrain remains an explicit art-review gate.
