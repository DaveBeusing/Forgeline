using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Ecs.Tests;

public sealed class EngineInvariantTests
{
    [Fact]
    public void InvalidEntityMutationReportsStructuredInvariantFailure()
    {
        var registry = new EntityRegistry();
        var invalid = new EntityId(12, 1);

        EngineInvariantException exception = Assert.Throws<EngineInvariantException>(
            () => registry.AddComponent(invalid, new Marker(1)));

        Assert.Equal(DiagnosticCategory.Ecs, exception.Category);
        Assert.Equal("ECS_ENTITY_NOT_ALIVE", exception.Code);
        Assert.Contains("not alive", exception.Message, StringComparison.Ordinal);
    }

    private readonly record struct Marker(int Value);
}
