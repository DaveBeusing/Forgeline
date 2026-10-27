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

| Technology | Domain | Phase | Prerequisite | Facility | Cost | Ticks | Effect |
| --- | --- | --- | --- | --- | --- | ---: | --- |
| Industrial Standardization | Industry | Industrial Foundation (T1) | — | Command Core | 80 Steel, 20 Electronics | 120 | Field Engineering: Combat Engineer production |
| Logistics Coordination | Logistics | Industrial Foundation (T1) | Industrial Standardization | Command Core | 60 Steel, 25 Electronics | 100 | Capability exists; priority routing consumer blocked on conveyor control integration |
| Mechanized Systems | Warfare | Mechanized Warfare (T2) | Industrial Standardization | Vehicle Factory | 100 Steel, 50 Electronics | 160 | Mobile Artillery production |
| Sensor Fusion | Intelligence | Mechanized Warfare (T2) | Industrial Standardization | Radar | 60 Steel, 70 Electronics | 140 | Owned powered radar identification x1.25, capped at detection |

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

## Gameplay effects and compatibility

Industrial Standardization retains Combat Engineer production. Mechanized Systems gates Mobile Artillery through the existing production capability contract; MBT, Cargo/Supply Trucks and the basic economy remain unrestricted. Sensor Fusion multiplies owned completed radar identification by 1.25, capped at detection, using the unmodified sensor state on every scan. Detection, contact eligibility, scan cadence and operating power are unchanged. New and existing radars use the same calculation. Costs, duration and the multiplier are current tunable values, not balance measurements.

Research introduced authoritative schema 8 for production, sensing and opponent decisions. Authoritative schema 9 also retires unreferenced order groups and destroyed unit supply inventories; earlier authoritative saves/replays are explicitly incompatible. Envelope 1 and payload 7 retain the compiled-scenario identity contract. No completion, priority rule or lifecycle migration is invented when loading an older document. The compiled scenario artifact, content fingerprint and registration remain qualified together; frozen legacy identities are retained and rejected when incompatible.

Logistics Coordination's capability currently has no routing consumer: master 3c261dc has fair conveyor distribution but no priority-routing state, command or control. The priority-routing unlock, matching player availability/rejection and opponent use remain blocked by that concrete integration dependency. This is not a completed four-effect progression tree.

## Capability gates

Gameplay systems query capability state through `TechnologyStateQueries.IsCapabilityUnlocked`. A non-specified capability remains unrestricted, allowing existing definitions to stay compatible while authored gates are introduced deliberately.

The Directorate Combat Engineer and Mobile Artillery definitions require their respective capabilities. UnitProductionSystem rejects requests before reserving their inputs when the owner's capability is absent. MBT, Rifle Squad, Scout, Cargo/Supply Trucks and basic industry retain their existing unrestricted definitions. Presentation mirrors the same capability through copied availability; activating a locked unit with Enter focuses its matching Technology entry without submitting production.

## Opponent investment

The research planner shares the normal opponent decision cadence after logistics/defense priorities; it does not run new selection between decisions. It considers at most three authored technologies, 32 captured owned buildings and the preferred construction source plus at most 32 owned storage sources. Industrial Standardization comes first. Mechanized Systems is relevant while the existing artillery target is unmet, and Sensor Fusion requires a real owned radar. Logistics Coordination is not selected while priority routing has no consumer.

The current progression correction also protects an active unit request's unpaid
inputs at a candidate source and subtracts them from the aggregate replacement
budget. Paid reservations are already excluded by InventoryStore and are not
subtracted twice. Power-blocked unpaid requests retain the same protection.
Mechanized Systems retains precedence over Sensor Fusion while artillery is
needed; optional intelligence research cannot consume its partial budget.

The normal Start command and TechnologyResearchPolicy validate owned completed facilities, prerequisites, power, source inventory and currently available materials. The planner additionally retains the authored costs of one Cargo Truck and one Supply Truck across available owned inventories for each research material, counting each inventory only once. Research still pays its entire cost from one valid source; other stocks are neither transferred nor spent by the budget check. No unaffordable request, reservation or unit queue slot is created. Cargo loss below the configured minimum fleet and base defense take precedence; optional fleet expansion does not postpone every investment. A single authoritative supply-established fact distinguishes first fleet investment from replacement after a real Supply Truck has existed; its loss postpones new research until recovery. Existing paid research continues under the regular research/power/cancellation contract.

