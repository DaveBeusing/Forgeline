# Technology Progression

## Purpose

FORGELINE technology progression is an industrial simulation subsystem rather than a universal-currency linear research tree. Research competes with production for real resources and remains constrained by infrastructure and power.

The current implementation is intentionally small. It establishes stable data contracts, deterministic research behavior, capability gating, persistence through the recorded-command reconstruction path, and a player-facing technology view. It is not the final content or balance pass.

## Technology definitions

Technology definitions use stable `TechnologyId` and `TechnologyCapabilityId` values. Each definition carries:

- domain and phase;
- explicit prerequisite technology IDs;
- physical resource costs;
- required completed building type;
- required power fraction;
- deterministic research duration in simulation ticks;
- zero or more capability unlock IDs.

The initial Directorate catalog is:

| Technology | Domain | Phase | Prerequisite | Facility | Cost | Ticks | Unlock |
| --- | --- | --- | --- | --- | --- | ---: | --- |
| Industrial Standardization | Industry | Industrial Foundation (T1) | — | Command Core | 80 Steel, 20 Electronics | 120 | Field Engineering |
| Logistics Coordination | Logistics | Industrial Foundation (T1) | Industrial Standardization | Command Core | 60 Steel, 25 Electronics | 100 | Logistics Coordination |
| Mechanized Systems | Warfare | Mechanized Warfare (T2) | Industrial Standardization | Vehicle Factory | 100 Steel, 50 Electronics | 160 | Mechanized Systems |
| Sensor Fusion | Intelligence | Mechanized Warfare (T2) | Industrial Standardization | Radar | 60 Steel, 70 Electronics | 140 | Sensor Fusion |

All four current definitions require full configured power availability at their facility. These entries exist to validate the progression architecture and do not represent the final production tree.

## Authoritative research state

Research requests are simulation entities owned by a player and carry the technology ID, required facility entity, source inventory, submitted tick, deterministic progress ticks, material-consumption state, status, and block reason.

Only one active research request per player is accepted by the current narrow model. Starting and cancelling research use `PlayerTechnologyActionCommand` through the normal player command gateway. The UI does not directly create completion state or capability unlocks.

The research system executes in the fixed-tick Production phase. On each tick it validates:

1. the technology exists and is not already complete;
2. all prerequisites are complete for the player;
3. the required completed facility exists and is owned by the player;
4. the facility satisfies the required power fraction;
5. the source inventory exists;
6. required materials are available when they have not already been consumed.

Materials are removed once, when research first becomes runnable. Research then advances exactly one progress tick per simulation tick. If a continuing infrastructure or power requirement becomes invalid, progress remains deterministic and the request reports a blocked state until the requirement recovers.

Completion creates an authoritative `CompletedTechnology` component plus one `TechnologyCapabilityUnlock` component per authored unlock. Cancellation removes the active research request.

## Capability gates

Gameplay systems query capability state through `TechnologyStateQueries.IsCapabilityUnlocked`. A non-specified capability remains unrestricted, allowing existing definitions to stay compatible while authored gates are introduced deliberately.

The representative end-to-end gate is Field Engineering. The Directorate Combat Engineer definition requires that capability, and `UnitProductionSystem` rejects Combat Engineer requests until Industrial Standardization has completed and granted the unlock. Presentation mirrors this as a copied availability flag but is not authoritative.

## Presentation and HUD

Technology read models are captured from the completed simulation snapshot and own copied collections for costs, prerequisites, and unlocks. The read model exposes available, locked, blocked, researching, and completed states together with progress and a concrete block reason.

Technology is available from the player ActionDock with `H`. The HUD shows domain, phase, facility, power, costs, prerequisite completion, progress, and concise text block reasons. `Enter` submits a start request when valid, and `C` submits cancellation for an active request.

Gameplay-critical meaning is not encoded only through color.

## Persistence and determinism

Technology start/cancel commands participate in the existing recorded-command codec. Save/restore and replay reconstruct technology progression by scheduling the same deterministic commands at their original tick boundaries and validating the resulting authoritative state hash.

The technology state is session-local because it lives in the simulation entity registry. A fresh simulation session begins without technology completion, active requests, or capability unlocks unless future scenario data explicitly authors such state.

## Current scope boundary

The subsystem currently proves the architecture with four representative Directorate technologies and one production capability gate. It does not add universal credits, wall-clock research, campaign/meta progression, a final T0–T5 tree, final balance values, or unreleased playable content.
