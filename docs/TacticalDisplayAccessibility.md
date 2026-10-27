# Tactical display readability

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

Native acceptance remains required: compare normal/outlined text, occupied and
hovered/disabled rings, minimap symbols and critical supply/power feedback over
bright terrain and dark vegetation; inspect 100/125/150% scale, compact/ultrawide
windows, long English/German strings and heavy overlays. Exercise save/reload,
reset/cancel, real monitor DPI changes, fullscreen, selection and F1 development
controls. Record original native captures and actual device/DPI/mode. No new
native screenshot or human accessibility approval is claimed by this document.
