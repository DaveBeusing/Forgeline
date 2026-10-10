using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientBuildIdentityTests
{
    [Fact]
    public void AssemblyAndFileVersionsRemainNumericAcrossChannels()
    {
        var assembly = typeof(ClientBuildIdentity).Assembly;
        string prefix = ClientBuildIdentity.ProductVersion.Split('-')[0];
        string[] components = prefix.Split('.');
        Assert.Equal(
            $"{components[0]}.{components[1]}.0.0",
            assembly.GetName().Version?.ToString());
        Assert.Equal(
            $"{prefix}.0",
            System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);
        Assert.Equal(
            2,
            ClientBuildIdentity.InformationalVersion.Split('+').Length);
        Assert.Matches("^[0-9a-f]{40}$", ClientBuildIdentity.SourceRevision);
    }

    [Fact]
    public void CrashReportContainsExactProductChannelAndRevision()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string? path = ClientDiagnostics.WriteCrashReport(
                new InvalidOperationException("Identity regression"), directory);
            Assert.NotNull(path);
            string report = File.ReadAllText(path);
            Assert.Contains($"Version: {ClientBuildIdentity.InformationalVersion}", report);
            Assert.Contains($"Channel: {ClientBuildIdentity.Channel}", report);
            Assert.Contains($"SourceRevision: {ClientBuildIdentity.SourceRevision}", report);
            Assert.Contains($"BuildId: {ClientBuildIdentity.BuildId}", report);
            Assert.Contains("Identity regression", report);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
