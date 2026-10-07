# Match Flow and Player Experience

## Purpose

The vertical-slice match flow turns the existing FORGELINE simulation systems into one complete skirmish lifecycle. Match authority remains inside simulation state; the client only submits commands and renders player-specific read models.

The current playable configuration uses the Central Divide prototype battlefield, Player 1 as the local Directorate side, Player 2 as the computer-controlled opposing side, and a seeded simulation.

## Match configuration and initialization

`MatchConfiguration` contains the map key, simulation seed, and participant assignments. Each `MatchParticipantConfiguration` declares:

- player identity;
- combat faction identity;
- battlefield start index;
- whether the slot is computer controlled.

`SkirmishMatchInitializer` validates the configuration against the selected battlefield, creates the normal `SkirmishStartingBase` instances, removes the opponent controller from human-controlled slots, and attaches the existing Command Core objectives.

The current prototype battlefield binds starts and objective ownership to Player 1 and Player 2. Configuration validation therefore rejects assignments that would silently disagree with the authored battlefield data.

## Authoritative lifecycle

The match-state entity has an explicit simulation-owned lifecycle:

`Initializing -> Ready -> Running <-> Paused -> Ending -> Completed`

`Initializing` exists while the world and starting state are being created. Attaching all Command Core objectives moves the match to `Ready`; the shared vertical-slice composition enters `Running` only after the simulation system pipeline is fully registered. Client pause/resume uses simulation control commands to move between `Running` and `Paused` without advancing the logical clock.

A match result is distinct from lifecycle phase. `MatchOutcome` records `Victory` or `Draw`, while `MatchTerminationReason` records why the result occurred. Current authoritative reasons are Command Core destruction, surrender, simultaneous Command Core destruction, and all-participant elimination.

`MatchObjectiveSystem` evaluates only `Running` matches. It resolves Command Core destruction and surrendered participants from simulation state. Result resolution moves the lifecycle to `Ending`; `EndMatchCommand` is the only normal transition from `Ending` to `Completed`. The result, winner, defeated participant, result tick, finalization tick, last transition tick, and transition count remain available for diagnostics after completion.

The compatibility-facing `MatchStatus` still exposes `Loading`, `Active`, `Victory`, `Draw`, and `Ended`, and `MatchState.ForPlayer` still maps a global victory to local `Victory` or `Defeat`. Presentation therefore does not own or reconstruct match authority.

Command Core resolution uses stable ECS iteration and the simulation tick, so simultaneous destruction is deterministic and headless-testable. Invalid lifecycle transitions fail explicitly instead of silently mutating state.

## End-of-match behavior

Once a terminal result is reached, the Windows client stops advancing normal match ticks. This prevents the skirmish opponent, combat, economy, logistics, and other gameplay systems from continuing behind the result screen.

The terminal overlay exposes:

- `R` — restart;
- `Escape` — return/exit the current client session.

Restart returns control to the host and creates a completely new `ClientApplication` match session. The new session rebuilds the simulation coordinator, entity registry, inventories, logistics network, intelligence store, systems, presentation state, and match-state entity rather than attempting to reset mutable systems in place. Each coordinator receives a new `SimulationSessionId`; `RenderWorld` drops previous-session interpolation state and selection/hover/pending presentation interaction is invalidated before the new session is consumed.

Return sends an explicit terminal acknowledgement to the simulation owner. The owner applies `EndMatchCommand` through the coordinator's non-ticking control transition at the already completed terminal tick. This republishes the copied terminal/Ended presentation state without advancing the logical clock or running another normal gameplay phase pipeline.

## Command Core objective

Destroying the opposing Command Core is the vertical-slice win condition.

Command Cores attached to the objective system are normal combat targets with authoritative health and combat state. The objective system does not inspect presentation state and does not accept a visual disappearance as proof of destruction.

If the local Command Core is destroyed while the opponent survives, the same global result identifies the opponent as winner and the player read model reports `Defeat`.

Surrender is an authoritative simulation command. `PlayerActionRequest.Surrender` dispatches through the same bounded `PlayerCommandGateway` as other player-authored actions, schedules `SurrenderCommand` for a future simulation tick, and publishes the accepted/rejected result through the normal command-result buffer. The objective system then resolves the surrendered participant exactly like other terminal objective changes. A dedicated final frontend/menu affordance can call this existing action without introducing a second match-authority path.

