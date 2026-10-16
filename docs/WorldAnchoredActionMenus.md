# World anchored action menus

## Action parity

Single owned entities use the same PlayerActionPanelController dispatcher and copied PlayerActionSnapshot as the previous dock. The global mode bar stays available. Group selection retains its existing dock and contextual commands.

| Category | Item identity | Immediate action | Parameter / queue controls | Target and result |
|---|---|---|---|---|
| Construction | BuildingId | BeginBuildingPlacement | Next / previous page | Existing placement preview, one subsequent world click; costs and rejection from existing model |
| Production | RecipeId; RequestEntity including generation | Queue recipe / pause or resume request | Cancel request; priority; OneShot / Repeat / DesiredStock; target quantity +/- | Existing facility command and pending/result feedback |
| Unit production | UnitId; RequestEntity including generation | Queue unit | Cancel request; priority | Existing facility command, costs / technology restrictions |
| Logistics | ResourceId | Apply stock policy | Remove policy; priority; minimum / target / maximum; +/- | Existing selected logistics entity and policy result |
| Supply | Fuel / ammunition / request | Apply automatic policy / request resupply | Supply priority; automatic toggle; thresholds +/- | Existing supply entity and provider restrictions |
| Tactical | Existing fixed action index | Attack / attack move / stop / hold / retreat / fire mission / cancel fire / recovery | Existing formation and targeting | Existing selected entities; next world click only for target actions |
| Technology | TechnologyId | Start research | Cancel research | Existing facility / source inventory, requirements and progress |

Every category has a paged item level and a parameter level. Item activation retains the existing immediate dispatch. Parameters can be edited before activation by entering Parameters and choosing Next item. Back, previous and next page are fixed positions. No new gameplay capabilities are introduced.

## Qualification status

Implementation and automated / native evidence are tracked below as they become available. Visual acceptance and physical interaction review require explicit review; no golden images are approved by this document.

## Implementation checkpoint

Base master: ce254914be080f92d5d3cd0ff76ccaa7ffe65938. Branch: ui/world-anchored-action-rings. Version: 0.1.387-dev (one patch increment).

The first implementation connects copied menu state, bounded annular geometry, interpolated render projection, published input geometry, the existing dispatcher, global mode controls, tooltips and central dismissal. Own single selection suppresses rectangular actions; group selection retains existing controls. Client and presentation test builds pass without warnings. The first focused geometry / existing contextual suite passes 24 tests.

Remaining: stable queue item positions, parameter value presentation, comprehensive interaction parity tests, native fixtures and resource / visual qualification, full canonical validation, current head CI and explicit visual review. The PR stays Draft while any required gate remains open. Existing texture budget and natural terminal-match failures must be reported separately and must not be weakened.
