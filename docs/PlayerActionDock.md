# Player Action Dock

## Purpose

The production player action dock replaces the former text-oriented player action palette with one RTS-specific visual and interaction surface while preserving the existing presentation-to-game request boundary.

The dock is presentation-owned. It consumes immutable `PlayerActionSnapshot`, `PlayerExperienceSnapshot`, targeting view state, and `GameplayHudLayout` geometry. It does not query or mutate live simulation state.

## Modes and shortcuts

The primary selection-command panel sits between the selection inspector and minimap.
Combat-eligible selections expose Attack, Attack Move, Stop, Hold, Retreat and Fire
Mission through existing handlers. Mixed selections operate on eligible units only;
disabled controls retain their reason and hover explanation. No Patrol or Repair is
inferred. Command Core opens Build; copied unit-production, processing, logistics and
supply capabilities open their advanced modes. Empty selection prompts selection while
the advanced mode bar remains available.

Primary tactical buttons activate by click. The keyboard alternative is `K`, `Tab` to
the desired card, then `Enter`; these are not new single-key combat bindings. A facility
button changes mode without submitting production. All seven advanced modes remain.
Hover explanations use the same captured availability as rendering and dispatch.

Primary commands require matching snapshot/action session and tick identities. Captured
pending commands disable tactical controls, and local activation is limited to once per
captured tick. Targeting and pending labels show intent; copied command-result feedback
establishes acceptance or rejection. Stop/Hold cancel active target or placement gestures.

Shared bounds own the entire primary panel, including disabled/empty space. Retained
mouse sequences recognize quick clicks and consume each press once; resize/session
transitions discard the affected press. High DPI/UI scale fits the primary panel below
the advanced dock. Extreme narrow/high-scale profiles fit text compactly; hover exposes
full disabled reasons.

| Mode | Shortcut | Current gameplay coverage |
| --- | --- | --- |
| Build | B | Building selection, costs, resource availability, deposit requirement, placement transition |
| Process | P | Recipes, queue, priority, one-shot/repeat/desired-stock, progress, block state, pause/resume, cancel |
| Units | U | Unit cards, real costs, production ticks, queue, progress/block state, cancel, rally presence |
| Logistics | L | Stock policies, min/target/max, priority, distribution/bottleneck/failure, cargo state |
| Supply | Y | Supply/Fuel/Ammunition, automatic thresholds, provider state, priority, explicit resupply |
| Combat | K | Attack, Attack Move, Stop, Hold, Retreat, Fire Mission, Cancel Fire Mission, Recovery |
| Technology | H | Technology domains/phases, real material/facility/power requirements, blocked state, progress, start/cancel |

`Tab` selects the next card. `Enter` activates the selected action. `C` cancels/removes supported queued or policy items. `T` cycles the mode's primary setting, `M` cycles its secondary setting, and Left/Right adjust the active numeric field.

Pointer interaction uses the same model. Mode buttons change mode, cards select context, and footer controls perform activation/cancel/settings/adjustment. The open ActionDock captures pointer input before world selection, movement, placement, or tactical targeting.

## Availability and authoritative validation

The dock disables an action only when the copied read model already provides enough information to know it is unavailable. Examples include missing build resources, missing processing/unit-production inputs, no combat-eligible units, no identified Attack target, no artillery/ammunition, and no active fire mission to cancel.

A disabled reason is presentation feedback, not simulation authority. An enabled click still produces only a `PlayerActionRequest`. The existing dispatcher and simulation systems revalidate ownership, resources, placement, production/logistics/supply state, intelligence, ranges, and tactical legality. Resolved success or rejection is displayed from copied command feedback.

Semantic icons are not a feature contract. Patrol and Repair icons may exist in `RtsUiIconCatalog`, but the dock does not expose those commands because corresponding player gameplay behavior is not implemented.

## Build

Construction cards use `PlayerConstructionActionReadModel` and show the authored building identity, semantic role icon, captured cost availability, and resource-deposit requirement. Activating an available card enters the existing building-placement flow. Placement validity and final rejection remain owned by the placement preview/build-command path.

## Processing