## Player HUD read model

`PlayerExperienceSnapshotFactory` remains the simulation/game-side copier for the minimum player-facing HUD, but the Windows render loop no longer invokes it against live ECS/inventory/system state. `PresentationExtractor` captures the local `PlayerExperienceSnapshot` once at the completed-tick boundary and publishes it inside the same `PresentationSnapshot` as render instances, intelligence, placement feedback, and match state.

The snapshot contains:

- local match result and winner;
- core stock quantities for Ferrous Ore, Volatiles, Silicates, Steel, Fuel, Electronics, and Ammunition;
- the local logical power network's generation, demand, allocation, deficit, brownout count, and offline count;
- local intelligence exploration, current visibility, and known-contact counts;
- selected owned entity count and primary entity details;
- Health, Fuel, Ammunition, supply state, and derived combat readiness where present;
- building power and inventory information where present;
- construction, unit-production, and industrial-processing progress and block reason;
- alert flags;
- basic development-readable match statistics.

The HUD never creates or advances gameplay state. Every published player-experience model carries the same completed tick as its enclosing presentation snapshot, and the enclosing snapshot carries the simulation-session ID. Rendering therefore cannot silently combine one session's match result with another session's resources or selection inspection.

## Gameplay HUD composition

`GameplayHudRenderer` is the production-facing root compositor for the in-match HUD. `ClientRenderHost` submits player-facing HUD state through this compositor independently from `DevelopmentOverlayRenderer`, which remains the owner of development metrics and engineering diagnostic labels.

`GameplayHudLayout` resolves the shared DPI-aware safe area and named regions for the top status bar, selection inspector, action dock, alert stack, minimap, and optional secondary views. Existing resource, selection, minimap, action, targeting, feedback, and pre-alpha surfaces are routed through the composition boundary without moving gameplay authority into presentation.

HUD surfaces implement the narrow `IGameplayHudSurface` contract. New surfaces should consume immutable presentation snapshots/read models, render within the named region that owns their presentation responsibility, and submit player-authored changes through the established request/command boundary. They must not acquire live ECS or simulation ownership.

The current text-heavy player surface is retained behind `GameplayHudLegacyTextSurface` as a compatibility adapter while the production surfaces are migrated incrementally. The adapter is deliberately isolated beneath `GameplayHudRenderer`; it is not a reason for new player-facing features to be added to the development overlay.

## Selection and production presentation

The normal RTS selection filter includes local units, logistics entities, and buildings.

The selected entity summary resolves names through the existing immutable unit/building catalogs. During completed-tick extraction it copies the selected entities' currently authorized local-player details from authoritative components and production read models. Rendering only consumes that copied summary; it does not re-query the selected entity from ECS. Full `EntityId` generation remains part of the copied selection identity, and selection is cleared when the session ID changes.

When the selected entity is working, the HUD reports:

- construction progress;
- unit-production progress and block reason;
- industrial-processing progress and block reason.

Block reasons originate from the existing production systems, including `NoInput`, `NoPower`, `OutputFull`, and `Paused`.

## Contextual command feedback

The HUD briefly presents the most recent local command result after simulation has resolved it.

Player submissions go through `PlayerCommandGateway`. Every accepted submission receives a session-scoped correlation ID and records command source, player-observed tick, scheduled target tick, and simulation sequence. The gateway retains mutable command objects internally only while resolving them.

Resolved outcomes are copied into `PlayerCommandResultReadModel` and delivered through a bounded consumptive `PlayerCommandResultBuffer`. This queue is intentionally separate from the latest presentation snapshot: replacing a visual/HUD snapshot cannot erase an unconsumed accepted/rejected command result. The gateway bounds pending plus published results; once full, new submissions fail explicitly with `BoundaryFull`.

Movement results report accepted and rejected target counts. Buildings remain selectable for inspection but are rejected as movement targets; mixed selections therefore produce partial feedback instead of receiving invalid movement state.

Construction feedback is correlated with the `BuildCommandResult` produced by `BuildingCommandProcessingSystem`. Rejections expose the existing authoritative `BuildCommandRejectionReason` and, where relevant, the concrete `BuildingPlacementFailureReason`. The build path still revalidates placement and resources when the command executes.

