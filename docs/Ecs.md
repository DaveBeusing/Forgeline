# Entity Component Foundation

## Purpose

The ForgeLine ECS is a deliberately narrow, data-oriented foundation for simulation entities. It provides stable entity identity, lifecycle management, typed component storage, basic queries, deterministic-friendly iteration, diagnostics, correctness coverage, and focused microbenchmarks.

It is not intended to become a general-purpose ECS framework.

## Entity Identity and Lifetime

EntityId consists of an entity slot index and a generation.

- Generation zero is reserved for the invalid/default identifier.
- A live entity remains valid only while both its slot index and generation match the registry.
- Destroying an entity advances the slot generation before the slot can be reused.
- Reusing a slot therefore produces a different EntityId.
- Stale identifiers cannot read or mutate a replacement entity.
- Direct managed-object references between simulation entities are not part of the ECS model.

EntityRegistry owns entity lifetime. Callers must not attempt to construct lifecycle state outside the registry.

## Component Ownership

Components are struct values owned by EntityRegistry.

The initial storage model is a typed sparse set:

- sparse lookup maps entity indices to dense component positions;
- dense arrays store entity identifiers and component values;
- add, lookup, update, membership, and removal are constant-time on the normal path;
- removal uses swap-back compaction;
- dense storage remains compact for iteration;
- stores grow geometrically rather than allocating per entity.

Each entity may own at most one component of a given component type.

Duplicate component insertion fails fast. Direct GetComponent<T> access to a missing component fails explicitly, while TryGetComponent<T> provides the non-throwing lookup path.

Destroying an entity removes all of its attached components before the entity slot is recycled.

## Queries

The ECS exposes one-component and two-component entity queries.

### Dense iteration

QueryIterationOrder.Dense is the default. It follows the dense component-store layout and is intended for hot simulation paths. Dense order is not a stable gameplay contract because swap-back removal may change it.

Two-component dense queries use the smaller component store as the iteration driver and test membership in the other sparse set.

### Stable iteration

QueryIterationOrder.StableByEntityIndex scans live entity slots in ascending index order and filters by component membership.

This path is deterministic-friendly and allocation-free after warm-up, but it can scan unused or non-matching entity slots. It should therefore be selected only where stable ordering is required.

## Mutation Rules During Iteration

Structural mutation while a query enumerator is active is not supported.

Structural changes include:

- entity creation;
- entity destruction;
- component insertion;
- component removal.

Query enumerators capture the registry structural version and fail fast if it changes.

Updating the value of an already-present component with SetComponent<T> or through the ref returned by GetComponent<T> is non-structural and is allowed while iterating, provided the caller does not create, destroy, add, or remove entity/component structure.

## Diagnostics

EntityRegistry exposes:

- live entity count;
- current entity capacity;
- registered component-type count;
- per-component-type element counts through GetComponentCount<T>();
- a lightweight EntityRegistryDiagnostics snapshot for common entity/capacity/type totals.

These diagnostics are intended to feed later simulation and development instrumentation without coupling ECS to presentation systems.

## Allocation and Performance Expectations

Normal warmed component lookup, component update, and query enumeration avoid per-entity managed allocations.

Capacity growth is intentionally amortized and may allocate when entity or component arrays expand. First use of a component type also creates its typed store.

The benchmark project covers:

- entity creation;
- entity destruction;
- component insertion;
- component removal;
- component lookup;
- dense iteration;
- common two-component query iteration.

Run the ECS benchmarks with:

~~~powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
~~~

Correctness tests remain separate from benchmark timing. Benchmark timing thresholds are not CI gates.

## Explicitly Unsupported Features

The initial ECS foundation does not provide:

- simulation system scheduling;
- fixed-tick orchestration;
- job-system parallelization;
- archetype migration;
- reflection-driven component registration;
- source-generated component APIs;
- gameplay component definitions;
- rendering or UI integration;
- savegame serialization beyond future-compatible data-oriented contracts;
- multiplayer replication.

These features must be introduced only when their owning subsystems require them and when measurement or product requirements justify additional ECS complexity.
