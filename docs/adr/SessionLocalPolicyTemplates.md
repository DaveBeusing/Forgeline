# Session-local policy templates

## Decision and scope

Store four bounded Stock/Production template slots in the existing client
Operations controller, scoped to SimulationSessionId. A template is copied local
intent, with no source-facility ownership, inventory or persistent economic state.
It cannot run in the headless simulation or trigger actions by itself.

Stock carries resource, three thresholds, logistics priority and enabled flag.
Production carries recipe, ordinary mode/priority and desired output target.
Reuse the existing quantity editor (50-unit adjustment; zero stock minimum
allowed) and impose a 1,000,000-unit template quantity bound. New, Restart and Load
sessions discard slots and drafts; no settings file or save payload stores them.

## Snapshot and preview boundary

While Operations is requested, copy at most 32 selected target rows, 32 resources
and stock policies per target, 16 recipes, 32 existing requests and the existing
eight-rule facility bound. Recipe input/output copies use at most nine entries:
more than eight is explicitly ineligible rather than silently omitting costs.
Keep at most 32 distinct output limits. Display names are bounded to 96 characters.
Foreign, unavailable and destroyed targets expose a reason without private stock,
recipes or rule data. Full entity generations and session identity are retained.

InventoryStore supplies a read-only configured resource-capacity query, including
the shared total limit. It is distinct from current free headroom. No inventory
quantity, reservation or counter is changed by capture. Copied available input
costs exclude reservations; stock and reserved quantities stay separate.

The preview checks shared/resource limits, combined configured targets, recipe
support, one-step input/output feasibility, existing work and active automation.
Preserve disabled policies, existing queues and automatic processing. No rule
builder, unit-production plan or conveyor-policy language is introduced.

## Explicit application and lifecycle

SAVE stores only a local slot. PREVIEW freezes concrete before/after values and
reasons. APPLY sends one normal player action request per ready facility through
the existing dispatcher and session-bound host/gateway, at most one pending item
at a time. Stock policies and production retain their current execution authority,
material requirements, queue semantics and replay codec.

Each item is rechecked against fresh copied settings before dispatch. Relevant
setting/queue/rule changes skip it; this is not an atomic multi-facility transaction
or a new execution-time stock revision protocol. After dispatch, existing commands
perform their normal ownership, generation and gameplay validation.

Retain bounded per-target Ready/Skipped/Pending/Confirmed/Rejected/Cancelled results
with original host sequence, session and correlation. Confirmation is command
acceptance, not delivery or completed output. Rejected items are never retried.
Selection changes, focus/modal/resize interruption and close stop unsent work;
already submitted commands remain observable. Reopening does not resume it.
A new session clears all local state and ignores old-session results.

The existing Operations window owns input and renderer lifetime. Its new Templates
entry and measured detail pages share the established panel layout. No permanent
HUD region, filesystem store, UI-owned ECS mutation or new simulation authority exists.

## Persistence and delivery boundary

No new authoritative component, replay command, catalog value, payload member or
schema is introduced. Confirmed ordinary StockPolicy and Production commands
already reconstruct through normal history; local slots/previews do not. Keep
authoritative schema 12, payload 9, envelope 1 and historical hashes unchanged.

No tests or qualification runs accompany this implementation. Existing assertions
and CI gates remain untouched. Affected Economy, Game and Presentation product
compilation and manual native/input/physical-flow/Save/Replay review remain open.
