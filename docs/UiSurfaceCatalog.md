# Player interface surface catalog

The [integrated qualification](GraphicalInterfaceQualification.md) records all
58 current states, native coverage, compact-layout repairs and pending acceptance.

## Audit contract

Production-owner audit updated against master `5bfd8a14ceaea60368ded7bad0b0a6e2f79a5be8` (0.1.379).
All paths below are repository relative. Renderer owners are in
`src/ForgeLine.Presentation/` unless another project is named. Input/layout owners
remain authoritative for bounds; artwork never defines independent hit targets.
`FrontendOverlayRenderer` and all six `GameplayHudRenderer` surfaces are covered.

**Frontend status:** all normal frontend rows now use the compiled skin through
[graphical frontend integration](GraphicalFrontend.md). Bootstrap/recovery and
studio playback remain intentional independent paths. Human acceptance is pending.
**Battlefield status:** all six normal HUD owners now prepare compiled production
skins, with the economic and tactical bindings detailed below. The historical
HUD labels in the mapping table identify the owning integration, not pending
source production. Human visual and physical-display acceptance remains pending.

Skin names below abbreviate `ui.skin.directorate.`. Shared control states are
normal/base, hover, pressed, focus, selected, disabled and applicable semantic
warning/error/pending/accepted/rejected layers. Text is always dynamic `UiTextBatch`.
No additional widget behavior is implied by a source sprite.

## Frontend

All ordinary frontend rows use compiled skin layers and the existing bundled
text atlas. Shared geometry is `FrontendGeometry` in Presentation; interaction is
`FrontendInteraction` in UI. The migration owner is the frontend integration lane.

| Surface / actual states | Render owner / existing graphics | Layout and hit geometry | Required production family / migration |
| --- | --- | --- | --- |
| Studio splash, regular/bootstrap/fade | `ForgeLine.Client/StudioSplashVideoRenderer`, `UndefinedBehaviorStudioSplash`; existing video/embedded fallback mark | Frontend splash composition; noninteractive | retain canonical mark; bootstrap remains independent |
| Main menu backdrop, ready/loading/missing | `FrontendOverlayRenderer`, `FrontendMenuBackground`; compiled `branding.forgeline.menu.directorate`, procedural schematic fallback | `Background`, `MenuArtwork` | retain image; panel.primary for navigation shell |
| Product mark, tagline, version/footer | `FrontendOverlayRenderer`, `FrontendBrandMark`; embedded canonical wordmark | `Header`, `Footer` | retain marks/live text; control.separator |
| Main menu entries, focus/disabled/hover/press | `FrontendOverlayRenderer`; compiled skin cards and state layers | `Navigation`, `MenuRow`, `MainMenuModel` | control.button, shared overlays |
| Loading progress, preparation, cancellation/error | `FrontendOverlayRenderer`; compiled progress and status skin | `Details`, `PrimaryAction`, `SecondaryAction`, `FrontendLoadingController` | panel.dialog, progress.*, control.button, overlay.error |
| New game scenario/seed/options, start/back | `FrontendOverlayRenderer`; detail rows | `DetailRow`, `DetailValue`, `NewGameModel` | panel.primary, control.row, control.button |
| Load game list, selected/valid/empty/broken/incompatible/error | `FrontendOverlayRenderer`; detail rows | `Details`, `DetailRow`, `LoadGameModel` | control.row, panel.inset, overlays.selected/error/disabled |
| Settings section tabs: General/Graphics/Audio/Bindings | `FrontendOverlayRenderer`; compiled tabs | `SettingsTab`, `SettingsInteractionModel` | control.tab, overlays.selected/focus |
| General: OutputResolution, BorderlessFullscreen, UiScale | `FrontendOverlayRenderer`; typed skin value / compiled stepper | `DetailLabel`, `DetailValue`, `Decrease`, `Increase` | control.row, control.button_compact; dropdown/toggle/slider shells |
| General: EdgeScroll, CameraSpeed, Onboarding, ReducedUiMotion | `FrontendOverlayRenderer`; typed skin value / compiled stepper | same shared detail regions | control.row, control.button_compact; toggle/slider |
| Graphics: RenderQuality, ResolutionScale, Antialiasing, Shadows, Textures | `FrontendOverlayRenderer`; typed skin value / compiled stepper | same shared detail regions | control.row, control.button_compact; dropdown/slider |
| Graphics: ObjectDetail, SurfaceDetail, VegetationMotion, Atmosphere, Particles | `FrontendOverlayRenderer`; typed skin value / compiled stepper | same shared detail regions | control.row, control.button_compact; dropdown/toggle |
| Audio: UiSoundEnabled, UiSoundVolume | `FrontendOverlayRenderer`; typed skin value / compiled stepper | same shared detail regions | control.row, control.button_compact; toggle/slider; no invented music/master channels |
| Bindings: BindingAction, BindingKey, capture/conflict/cancel | `FrontendOverlayRenderer`; typed skin value / compiled stepper | same shared detail regions; `SettingsInteractionModel` | control.row, control.input, overlays.focus/error |
| Settings apply/cancel, invalid/display rollback | `FrontendOverlayRenderer`; compiled actions/feedback | `PrimaryAction`, `SecondaryAction`, `Feedback`; Client display transaction | control.button, panel.banner, overlays.disabled/warning/error |
| Controls Action/Input table, long list/scroll | `FrontendOverlayRenderer`; live text / compiled table rows | `Details`, `DetailRow`, `DetailScroll`; Client controls scroll state and `FrontendHitTesting` | control.row, control.row_compact; visible scrollbar from published list extent |
| Credits studio/developer/version/engine | `FrontendOverlayRenderer`; live detail text | `Details`, `SecondaryAction` | panel.primary, control.button; no extra credit content |
| Pause resume/save/settings/main-menu/exit; feedback | `FrontendOverlayRenderer`; opaque backing and compiled skin over live world | `PauseBackdrop`, `MenuRow`, `PauseMenuModel` | panel.primary, control.button, panel.banner |
| Settings opened from pause, all four sections | same frontend owners; current settings renderer | shared settings regions; UI return-state routing | same settings skin; keep world context/input suppression |
| Detail primary/back actions and feedback | `FrontendOverlayRenderer`; control affordances | `PrimaryAction`, `SecondaryAction`, `Feedback` | control.button, panel.banner, shared overlays |
| Scroll overflow, long values and disabled reasons | `FrontendOverlayRenderer`; fitted live text | `VisibleDetailRows`, `DetailScroll`, text clipping | control.row_compact, panel.tooltip; retain clipping/scroll semantics |

