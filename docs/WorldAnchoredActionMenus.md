# World anchored action menus

## Current sector hover and selection inspector

A hovered ring sector receives a lower-luminance fill at the same 0.30 material alpha and a dedicated high-contrast 1.5–4 physical-pixel arc inside **only** its outer radius. Pressed, disabled and pending retain distinct visual identities. This arc is decorative: input remains the published annular sector geometry, and inactive animation owns no pointer capture.

For an authorized object or selection group, the existing status/tooltip owner retains the inspector while the pointer moves over a ring sector. An inspector-aware ring fit reserves available right-hand safe-area width; a bounded placement fallback avoids the minimap, alerts, guidance, operations and ring hit surfaces. The card uses 0.38 background alpha and high-contrast text with one blank row after its title. Its snapshot-bound health/supply/work/stock/progress, valid action-model logistics/production/queue summary, and last command results are read-only. Missing capability facts are omitted; an available numeric field with invalid data is rendered N/A instead of a guessed value. Existing global rings do not manufacture an object inspector. All native current-source UI qualification and human contrast acceptance remain outstanding.

Current single/group rings have no rectangular mode bar, dock or selection inspector. Existing mode shortcuts and selection release open the rings; the shared tooltip preserves authorized status and results. See [current HUD parity and qualification](HudSurfaceConsolidation.md).

Current validation and remaining gates: [qualification report](WorldAnchoredActionMenuQualification.md). Checkpoints below retain their original source identities.

## Current radial material and motion contract

Single, group, category, item and parameter rings render through the same radial sector pipeline. Every settled background sector uses straight-alpha compositing with effective alpha **0.30** (70% transparency), including disabled, hovered, pressed and pending tint states. Color-state tints come from the shared command-interface palette; icons and text retain independently specified contrast and receive only the presentation visibility factor. The one-pixel inner/outer alpha fringe does not introduce another opaque background pass.

A 160 ms SmoothStep animates the visible ring alpha without changing the published annular input geometry. Closing dismisses hit ownership immediately even while the previous frame fades. Reversal reuses the current alpha as its starting point. Reduced UI motion, Low quality, pause/suspended presentation, invalid projection, presentation clock rollback/stall, and viewport/DPI/UI-scale changes settle or discard stale visuals immediately. Entity, session and selection changes never leave a clickable outgoing ring. Native display/physical-monitor acceptance remains a separate qualification gate; previous screenshots and measurements are historical, not evidence for this revision.

## Action parity

Single owned entities use the same PlayerActionPanelController dispatcher and copied PlayerActionSnapshot as the previous dock. Existing global shortcuts remain available, including empty-selection global actions. Groups use the same ring with their existing tools.

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

## Geometry and lifetime

The input owner copies entity index/generation, session, selection revision, menu revision and eight value-type slots. The render owner projects only the permitted interpolated RenderWorld instance with the current camera. Ground footprint corners determine the inner hole within a bounded size range. Screen-space annuli remain round. Deterministic obstacle-edge candidates choose the smallest valid translation inside the safe area; a thin noninteractive tether retains the actual projected anchor. If no readable placement fits, the ring closes and the global bar remains available.

The renderer publishes resolved interaction geometry after presenting, before decorative animation. Input uses that exact publication, never a separate camera projection. An invalid/offscreen/dead/foreign anchor closes the controller; returning onscreen cannot reopen it. Opening waits for the selection decision and requires a fresh selection release or explicit category shortcut. Each press is bound to its session, entity generation, selection/menu revision, slot kind and item key. A release validates the same identity against the current action snapshot. Annular gaps, the inner hole, tether and removed rectangular surfaces remain world input. Central transient routing owns dismissal and release provenance. Selection commit reconciles central identity before opening a new ring.

Each item page retains four stable ID slots; queue removal leaves a hole instead of shifting surviving entries. Page navigation refreshes the bounded slot set. Parameters use the existing six footer operations, Next item and Back. Availability/pending changes do not rotate or reorder controls. Pending/result, cost, requirement and full title explanations reuse existing copied feedback and tooltips. Global mode keys and existing parameter shortcuts retain their bindings.

The existing action renderer retains its pipeline, per-frame vertex buffers, shared atlas leases and text owner. Annular segments use 20 subdivisions per sector and a one-pixel radial alpha fringe. Alpha follows a 160 ms SmoothStep on monotonic presentation time. Closed/fading visuals own no input; reduced motion, Low quality and interrupted clocks settle immediately; a new object context clears the previous track and starts a fresh reveal on a continuous clock. No gameplay command or camera change is generated by projection or animation.

## Integration checkpoint

