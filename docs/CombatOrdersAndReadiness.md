# Combat Orders, Tactical Behavior, and Readiness

## Purpose

Phase 5 connects player combat intent, movement and formations, battlefield intelligence, target acquisition, weapons, Fuel/Ammunition, battlefield resupply, destruction, and derived readiness into one authoritative fixed-tick battlefield loop.

Commands express intent. Simulation systems remain authoritative for navigation, sensing, firing, damage, supply transfer, and entity lifecycle.

## Combat Orders

The tactical command layer supports:

- `Attack`
- `AttackMove`
- `Stop`
- `HoldPosition`
- `Retreat`

Each accepted unit receives a `CombatOrderState`. Multi-unit orders may also create `CombatGroupIntent` and `CombatGroupMember` state for deterministic group coordination and aggregate readiness.

A later combat order replaces prior combat intent. New combat orders cancel an active `ResupplyOrder`; commands never create Fuel, Ammunition, targets, damage, or hidden knowledge directly.

## Phase Ownership

```text
InputCommands
    combat commands create intent
        ↓
OrderProcessing
    TacticalOrderPreparationSystem
        ↓
AiDecisions
    TacticalTestOpponentSystem
    AutomaticResupplyDecisionSystem
        ↓
NavigationRequests
    existing navigation / formation routing
        ↓
Movement
    existing GroundMovementSystem
        ↓
Sensors
    BattlefieldIntelligenceSystem
    TargetAcquisitionSystem
    TacticalCombatSystem
        ↓
Combat
    direct fire and artillery
        ↓
DamageResolution
        ↓
Supply
    BattlefieldSupplySystem
        ↓
EntityLifecycle
        ↓
SnapshotEvents
    CombatReadinessSystem
```

Registration order inside the same phase remains deterministic.

## Attack

`AttackCommand` requires an explicit target entity, but accepting the command does not grant knowledge of that entity.

`TacticalCombatSystem` first resolves the attacker's faction and verifies that the target is currently `Identified` for that faction before reading the target transform.

If the target is legitimate:

- inside weapon range and permitted by `FirePolicyState`: stop and engage;
- outside weapon range but inside the pursuit leash: issue a normal `MovementOrder` toward the current legitimate position;
- outside the pursuit leash: hold;
- no longer identified: clear the exact target and wait for legitimate intelligence.

The pursuit anchor is the unit position when the order is accepted.

## AttackMove

`AttackMoveCommand` reuses the normal movement/formation pipeline.

Target discovery remains owned by `TargetAcquisitionSystem`, including faction checks, current intelligence, target class, range, Fire Policy, target priority, distance, and deterministic entity-ID ties.

When a legitimate target is acquired, `TacticalMovementConstraint` pauses Ground Movement without discarding the underlying route/group intent where practical. When the engagement ends, movement is re-enabled and the original destination resumes.

`GroundMovementStatus.TacticallyPaused` represents this state explicitly.

## Stop and Hold Position

`StopCombatCommand` clears movement/navigation intent and the current weapon target, disables automatic targeting, and prevents tactical movement.

`HoldPositionCommand` clears movement intent and anchors the unit. Target acquisition remains available according to Fire Policy, but movement remains disabled. V1 Hold Position therefore has a zero pursuit leash.

## Retreat / Disengage

`RetreatCommand` reuses `MoveEntitiesCommand`, hierarchical navigation, formation movement, and Ground Movement. It disables automatic combat targeting and clears the current target.

No teleport, speed bonus, Fuel exemption, or hidden route knowledge is granted.

If resupply temporarily takes precedence, the stored Retreat destination can resume afterward.

## Fire Policy

The tactical layer reuses the existing `FirePolicyState`:

- `HoldFire`: no target is permitted;
- `ReturnFire`: only the retaliation target is permitted;
- `FireAtWill`: valid targets may be engaged.

Combat intent does not bypass Fire Policy.

## Combat Groups and Target Assignment

A multi-unit tactical order creates a lightweight `CombatGroupIntent`. It does not replace the existing movement-group model: combat groups represent tactical intent/readiness, while movement groups own shared routing/formations.

For AttackMove and Hold Position groups, candidates are currently `Identified` enemies only. Per-member compatibility still requires weapon target-class support, range, and Fire Policy.

Assignments prefer:

1. fewest already assigned group members;
2. higher `TargetPriority`;
3. shorter range;
4. stable entity identity.

This avoids every compatible unit blindly selecting the same target while remaining deterministic.

## Pursuit Leash

Attack uses its acceptance position as the pursuit anchor and may chase only while the target's current legitimate position remains within the configured horizontal leash.

AttackMove uses its tactical leash to bound engagement behavior while retaining its advance destination.

Hold Position has no chase allowance.

## Automatic Resupply

`AutomaticResupplyPolicy` defines Fuel and Ammunition fraction thresholds.