## Battlefield HUD

The six surfaces below compose in `GameplayHudRenderer`. The normal resource,
selection, group, command-shell, tooltip and guidance paths now use prepared
production atlas layers and the existing live text atlas; see
[graphical battlefield HUD](GraphicalBattlefieldHud.md). Their legacy bitmap
geometry remains an independent bootstrap/debug path. [Domain cards, operations and alerts](GraphicalProductionAndOperations.md) now consume compiled production layers through the existing owners. Tactical map/cursor/world geometry retains its later migration owner. No new simulation/read-model authority is introduced.

| Surface / actual states | Owner / existing graphics | Layout and hit geometry | Required production family / migration |
| --- | --- | --- | --- |
| WaitingForSnapshot, WaitingForPlayerData | `ResourcePowerHudSurface.RenderWaiting`; compiled resource panel and live waiting text | `GameplayHudLayout.TopStatusBar` | panel.resource; explicit required-asset failure and independent bootstrap; implemented |
| Seven resource quantities, compact two-row/unavailable/zero | `ResourcePowerHudSurface`; compiled resource cells/icons and live totals | `ResourceStripLayout.Create`, `Resource(index)` | panel.resource; seven mapped semantic icons; implemented |
| Power generation/demand, low power | `ResourcePowerHudSurface`; compiled power cell and state overlay | `ResourceStripLayout.Power` | panel.resource, overlay.warning/error; HUD |
| Command result feedback accepted/partial/rejected | `ResourcePowerHudSurface`; compiled cards, live labels/rails | `HudInformationLayout.Alerts` | panel.raised, severity overlays and live OK/PART/FAIL labels; implemented |
| Alerts standard/warning/critical; facility/core/supply focus | `ResourcePowerHudSurface`; live severity and semantic icons | `ActionableAlertLayout`, `GameplayHudLayout.AlertStack` | panel.raised, overlays.warning/error; implemented |
| FPS/TPS and expanded runtime diagnostics | `ResourcePowerHudSurface`; independent text/quads | `GameplayHudLayout.RuntimeMetrics` | retain independent diagnostic path |
| Empty selection, single owned unit/building, permitted enemy/contact | `SelectionInspectorHudSurface`; compiled shell and permitted roster portrait | `GameplayHudLayout.SelectionInspector`, `HudInformationLayout.InspectorScale` | panel.primary/portrait; mapped roster portraits for permitted single identities; implemented |
| Health, ammunition, fuel, supply/readiness, copied work | `SelectionInspectorHudSurface`; clipped atlas fills/tracks and live text | inspector internal fitted bounds | progress.*, panel.inset; live quantities and permitted facts; HUD |
| Multi-selection filters/summaries, damaged/critical focus, cycling | `SelectionInspectorHudSurface`; compiled controls | `CombatGroupCardLayout.Control` | control.row, control.button_compact, shared overlays; HUD |
| Formation controls | `SelectionInspectorHudSurface`; compiled formation controls | `CombatGroupCardLayout.Control` | control.button_compact, selected/focus layers; HUD |
| Primary RTS commands/targeting availability | `PlayerActionDockHudSurface`; compiled shell controls with shared hit targets | `GameplayHudLayout.PrimaryCommands`, `PlayerActionDockInteractionLayout` | control.button, shared overlays; HUD shell / tactical content |
| Dock closed/collapsed/open; compact/scroll | `PlayerActionDockHudSurface`; compiled shell | `ActionDock`, `GetCollapsedRect`, `GetCardsRect`, `FirstVisibleItem` | panel.primary/compact, control.row_compact; HUD shell |
| Mode tabs: Construction, Production, UnitProduction, Logistics, Supply, Tactical, Technology | `PlayerActionDockHudSurface`; compiled tabs | `GetModeBarRect`, `GetModeButtonRect` | control.tab, selected/focus layers; HUD shell |
| Construction cards, costs, placement availability | `PlayerActionDockHudSurface`; live card data and icons | `GetCardRect`, `GetHeaderRect` | control.button_compact, panel.portrait, status overlays; actual building portraits; implemented |
| Production recipes, stock/blocked/current work | same dock owner; compiled cards and live recipe/request data | same card/header geometry | control.button_compact, progress.*, status overlays; implemented |
| UnitProduction roster/queue/cancel | same dock owner; actual roster portraits and live queue data | card/header/footer geometry | control.button_compact, progress.*, control.button_compact; implemented |
| Logistics stock policy/distribution/cargo facts | same dock owner; live logistics data | card/header/footer geometry | control.button_compact, logistics artwork, warning/error; implemented |
| Supply fuel/ammunition thresholds/provider, unavailable facts | same dock owner; live supply data | card/header/footer geometry | control.button_compact, fuel/ammunition/provider artwork, warning/error; implemented |
| Tactical targeting and current order | same dock owner; live targeting facts | card/header/footer geometry | control.button_compact, command artwork, selected/focus/error; implemented cards; world targeting retains tactical owner |
| Technology current/research/blocked | same dock owner; live technology data | card/header/footer geometry | control.button_compact, explicit TechnologyId artwork, progress.*, disabled/pending/accepted; implemented |
| Dock pagination, six rebound-key footer actions | same dock owner; live hotkeys | `GetPageButtonRect`, `GetFooterButtonRect` | control.button_compact, shared overlays; implemented shell controls |
| Dock focus/hover/press/pending/accepted/rejected | same dock owner; compiled shell/control state layers; compiled domain-card states and retained structural cues | shared dock hit regions and controller state | nine shared overlays; preserve command lifecycle; implemented |
| Minimap frame and selectors/legend | `RtsInformationHudSurface` / `RtsInformationOverlayRenderer`; compiled panel/controls/glyphs | `GameplayHudLayout.Minimap`, `RtsMinimapInteractionLayout` | panel.minimap, control.tab, panel.inset; implemented |
| Minimap terrain, fog, permitted contacts, objectives, camera footprint | same information owner; live geometry plus clipped permitted atlas symbols | `RtsMinimapInteractionLayout.MapWorldToPointer` / minimap layout | retain live terrain/fog/transform/camera rays; mapped contact/objective art; implemented |
| Strategic information-layer tabs/legend | same information owner; live layer view | `GameplayHudLayout.SecondaryView`, information controls | control.tab, panel.inset, mapped layer glyphs; implemented |
| Combat-group overview assigned/selected/mixed, empty omitted, keyboard shortcuts | same information owner; compiled group controls and live summaries | `SecondaryView`, `GameplayGuidanceLayout.Remaining`; group keyboard controller | control.button_compact, selected/focus; HUD |
| Match setup/pause/terminal result/restart | same information owner; live `PreAlphaUxView` and match result | `EmitSystemPanel` fitted viewport bounds | panel.dialog, control.button; HUD |
| Legacy help renderer (currently unreachable from F1) | same information owner; procedural help text | `EmitHelpPanel` fitted bounds | retain dormant compatibility path; no new help page |
| Onboarding objective/context/hint/dismiss | same information owner; `EarlyGameGuidance`; compiled backplate | `GameplayGuidanceLayout.Resolve`, secondary bounds | panel.tooltip, control.button_compact; HUD |
| World hover tooltip owned/visible/unknown, delay/clamp | `WorldHoverTooltipSurface`; compiled fitted backplate/live text | pointer-derived bounds clamped to viewport; no new hit region | panel.tooltip; omit unauthorized facts; HUD |
| Operations entry/open/closed/suppressed/waiting | `OperationsHudSurface`; compiled shell with opaque backing | `OperationsLayout.Entry`, `Panel` | panel.primary/raised/inset, control.button_compact; implemented |
| Operations resource totals, facilities/routes, no-flow-data | same operations owner; live text | `OperationsLayout.Panel` fitted internal bounds | panel.primary/inset, explicit N/A; implemented |
| Operations filter/page/facility selection/blocked/details | same operations owner; compiled category rows/controls | `OperationsLayout.Control`, `Row`, `PageSize` | control.button_compact, category artwork, selected/warning/disabled; implemented |
| Operations focus facility/open existing controls/close | same operations owner | `OperationsLayout.Control` indices 0,9,10 | control.button_compact, disabled/focus; implemented |

