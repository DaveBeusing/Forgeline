# Operations Rules and Recovery

## Scope and ownership

V1 operations rules are opt-in, player-owned commands for an explicitly identified processing facility. They do not replace production, distribution, cargo fleets, vehicle queues, power allocation, or movement. Each rule has an owner, a stable numeric ID, and one generation-aware facility ID. Explicit groups can be represented by separate per-facility rules; no global ownership or resource transfer is inferred.

Two bounded templates are available:

- **EnsureInputStockPolicy:** After three production ticks blocked by a genuinely missing recipe input, install one configured stock policy only when none exists. Existing enabled policies win without replacement; a manually disabled policy remains disabled. The distribution system still schedules and retries physical cargo transport. The rule does not create a second request, load cargo, reserve inventory or automatically recreate a manually removed policy.
- **PauseUntilOutputAvailable:** After three ticks blocked by full output capacity, pause the exact active production request using the existing owner-checked production command. Once its real output inventory can accept the recipe outputs, resume only that same request. Manual pause/unpause commands suppress the rule until explicitly reconfigured. The production system remains responsible for power, inventory reservation and cycle commitment.

Recovery of existing logistics requests after missing stock, truck capacity, route or docking constraints belongs to the normal distribution/cargo systems; it is not independently retried. Power interruptions cannot authorize free manufacturing.

## Command and rule lifecycle

PlayerCommandGateway.SubmitOperationsRule submits Upsert, SetEnabled or Remove as scheduled fixed-tick OperationsRuleCommand instances. OperationsRuleDefinition includes owner, rule ID, facility, template, resource and thresholds for the input rule, priority and cooldown. Initial creation requires expected revision zero; subsequent changes require the current revision. All operations validate ownership and entity generation on execution, returning an explicit OperationsRuleResult with the ordinary player command receipt.

Limits: 32 rules per player, eight per facility, 64 evaluated per tick, three stable trigger ticks, five to 10,000 ticks of cooldown and a maximum of 16 actions per rule revision. Deterministic stable owner/ID ordering prevents request floods. Recovery of a request the rule has already paused is allowed even at the action limit to avoid stranding a request. Disabled rules issue no commands. Destroyed or foreign facilities disable their rule. Paused, ending or completed matches do not execute these rules.

## Read models and diagnostics

OperationsRuleSystem.Capture(owner) returns a detached read-only copy for the supplied owner. It exposes rule revision, enabled state, evaluation tick, last action tick, action count, result, pending state and the exact managed request. Rejections include InvalidDefinition, RevisionMismatch, LimitReached and TargetUnavailable. A successful policy command does not certify source stock, assigned transport or delivery. See [Operations Diagnosis](OperationsDiagnosis.md) and [Logistics Setup](LogisticsSetup.md) for distinct physical stock, reservation, backlog, assigned load and delivery quantities. No new rule-builder UI or player notifications are added.

## Save, restore and replay

Rule configuration is encoded through the existing scheduled-command history as ReplayCommandKind.OperationsRule. Replay records the owner, revision, target, template, thresholds, priority, enabled state and cooldown. Deterministic reconstruction replays these commands and re-evaluates automation at its original fixed ticks. Existing save documents without rule commands remain compatible with the existing schema; rule state is not independently serialized or duplicated in presentation. As with other commands, recording requires a complete command history.

## Qualification

OperationsRuleTests covers revision/ownership/target validation, blocked-input policies without duplicate demand, manual disabled-policy precedence, output-full pause/resume, manual override and codec preservation. Production, logistics, save/replay, architecture and canonical CI gates remain mandatory. See [Development](Development.md).
