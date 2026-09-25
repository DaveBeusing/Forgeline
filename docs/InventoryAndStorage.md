# Inventory and Storage

## Purpose

FORGELINE inventories are authoritative simulation-owned aggregate resource stores. They provide the shared storage contract used by extraction, construction, and industrial production now, with logistics, supply, and transport systems building on the same contract later.

Inventories store numerical resource quantities. They do not model individual crates or presentation objects.

## Ownership model

`InventoryStore` owns mutable inventory state.

ECS entities reference that state through the lightweight `InventoryStorage` component and a stable `InventoryId`. IDs are monotonically allocated and are not reused after destruction, so a stale inventory handle cannot silently target a replacement inventory.

`StorageDepot` adds depot-specific simulation state and faction ownership while reusing the same inventory model.

Presentation and tooling consume `InventoryDebugSnapshot`; they do not mutate inventory state.

## Quantity semantics

Resource quantities use finite non-negative `double` values, matching the existing extraction model.

Authoritative rules are:

- NaN, infinity, and negative operation quantities are rejected;
- additions that would produce non-finite values fail the engine invariant;
- zero-quantity operations are valid no-ops;
- no epsilon is used to silently exceed a configured capacity;
- total quantity is the sum of committed stored quantities;
- available quantity is stored quantity minus reserved quantity.

This is deterministic-friendly within the existing fixed-tick simulation policy. A future fixed-point migration can be introduced behind the inventory API if cross-machine bit-level determinism becomes a multiplayer requirement.

## Capacity

Every inventory has a finite positive total capacity.

Optional per-resource capacities may further limit a specific resource and may never exceed total capacity.

`GetAddableQuantity` returns the exact amount that can currently be committed considering:

- total remaining capacity;
- per-resource remaining capacity;
- accepted-resource filtering;
- requested quantity.

`CanAdd` and `Add` never partially commit. Partial acceptance is an explicit caller decision, used by extraction backpressure.

## Accepted-resource filters

An inventory may either:

- accept all stable resource IDs; or
- define an explicit accepted-resource set.

A rejected resource never mutates inventory state.

Resource filters are deterministic data and do not depend on presentation strings.

## Remove and transfer semantics

`CanRemove` and `Remove` operate only on unreserved available quantity.

`Transfer` is all-or-nothing:

1. validate source and destination handles;
2. validate available source quantity;
3. validate destination filter;
4. validate total and per-resource destination capacity;
5. mutate source and destination only after every check succeeds.

A failed transfer leaves both inventories unchanged. This is the conservation boundary future logistics systems build on.

Transfers to the same inventory are validated no-ops.

## Reservations

The foundation includes aggregate reservation semantics:

- `Reserve` moves stored quantity from available to reserved without changing total quantity;
- `ReleaseReservation` returns reserved quantity to available state;
- `ConsumeReserved` atomically reduces both reserved and stored quantity;
- ordinary `Remove` and `Transfer` cannot consume reserved quantity.

The current model reserves by resource quantity. Future requester-owned reservation tokens can extend this layer without changing callers that only depend on available/reserved totals.

## Storage depots

A storage depot is an ECS entity carrying:

- `InventoryStorage`;
- `StorageDepot`.

`StorageDepot` records:

- the shared inventory ID;
- optional faction ownership;
- operational or disabled state.

Capacity utilization and contained quantities remain derived from the shared inventory rather than duplicated in the depot component.

## Extractor integration

`ResourceExtractor` can reference an output inventory entity.

During the Economy phase, `ResourceExtractionSystem` calculates normal fixed-tick extraction and then asks the inventory for storable quantity before the deposit is mutated.

Policies are:

- full or resource-rejecting output: extraction pauses for that tick;
- partial remaining capacity: extraction throttles to exactly that remaining capacity;
- stale or missing output entity/store: extraction pauses with an explicit unavailable state;
- successful output: stored quantity and deposit depletion advance by the same amount.

The old extraction sink remains an observation/handoff hook and receives only successfully committed extraction.

## Diagnostics and read models

`InventoryStoreMetrics` tracks:

- inventory count;
- add attempts and failures;
- remove attempts and failures;
- transfer attempts and failures;
- reservation attempts and failures.

`InventoryDebugSnapshot` exposes stable entity-ordered read models with:

- inventory validity;
- total quantity;
- total capacity;
- capacity utilization;
- per-resource stored, reserved, and available quantities;
- storage-depot state.

`ResourceExtractionMetrics` additionally exposes blocked extractor count.

Debug snapshot creation may allocate because it is not part of the simulation hot path. Inventory mutations themselves avoid per-operation temporary collections.

## Failure and stale-handle behavior

Expected capacity, filter, quantity, and stale-handle failures return `InventoryOperationResult` with a specific `InventoryFailureReason`.

Arithmetic overflow to a non-finite quantity is an engine invariant failure because continuing would corrupt authoritative simulation state.

Destroying an inventory invalidates its ID permanently.

Destroying an output entity causes its extractor to enter `OutputUnavailable` without draining the deposit.

## Headless validation

Inventory and extraction integration remain independent of graphics, audio, UI, and windowing.

Coverage includes:

- exact total capacity;
- accepted-resource rejection;
- per-resource capacity;
- atomic transfer failure;
- successful transfer conservation;
- reservation availability;
- stale inventory handles;
- storage-depot diagnostics;
- extraction into inventory;
- full-output blocking;
- partial-output throttling;
- stale output entity safety.

## Benchmarks

`InventoryTransferBenchmarks` measures round-trip transfer batches at:

- 100 operations;
- 1,000 operations;
- 10,000 operations.

The benchmark keeps resource keys warm in both inventories so measurements focus on steady-state transfer work rather than first-use dictionary growth.

Benchmark results are measurement evidence, not hardware-sensitive CI pass/fail gates.

## Architectural boundaries

The inventory foundation lives in `ForgeLine.Economy`.

It has no dependency on graphics, presentation, UI, audio, or platform code.

Future `ForgeLine.Logistics` systems already depend on Economy and can therefore use the same inventory API for physical aggregate cargo and depot transfers without duplicating storage state.
