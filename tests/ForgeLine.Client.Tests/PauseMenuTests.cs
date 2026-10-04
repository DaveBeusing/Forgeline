using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class PauseMenuTests
{
    [Fact]
    public void PauseMenuExposesResumeSaveAndReturnActions()
    {
        var menu =
            new PauseMenuModel();

        Assert.Collection(
            menu.Items,
            item =>
            {
                Assert.Equal(
                    PauseMenuCommand.Resume,
                    item.Command);
                Assert.Equal(
                    "RESUME",
                    item.Label);
            },
            item =>
                Assert.Equal(
                    PauseMenuCommand.SaveGame,
                    item.Command),
            item =>
                Assert.Equal(
                    PauseMenuCommand.SaveAndReturnToMenu,
                    item.Command),
            item =>
                Assert.Equal(
                    PauseMenuCommand.ReturnToMenu,
                    item.Command));

        Assert.Equal(
            PauseMenuCommand.Resume,
            menu.ActivateFocused());
    }

    [Fact]
    public void PauseMenuKeyboardNavigationWraps()
    {
        var menu =
            new PauseMenuModel();

        menu.MovePrevious();

        Assert.Equal(
            PauseMenuCommand.ReturnToMenu,
            menu.ActivateFocused());

        menu.MoveNext();

        Assert.Equal(
            PauseMenuCommand.Resume,
            menu.ActivateFocused());
    }

    [Fact]
    public void PauseMenuPointerHitTestingMatchesVisibleRows()
    {
        var menu =
            new PauseMenuModel();
        FrontendLayout layout =
            FrontendDesign.ResolveLayout(
                1920f,
                1080f);

        string? first =
            FrontendHitTesting.PauseMenu(
                120f,
                350f,
                layout,
                menu.Items);
        string? second =
            FrontendHitTesting.PauseMenu(
                120f,
                422f,
                layout,
                menu.Items);

        Assert.Equal(
            "resume",
            first);
        Assert.Equal(
            "save-game",
            second);
    }
}
