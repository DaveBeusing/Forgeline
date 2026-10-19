# HUD surface consolidation

## Integration baseline

Initial source baseline: `master` `d4c7b62077c98500d6309cdd6c6dbf5e6bd6490c` (0.1.406).
During implementation, `master` advanced through PR #241 to
`09b5f1ae51164dadeb56d2d37249c114f509a5dc` (0.1.407). That history is merged;
this PR increments PATCH once to 0.1.408. Branch: `ui/hud-surface-consolidation`.
[Pull request #242](https://github.com/DaveBeusing/Forgeline/pull/242).
The player SelectionPanel maps to `SelectionInspectorHudSurface`; ActionBar maps to
its contextual command band, seven mode buttons and rectangular action dock in
`PlayerActionDockHudSurface`. The existing single/group ring shares that renderer
and must survive removal. F2 and Shift+F1 remain diagnostic paths.

## Functional parity contract

| Function | Previous entry | Remaining entry | Information and input |
| --- | --- | --- | --- |
| Build | Command band, B, dock | Object ring category, B | Existing catalog tooltip, mouse sector or Enter, existing placement controller |
| Processing / units | P/U, mode bar, dock | Object ring, P/U; Operations controls | Recipe costs, progress, block reasons and requests in existing action tooltips; parameters and cancellation in ring |
| Logistics / loading / supply | L/Y, dock | Object ring, L/Y; Operations controls | Existing copied queue/stock/policy models and tooltips; existing +/- and parameter shortcuts |
| Tactical / artillery | K, command band, dock | Single/group ring, K | Existing stance/order sectors and parameters; existing targeting owner |
| Technology | H, global mode bar | Object ring or H without selection | Existing global technology catalog; ring uses current player/session snapshot |
| Group filtering / attention / formation / focus | Inspector controls | Existing group ring | Existing full entity identities and selection adapter, no new simulation commands |
| Single identity / health / supply / readiness / power / work / inventory | Inspector | Existing tooltip beside open ring | Read-only copied selection details, including keyboard invocation |
| Group composition / coverage / damage / supply / formation | Inspector | Existing tooltip beside group ring | Cached selected-group aggregate; unknown component coverage remains explicit |
| Accepted / partial / rejected counts | Inspector and dock | Ring status tooltip and existing notification feedback | Authoritative result; no optimistic execution claims |
| Facility navigation | Operations entry / inspector shortcut | Operations entry and existing keyboard binding | Operations owns its own panel and hit geometry |
| Guidance / notifications / minimap | Shared layout | Reflowed shared layout | Independent existing owners; no removed panel captures |

The ring center, gaps, absent sectors and former rectangular panel locations pass
world input through. Transient menu dismissal retains the original gesture once.
Modal, focus, session, selection revision, full entity generation and displayed
ring revision checks remain required. No new bindings or gameplay requests.

## Qualification

Selection-status tooltip: Release presentation project build succeeded without
warnings; 29 hover-tooltip tests passed, including safe-area display profiles,
keyboard status, stale identity rejection and zero warm managed allocations.
These results qualify the first status-access change only. Composition, native
images, full test suites and PR checks require fresh evidence after integration.

## Implemented composition and checks

`PlayerActionRingHudSurface` retains the original ring renderer, bounded to 4,096
geometry vertices and eight icon quads. Its unused core-panel/portrait batches
are removed. The inspector renderer and old rectangle geometry/capture paths are
deleted. `HudOverlayPrimitives` retains the shader/glyph helpers still used by
Operations/tooltips; semantic labels and request/read-model authorities survive.
`GameplayHudLayout` version 4 owns only remaining persistent bounds. Resource,
notification, minimap, guidance and Operations consumers use that contract.

Status resolution requires a real owned/group/global ring context, current tick,
menu/selection revisions and full identity. Group coverage comes from the existing
retained selected-group aggregate. `SelectionStatusPrivacyTests` moves the old
native hidden-field privacy regression to the real tooltip. Deleted rectangle
geometry tests are replaced by remaining-owner safe-area/separation and actual
ring sector/absent-sector/closed-render checks. Existing object/group ring suites
retain all seven request kinds, parameters, cancellation, ownership, stale
identities, disabled controls and exactly-once release. The global technology
shortcut now has a direct empty-selection dispatch regression.

Checkpoint results (before final capture build): 372 focused presentation cases,
65 client ring/journey/reference cases and one native WARP status privacy case
passed. A full Release solution build has zero warnings/errors; the 46-project
reference graph is valid. The explicit native matrix contract covers 90 states
and 45 display/DPI/scale combinations (4,050 planned cases); this synthetic
coverage check does not claim those captures were executed. CPU null-backend
measurements pass 24 group cases, 120 full HUD cases and 54 active hover cases
with their unchanged allocation/geometry limits. Native timing, framebuffer
inspection, broad tests and final CI are recorded separately after completion.

The first PR CI build exposed a retained benchmark call to the deleted inspector
pointer adapter (`CombatGroupCardController.Update`, CS1061). The benchmark now
records the actual retained ring status and measures summary rebuild separately.
No budget or assertion was weakened. Failed local/CI evidence remains historical.