`AutomaticResupplyDecisionSystem` does not transfer resources. It delegates to the shared `BattlefieldResupplyPlanner`, which chooses an enabled friendly provider deterministically and creates a real `ResupplyOrder` plus a normal `MovementOrder`.

Actual Fuel and Ammunition are transferred only by `BattlefieldSupplySystem` in the Supply phase.

The manual `ResupplyCommand` reuses the same planner so manual and automatic provider-selection semantics cannot diverge.

While resupplying, tactical engagement yields to the supply order. AttackMove or Retreat can continue afterward.

## Unit Readiness

`CombatReadinessSystem` derives `UnitCombatReadiness` during `SnapshotEvents`.

The unit read model contains normalized values for:

- Strength
- Health
- Fuel
- Ammunition
- Mobility
- Weapon Availability
- Supply Condition
- Combat Capability
- Overall Readiness

Health comes from `HealthState`. Fuel and Ammunition come from real `InventoryStore` quantities and capacities. Mobility reflects real movement capability and `SupplyMovementConstraint`. Weapon availability derives from usable direct-fire/artillery state and reload state. Supply Condition comes from the real supply model.

Readiness is derived observation, never an independent source of combat truth.

## Group Readiness

`CombatGroupReadiness` contains Strength, Health, Fuel, Ammunition, Mobility, Weapon Availability, Supply Condition, Combat Capability, Overall Readiness, surviving members, and initial members.

Strength is the surviving-member fraction relative to the group's original accepted size. Other dimensions aggregate surviving member readiness.

Destroyed entities therefore reduce group Strength naturally through the existing entity lifecycle rather than a parallel casualty counter.

## Read Models and Diagnostics

`UnitCombatReadinessReadModel`, `CombatGroupReadinessReadModel`, and `CombatReadinessDebugSnapshot` provide presentation-safe copies.

`TacticalCombatSystem.Metrics` reports ordered, engaging, pursuing, holding, retreating, intelligence-waiting units, and group assignments.

`AutomaticResupplyDecisionSystem.Metrics` reports low-supply evaluation, active/issued resupply orders, and unavailable-provider decisions.

`CombatReadinessSystem.Metrics` reports ready/degraded/combat-ineffective counts and average unit/group readiness.

F2 can display current order/status, target/destination, pursuit leash, movement permission, resupply state, and Health/Fuel/Ammunition/readiness values. Presentation never calculates or changes tactical state.

## Tactical Test Opponent

`TacticalTestOpponentSystem` is a deterministic development opponent, not a strategic AI.

It uses the same legal simulation information and systems as player units:

- current `Identified` contact: direct Attack through the safe current-identification resolver;
- current `Detected` contact: AttackMove toward `LastKnownPosition`, never a hidden entity transform;
- low real Fuel/Ammunition: normal automatic resupply;
- low Overall Readiness: normal Retreat movement;
- no contact: Hold Position.

It has no global sensing, resource, damage, movement, targeting, or supply advantage.

`FactionIntelligenceStore.TryResolveCurrentlyIdentifiedEntity` resolves an opaque contact key to an entity only while that contact is currently `Identified` for the requesting faction.

## Development Battle

The Windows development client seeds a small two-sided tactical battle.

Blue receives four supplied direct-fire units under one grouped AttackMove, one supplied artillery unit with an opening three-round coordinate Fire Mission, visual/radar sensing, automatic resupply policies, and a friendly physical provider.

Red receives four supplied direct-fire units, visual/radar sensing, `TacticalTestOpponent` behavior, and its own friendly provider.

The battle uses the same fixed-tick systems as headless tests. Rendering/input do not select targets or apply damage.

## Correctness Coverage

Automated tests cover:

- explicit Attack with current intelligence;
- pursuit inside and rejection outside the leash;
- AttackMove engagement pause and resume after visibility loss;
- Hold Position without pursuit;
- Stop cancellation of movement/targeting;
- deterministic group target spreading;
- readiness from Health/Fuel/Ammunition/Mobility/weapon state;
- automatic resupply through a real provider and real `BattlefieldSupplySystem` transfer;
- Retreat movement intent;
- Detected-coordinate versus Identified-entity behavior for the test opponent;
- group Strength after entity destruction.

## Performance Coverage

The simulation BenchmarkDotNet host includes 100- and 1,000-unit tactical acquisition/coordination workloads exercising preparation, spatial target acquisition, current intelligence validation, Fire Policy/weapon compatibility, group candidate construction, and deterministic target spreading.

Benchmark timing remains measurement evidence rather than a hardware-sensitive CI gate.

## Deferred Work

The first tactical layer deliberately leaves strategic/campaign AI, morale/suppression, veterancy, repair/recovery, advanced cover tactics, faction-specific doctrine, multiplayer, and final combat-command UI polish to later work.
