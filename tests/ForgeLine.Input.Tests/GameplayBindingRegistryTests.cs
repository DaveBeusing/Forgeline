using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Input.Tests;

public sealed class GameplayBindingRegistryTests
{
    [Fact]
    public void CapturesOverridesAndPromptsWithoutMutableAlias()
    {
        var source = new[] { new GameplayBindingOverride(GameplayAction.Build, PlatformKey.Down) };
        var registry = new GameplayBindingRegistry(new() { Overrides = source });
        source[0] = new(GameplayAction.Build, PlatformKey.Up);
        Assert.Equal(PlatformKey.Down, registry.Key(GameplayAction.Build));
        Assert.Equal("DOWN", registry.Prompt(GameplayAction.Build));
        Assert.Contains("DOWN", registry.DockModesPrompt);
    }
    [Theory]
    [InlineData(PlatformKey.Unknown)]
    [InlineData(PlatformKey.F1)]
    [InlineData(PlatformKey.Escape)]
    [InlineData(PlatformKey.LeftShift)]
    [InlineData(PlatformKey.D1)]
    [InlineData((PlatformKey)255)]
    public void RejectsReservedAndUnknownKeys(PlatformKey key) => Assert.Throws<InvalidDataException>(() =>
        new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Build, key)));
    [Fact]
    public void RejectsDuplicatesAndUnknownActions()
    {
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(new()
        {
            Overrides =
            [new(GameplayAction.Build, PlatformKey.Down), new(GameplayAction.Build, PlatformKey.Up)]
        }));
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(new GameplayBindings().With((GameplayAction)255, PlatformKey.Down)));
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Build, PlatformKey.P)));
    }
    [Fact]
    public void PreservesLegacyCameraAndExplicitlySuppressesDefaultOverlap()
    {
        var camera = new RtsCameraBindings { PanForward = PlatformKey.Home };
        var registry = new GameplayBindingRegistry(new(), camera);
        Assert.Equal(PlatformKey.Home, camera.PanForward);
        Assert.Equal(PlatformKey.Unknown, registry.Key(GameplayAction.FocusBase));
        Assert.Equal("CAMERA KEY", registry.Prompt(GameplayAction.FocusBase));
        Assert.Equal(PlatformKey.Left, registry.Key(GameplayAction.Decrease));
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Build, PlatformKey.A), camera));
    }
    [Fact]
    public void DockHelpListsEveryMappedNavigationAndSettingKey()
    {
        var registry = new GameplayBindingRegistry(new GameplayBindings()
            .With(GameplayAction.PrimarySetting, PlatformKey.G).With(GameplayAction.SecondarySetting, PlatformKey.I)
            .With(GameplayAction.Decrease, PlatformKey.J).With(GameplayAction.Increase, PlatformKey.N));
        Assert.Equal("TAB NEXT  ENTER ACT  C CANCEL  G PRIMARY  I SECONDARY  J/N ADJUST", registry.DockNavigationPrompt);
    }
    [Fact]
    public void TacticalOverridesUseExistingConflictRulesAndPreserveUnboundDefaults()
    {
        var bindings = new GameplayBindings().With(GameplayAction.Stop, PlatformKey.J);
        Assert.Equal(PlatformKey.J, new GameplayBindingRegistry(bindings).Key(GameplayAction.Stop));
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(bindings.With(GameplayAction.Hold, PlatformKey.J)));
        Assert.Throws<InvalidDataException>(() => new GameplayBindingRegistry(bindings.With(GameplayAction.Hold, PlatformKey.A), new RtsCameraBindings()));
        for (int i = 0; i < 8; i++)
        {
            var action = GameplayBindingRegistry.TacticalAction(i);
            Assert.Equal(PlatformKey.Unknown, GameplayBindingRegistry.Default.Key(action));
            Assert.Equal(GameplayBindingRegistry.Default.TacticalPrompt, GameplayBindingRegistry.Default.TacticalShortcut(i));
        }
    }

    [Fact]
    public void ResetRemovesOnlyChosenOverrideAndCanRestoreUnboundTacticalDefault()
    {
        var settings = new GameplayBindings().With(GameplayAction.Stop, PlatformKey.J).With(GameplayAction.Hold, PlatformKey.G);
        var reset = new GameplayBindingRegistry(settings.Reset(GameplayAction.Stop));
        Assert.Equal(PlatformKey.Unknown, reset.Key(GameplayAction.Stop));
        Assert.Equal(PlatformKey.G, reset.Key(GameplayAction.Hold));
        Assert.Equal(2, settings.Overrides.Count);
    }

    [Fact]
    public void WarmLookupDoesNotAllocate()
    {
        var registry = GameplayBindingRegistry.Default;
        for (int i = 0; i < 128; i++) _ = registry.Prompt(GameplayAction.Build);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { _ = registry.Key(GameplayAction.Build); _ = registry.Prompt(GameplayAction.Build); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