## World, diagnostics and independent paths

| Surface | Owner / geometry | Disposition |
| --- | --- | --- |
| Selection outlines, drag box, orders and targeting | `RtsInformationOverlayRenderer`, `TacticalFootprint` and interaction owners; actual extents, rotation, camera and copied intent | projected corner/target/move art; terrain-sampled oriented group outlines and drag geometry retained |
| Build placement footprint and valid/invalid preview | Client placement controller and presentation world overlays; actual placement footprint | retain authoritative validity and dynamic geometry; tactical migration |
| Native pointer | Windows platform / `TacticalCursorSet` | single native pointer, eleven roles, four sizes with authored hotspots; OS ownership restored on exit |
| Debug primitives, development overlays | `DebugDrawRenderer`, `DebugOverlayState`; debug projection/layout | retain asset-independent diagnostics |
| Early bootstrap and native fatal/startup failure | `ForgeLine.Platform.Windows/WindowsBootstrapSurface`, Client bootstrap/failure owners | retain independent native rendering; no required production art |
| Studio/product identity | existing embedded marks and wordmark | retain approved branding; no reference insignia replacement |

## Evidence and maintenance

The [semantic artwork catalog](UiIconArtwork.md) now supplies icon/portrait/cursor
source mappings for these owners. Normal production UI uses the shared atlas
renderer. Individual texture IDs remain compiled compatibility content; their
presence does not imply an additional upload by the normal skinned owner.
Source availability and completed integration do not grant human acceptance.