Feedback is transient presentation of authoritative results. It does not become gameplay state or alter command acceptance.

## Alerts

The current HUD derives focused alerts from authoritative state:

- low or constrained local power;
- blocked local production;
- critical or unsupplied local units;
- damaged Command Core;
- destroyed Command Core.

These alerts are intentionally causal rather than decorative. They explain conditions that can affect production, logistics, combat readiness, or match outcome.

A distinct transient "base under attack" event is not added yet because the current combat event pipeline does not expose a durable player-notification read model. Command Core damage provides a reliable authoritative warning without inventing presentation-owned combat history.

## Power-network ownership

Vertical-slice bases and newly completed buildings use a logical power network derived from their owning player.

This prevents Player 1 and Player 2 from sharing generation or demand through the previous hardcoded network ID. It also gives the player HUD a stable local network to summarize.

Physical grid topology remains deferred to the existing power roadmap.

## Intelligence and minimap boundary

HUD intelligence data comes only from the local player's `FactionIntelligenceStore`.

No raw enemy ECS scan is used for player-visible intelligence. Known-contact counts therefore respect the same faction-specific sensing and Fog-of-War rules as targeting and presentation extraction.

A standalone minimap renderer is not introduced in this package. The existing faction-filtered intelligence snapshot, explored/visible grid state, contact state, terrain bounds, and render-world extraction already form the data foundation for a later minimap without creating a second visibility model or a new major rendering subsystem.

## Development statistics

The terminal HUD currently reports the statistics that existing authoritative state supports without inventing historical bookkeeping:

- match duration;
- locally produced units;
- locally completed post-start buildings;
- locally processed output quantity.

Historical unit-loss and delivered-supply totals remain deferred until those systems expose durable per-player metrics.

## Validation

Lifecycle validation verifies:

- explicit `Initializing -> Ready -> Running` bootstrap;
- controlled `Running <-> Paused` transitions;
- invalid-transition rejection;
- Command Core victory and player-relative defeat;
- deterministic simultaneous destruction as a draw;
- surrender through the request, command gateway, simulation command, result buffer, and objective evaluator;
- result-reason diagnostics and terminal read-model propagation;
- `Ending -> Completed` finalization without an extra gameplay tick;
- fresh restart sessions with no retained terminal/world state;
- canonical vertical-slice configuration;
- bounded correlated command-result ordering and overflow behavior;
- completed-tick/session coherence for player-facing snapshots;
- old-session interpolation and selection invalidation.

The headless full-match CI path runs the shared vertical-slice composition until an authoritative result exists, finalizes that result through `EndMatchCommand`, requires the lifecycle to reach `Completed`, and emits the complete lifecycle/outcome/reason transition data in its JSON report.

The repository CI remains responsible for the full Release build, Windows client smoke run, headless diagnostics/stress runs, and the complete test suite.

## Current UX limitations

This is the minimum coherent vertical-slice player experience, not final UI polish.

Current limitations include:

- the HUD uses the existing lightweight overlay text renderer;
- the resource line summarizes the starting Command Core inventory rather than aggregating every distributed inventory in the economy;
- there is no final menu shell or frontend;
- the minimap remains part of the lightweight RTS information surface rather than a final production map-control surface;
- transient event history/notification queues are not yet persistent;
- the surrender command path exists, but a dedicated final frontend/menu affordance is still deferred;
- final visual hierarchy, iconography, accessibility treatment, localization, and audio feedback are deferred.


## Player Action Surface

The interactive match uses a reusable action palette for player-authored construction and production while keeping simulation ownership unchanged.

The palette is session-scoped and consumes immutable completed-tick `PlayerActionSnapshot` data. Construction entries come from the current building catalog and Command Core inventory. Processing and unit-production entries exist only for a single selected owned compatible facility and use that facility's local input inventory. Its screen origin and pointer bounds come from the shared DPI-aware action-dock region rather than controller-owned viewport constants.

