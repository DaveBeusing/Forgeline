using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendLoadingControllerTests
{
    [Fact]
    public void LoadingStartsIndeterminateWithoutFakeProgress()
    {
        var loading = new FrontendLoadingController();

        Assert.Equal(FrontendLoadingPhase.Starting, loading.State.Phase);
        Assert.False(loading.State.HasDeterminateProgress);
        Assert.Equal(0f, loading.State.Progress);
        Assert.Equal("FORGELINE", FrontendLoadingController.ProductName);
        Assert.Equal("Build. Supply. Conquer.", FrontendLoadingController.Tagline);
    }

    [Fact]
    public void DeterminatePhaseReportsRealCompletedWork()
    {
        var loading = new FrontendLoadingController();

        loading.BeginPhase(
            FrontendLoadingPhase.LoadingAssets,
            "Loading runtime assets",
            totalSteps: 4);
        loading.ReportProgress(3, "Loading runtime assets 3/4");

        Assert.True(loading.State.HasDeterminateProgress);
        Assert.Equal(0.75f, loading.State.Progress);
        Assert.Equal("Loading runtime assets 3/4", loading.State.Status);
    }

    [Fact]
    public void CompletionCanDriveFrontendShellToMainMenu()
    {
        var loading = new FrontendLoadingController();
        var shell = new GameFrontendShell();

        loading.Complete();

        Assert.True(loading.State.IsComplete);
        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        Assert.Equal(GameFrontendScreen.MainMenu, shell.Screen);
    }

    [Fact]
    public void FailureIsTerminalAndPreservesDiagnosticMessage()
    {
        var loading = new FrontendLoadingController();

        loading.Fail("Runtime asset manifest is unavailable.");

        Assert.True(loading.State.HasFailed);
        Assert.Equal("Runtime asset manifest is unavailable.", loading.State.FailureMessage);
        Assert.Throws<InvalidOperationException>(() => loading.ReportProgress(0));
    }

    [Fact]
    public void IndeterminatePhaseRejectsSyntheticProgress()
    {
        var loading = new FrontendLoadingController();
        loading.BeginPhase(
            FrontendLoadingPhase.LoadingSettings,
            "Loading settings");

        Assert.Throws<InvalidOperationException>(() => loading.ReportProgress(1));
    }
}
