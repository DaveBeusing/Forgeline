using ForgeLine.Core;

namespace ForgeLine.Ecs;

internal interface IComponentStore
{
    int Count { get; }

    bool Remove(EntityId entity);
}
