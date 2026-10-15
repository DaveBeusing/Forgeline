# Player interface surface catalog

## Audit contract

Audited against master `27d80f8b04d77404b5287e66d533dcfea3788250` (0.1.372).
All paths below are repository relative. Renderer owners are in
`src/ForgeLine.Presentation/` unless another project is named. Input/layout owners
remain authoritative for bounds; artwork never defines independent hit targets.
`FrontendOverlayRenderer` and all six `GameplayHudRenderer` surfaces are covered.

**Frontend status:** all normal frontend rows now use the compiled skin through
[graphical frontend integration](GraphicalFrontend.md). Bootstrap/recovery and
studio playback remain intentional independent paths. Human acceptance is pending.
**Battlefield status:** source families are available; live HUD migration remains
pending. Source availability never implies final visual approval.

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

The six surfaces below compose in `GameplayHudRenderer`. Existing semantic icons
are `ui.icon.*` material-backed glyphs through `RtsUiRuntimeAssets`; they are not
new skin textures or final semantic portrait art. Existing health/progress fills
and all text remain copied live data. HUD integration owns shell/selection/groups;
production integration owns domain cards/operations/alerts; tactical integration
owns map/cursor/world indicators. These are migration responsibilities, not new
runtime classes.

| Surface / actual states | Owner / existing graphics | Layout and hit geometry | Required production family / migration |
| --- | --- | --- | --- |
| WaitingForSnapshot, WaitingForPlayerData | `ResourcePowerHudSurface.RenderWaiting`; geometric status/text | `GameplayHudLayout.TopStatusBar` | panel.resource if available; retain independent failure/wait fallback; HUD |
| Seven resource quantities, compact two-row/unavailable/zero | `ResourcePowerHudSurface`; semantic resource glyphs/quads | `ResourceStripLayout.Create`, `Resource(index)` | panel.resource; semantic icons separate; HUD |
| Power generation/demand, low power | `ResourcePowerHudSurface`; power icon/quads | `ResourceStripLayout.Power` | panel.resource, overlay.warning/error; HUD |
| Command result feedback pending/accepted/rejected | `ResourcePowerHudSurface`; labels/rails | `HudInformationLayout.Alerts` | panel.banner, status overlays; production |
| Alerts standard/warning/critical; facility/core/supply focus | `ResourcePowerHudSurface`; live severity and semantic icons | `ActionableAlertLayout`, `GameplayHudLayout.AlertStack` | panel.banner, overlays.warning/error; production |
| FPS/TPS and expanded runtime diagnostics | `ResourcePowerHudSurface`; independent text/quads | `GameplayHudLayout.RuntimeMetrics` | retain independent diagnostic path |
| Empty selection, single owned unit/building, permitted enemy/contact | `SelectionInspectorHudSurface`; semantic glyph and geometric header | `GameplayHudLayout.SelectionInspector`, `HudInformationLayout.InspectorScale` | panel.primary, panel.portrait; real identity portraits separate; HUD |
| Health, ammunition, fuel, supply/readiness, copied work | `SelectionInspectorHudSurface`; live quads/text | inspector internal fitted bounds | progress.*, panel.inset; live quantities and permitted facts; HUD |
| Multi-selection filters/summaries, damaged/critical focus, cycling | `SelectionInspectorHudSurface`; geometric controls | `CombatGroupCardLayout.Control` | control.row, control.button_compact, shared overlays; HUD |
| Formation controls | `SelectionInspectorHudSurface`; geometric actions | `CombatGroupCardLayout.Control` | control.button_compact, selected/focus layers; HUD |
| Primary RTS commands/targeting availability | `PlayerActionDockHudSurface`; existing command glyphs/quads | `GameplayHudLayout.PrimaryCommands`, `PlayerActionDockInteractionLayout` | control.button, shared overlays; HUD shell / tactical content |
| Dock closed/collapsed/open; compact/scroll | `PlayerActionDockHudSurface`; geometric shell | `ActionDock`, `GetCollapsedRect`, `GetCardsRect`, `FirstVisibleItem` | panel.primary/compact, control.row_compact; HUD shell |
| Mode tabs: Construction, Production, UnitProduction, Logistics, Supply, Tactical, Technology | `PlayerActionDockHudSurface`; compiled tabs | `GetModeBarRect`, `GetModeButtonRect` | control.tab, selected/focus layers; HUD shell |
| Construction cards, costs, placement availability | `PlayerActionDockHudSurface`; live card data and icons | `GetCardRect`, `GetHeaderRect` | control.row, panel.portrait, status overlays; production |
| Production recipes, stock/blocked/current work | same dock owner; live recipe data | same card/header geometry | control.row, progress.*, status overlays; production |
| UnitProduction roster/queue/cancel | same dock owner; live queue data | card/header/footer geometry | control.row, progress.*, control.button_compact; production |
| Logistics route/capacity/storage/threshold actions | same dock owner; live logistics data | card/header/footer geometry | control.row, control.button_compact, warning/error; production |
| Supply service/refuel/rearm/repair, unavailable facts | same dock owner; live supply data | card/header/footer geometry | control.row, progress.*, warning/error; production |
| Tactical targeting and current order | same dock owner; live targeting facts | card/header/footer geometry | control.row, selected/focus/error; tactical |
| Technology current/research/blocked | same dock owner; live technology data | card/header/footer geometry | control.row, progress.*, disabled/pending; production |
| Dock pagination, six rebound-key footer actions | same dock owner; live hotkeys | `GetPageButtonRect`, `GetFooterButtonRect` | control.button_compact, shared overlays; production |
| Dock focus/hover/press/pending/accepted/rejected | same dock owner; geometric state patterns | shared dock hit regions and controller state | nine shared overlays; preserve command lifecycle; HUD/production |
| Minimap frame and selectors/legend | `RtsInformationHudSurface` / `RtsInformationOverlayRenderer`; quads/glyphs | `GameplayHudLayout.Minimap`, `RtsMinimapInteractionLayout` | panel.minimap, control.tab/row_compact; tactical |
| Minimap terrain, fog, permitted contacts, objectives, camera footprint | same information owner; dynamic colored geometry | `RtsMinimapInteractionLayout.MapWorldToPointer` / minimap layout | retain live geometry; semantic markers separate; tactical |
| Strategic information-layer tabs/legend | same information owner; live layer view | `GameplayHudLayout.SecondaryView`, information controls | control.tab, panel.compact; tactical |
| Combat-group overview assigned/selected/mixed, empty omitted, keyboard shortcuts | same information owner; live group glyphs/text | `SecondaryView`, `GameplayGuidanceLayout.Remaining`; group keyboard controller | control.button_compact, selected/focus; HUD |
| Match setup/pause/terminal result/restart | same information owner; live `PreAlphaUxView` and match result | `EmitSystemPanel` fitted viewport bounds | panel.dialog, control.button; HUD |
| Legacy help renderer (currently unreachable from F1) | same information owner; procedural help text | `EmitHelpPanel` fitted bounds | retain dormant compatibility path; no new help page |
| Onboarding objective/context/hint/dismiss | same information owner; `EarlyGameGuidance` | `GameplayGuidanceLayout.Resolve`, secondary bounds | panel.tooltip, control.button_compact; HUD |
| World hover tooltip owned/visible/unknown, delay/clamp | `WorldHoverTooltipSurface`; fitted quads/text | pointer-derived bounds clamped to viewport; no new hit region | panel.tooltip; omit unauthorized facts; HUD |
| Operations entry/open/closed/suppressed/waiting | `OperationsHudSurface`; geometric shell | `OperationsLayout.Entry`, `Panel` | panel.primary/compact, control.button_compact; production |
| Operations resource totals, facilities/routes, no-flow-data | same operations owner; live text | `OperationsLayout.Panel` fitted internal bounds | panel.inset, control.separator; production |
| Operations filter/page/facility selection/blocked/details | same operations owner; geometric controls | `OperationsLayout.Control`, `Row`, `PageSize` | control.row, button_compact, selected/warning/error; production |
| Operations focus facility/open existing controls/close | same operations owner | `OperationsLayout.Control` indices 0,9,10 | control.button_compact, disabled/focus; production |

