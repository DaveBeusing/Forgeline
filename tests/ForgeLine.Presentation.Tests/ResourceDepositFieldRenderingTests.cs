using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation.Tests.AssetResidency;
using ForgeLine.Presentation.Tests.RenderSubmission;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourceDepositFieldRenderingTests
{
    [Fact]
    public void FieldPartsShareCanonicalResourcesWithoutCreatingExtraSnapshotOrSelectionRoots()
    {
        using var fixture = new ResidencyFixture(sealedManifest: false);
        const string material = "material.world.resource.ferrous_ore";
        fixture.AddMaterial(material);
        byte[] mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent,
            [new(-.5f,-.5f,0,0,0,1,0,0,1,0,0,1), new(.5f,-.5f,0,0,0,1,1,0,1,0,0,1), new(0,.5f,0,0,0,1,.5f,1,1,0,0,1)],
            [0u,1u,2u], [material], [new(0,3,0)]).ToPayload();
        fixture.Add("mesh.world.resource.ferrous_ore", RuntimeAssetType.Mesh, mesh, [material]);
        fixture.Add("mesh.world.resource.ferrous_ore_lod1", RuntimeAssetType.Mesh, mesh, [material]);
        var field = ResourceDepositFieldComposition.WithRemaining(
        [
            new(0, new(new(-8, 5, 0), Quaternion.Identity, new(6)), "mesh.world.resource.ferrous_ore", "mesh.world.resource.ferrous_ore_lod1", Vector4.One, true),
            new(1, new(new(8, 5, 0), Quaternion.Identity, new(6)), "mesh.world.resource.ferrous_ore", "mesh.world.resource.ferrous_ore_lod1", Vector4.One, false)
        ], 1)!;
        var root = new RenderInstance(new EntityId(1, 1), new(Vector3.Zero, Quaternion.Identity, new(30)),
            new(4001), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            WorldFeature: new(WorldVisualId.ResourceFerrousOre, WorldPresentationKind.ResourceDeposit,
                ResourceDepositPresentationState.Untouched, true), ResourceField: field);
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, [root]));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        using var graphics = new FieldGraphicsDevice();
        using var renderer = new SimpleInstanceRenderer(graphics, fixture.Catalog());
        var camera = new RtsCamera(new() { InitialDistance = 100 });
        renderer.Render(new SubmissionGraphics.FakeGraphicsCommandContext(), camera, world, 1);
        Assert.Equal(1, world.InstanceCount);
        Assert.Equal(root, world.GetInterpolatedInstance(0, 1));
        Assert.Equal(1, renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(2, renderer.SubmissionMetrics.SubmittedInstances);
        Assert.Equal(1, renderer.LastDiagnostics.DrawCalls);
        Assert.True(world.GetInterpolatedInstance(0, 1).WorldFeature.IsInspectable);
        Assert.False(world.GetInterpolatedInstance(0, 1).Selectable.IsSelectable);
        buffer.Publish(new(new SimulationTick(2), TimeSpan.FromMilliseconds(50), 1, [root with { Visibility = RenderVisibilityMask.Debug }]));
        Assert.True(world.Update(buffer));
        renderer.Render(new SubmissionGraphics.FakeGraphicsCommandContext(), camera, world, 1);
        Assert.Equal(0, renderer.LastDiagnostics.VisibleInstances);
    }

    private sealed class FieldGraphicsDevice : IGraphicsDevice
    {
        private readonly SubmissionGraphics.FakeGraphicsDevice _inner = new();
        public GraphicsDiagnostics Diagnostics => new(new("mineral-fields", 0, true, "test", false), default);
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => _inner.CreateGraphicsPipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => _inner.CreateBuffer(description);
        public IGraphicsTexture CreateTexture(GraphicsTextureData texture) => new ResidencyDevice.Texture(texture.Description);
        public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null) => _inner.RenderFrame(clearColor, recordCommands);
        public void Resize(int width, int height) => _inner.Resize(width, height);
        public void WaitForIdle() => _inner.WaitForIdle();
        public void Dispose() => _inner.Dispose();
    }
}
