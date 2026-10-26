using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class DeveloperMenuCapabilityTests
{
    [Fact]
    public void PauseEntryAndWorldDebugHelpFollowCompiledCapability()
    {
        bool compiled = DeveloperToolsCapability.Current.IsCompiled;
        var menu = new PauseMenuModel(true);
        Assert.Equal(compiled, menu.Items.Any(i => i.Command == PauseMenuCommand.DeveloperTools));
        var controls = FrontendPresentationAdapter.Controls(new());
        Assert.Equal(compiled, controls.DetailLines.Any(line => line.Label == "WORLD DEBUG"));
        Assert.Equal(compiled, controls.DetailLines.Any(line => line.Label == "DEVELOPER TOOLS"));
        Assert.DoesNotContain(GameplayBindingRegistry.FixedBindings, line => line.Contains("WORLD DEBUG", StringComparison.Ordinal));
        Assert.Equal(compiled, GameplayBindingRegistry.FixedBindingsFor(compiled).Any(line => line.Contains("WORLD DEBUG", StringComparison.Ordinal)));
        var inactiveControls = FrontendPresentationAdapter.Controls(new(), developerSessionActive: false);
        Assert.DoesNotContain(inactiveControls.DetailLines, line => line.Label is "WORLD DEBUG" or "DEVELOPER TOOLS");
        menu.SetDeveloperToolsAvailable(false);
        Assert.DoesNotContain(menu.Items, i => i.Command == PauseMenuCommand.DeveloperTools);
    }
}
