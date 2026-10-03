using ForgeLine.Core;

namespace ForgeLine.Ecs;

public sealed record EntityRegistrySnapshot(
    int EntityCount,
    int Capacity,
    ulong StructuralVersion,
    IReadOnlyList<EntityId> AliveEntities,
    IReadOnlyList<ComponentStoreSnapshot> ComponentStores);

public sealed record ComponentStoreSnapshot(
    string ComponentType,
    IReadOnlyList<ComponentStateSnapshot> Components);

public sealed record ComponentStateSnapshot(
    EntityId Entity,
    string Json);
