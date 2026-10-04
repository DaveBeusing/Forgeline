using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendUxFeedbackTests
{
    [Fact]
    public void DetailFactoryCarriesExplicitActions()
    {
        FrontendSurfaceView view =
            FrontendSurfaceView.Detail(
                "TEST",
                [],
                "ESC BACK",
                "APPLY");

        Assert.Equal("APPLY", view.PrimaryAction);
        Assert.Equal("BACK", view.SecondaryAction);
    }

    [Fact]
    public void InteractionDecorationPreservesSurfaceContent()
    {
        FrontendSurfaceView original =
            FrontendSurfaceView.Detail(
                "LOAD GAME",
                [
                    new FrontendDetailLineView(
                        "SAVE",
                        "TICK 12")
                ],
                "ESC BACK",
                "LOAD");

        FrontendSurfaceView decorated =
            original.WithInteraction(
                "SELECT A VALID SAVE TO LOAD",
                0.5f,
                true,
                true,
                false,
                false);

        Assert.Equal(original.Title, decorated.Title);
        Assert.Equal(original.DetailLines, decorated.DetailLines);
        Assert.Equal("SELECT A VALID SAVE TO LOAD", decorated.Feedback);
        Assert.Equal(0.5f, decorated.Transition);
        Assert.True(decorated.PrimaryHovered);
        Assert.True(decorated.PrimaryPressed);
        Assert.False(decorated.SecondaryHovered);
    }

    [Fact]
    public void DetailLineCarriesHoverAndPressedState()
    {
        var line =
            new FrontendDetailLineView(
                "SEED",
                "17",
                IsFocused: true,
                CanDecrease: true,
                CanIncrease: true,
                IsHovered: true,
                IsPressed: true);

        Assert.True(line.IsFocused);
        Assert.True(line.IsHovered);
        Assert.True(line.IsPressed);
    }
}
