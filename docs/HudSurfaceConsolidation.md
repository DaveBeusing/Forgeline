# HUD surface consolidation

## Integration baseline

Source baseline: `master` `d4c7b62077c98500d6309cdd6c6dbf5e6bd6490c` (0.1.406).
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
