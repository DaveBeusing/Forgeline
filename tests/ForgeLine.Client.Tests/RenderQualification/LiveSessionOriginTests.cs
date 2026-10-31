using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class LiveSessionOriginTests
{
    internal static LiveSessionOrigin Origin(ulong seed = 2026, bool smoke = true)
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed);
        return LiveSessionOrigin.Create(ClientSessionRequest.NewGame(settings), settings, smoke, 0,
            new(), SceneLightingSettings.Default, SceneAtmosphereSettings.Default, ParticleSettings.Default, new());
    }
    [Fact]
    public void ActualOriginSerializesScenarioSeedSettingsAndLoadModeWithoutLiveObjects()
    {
        var first = Origin();
        byte[] raw = Convert.FromBase64String(first.InputBytesBase64);
        Assert.Equal(first.Fingerprint, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)));
        Assert.NotEqual(first.Fingerprint, Origin(2027).Fingerprint);
        Assert.NotEqual(first.Fingerprint, Origin(smoke: false).Fingerprint);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(2026UL, json.RootElement.GetProperty("seed").GetUInt64());
        Assert.True(json.RootElement.GetProperty("smoke").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("scenarioKey").GetString()));
    }
}
