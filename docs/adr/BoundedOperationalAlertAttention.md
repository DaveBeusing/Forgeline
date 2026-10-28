# Bounded operational alert attention

## Context

Existing completed-tick player facts, alert identities, notification geometry, Operations
and camera validation already own the relevant responsibilities. Rebuilding them as an
incident service would duplicate authority and create an unnecessary persistent UI log.

## Decision

Retain eight typed PlayerAlertState categories and one immutable ActionableAlert record
per active category. AlertIdentity adds an observed cause value to session, player, kind
and onset tick. AlertObservation is the narrow adapter: callers supply only current
authorized completed-tick cause masks/labels; the runtime adapter obtains representative
full-generation entity IDs from the existing stable owned scan. A maximum of eight copied
AlertChange records describes transitions on that tick. No world position, enemy source,
free resource or new simulation command is introduced.

Remove absent live rows immediately; confirm attention resolution after three seconds of
healthy simulation ticks and rearm an unchanged warning after a five-second cooldown.
Changed causes and new critical incidents bypass cooldown. Repeated captures, pause,
rollback, session replacement and player replacement cannot turn wall time into evidence.
Derived lifecycle/completion scratch resets on timeline replacement and is not authoritative
save/replay state; current simulation schemas and command payloads remain unchanged.

Core threat consumes only positive finite current-tick DamageApplied events to a living
owned configured core. Production completion consumes only observed owned counters after
a baseline capture. New events never infer enemy positions, future deliveries or predicted
production. Batching and quiet filtering remain presentation preferences. Raw current
observations remain available to Operations even when a compact row is hidden.

Route mouse and mapped Next Alert through existing current-snapshot identity and camera
checks. Skip absent targets and retain Operations as the unavailable destination. No
destroyed or fog-hidden entity supplies a replacement camera position. The existing stack
geometry, bounded MORE summary, input ownership and developer F1 route stay authoritative.

## Consequences

This is category-level attention, not exhaustive per-entity incident history. A representative
target is not a claim that all affected facilities share its exact local cause. Completion
counts describe observed counter deltas, without a guessed source entity or elapsed ETA.
Human terrain/display/input review and the complete production CI/package path remain
separate from unit, native WARP and CPU/null-graphics qualification.
