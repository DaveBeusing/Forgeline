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

The match-state entity is created in `Loading`.

After both Command Core objectives are attached, setup transitions the state to `Active`. Only `Active` matches are evaluated by `MatchObjectiveSystem`.

Authoritative terminal states are:

- `Victory` — exactly one Command Core remains and `Winner` identifies its owner;
- `Draw` — all Command Cores are lost in the same objective-evaluation tick;
- `Ended` — a completed match has been explicitly acknowledged through `EndMatchCommand`.

`MatchState.ForPlayer` converts the global state into the local player-facing result. A global `Victory` is therefore rendered as either `Victory` or `Defeat` without storing contradictory winner/loser states in simulation.

Command Core resolution uses stable ECS iteration and the simulation tick, so simultaneous destruction is deterministic and headless-testable.

## End-of-match behavior

Once a terminal result is reached, the Windows client stops advancing normal match ticks. This prevents the skirmish opponent, combat, economy, logistics, and other gameplay systems from continuing behind the result screen.

The terminal overlay exposes:

- `R` — restart;
- `Escape` — return/exit the current client session.

Restart returns control to the host and creates a completely new `ClientApplication` match session. The new session rebuilds the simulation coordinator, entity registry, inventories, logistics network, intelligence store, systems, presentation state, and match-state entity rather than attempting to reset mutable systems in place.

Return submits `EndMatchCommand` through the simulation command queue before leaving the completed session.

## Command Core objective

Destroying the opposing Command Core is the vertical-slice win condition.

Command Cores attached to the objective system are normal combat targets with authoritative health and combat state. The objective system does not inspect presentation state and does not accept a visual disappearance as proof of destruction.

If the local Command Core is destroyed while the opponent survives, the same global result identifies the opponent as winner and the player read model reports `Defeat`.

Surrender is not implemented in this slice because the existing client has no general match command/menu surface yet. It remains an optional extension rather than a parallel one-off UI path.

## Player HUD read model

`PlayerExperienceSnapshotFactory` is the boundary between authoritative gameplay state and the minimum player-facing HUD.

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

The HUD never creates or advances gameplay state.

## Selection and production presentation

The normal RTS selection filter includes local units, logistics entities, and buildings.

The selected entity summary resolves names through the existing unit/building catalogs. It reads existing authoritative components and production read models instead of storing a second UI-owned copy.

When the selected entity is working, the HUD reports:

- construction progress;
- unit-production progress and block reason;
- industrial-processing progress and block reason.

Block reasons originate from the existing production systems, including `NoInput`, `NoPower`, `OutputFull`, and `Paused`.

## Contextual command feedback

The HUD briefly presents the most recent local command result after simulation has resolved it.

Movement feedback uses the executed `MoveEntitiesCommand` result and reports accepted and rejected target counts. Buildings are selectable for inspection but are rejected as movement targets; mixed selections therefore produce partial feedback instead of receiving invalid movement state.

Construction feedback comes from a player-scoped `BuildCommandResult` published by `BuildingCommandProcessingSystem`. Rejections expose the existing authoritative `BuildCommandRejectionReason` and, where relevant, the concrete `BuildingPlacementFailureReason` rather than inventing a separate UI explanation.

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

Headless lifecycle coverage verifies:

- loading before objective setup;
- activation after objective attachment;
- Command Core victory;
- player-relative defeat;
- deterministic simultaneous destruction as a draw;
- completed-match transition to `Ended`;
- fresh restart state without retained terminal state;
- canonical vertical-slice configuration.

The repository CI remains responsible for the full Release build, Windows client smoke run, headless diagnostics/stress runs, and the complete test suite.

## Current UX limitations

This is the minimum coherent vertical-slice player experience, not final UI polish.

Current limitations include:

- the HUD uses the existing lightweight overlay text renderer;
- the resource line summarizes the starting Command Core inventory rather than aggregating every distributed inventory in the economy;
- there is no final menu shell or frontend;
- there is no dedicated minimap render surface yet;
- transient event history/notification queues are not yet persistent;
- no surrender action is exposed yet;
- final visual hierarchy, iconography, accessibility treatment, localization, and audio feedback are deferred.
