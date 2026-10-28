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

## Bounded unit order sequences and patrol

Eligible living owned mobile combat/scout units can retain at most 16 movement or tactical entries per unit. At most 1,024 units have sequence state across the match; a single queue edit admits no more than 1,024 supplied identities. Invalid, duplicate, foreign, recycled or ineligible entities are rejected independently. Cargo Trucks, Supply Trucks and their cargo/loading orders remain under the existing logistics owners and cannot enter this queue.

The existing viewport interaction supports:

- Right click: the existing replacement Move; remove the unit's previous sequence.
- Shift + right click: append Move after the current order and retained entries.
- Resolve Attack or Attack Move targeting while holding Shift: append that tactical order.
- Ctrl + right click: replace with a two-waypoint patrol between the unit's actual current position and the chosen point.
- Ctrl + Shift + right click: append a waypoint to an already existing patrol cycle.
- Existing Stop or Hold: discard the sequence and apply the existing command. A normal replacement Attack, AttackMove, Retreat/recovery, or accepted manual fire mission also discards older queued intentions.

A patrol endpoint within two horizontal meters of its starting point is rejected. Shift-patrol without an existing patrol is rejected, and a finite entry cannot be appended after an endless patrol. Full queues and admission failures leave the existing sequence intact; normal command feedback reports rejected targets. A finite append compacts already consumed entries before checking its 16 retained-entry capacity. Cancellation of pending queue state through the command API leaves the current underlying order alone; Stop is the player action for stopping current tactical movement and clearing the sequence.

Queue acceptance means the intent was retained, not that a target has already been reached or a later attack applied. A rejected activation stops the sequence with a visible QUEUE REJECTED state. A blocked route retains its entry rather than being counted as complete or silently skipped. A user can replace/cancel it or use the ordinary recovery mechanisms.

### Completion, priorities and formation

Each unit advances at most one entry per fixed tick. Move requires the existing movement/formation intents to have finished and actual horizontal proximity within two meters of the destination. Attack/AttackMove requires the existing TacticalCombatState to report Complete. Unknown intelligence, combat engagement, failed navigation and an unfinished current Hold/fire mission do not become fictitious completion.

A finite append waits for the current movement/formation/combat/fire mission. Its current active formation remains authoritative until the formation owner releases it. Later queued legs execute per unit through the normal commands; they do not create new shared formations at each waypoint. A new manual multi-unit formation order follows the normal group path and replaces the affected units' sequences. This feature does not promise formation cohesion throughout a recurring patrol.

ResupplyOrder suspends queue progression. After actual service completion, an active queued leg is reissued through the existing command path; the queue cannot create resources, complete service or waive Fuel/Ammunition. Retreat and explicit replacement orders take precedence by clearing old queue state at normal command acceptance. Owner changes, destruction, loss of movement eligibility and stale entity generations retire the queue. A pending sequence does not own vehicle bay, convoy, loading or navigation-scheduler state.

Patrol retains one bounded waypoint array and cycles its cursor. Automatic legs execute existing single-unit commands on the simulation owner, without submitting additional recorded commands or creating repeated combat/formation groups. Only explicit player edits enter the replay command journal. A patrol does not produce an ever-growing waypoint list, per-cycle history or new strategic opponent behavior.

### Intelligence, observation and recovery

Queued Attack requires a currently identified target when the edit executes, then uses PlayerTacticalActionCommand again when activated. Lost identification or incompatible targets cannot grant hidden pursuit. Next-attack markers use current authorized intelligence contact positions only; no enemy transform is read by the queue projection.

The existing selected-order status includes current order, next kind, pending count, loop and suspended/blocked/rejected state. Current world segments retain their normal style; a pale amber segment shows the next permitted coordinate. The combined current/next connection cap remains 1,024, within the existing 8,192-line overlay budget. Missing target observations produce no invented marker.

Authoritative schema 14 includes the bounded queue array/cursor, ownership, active/wait/resume flags and status. Payload 11 adds typed queued-order edits; envelope 1 remains unchanged. Schema-13 and earlier authoritative checkpoints/replays are explicitly incompatible. A queued-order command in a payload older than 11 is rejected. Loading reconstructs the externally recorded edits and fixed-tick progress; automatic patrol legs are reproduced by the system rather than recorded repeatedly. Historical hashes are not rewritten.

