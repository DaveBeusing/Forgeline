# Operations Rules and Recovery

## Scope and ownership

V1 operations rules are opt-in, player-owned commands for an explicitly identified processing facility. They do not replace production, distribution, cargo fleets, vehicle queues, power allocation, or movement. Each rule has an owner, a stable numeric ID, and one generation-aware facility ID. Explicit groups can be represented by separate per-facility rules; no global ownership or resource transfer is inferred.

Two bounded templates are available:

- **EnsureInputStockPolicy:** After three production ticks blocked by a genuinely missing recipe input, install one configured stock policy only when none exists. Existing enabled policies win without replacement; a manually disabled policy remains disabled. The distribution system still schedules and retries physical cargo transport. The rule does not create a second request, load cargo, reserve inventory or automatically recreate a manually removed policy.
- **PauseUntilOutputAvailable:** After three ticks blocked by full output capacity, pause the exact active production request using the existing owner-checked production command. Once its real output inventory can accept the recipe outputs, resume only that same request. Manual pause/unpause commands suppress the rule until explicitly reconfigured. The production system remains responsible for power, inventory reservation and cycle commitment.

Recovery of existing logistics requests after missing stock, truck capacity, route or docking constraints belongs to the normal distribution/cargo systems; it is not independently retried. Power interruptions cannot authorize free manufacturing.

## Command and rule lifecycle

PlayerCommandGateway.SubmitOperationsRule submits Upsert, SetEnabled or Remove as scheduled fixed-tick OperationsRuleCommand instances. OperationsRuleDefinition includes owner, rule ID, facility, template, resource and thresholds for the input rule, priority and cooldown. Initial creation requires expected revision zero; subsequent changes require the current revision. All operations validate ownership and entity generation on execution, returning an explicit OperationsRuleResult with the ordinary player command receipt.

Limits: 32 rules per player, eight per facility, 64 evaluated per tick, three stable trigger ticks, five to 10,000 ticks of cooldown and a maximum of 16 command attempts per rule revision (including rejected attempts). Deterministic stable owner/ID ordering prevents request floods. Recovery of a request the rule has already paused is allowed even at the action limit to avoid stranding a request. Disabled rules issue no commands. Destroyed or foreign facilities disable their rule. Paused, ending or completed matches do not execute these rules.

## Read models and diagnostics

OperationsRuleSystem.Capture(owner) returns a detached read-only copy for the supplied owner. It exposes rule revision, enabled state, evaluation tick, last successful action tick, action count, last attempt tick, attempt count, result, pending state and the exact managed request. Rejections include InvalidDefinition, RevisionMismatch, LimitReached and TargetUnavailable. A successful policy command does not certify source stock, assigned transport or delivery. See [Operations Diagnosis](OperationsDiagnosis.md) and [Logistics Setup](LogisticsSetup.md) for distinct physical stock, reservation, backlog, assigned load and delivery quantities. The existing Operations window provides the bounded selected-facility controls below. No generic rule builder, global automation or additional notification stream is introduced.

## Save, restore and replay

Rule configuration is encoded through the existing scheduled-command history as ReplayCommandKind.OperationsRule. Replay records the owner, revision, target, template, thresholds, priority, enabled state and cooldown. Deterministic reconstruction replays these commands and re-evaluates automation at its original fixed ticks. Existing save documents without rule commands remain compatible with the existing schema; rule state is not independently serialized or duplicated in presentation. As with other commands, recording requires a complete command history.

## Qualification

OperationsRuleTests covers revision/ownership/target validation, blocked-input policies without duplicate demand, manual disabled-policy precedence, output-full pause/resume, manual override and codec preservation. Production, logistics, save/replay, architecture and canonical CI gates remain mandatory. See [Development](Development.md).

## Player controls

Select exactly one owned processing facility in the world and open Operations from the resource/power strip or an existing OPS entry. Choose RULES; selecting an overview row alone does not retarget the world selection. Missing, foreign, destroyed or uncontrollable selections show an explicit unavailable state. The rule section replaces the existing panel content and adds no permanent HUD area.

1. Keep NEW DRAFT selected, or cycle the rule selector to an existing rule. New numeric request identities are independent of row position; DETAILS includes the full rule reference and concrete revision.
2. Choose INPUT STOCK POLICY or OUTPUT RECOVERY. For the input template, queue the intended processing recipe first and select one of its copied inputs. Other resources are unavailable.
3. Cycle MIN, TARGET, MAX, PRIORITY and COOLDOWN. Quantities reuse LogisticsQuantityEditor: directional 50-unit steps, inventory capacity and preserved legacy values. Rule minimum zero is valid; target must remain positive. Existing non-step values are copied unchanged until a deliberate field edit. Small capacities that cannot support a new 50-unit draft remain unavailable. Cooldown uses the authoritative 5–10,000-tick bounds and shows seconds from the copied simulation tick duration.
4. APPLY is the only submission of a new or edited definition. A new enabled draft does nothing until APPLY. Creation sends expected revision 0; updates send the copied numeric revision. After acceptance, unchanged APPLY is unavailable.
5. ENABLE/DISABLE and REMOVE use the same normal gateway. ENABLE explicitly rearms the rule revision and its attempt budget; it is not a promise of unlimited activity. Dirty edits must be applied or reloaded before a direct enable/disable action. RELOAD discards local changes and copies current settings; BACK discards the unapplied draft.

With default bindings, Tab focuses the next control and Enter activates it. T cycles the edited field, M changes template, Left/Right adjust, and C removes the selected existing rule. The detail pages show the active rebound prompts. In the Operations overview, Enter opens the initially focused RULES entry; Tab switches the entry/close focus. Existing facility controls leave Operations before taking keyboard ownership.

Pointer actions require a press and release inside the same control with unchanged target/draft identity. Held keys, pending submissions and rapid repeated change clicks cannot submit another mutation. A short presentation-only duplicate-click guard is separate from rule cooldown. Focus loss, modal interruption and resize cancel gestures; an unapplied draft survives temporary missing requested data while the same authorized selection remains. Selection, owner/generation, session and terminal changes invalidate stale controls. Match pause blocks input rather than advancing rules.

## Feedback and manual precedence

The panel distinguishes a local draft, host submission, accepted/rejected command and the actual copied rule result. Essential correlated results are consumed even if snapshot replacement skips a tick. Boundary rejection does not retry automatically. RevisionMismatch displays current copied state and revision while retaining the rejected local upsert for deliberate review and a new APPLY; it never enters an automatic retry loop.

DETAILS pages retain full thresholds, priority, cooldown, revision, evaluation tick, last successful action, attempts/remaining attempt budget, last attempt and the named exact managed request where copied. A policy action confirms configuration only: requested, reserved, loaded and delivered resources remain separate logistics facts. Manual pause/resume suspends output automation. Removing or disabling a rule releases only its own pause, including yielding to a manual pause submitted in the same tick before the AI observer phase.

## Executable UI recovery coverage

OperationsRuleControlsTests drives selection, mouse/keyboard draft editing, APPLY, the normal request dispatcher/gateway, completed-tick confirmation and real production. Both templates have serialized save/replay hash comparisons. The output-full case checkpoints an actual managed pause with a queued downstream Ammunition recipe; consuming finite shared Steel/Electronics makes real output capacity available after restore and the same Steel request resumes. No inventory removal, resource grant or forced production result is used for that recovery. Input-policy reconstruction retains the real stock policy without treating it as delivery. No new authoritative component or persistence schema is introduced.
