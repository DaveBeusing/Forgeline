# Tactical display readability

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Settings → General retains the existing UI scale (75–200%), reduced UI motion,
tooltip delay and tooltip text-size controls. Additional rows scroll in the same
keyboard and pointer viewport:

- **Readability preset:** Standard selects 100% scale, standard team colors and
  normal motion. Readable selects 125% scale and reduced motion. High Contrast
  additionally enables outlined text and blue/orange minimap team glyphs.
- **High contrast text:** white SDF text with a black halo on frontend and player
  HUD text surfaces; occupied action-ring sectors receive opaque black backgrounds.
  Hover arcs, labels, disabled reasons and logical hit regions remain present.
- **Minimap team colors:** Standard retains production artwork. BlueOrange and
  Grayscale use the existing distinct friendly, visible-enemy and detected-contact
  procedural glyphs with alternate colors. A detected contact remains a hollow
  contact glyph; no additional intelligence is exposed. Other icon roles retain
  their existing semantics and artwork.
- **Reset readability:** resets scale, contrast, team colors, reduced motion and
  tooltip preferences in the current draft. APPLY saves; BACK discards the draft.
  Resolution, camera and gameplay bindings, graphics, onboarding and sound choices
  are preserved. Presets preserve tooltip choices; the explicit reset also restores
  their defaults.

These display preferences apply during a paused match through the existing live
settings path. Graphics and gameplay bindings retain their next-match policy.
The existing guarded display-preview path remains responsible for resolution and
fullscreen changes. Layout/input cancellation after viewport/DPI/scale transitions
is unchanged. Text geometry, clipping, metrics, glyph capacity and vertex stride
are identical between normal and outlined rendering. The optional contrast shader
pipeline is created once per text owner when first used and released on disposal.
No additional glyph draw or upload is required; no GPU performance claim follows
from that structural property.

## Persistence and ownership

`ClientUserSettings.TacticalDisplay` is an additive, version-1 presentation record
within the existing settings schema. Missing fields retain normal presentation.
Unsupported versions/palettes or malformed values recover only this record, using
the existing quarantine mechanism, while saved display/input/audio values survive.
UI scale continues to use the existing finite validation and clamped menu/layout
controls. No parallel settings store, remapping logic, simulation state, command or
read-model format is introduced.

The render frame copies preferences to the render owner. HUD surfaces configure
their own text batch; no process-global palette or mutable simulation entity is
used. Reduced motion continues to settle decorative transitions through the
existing feedback preference and leaves status text and input active. Power,
supply, alarms and team identity retain existing text/glyph/geometry cues.

## Qualification

Focused regression classes:

```powershell
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release -- --filter-class ForgeLine.Client.Tests.TacticalDisplaySettingsTests ForgeLine.Client.Tests.ClientSettingsFrontendAdapterTests ForgeLine.Client.Tests.PausedSettingsControllerTests
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release -- --filter-class ForgeLine.Presentation.Tests.TacticalDisplaySettingsTests ForgeLine.Presentation.Tests.UiTextBatchTests ForgeLine.Presentation.Tests.GameplayDisplayQualificationTests
```

The new cases cover scoped invalid-data recovery, draft/apply/reload/reset,
preserved bindings/audio, compact row focus, 100/125/150% layout transitions,
grayscale team values and contrast-pipeline atlas/buffer lifetime. Existing
motion, selection, F1/developer controls, minimap intelligence and gameplay UI
fixture suites remain required. These fixtures do not certify physical displays.

`ForgeLine.Client.exe --tactical-display-capture <empty-output> <compiled-runtime-root>`
reuses the native battlefield harness for paired normal/contrast,
blue/orange/grayscale captures at 1280×720 and 1920×1080, 96 layout DPI and
100/125/150% scale. Mixed, supply and production states use copied authored facts;
each capture must repeat exactly and contain complete native text. This inspection
does not drive live commands, real monitor transitions or action-ring input.

Native acceptance remains required: compare normal/outlined text, occupied and
hovered/disabled rings, minimap symbols and critical supply/power feedback over
bright terrain and dark vegetation; inspect 100/125/150% scale, compact/ultrawide
windows, long English/German strings and heavy overlays. Exercise save/reload,
reset/cancel, real monitor DPI changes, fullscreen, selection and F1 development
controls. Record original native captures and actual device/DPI/mode. The automated
series below covers authored facts at fixed layout DPI. Physical-monitor and human
acceptance remain open.

## Executed inspection — 28 October 2026

The [qualification record](evidence/tactical-display/README.md) contains unchanged
native image pairs and hashes for 54 repeated hardware cases. Canonical Release
build, 276 focused existing/new cases, runtime asset qualification and the
repository Windowed/Borderless smoke passed. Two stale settings-test expectations
were aligned with the actual last General row and the schema already present on
master; their 21-case regression rerun passed.

The complete suites are not green: final Client is 537 passed / 4 failed / 2
opt-in skipped; Presentation is 1,888 passed / 4 failed / 66 opt-in skipped.
Remaining paths and the required CI Game failures are recorded separately in the
qualification record. No assertion, budget or opt-in policy was weakened. Physical
monitor transitions, live F1/selection/reset journeys and human readability
acceptance remain open. The images are inspection evidence, not approved goldens.
