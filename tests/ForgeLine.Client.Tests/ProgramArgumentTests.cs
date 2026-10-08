using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ProgramArgumentTests
{
    [Fact]
    public void QualificationArgumentsAcceptIsolatedSettingsRoot()
    {
        bool parsed =
            Program.TryParseArguments(
                [
                    "--smoke-test",
                    "--render-stress",
                    "1000",
                    "--visual-qualification-output",
                    "artifacts/report.json",
                    "--settings-root",
                    "artifacts/settings"
                ],
                out bool smokeTest,
                out int renderInstanceCount,
                out string? visualQualificationOutput,
                out string? settingsRoot);

        Assert.True(
            parsed);
        Assert.True(
            smokeTest);
        Assert.Equal(
            1000,
            renderInstanceCount);
        Assert.Equal(
            "artifacts/report.json",
            visualQualificationOutput);
        Assert.Equal(
            "artifacts/settings",
            settingsRoot);
    }

    [Fact]
    public void MissingSettingsRootValueIsRejected()
    {
        bool parsed =
            Program.TryParseArguments(
                ["--settings-root"],
                out _,
                out _,
                out _,
                out _);

        Assert.False(
            parsed);
    }
    [Fact]
    public void StudioSplashBypassFlagIsAccepted()
    {
        bool parsed = Program.TryParseArguments(
            ["--skip-splash", "--settings-root", "artifacts/settings"],
            out bool smokeTest,
            out int renderInstances,
            out _,
            out string? settingsRoot);
        Assert.True(parsed);
        Assert.False(smokeTest);
        Assert.Equal(0, renderInstances);
        Assert.Equal("artifacts/settings", settingsRoot);
    }
}
