namespace ForgeLine.Ecs;

public readonly record struct EntityRegistryDiagnostics(
    int LiveEntityCount,
    int EntityCapacity,
    int ComponentTypeCount,
    int TotalComponentCount);