[Skin contract](UiSkinArtwork.md) and `assets/source/ui/directorate/skin.catalog.json`
define the available source families/states. [Qualification](UiSkinQualification.md)
records exact validation status. Live migrations must update their rows with the
actual runtime binding, candidate evidence identity and attributable review;
source existence is never consumer acceptance. All controls use existing input
contracts; catalog coverage does not authorize new gameplay or speculative widgets.

## Graphical economic surface coverage

Build/processing/unit/logistics/supply/technology/tactical domain cards, actual queue work states, existing tooltip costs/prerequisites, Operations controls/rows/details and actionable alerts now use prepared production layers. Empty rows remain non-actions; cancel/priority/mode controls retain existing capabilities. See [exact roles and ownership](GraphicalProductionAndOperations.md) and [source-bound validation](GraphicalProductionAndOperationsQualification.md). No source artwork, new research, queue capacity, reordered gameplay, map or cursor mechanics are introduced.

## Graphical tactical consumer coverage

[Map, native cursor and world indicators](GraphicalTacticalInterface.md) now use production panels, selector/layer glyphs, permitted contact/objective/group art, projected primary/hover corner components, transient order/target art and current owned single-entity status frames. Dynamic fog, ranges, routes, placement and terrain-sampled group outlines remain intentional geometry. [Qualification](GraphicalTacticalInterfaceQualification.md) separates native repeatability, privacy/hotspot/resource contracts, unresolved gates and pending physical/human acceptance.
