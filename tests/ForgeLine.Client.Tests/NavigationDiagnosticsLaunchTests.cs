using System.Text.Json;
using Xunit;
namespace ForgeLine.Client.Tests;
public sealed class NavigationDiagnosticsLaunchTests
{
    [Fact]
    public void NormalLaunchKeepsDiagnosticsDisabled()
    {
        Assert.True(Program.TryParseArguments([], out _, out _, out _, out _, out bool enabled));
        Assert.False(enabled);
    }
    [Theory]
    [InlineData("--developer-navigation-diagnostics")]
    [InlineData("--DEVELOPER-NAVIGATION-DIAGNOSTICS")]
    public void OnlyExplicitSuccessfulLaunchRequestsEnableDiagnostics(string option)
    {
        Assert.True(Program.TryParseArguments([option], out _, out _, out _, out _, out bool enabled));
        Assert.True(enabled);
    }
    [Fact]
    public void InvalidLaunchNeverPublishesDeveloperAuthorization()
    {
        Assert.False(Program.TryParseArguments(["--developer-navigation-diagnostics", "--unknown-option"], out _, out _, out _, out _, out bool enabled));
        Assert.False(enabled);
    }
    [Fact]
    public void OptionCannotBeSmuggledAsAMissingSettingValue()
    {
        Assert.False(Program.TryParseArguments(["--settings-root", "--developer-navigation-diagnostics"], out _, out _, out _, out _, out bool enabled));
        Assert.False(enabled);
    }
    [Fact]
    public void UserSettingsDoNotPersistDeveloperAuthorization()
    {
        Assert.DoesNotContain("NavigationDiagnostics", JsonSerializer.Serialize(new ClientUserSettings()), StringComparison.OrdinalIgnoreCase);
    }
}
