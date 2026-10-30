using ForgeLine.Presentation;
using Xunit;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Core;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceFixtureSequenceTests
{
    [Fact]
    public void AcknowledgementsCannotSkipDuplicateOrCoalesceCameraPoses()
    {
        var sequence = new ReferenceFixtureSequence(3);
        Assert.False(sequence.TryRead(out _));
        Assert.Throws<InvalidOperationException>(() => sequence.Request(1));
        sequence.Request(0);
        Assert.Throws<InvalidOperationException>(() => sequence.Request(1));
        Assert.True(sequence.TryRead(out int ordinal)); Assert.Equal(0, ordinal);
        Assert.Throws<InvalidOperationException>(() => sequence.Acknowledge(1));
        sequence.Acknowledge(0);
        Assert.Throws<InvalidOperationException>(() => sequence.Acknowledge(0));
        Assert.False(sequence.TryRead(out _));
        sequence.Request(1); sequence.Acknowledge(1);
        sequence.Request(2); sequence.Acknowledge(2);
        Assert.Equal(2, sequence.Acknowledged);
        Assert.Throws<InvalidOperationException>(() => sequence.Request(3));
    }
    [Fact]
    public void UnsupportedUiAndMultipleCaseRequestsCannotBeRelabelledAsMatchedWorldFixtures()
    {
        var request = Request();
        ReferenceFixtureHostCapture.ValidateSingleCase(request);
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { UiScenarios = [UiReferenceScenario.GameplayTooltip] }));
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { Scenes = [VisualReferenceSceneKind.IndustrialBase, VisualReferenceSceneKind.BattlefieldCombat] }));
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { Scenes = [VisualReferenceSceneKind.MaterialFoundation] }));
    }
    [Fact]
    public void ColdWindowHasNoInventedWarmBudgetSamples()
    {
        var request = Request() with { RunProfile = ReferenceRunProfile.RendererCold, Warmups = 0, Samples = 128 };
        ReferenceFixtureHostCapture.ValidateSingleCase(request);
        var sequence = new ReferenceFixtureSequence(128);
        for (int index = 0; index < 128; index++) { sequence.Request(index); sequence.Acknowledge(index); }
        Assert.Throws<InvalidOperationException>(() => sequence.Request(128));
    }
    [Fact]
    public void ActualWorldRecipeAndReadmodelsSerializeWithoutDroppingUiOrCoordinates()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ForgeLine.sln"))) root = Directory.GetParent(root)!.FullName;
        string runtime = Path.Combine(root, "assets/runtime");
        var catalog = RuntimeAssetCatalog.Load(runtime);
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var fixture = VisualReferenceScene.Create(VisualReferenceSceneKind.IndustrialBase, terrain, runtimeAssets: catalog);
        var request = Request() with { RuntimeRoot = runtime, RasterDpi = 96 };
        string runtimeHash = VisualReferenceCapture.RuntimeFingerprint(runtime);
        var recipe = new ReferenceFixtureRecipe(fixture, terrain, catalog, request, request.Displays[0], RenderQuality.Medium,
            RtsReferenceZoom.NormalGameplay, 96, 1, runtimeHash);
        byte[] bytes = Convert.FromBase64String(recipe.InputBytesBase64);
        Assert.Equal(recipe.Fingerprint, Convert.ToHexString(SHA256.HashData(bytes)));
        using var proof = JsonDocument.Parse(bytes);
        Assert.Equal(fixture.Target.X, proof.RootElement.GetProperty("cameraSettings").GetProperty("initialTarget").GetProperty("x").GetSingle());
        Assert.Equal(recipe.Information.SelectedEntities.Length, proof.RootElement.GetProperty("ui").GetProperty("information").GetProperty("selectedEntities").GetArrayLength());
        Assert.True(proof.RootElement.GetProperty("ui").GetProperty("panel").GetProperty("bindings").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Null, proof.RootElement.GetProperty("ui").GetProperty("panel").GetProperty("ring").GetProperty("group").ValueKind);
        var changed = new ReferenceFixtureRecipe(fixture, terrain, catalog, request with { YawDegrees = 46 }, request.Displays[0], RenderQuality.Medium,
            RtsReferenceZoom.NormalGameplay, 96, 1, runtimeHash);
        Assert.NotEqual(recipe.Fingerprint, changed.Fingerprint);
    }
    [Fact]
    public void SameLabelCannotHideDifferentVectorMatrixScalarOrEntityGeneration()
    {
        object[] inputs =
        [
            new { label = "same", vector = new Vector3(1, 2, 3), matrix = Matrix4x4.CreateTranslation(1, 2, 3), scalar = .2, entity = new EntityId(7, 2) },
            new { label = "same", vector = new Vector3(9, 2, 3), matrix = Matrix4x4.CreateTranslation(1, 2, 3), scalar = .2, entity = new EntityId(7, 2) },
            new { label = "same", vector = new Vector3(1, 2, 3), matrix = Matrix4x4.CreateTranslation(9, 2, 3), scalar = .2, entity = new EntityId(7, 2) },
            new { label = "same", vector = new Vector3(1, 2, 3), matrix = Matrix4x4.CreateTranslation(1, 2, 3), scalar = .3, entity = new EntityId(7, 2) },
            new { label = "same", vector = new Vector3(1, 2, 3), matrix = Matrix4x4.CreateTranslation(1, 2, 3), scalar = .2, entity = new EntityId(7, 3) }
        ];
        Assert.Equal(inputs.Length, inputs.Select(value => Convert.ToHexString(SHA256.HashData(ReferenceFixtureRecipe.SerializeIdentityInputs(value)))).Distinct().Count());
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureRecipe.WorldUiIdentity(default(PlayerActionPanelView) with
            { Ring = new(default, default, 0, 0, true, true) }, RtsInformationLayerView.Empty));
    }
    private static VisualReferenceCaptureRequest Request() => new()
    {
        OutputRoot = "unused", RuntimeRoot = "unused", Commit = new string('a', 40),
        Scenes = [VisualReferenceSceneKind.IndustrialBase], Profiles = [RenderQuality.Medium],
        Displays = [new(1920, 1080)], Zooms = [RtsReferenceZoom.NormalGameplay], Warmups = 64, Samples = 512
    };
}
