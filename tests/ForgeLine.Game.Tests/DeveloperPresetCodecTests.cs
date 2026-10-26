#if FORGELINE_DEVELOPER_TOOLS
using System.Text;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DeveloperPresetCodecTests
{
    private static DeveloperPresetDocument CreateDocument()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 481);
        var plan = DeveloperRestartBuilder.ForExisting(settings);
        var metadata = plan.InitialOverrides.Metadata;
        var profileMetadata = new DeveloperPresetMetadata(DeveloperPresetMetadata.CurrentSchema, metadata.CompositionKey,
            metadata.BaseIdentity, metadata.Seed, metadata.Profile, metadata.Participants.ToArray(), true);
        var overrideValue = new DeveloperEffectiveParameter(DeveloperParameterId.ConveyorThroughputMultiplier,
            DeveloperTarget.Global, DeveloperValue.FromDouble(1.25), new(DeveloperRevisionDomain.Parameters, 0));
        var profile = new DeveloperInitialProfile(profileMetadata, [overrideValue]);
        return DeveloperPresetCodec.Create("codec-test", "Codec test", profile);
    }

    [Fact]
    public void RoundTripPreservesCanonicalStartIdentityAndSeed()
    {
        var source = CreateDocument();
        byte[] json = DeveloperPresetCodec.Serialize(source);
        var restored = DeveloperPresetCodec.Deserialize(json);

        Assert.Equal(source.Key, restored.Key);
        Assert.Equal(source.Profile.Metadata.BaseIdentity, restored.Profile.Metadata.BaseIdentity);
        Assert.Equal(source.Profile.Metadata.CompositionKey, restored.Profile.Metadata.CompositionKey);
        Assert.Equal(source.Profile.Metadata.Seed, restored.Profile.Metadata.Seed);
        Assert.Equal(source.Profile.Metadata.Participants, restored.Profile.Metadata.Participants);
    }

    [Fact]
    public void RejectsDuplicateFieldsUnknownParametersAndOversizedDocuments()
    {
        string json = Encoding.UTF8.GetString(DeveloperPresetCodec.Serialize(CreateDocument()));
        Assert.Throws<DeveloperPresetException>(() => DeveloperPresetCodec.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("\"key\": \"codec-test\",", "\"key\": \"codec-test\",\n  \"key\": \"other\",", StringComparison.Ordinal))));
        Assert.Throws<DeveloperPresetException>(() => DeveloperPresetCodec.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("conveyor.throughput_multiplier", "unregistered.parameter", StringComparison.Ordinal))));
        Assert.Throws<DeveloperPresetException>(() => DeveloperPresetCodec.Deserialize(
            Encoding.UTF8.GetBytes(json.Replace("1.25", "5", StringComparison.Ordinal))));
        Assert.Throws<DeveloperPresetException>(() => DeveloperPresetCodec.Deserialize(new byte[DeveloperPresetCodec.MaximumDocumentBytes + 1]));
    }

    [Fact]
    public void PresetProfileBuildsANewSettingsObjectWithoutChangingBasis()
    {
        var document = CreateDocument();
        var plan = DeveloperPresetCodec.ValidateAndBuildPlan(document.Profile);

        Assert.Equal(document.Profile.Metadata.BaseIdentity, plan.Settings.Composition.Identity);
        Assert.Equal(document.Profile.Metadata.Seed, plan.Settings.Seed);
        Assert.NotSame(plan.BaseSettings, plan.Settings);
        Assert.NotNull(plan.Settings.InitialDeveloperProfile);
        Assert.True(DeveloperToolsCapability.Current.IsCompiled);
    }
}
#endif