Normal-profile material recovery raises an Electronics Plant without input to Critical Silicates priority only after every authored bootstrap industry exists and the existing power-reserve check is satisfied. Earlier requests retain High priority so electronics recovery cannot starve initial industry construction. Idle owned guards may receive one normal order per decision to clear an observed obstructed dock; fuel and existing resupply range still constrain that order. Depot recovery gathers on the side opposite its cargo service, within the existing supply radius. No docking collision gate, queue ownership, travel charge or stock quantity is relaxed.

Production planning skips locked artillery and continues with available units. It does not add a research prerequisite to MBT/Supply admission or the core-capable main attack. Diagnostics copy the candidate, decision tick, requirement failure, production reserve, active request or priority reason. Candidate lists and diagnostics are derived; research requests, capability unlocks and the supply-established fact reconstruct through the normal authoritative command/tick path.

An accepted mobile rescue without any remaining movement no longer counts as continuing service. The normal planner revalidates and reissues the paid trip; routing costs, reserved propulsion fuel and later player-order ownership remain enforced.

## Presentation and HUD

Technology read models are captured from the completed simulation snapshot and own copied collections for costs, prerequisites, and unlocks. The read model exposes available, locked, blocked, researching, and completed states together with progress and a concrete block reason.

Technology is available from the player ActionDock with `H`. The HUD shows domain, phase, facility, power, costs, prerequisite completion, progress, and concise text block reasons. `Enter` submits a start request when valid, and `C` submits cancellation for an active request.

Gameplay-critical meaning is not encoded only through color.

## Persistence and determinism

Technology start/cancel commands participate in the existing recorded-command codec. Save/restore and replay reconstruct technology progression by scheduling the same deterministic commands at their original tick boundaries and validating the resulting authoritative state hash.

The technology state is session-local because it lives in the simulation entity registry. A fresh simulation session begins without technology completion, active requests, or capability unlocks unless future scenario data explicitly authors such state.

## Current scope boundary

Three technologies have implemented consumers. The fourth remains explicitly dependent on missing priority-routing integration, including matching authoritative rejection, copied client availability and old-priority-rule compatibility coverage. The existing paid normal-profile tests specify two research investments, physical Cargo-supplied artillery and active/completed research reconstruction without extra initial stock. Current baseline CI fails the Mechanized Systems completion assertion; those tests are behavioral references, not acceptance of this correction. No universal research timing or final balance guarantee is established.

This work does not add universal credits, wall-clock research, campaign/meta progression, a final T0–T5 tree or final balance values. Native visuals, physical D3D12 performance and human gameplay acceptance remain separate from automated behavior evidence.

## Progression correction and manual acceptance

The progression change based on master `89086e68` retains normal physical costs,
commands and research timing. Critical hauling uses the existing essential Cargo
target; processing-driven fleet growth uses the optional production path.
Optional hub/depot expansion waits for a live Scout, the configured live tank
minimum and one live artillery unit. Required power, raw extraction and bootstrap
construction continue through their existing higher-priority paths. Core-force
replacement uses the existing High-priority queue/cancel commands when an unpaid
Normal request blocks it; paid production is not cancelled.

Research diagnostics include the selected source, resource, research cost,
available stock, paid reservations, active unpaid production inputs, replacement
reserve and aggregate budget after unpaid inputs. `ProductionInputs` distinguishes
a source committed to a live unit from a genuine research-material shortage.
A paid active request remains visible during defensive/recovery priorities.
The existing headless progression sidecar copies this diagnostic with decision
history; production and physical transport diagnostics remain separate.

Authoritative schema **10** rejects earlier simulation semantics before
reconstruction. Opponent ordering changes deterministic command/entity history
even without new authoritative components. Envelope 1, payload 8, command codecs
and historical hashes are unchanged. See the recovery contract.

No new tests or automated qualification runs were created or executed for this
change. Existing tests were read only. Product compilation and manual acceptance
remain open; no successful research timing or natural terminal result is claimed.
The baseline CI failure at line 49 of TechnologyNormalMatchTests shows missing
Mechanized Systems with unchanged totals from roughly tick 30,000 onward. These
totals do not identify the physical truck/dock/fuel blocker. The manual check must
trace the progression sidecar's transport and industry details if production
stops again; this scheduling correction does not establish a cargo-motion fix.