`B`, `P`, and `U` select construction, processing, and unit-production modes. While a mode owns keyboard focus, RTS camera keys are suppressed. Pointer clicks inside the panel are captured through the shared `HudInteractionContext` before world selection, movement, or placement. Mixed selections, foreign ownership, destroyed/stale facilities, terminal matches, and a fresh session remove incompatible transient actions instead of retaining stale authority.

Command submission is not success feedback. `PlayerActionSnapshot.PendingCommandCount` identifies unresolved player submissions; completed results are published separately as accepted/rejected `PlayerCommandFeedback`. Production progress and block reasons remain authoritative copied state from the selected facility.

## Player Logistics and Supply Surface

The existing reusable player action palette now includes logistics and battlefield supply without changing construction or production ownership.

`L` exposes stock-policy and Cargo status for one selected owned logistics-capable entity. `Y` exposes automatic and explicit resupply controls for one selected owned supply-capable unit. The same panel focus and pointer-capture rules used by construction/production prevent clicks and keyboard actions from leaking into world selection, movement, or placement.

All displayed policy, cargo, provider, and supply information is copied at the completed-tick presentation boundary. Actions are submitted through the bounded `PlayerCommandGateway`; pending submission remains distinct from accepted/rejected feedback. Session changes clear transient panel state, and stale or foreign selections do not retain player authority.

## Player Tactical Combat Surface

The shared player action surface now spans Build, Process, Units, Logistics, Supply, and Combat. The Combat view reports the selected count, combat-eligible/rejected counts, current common or mixed tactical order state, identified attack-target count, critical/resupplying supply counts, and artillery ammunition/range/mission state. Target-mode instructions are shown separately from owned selection.

Tactical results use the same pending/result boundary as economy and supply actions. Accepted, partial, and rejected outcomes include accepted/rejected entity counts and tactical failure causes. Session changes and terminal match state clear transient targeting state instead of retaining authority across restart or result screens.

The vertical-slice Command Core contract is implemented as documented: starting Command Cores are normal Structure combat targets with authoritative `HealthState`, `Combatant`, `Targetable`, `TargetPriority`, and `CombatHitbox` components. Damage flows through the normal combat runtime and lifecycle; when health reaches zero the entity is destroyed normally and `MatchObjectiveSystem` resolves victory/defeat from objective survival. No presentation action and no match-objective shortcut deletes a Command Core.

The bounded player-commanded acceptance path uses the shared gameplay composition with seed `4119` and both participant slots configured as human, so no `SkirmishOpponentController` drives either side. Starting from finite stock, it constructs power generation, a Smelter, Vehicle Factory, and Supply Depot; establishes stock policies through the same `PlayerActionRequestDispatcher` used by the client; processes Steel; produces two Main Battle Tanks, one Scout Vehicle, and one Supply Truck; physically replenishes the combat group; performs legal reconnaissance until the opposing Command Core is currently identified; moves the supplied assault group into a legitimate staging position; and submits the final Attack through the shared request adapter. Fuel and Ammunition are consumed normally, damage is resolved by the combat pipeline, the opposing Command Core is destroyed by combat lifecycle processing, and `MatchObjectiveSystem` resolves the local player as winner.

This bounded evidence validates the cross-feature Build-Supply-Conquer path, command authority, physical supply, intelligence gating, damage, and natural terminal resolution. It does not claim to be a long free-form human playthrough, exhaustive battlefield-balance proof, or a substitute for the canonical 80,000-tick terminal CI gate. It is also distinct from the Windows graphics smoke, which intentionally destroys a Command Core only to validate native-host/render/session lifecycle behavior rather than natural combat progression.


## Independent client lifecycle

The Windows client no longer advances authoritative gameplay from the render/event loop.

A completed Victory/Defeat/Draw snapshot causes the simulation owner to freeze normal ticks immediately after that completed tick. Platform input and rendering remain live so the result screen can be interacted with and redrawn.

Minimize pause is an explicit `SetMatchPausedCommand` control transition between complete ticks. The authoritative match lifecycle changes to `Paused` and back to `Running` without executing a gameplay tick; pause is not inferred from whether the renderer happens to submit a frame.

Restart stops and joins the render/simulation owners before the shared runtime and job scheduler are disposed, then creates a fresh runtime/session. Requests carry the expected `SimulationSessionId`; old-session or terminal gameplay work is rejected before it can reach the authoritative command scheduler.
