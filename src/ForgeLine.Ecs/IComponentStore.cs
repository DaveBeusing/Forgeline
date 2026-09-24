using ForgeLine.Core;

namespace ForgeLine.Ecs;

internal interface IComponentStore
{
    Type ComponentType { get; }

    int Count { get; }

    bool Remove(EntityId entity);
}
