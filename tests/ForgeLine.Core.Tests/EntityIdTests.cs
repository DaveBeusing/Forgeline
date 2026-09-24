using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Core.Tests;

public sealed class EntityIdTests
{
    [Fact]
    public void DefaultEntityIdIsInvalid()
    {
        EntityId entity = default;

        Assert.False(entity.IsValid);
        Assert.Equal(EntityId.Invalid, entity);
    }

    [Fact]
    public void ComparisonOrdersByIndexThenGeneration()
    {
        var first = new EntityId(2, 5);
        var newerGeneration = new EntityId(2, 6);
        var laterIndex = new EntityId(3, 1);

        Assert.True(first.CompareTo(newerGeneration) < 0);
        Assert.True(newerGeneration.CompareTo(laterIndex) < 0);
    }
}