Implementation is complete with manual acceptance outstanding. Tests are not created, changed, built or manually executed for this work, and no smoke/native/benchmark/qualification run or workflow dispatch/rerun is requested. Product compiler results are source-specific and do not establish functional acceptance. Manual review covers Shift appends through an existing formation, full queues, blocked routes, old/new unit generations, interrupted attacks, multiple patrol loops, supply interruption/resumption, manual replacement/Retreat, current/next markers and save/load/replay continuation.

See [bounded queued orders and patrol](adr/BoundedQueuedOrdersAndPatrol.md) for the ownership and persistence decision.

## Phase Ownership

```text
InputCommands
    combat commands create intent
        ↓
OrderProcessing
    QueuedOrderSystem (bounded deferred intent)
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

`RetreatToRecovery` adds a command-owned recovery choice without adding a second movement implementation. The planner evaluates owned repair and supply providers in stable order, chooses the nearest provider that satisfies the selected force's support needs, resolves an approach point inside the provider radius, and submits the normal Retreat path in Column formation from the player action surface.

`RetreatRecoveryState` records the selected provider, whether it offers Repair, Supply, or both, the resolved destination, and the issue tick for diagnostics/read models. A replacement tactical order clears stale recovery intent.

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

An active `ResupplyOrder` has priority in order preparation, individual tactical processing and combat-group coordination. The shared `ApplyResupplyPriority` rule clears the weapon target, allows tactical movement and retains `Resupplying`; other eligible group members continue fighting. Target acquisition cannot restore a fire target across the completed Sensors phase. Combat-group identity and the stored combat order remain intact.

Supply transfers remain in the later Supply phase. Partial transfer keeps the active supply order; completion can therefore be observed after the tactical state was last resolved, and normal tactical intent resumes on the next Sensors phase. AttackMove resumes its stored destination through normal navigation; Hold retains stationary intent. A replacement combat command owns the new intent and cancels the old supply order. Provider and enemy references retain full entity generations; recycled entities do not revive obsolete targets.

Regression coverage includes grouped/ungrouped low Fuel with full Ammunition, partial physical transfer, actual Combat-phase ammunition conservation, successful completion, provider destruction, replacement Hold and recycled target identities.

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

## Suppression and Recovery

Infantry units carry `SuppressionProfile` and `SuppressionState`. Applied combat damage raises suppression from the actual damage fraction rather than from a separate hit pool.

- Normal infantry has no suppression movement constraint.
- Suppressed infantry remains mobile at reduced maximum speed.
- Pinned infantry cannot move or fire.
- Suppression decays on fixed simulation ticks and naturally returns through Suppressed to Normal when no further impacts arrive.

`RepairRecoverySystem` runs in the Supply phase after damage resolution. Owned damaged units inside an owned `RepairProvider` radius consume physical Steel from that provider's authoritative inventory and restore bounded Health. Command Cores and completed Supply Depots are the first recovery providers. Repair never creates material implicitly: a provider can restore Health only from Steel that physically exists in its inventory, so recovery remains coupled to the existing production/logistics economy without adding automatic high-priority transport demand.

Destroyed units are never repaired, and missing repair material produces an explicit `NoMaterial` recovery state.

## Read Models and Diagnostics

`UnitCombatReadinessReadModel`, `CombatGroupReadinessReadModel`, and `CombatReadinessDebugSnapshot` provide presentation-safe copies.

`TacticalCombatSystem.Metrics` reports ordered, engaging, pursuing, holding, retreating, intelligence-waiting units, and group assignments.

`AutomaticResupplyDecisionSystem.Metrics` reports low-supply evaluation, active/issued resupply orders, and unavailable-provider decisions.

`CombatReadinessSystem.Metrics` reports ready/degraded/combat-ineffective counts and average unit/group readiness.

F2 can display current order/status, target/destination, pursuit leash, movement permission, resupply state, and Health/Fuel/Ammunition/readiness values. Combat diagnostics additionally expose suppression state, while the tactical player read model exposes Suppressed/Pinned counts, active repair count, and recovery-aware Retreat provider/reason. Presentation never calculates or changes tactical state.

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
- continued pursuit when terrain blocks direct fire inside nominal weapon range;
- pursuit inside and rejection outside the leash;
- AttackMove engagement pause and resume after visibility loss;
- Hold Position without pursuit;
- Stop cancellation of movement/targeting;
- deterministic group target spreading;
- readiness from Health/Fuel/Ammunition/Mobility/weapon state;
- automatic resupply through a real provider and real `BattlefieldSupplySystem` transfer;
- Retreat movement intent;
- Detected-coordinate versus Identified-entity behavior for the test opponent;
- group Strength after entity destruction;
- Normal → Suppressed → Pinned transitions, movement/fire consequences, and fixed-tick recovery;
- Steel-backed repair and explicit no-material behavior;
- recovery-aware Retreat provider selection and player command routing;
- reconnaissance identification enabling an artillery strike beyond the artillery unit's own visual range.

## Performance Coverage

The simulation BenchmarkDotNet host includes 100- and 1,000-unit tactical acquisition/coordination workloads exercising preparation, spatial target acquisition, current intelligence validation, Fire Policy/weapon compatibility, group candidate construction, and deterministic target spreading. Combined-arms coverage also includes 100/1,000-check terrain line-of-fire workloads and 100/1,000-unit suppression updates.

Benchmark timing remains measurement evidence rather than a hardware-sensitive CI gate.

## Deferred Work

The first tactical layer deliberately leaves strategic/campaign opponent planning, morale beyond the current suppression model, veterancy, advanced semantic cover tactics, dedicated maintenance resources, mobile repair specialization, faction-specific doctrine, multiplayer, and final combat-command UI polish to later work.

## Human Player Tactical Controls

The human player uses the same authoritative tactical commands as the simulation systems: Attack, AttackMove, Stop, Hold Position, Retreat, and Retreat to Recovery. `K` opens the combat section of the shared action palette. `Tab` selects an action and `Enter` activates it. Attack, AttackMove, Retreat, and Fire Mission enter an explicit target mode; Stop, Hold Position, Retreat to Recovery, and fire-mission cancellation submit immediately. The optional Formation binding starts unbound; another permitted key can cycle Compact, Line, Column and Wedge. F3-F9 are retired. The selected formation remains preserved for AttackMove and Retreat.

`PlayerActionRequestDispatcher` and `PlayerActionRequest` live in the cross-platform game layer and are shared by the Windows client, interaction tests, and the bounded headless player-acceptance flow. They submit resolved player actions through `PlayerCommandGateway`; simulation commands remain the only tactical state authority. The gateway reports accepted, partial, and rejected target counts plus a causal tactical failure such as missing current identification, incompatible target class, unavailable artillery target, or range failure.

Direct Attack requires a current identified hostile target. The command adapter revalidates liveness, faction, health, targetability, current identification, ownership of the selected units, and weapon effectiveness before creating the existing `AttackCommand`. A copied target row is therefore not authority to attack later after intelligence becomes stale.

Stop and Hold remain distinct existing commands. Stop clears tactical intent through the existing stop semantics; Hold creates a stationary combat order that can engage in range without pursuing. Retreat continues to use real movement and can still interact with the existing supply/resupply systems rather than teleporting or restoring resources. Retreat to Recovery resolves an owned support destination from current damage/supply needs and then submits the same existing Retreat/formation/navigation path.

## Active-state recovery qualification

GroupedResupplyAndReplacementRoundTripThroughSaveAndReplay uses the normal MatchRuntime systems with an explicit reproducible initial composition, finite Ammo stock and supported recorded PlayerTacticalActionCommand / PlayerLogisticsActionCommand adapters. It captures active grouped partial resupply, a replacing Hold order and subsequent normal navigation. Each checkpoint is serialized, restored and replayed, with complete authoritative hashes checked immediately and during continued ticks. The same-generation combat group and current command remain authoritative; no private command type or test-only persistence exception is accepted.

## Explicit engagement policies

The existing Combat ring now exposes Hold Fire, Return Fire and Fire At Will through owned tactical commands, independent of movement/queued orders. See [tactical groups and engagement rules](TacticalGroupsAndEngagement.md) for precedence, component coverage and persistence.
