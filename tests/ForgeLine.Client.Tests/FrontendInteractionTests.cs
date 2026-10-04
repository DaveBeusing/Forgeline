using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendInteractionTests
{
    [Fact]
    public void MainMenuHitTestingMatchesRenderedRows()
    {
        var menu =
            new MainMenuModel(
                hasValidContinueTarget: false);

        string? hit =
            FrontendHitTesting.MainMenu(
                120,
                502,
                FrontendDesign.ResolveLayout(1920, 1080),
                menu.Items);

        Assert.Equal("load-game", hit);
    }

    [Fact]
    public void DisabledContinueCannotBeHit()
    {
        var menu =
            new MainMenuModel(
                hasValidContinueTarget: false);

        string? hit =
            FrontendHitTesting.MainMenu(
                120,
                350,
                FrontendDesign.ResolveLayout(1920, 1080),
                menu.Items);

        Assert.Null(hit);
    }

    [Fact]
    public void SettingsInteractionAdjustsExistingModel()
    {
        var model =
            new SettingsModel(
                new FrontendSettingsSnapshot(
                    1600,
                    900,
                    false,
                    1.0f,
                    true,
                    true,
                    1.0f,
                    new ForgeLine.Input.RtsCameraBindings()));
        var interaction =
            new SettingsInteractionModel();

        interaction.MoveNext();
        interaction.Adjust(
            model,
            1);

        Assert.Equal(1.05f, model.Settings.UiScale);
    }

    [Fact]
    public void SettingsInteractionWrapsFocus()
    {
        var interaction =
            new SettingsInteractionModel();

        interaction.MovePrevious();

        Assert.Equal(
            FrontendSettingsField.Onboarding,
            interaction.FocusedField);
    }
}