Full solution Release build passes with zero warnings/errors. Sixteen ring integration tests and 59 focused geometry / existing action / nonmodal tests pass. Added explicit native reference states for all seven categories, parameters, pending, edges, tooltips, half reveal/close and offscreen anchors; the matrix contract covers 82 states × 45 output/DPI/scale combinations (3,690 planned cases). This count is coverage planning, not completed native evidence. Native runtime comparison includes 128 open/close/context cycles and injected 30/60/144 Hz motion sampling. Next step: committed-source native capture and canonical qualification, then archive original candidate evidence and report remaining gates.

## Native iteration

Source 16434321: fourteen 1080p reference states completed eight identical native pixel repeats per state. Initial native runtime churn passed 128 cycles: live resources 112 → 112, atlas buffers 52 → 52, eight resident textures, 28 MiB shared atlas pages. Same-device complete clear/HUD/text comparison measured legacy expanded dock CPU submit p50 5,033.2 µs / GPU p50 2.736416 ms, single closed 5,027.7 µs / 2.384032 ms, ring 5,023.0 µs / 2.487744 ms. These are sequential local samples including Present/waits, not gameplay FPS or universal improvement claims. Original outputs remain under artifacts/rings-reference-16434321 and artifacts/rings-runtime-16434321.

Inspection identified labels too close to angular boundaries. The following refinement increases the free center/band spacing, bounds each icon/title/value column inside its sector (asserted at all eight positions), and uses shorter navigation labels. Closed animation state also retains the single-object migration flag so it cannot restore legacy visuals. Requalification on the new source is required; previous captures are historical iteration evidence.

Final review also covers a render snapshot advancing ahead of input: ring slots re-resolve their copied stable item keys against the snapshot actually rendered, leaving removed requests empty. They cannot label a reused queue index as a different action. The parameter item title follows the same resolved key. A newly opened ring has no synthetic first-sector hover before its first presentation.

The final input audit includes ordinary mouse-down and mouse-up delivered in different frames. InputState clears per-frame press positions, so the selection opener retains only the authorized world-gesture arm/sequence across those frames, then consumes it after selection release. Coalesced down/up and split events both open exactly once. Removed item slots can accept a newly arrived queue ID while surviving IDs retain their sectors; a press armed for the removed ID cannot activate its replacement.

A final first-context motion contract primes the idle presentation clock, then requires 0 / 0.5 / 1 opacity at 0 / 80 / 160 ms for the first owned entity. Native injected 30/60/144 Hz traces now assert intermediate opacity for both opening and closing and immediate settling under reduced motion.

Source 368973ff full-suite attempt retained under artifacts/rings-full-tests-368973ff.log: native Graphics.Tests terminated with 0xC0000005 after a ten-second GPU fence shutdown warning. Completed cases had 2,335 passes, nine opt-in skips and no failed assertions; the process failure still makes that run unsuccessful. Packaging was not started. The follow-up isolates Graphics.Tests and schedules full-suite modules sequentially without changing assertions, budgets, skips or terminal rules.

## Native test-owner isolation

The 5445b66e full run with sequential modules still encountered a native Graphics.Tests process access violation (2,339 completed passes, nine existing opt-in skips, zero failed assertions; process failure remains unsuccessful). The complete Graphics.Tests project passed separately with `--parallel none`: 76 passes and the same two opt-in skips. The graphics test assembly now serializes its collections so independent native window/device failure-path owners cannot race. No test case, assertion, production behavior, timeout, budget or explicit concurrency inside a test is changed. Both failed logs are retained. The final full solution is requalified with this scheduling isolation; successful partial runs are not substituted for a successful full invocation.

The initial isolation annotation was rejected by the pinned xUnit 4 compile-time deprecation. It is corrected to `Parallelization(Mode = ParallelMode.None)`. The no-build solution run mistakenly started after that rejected build was canceled and is not qualification evidence. Subsequent qualification explicitly stops on any build failure before executing tests.

## Global cursor anchor and contextual navigation

Without a selected entity, registry-mapped category keys B/P/U/L/Y/K/H open the existing category's item ring at a current, safe-area-validated pointer location. These are cursor-anchored presentation states with no fabricated EntityId; if pointer validity is lost, the global menu closes without transferring input. Unsupported selection-dependent actions have a disabled `No target` sector and an owner requirement, while eligible global actions still dispatch via the existing player-command boundary. Single/group rings continue to use their original entity and selection identities. Q/E page backward/forward only when the current item/group submenu has multiple real four-item pages; root and parameter levels do not invent pages or take camera input. Navigation invalidates old ring geometry/revision so delayed pointer releases cannot invoke an outdated slot.