Processing uses the selected owned production facility read model. Recipe cards expose captured inputs/outputs; queue cards expose active/waiting/paused state, priority, and production mode. The header exposes facility progress and existing block reasons.

Priority and one-shot/repeat/desired-stock controls remain controller state until a request is submitted. Desired-stock quantity adjustment is derived from the selected recipe's copied output information. Queue cards can pause/resume through activation and cancel through the existing cancel request.

## Unit production

Unit cards use the immutable unit catalog-derived action read models, including real cost availability and production ticks. Queue entries expose active/waiting/paused state and priority. The facility header exposes progress, block reason, selected priority, and whether an authoritative rally point is currently present.

Queue cancellation uses the existing unit-production cancel request. The dock does not invent a rally-edit command; it only reports current rally state because that is what the read model exposes.

## Logistics

The logistics view exposes per-resource current quantity and existing stock policy values. Minimum, target, maximum, priority, enabled state, distribution lifecycle, bottleneck and transport-failure data remain sourced from the copied logistics read model.

When the selected owned entity is a cargo transport, the header uses its copied cargo quantity/capacity plus lifecycle, wait reason, or failure state instead of inventing stock-policy state for that vehicle.

## Supply

Supply mode shows the copied unit supply state, Fuel/Ammunition fractions, automatic-resupply enablement and thresholds, provider state, provider quantities/rejections where available, and supply priority. The explicit Resupply card submits the existing request rather than transferring resources from UI code.

## Combat and artillery

Combat mode uses only the owned tactical selection and intelligence-bounded target read model. It shows requested/eligible/rejected selection counts, common or mixed order state, identified targets, critical-supply and resupplying counts, formation context, and supported tactical actions.

Attack remains identified-target gated. Attack Move and Retreat enter their existing targeting flows. Stop and Hold submit immediate commands. Fire Mission requires artillery with ammunition and uses the existing detected/identified-contact or coordinate targeting path. Cancel Fire Mission is enabled only when the copied artillery state contains an active mission. Recovery uses the existing retreat/recovery request.


## Technology

Technology mode is a presentation over authoritative simulation state. It groups the current Directorate technology catalog by domain and exposes phase, prerequisites, real material costs, required facility, required power fraction, current progress, completed state, and the authoritative reason why a technology cannot currently start or continue.

Research does not spend a universal currency. Starting research creates a simulation-owned request through the normal player command boundary. The research system validates prerequisites, the required completed player-owned facility, power availability, and physical materials in the source inventory. Materials are consumed once when research can actually begin. Progress advances only during deterministic simulation ticks and pauses in an explicit blocked state when a continuing requirement is unavailable.

The initial representative Directorate catalog contains Industrial Standardization (Industry/T1), Logistics Coordination (Logistics/T1), Mechanized Systems (Warfare/T2), and Sensor Fusion (Intelligence/T2). The latter three depend on Industrial Standardization. This is architecture-validation content, not the final production technology tree.

Industrial Standardization unlocks the Field Engineering capability. Combat Engineer production is capability-gated by that unlock in the simulation and is also reported as technology-locked in its copied unit-production read model. The UI does not unlock units or technology directly.

Technology mode uses `H`. `Enter` submits start research when the selected technology is available; `C` submits cancellation when the selected technology owns an active research request. Both actions remain subject to authoritative command execution and feedback.

## Lifetime and terminal behavior

The controller resets open mode, editor state, pending presentation request, pointer capture, and key-edge state when `SimulationSessionId` changes. Stale or mixed selection naturally removes single-entity production/logistics/supply read models, leaving those contextual modes with no activatable item.

When the player experience becomes terminal, the controller closes the dock, clears pending presentation requests, consumes the action-key edges, and rejects pointer capture. The renderer suppresses the dock for that terminal snapshot. A restarted match receives a new session and starts from clean dock state.

## Validation

Focused tests cover existing request creation plus dock hit testing, pointer capture, card selection versus explicit activation, disabled input actions, session replacement, stale context, terminal behavior, supported tactical action inventory, and DPI/UI-scale-bounded layout. Repository CI remains responsible for the Release solution build, project-reference checks, client/window qualification, and the broader test suite.
