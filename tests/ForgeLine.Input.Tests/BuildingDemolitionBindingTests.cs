using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class BuildingDemolitionBindingTests
{
    [Fact]
    public void DeleteIsTheDefaultRebindableDemolitionShortcut()
    {
        Assert.Equal(PlatformKey.Delete,
            GameplayBindingRegistry.Default.Key(GameplayAction.Demolish));
        Assert.Equal("DELETE",
            GameplayBindingRegistry.Default.Prompt(GameplayAction.Demolish));

        var bindings = new GameplayBindings().With(GameplayAction.Demolish, PlatformKey.X);
        var registry = new GameplayBindingRegistry(bindings);
        Assert.Equal(PlatformKey.X, registry.Key(GameplayAction.Demolish));
        Assert.Equal("X", registry.Prompt(GameplayAction.Demolish));
    }
}