## World, diagnostics and independent paths

| Surface | Owner / geometry | Disposition |
| --- | --- | --- |
| Selection outlines, drag box, orders and targeting | `RtsInformationOverlayRenderer` and interaction overlays; camera projection and live pointer/world coordinates | retain data-driven geometry; tactical art only where appropriate |
| Build placement footprint and valid/invalid preview | Client placement controller and presentation world overlays; actual placement footprint | retain authoritative validity and dynamic geometry; tactical migration |
| Native pointer | Windows platform | retain OS cursor; no additional cursor introduced |
| Debug primitives, development overlays | `DebugDrawRenderer`, `DebugOverlayState`; debug projection/layout | retain asset-independent diagnostics |
| Early bootstrap and native fatal/startup failure | `ForgeLine.Platform.Windows/WindowsBootstrapSurface`, Client bootstrap/failure owners | retain independent native rendering; no required production art |
| Studio/product identity | existing embedded marks and wordmark | retain approved branding; no reference insignia replacement |

## Evidence and maintenance

The [semantic artwork catalog](UiIconArtwork.md) now supplies icon/portrait/cursor
source mappings for these owners. Source availability does not change any row's
pending live-migration or human-acceptance status. Existing legacy material icons
remain active until the shared atlas renderer and consumer integration are qualified.

[Skin contract](UiSkinArtwork.md) and `assets/source/ui/directorate/skin.catalog.json`
define the available source families/states. [Qualification](UiSkinQualification.md)
records exact validation status. Live migrations must update their rows with the
actual runtime binding, candidate evidence identity and attributable review;
source existence is never consumer acceptance. All controls use existing input
contracts; catalog coverage does not authorize new gameplay or speculative widgets.
