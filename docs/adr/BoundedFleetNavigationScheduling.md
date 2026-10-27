# Bounded fleet navigation scheduling

## Decision

Use the existing NavigationSearchScheduler and policy ledger as the sole fleet
admission/dispatch owner for the connected Cargo/Supply kernel. Borrow the host's
JobScheduler; do not create another worker pool. Keep ECS mutation and all
GroundMovement, Fuel, precise Dock and Queue authority in their existing owners.

An optional coordinator-search interface surrounds worker Advance. Pool leases,
charged initialization and exact-cache reads happen before dispatch. Cache
writes happen after every worker joins, in fixed grant order. The three phases
share one disjoint grant and one request-wide ledger. Zero progress and exhausted
work remain explicit budget outcomes. No wall-clock value decides service.

Use the prepared exclusive arena pool as actual cache scratch, with its fixed
idle reserve removed from active scheduler credit. Use the prepared exact route
cache with globally pinned geometry, full pose/profile/goal/scope/cost keys and
bounded maintenance. Regional keys are not enabled without a complete connector
dependency proof. New costs do not invalidate already pinned geometric searches.

## Alternatives and limits

A second scheduler/worker pool would duplicate ownership and is rejected.
Concurrent worker mutation of the LRU cache is rejected. Disabling Jobs for every
request to hide cache access inside worker Advance is unnecessary: coordinator
phases keep the existing parallel dispatch boundary.

The product configures 128 retained requests, 16 submissions/admissions per tick,
8 active searches, 16,384 total tick units and 8-MiB per-session peaks. The pending
scan is capped at 4,096 components. Bounds and the exact work/memory ledgers are in
[Connected fleet scheduling](../navigation/NavigationSearchScheduling.md).
They are unmeasured operational choices, not performance evidence.

## Persistence and lifecycle

Authoritative schema 15 replaces schema 15 semantics; envelope 1/payload 11 and
historical hashes stay intact. Scheduler checkpoint revision 2 retains the pinned
cost mode, policy state, ownership, ages and concrete work/frontier continuations.
The exact cache layout/expiry/access state is retained because derived-route
lookup work can change completion ticks. Scratch bytes and allocation warmth
are not decision state; the initialization cursor is.

Normal recovery uses command reconstruction and full checkpoint comparison.
In-process restore requires cache restoration at a joined empty-pool boundary,
then exact snapshot resolution and scheduler restoration; missing frontiers or
work mismatches fail explicitly. A fresh empty cache is not silently substituted.
Explicit DTO projection avoids span-constructor helper types in authoritative JSON.

The navigation owner is IDisposable and releases sessions/storage before its
runtime disposes owned Jobs. Borrowed Jobs stay with the host. Disposal failures
retain memory credit rather than making it available twice.

## Observation and acceptance

Current-tick copied diagnostics report only the viewer's request and storage
activity. Shared cache bytes are not misrepresented as a player-owned count.
Compiler checks are separate from functional evidence. Manual load/fairness,
replacement/cancellation, cache/pool and Save/Replay acceptance remains pending.
No test or qualification execution is part of this change.
