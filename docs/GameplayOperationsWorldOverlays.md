# Gameplay and Operations world overlays

Operations world annotations are composed by `OperationsWorldOverlaySurface`
through the existing `GameplayHudRenderer`. They use its native-resolution HUD
pass, the existing primitive pipeline and shared font atlas. No Client hook,
render-pass reordering or simulation/replay change is required.

## Observations and authorization

`OperationsWorldOverlay` resolves the existing Operations and diagnosis envelopes.
Session, completed tick and player must agree with the current presentation
snapshot. Positions come exclusively from copied render instances whose full
entity ID, owner and World visibility match. A hidden or missing endpoint suppresses
its connection; no live ECS/intelligence lookup or guessed position is used.

The open Operations filter supplies facility context. Its selected facility takes
priority, followed by authorized current hover, matching world selection, blocked
facilities and remaining facilities. Live selection must match the copied
`SelectedUnitOrderSnapshot`, including entity generations and selection order.
Pending selection, drag selection, placement, pause/help, inconsistent viewport
geometry and unavailable player data suppress these annotations. Old marker
references are cleared before every rebuild.

Labels show observed cause/status, stock, queue position, requested quantity,
confirmed incoming quantity and loaded cargo when those fields are available.
Missing quantities are omitted, never fabricated or inferred from nearby objects.
Diagnosis subjects remain separate observations; quantities are not added across
policy, request and physical truck rows.

Directional links have explicit meanings:

| Appearance / caption | Copied source | Meaning |
| --- | --- | --- |
| Quiet dashed / NETWORK LINK | `OperationsRoute` | Existing enabled topology, not proof of transport |
| Red dashed / DISABLED LINK | Disabled `OperationsRoute` | Reported unavailable topology |
| Quiet dashed / CONVEYOR CONNECTION | Typed ConveyorConnection diagnosis link with current owned port identities | Actual port continuity, with no invented truck request or delivery promise |
| Amber dashed / TRANSPORT REQUEST | `OperationsDiagnosisLink` with a real request ID | An observed request, not guaranteed delivery |
| Green solid / LOADED CARGO | Active request, assigned truck and positive copied loaded-minus-delivered progress, already matched to its live order by the player extractor | Observed remaining transport load, not completed delivery |

Request loaded-minus-delivered progress is labeled LOAD; direct truck inventory
is labeled ON BOARD. The existing player extractor checks truck generation,
endpoints, resource and active request state before publishing loaded progress.
Inactive, missing-truck and pending requests cannot produce a green loaded link.

Markers are passive. Existing Operations rows, Focus Facility, Focus Next and
Open Existing Controls provide the interactive drill-down. The existing TRACE NEXT
control follows current copied causes/connections and retains visible branch,
cycle, missing-observation and omission information. Focus resolves only
an owned World-visible full-generation entity in the current snapshot. A press
that began in Operations stays captured through release, including pointer exit,
layout/session transitions and cancellation; release cannot activate a new row
after the pressed row's entity changes.

## Bounded composition

The render owner reuses fixed marker/link arrays and bounded lookup storage:

- At most 64 markers and 64 directed links; marker admission is priority then
  full entity ID, and link admission is request priority then endpoint IDs.
- At most the first 1,024 IDs of a matching published selection are considered.
  The 256-facility/128-diagnosis-row and 256-route/64-request-link source limits
  bound position lookup to 1,024 identities. Large instance snapshots are scanned
  once without retaining a second snapshot or allocating per instance.
- At most 12 facility captions, reduced to four beyond 600 m camera distance;
  at most four connection captions. Crowded captions/glyphs are omitted in
  deterministic priority order. Existing HUD/radial obstacles and viewport bounds
  take precedence over world annotations.
- A 4,096-vertex primitive batch uses at most 98,304 upload bytes per frame slot.
  Existing font limits, GPU budgets and snapshot buffers are unchanged. This is
  a bounded additional HUD surface, not a measured performance improvement.

Annotations are static at every quality tier, including Reduced Motion. Glyphs,
strokes and captions scale with the native HUD layout; reduced world resolution
does not reduce their output resolution. No moving flow animation or speculative
convoy is introduced.

Existing selected-order drawing now accepts only matching session/tick/player
data and owned visible origins. It admits at most 1,024 connection strokes and
1,024 seven-stroke destination markers, fitting the existing 8,192-line buffer.
Order captions reject stale ticks. Existing conveyor annotations require real
current conveyor data, matching selection/hover and owned visible endpoints;
they retain at most 32 focused pieces and eight labels. Docking geometry continues
to use the existing authoritative `LogisticsZoneSnapshot` path.

## Automated contracts and separate manual acceptance

The bounded chain integration adds actual paid production/connection/cycle tests,
held-focus authorization regressions, retained HUD/overlay zero-allocation checks
and a dense CPU observation report. See [Operations diagnosis](OperationsDiagnosis.md)
for exact evidence, source-specific cost observations and limits. Current marker
priority includes the copied selected chain before unrelated blocked facilities.
The new typed conveyor line remains distinct from request and loaded-cargo lines.
No primitive, marker, link or caption budget is increased.

These automated checks do not establish native human readability, physical input,
reference-hardware performance or accessibility acceptance. Perform the following
manual cases separately; the complete required CI gates remain in place.

1. Select a powered/unpowered processor, pause production, exhaust an input and
   fill output stock. Open Operations filters and select/focus the facility.
   Verify world cause/stock captions and the existing control/deep-link paths.
2. Observe an actual request before dispatch, a loaded Cargo Truck, disabled
   topology and a real occupied loading queue. Verify the distinct line captions
   and queue values; no requested quantity may be presented as delivered cargo.
3. Hover foreign or fog-hidden entities and endpoints. Restart a match, remove
   and recreate an entity, switch selection before the next tick and change hover
   across HUD/world boundaries. No stale/private marker, amount or link may remain.
4. Select more than 1,024 units and view a dense base. Verify deterministic
   admission, caption decluttering and bounded vertex/line counts, including
   shared destinations and missing/off-screen endpoints.
5. Press an Operations row/control, drag outside and release. Repeat across
   pause, focus loss, restart, DPI changes and resize. No navigation/control may
   activate from a different row or leak the captured release into world input.
6. Inspect real conveyor Flowing/Blocked/Offline states, ownership and endpoint
   removal. Inspect real dock/holding strokes; no fabricated flow or queue geometry
   is accepted.
7. Review tactical/strategic cameras at Low/Medium/High/Ultra, native/scaled world
   output, several DPI/UI scales and Reduced Motion. Check readable type, HUD/ring
   occlusion and unchanged input capture.
8. Run the existing Presentation/Input/native suites and complete required CI as
   documented in [Development](Development.md). Measure representative frame and
   allocation costs before claiming performance acceptance.
