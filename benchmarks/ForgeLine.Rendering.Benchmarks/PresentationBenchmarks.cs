using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Rendering.Benchmarks;

[MemoryDiagnoser]
public class PresentationBenchmarks : IDisposable
{
    private SimpleInstanceRenderer _renderer = null!;
    private RtsCamera _camera = null!;
    private RtsCamera _normalRtsCamera = null!;
    private RtsCamera _strategicCamera = null!;
    private NullGraphicsCommandContext _context = null!;
    private RenderWorld _visibleWorld = null!;
    private RenderWorld _culledWorld = null!;
    private RenderWorld _representativeWorld = null!;
    private RenderWorld _roadReadabilityWorld = null!;

    public InstanceSubmissionMetrics SubmissionMetrics => _renderer.SubmissionMetrics;

    [GlobalSetup]
    public void Setup()
    {
        var graphics = new NullGraphicsDevice();
        RuntimeAssetCatalog runtimeAssets =
            LoadRuntimeAssets();
        _renderer =
            new SimpleInstanceRenderer(
                graphics,
                runtimeAssets);
        _camera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 120.0f,
                MaximumDistance = 1_200.0f
            });
        _normalRtsCamera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 420.0f,
                MaximumDistance = 1_200.0f
            });
        _strategicCamera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 900.0f,
                MaximumDistance = 1_200.0f
            });
        _context = new NullGraphicsCommandContext();
        _visibleWorld = CreateWorld(1_000, includeFarField: false);
        _culledWorld = CreateWorld(5_000, includeFarField: true);
        _representativeWorld = CreateRepresentativeWorld(1_200);
        _roadReadabilityWorld =
            CreateRoadReadabilityWorld(
                500);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _renderer?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public InstanceRenderDiagnostics Submit1000NearFieldInstances()
    {
        _renderer.Render(_context, _camera, _visibleWorld, 1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics Submit5000InstancesWithCulling()
    {
        _renderer.Render(_context, _camera, _culledWorld, 1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRepresentativeVerticalSliceTacticalView()
    {
        _renderer.Render(
            _context,
            _camera,
            _representativeWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRepresentativeVerticalSliceNormalRtsView()
    {
        _renderer.Render(
            _context,
            _normalRtsCamera,
            _representativeWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRepresentativeVerticalSliceStrategicView()
    {
        _renderer.Render(
            _context,
            _strategicCamera,
            _representativeWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRoadReadabilityTacticalView()
    {
        _renderer.Render(
            _context,
            _camera,
            _roadReadabilityWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRoadReadabilityNormalRtsView()
    {
        _renderer.Render(
            _context,
            _normalRtsCamera,
            _roadReadabilityWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public InstanceRenderDiagnostics SubmitRoadReadabilityStrategicView()
    {
        _renderer.Render(
            _context,
            _strategicCamera,
            _roadReadabilityWorld,
            1.0f);
        return _renderer.LastDiagnostics;
    }

    private static RuntimeAssetCatalog LoadRuntimeAssets()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                repositoryRoot,
                "assets",
                "runtime");

        return RuntimeAssetCatalog.Load(
            runtimeRoot);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory =
            new(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ForgeLine.sln")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the benchmark host.");
    }

    private static RenderWorld CreateRepresentativeWorld(
        int count)
    {
        var instances =
            new RenderInstance[count];
        int side =
            checked(
                (int)Math.Ceiling(
                    Math.Sqrt(
                        count)));
        const float spacing =
            7.0f;
        float halfSpan =
            (side - 1) *
            spacing *
            0.5f;

        for (int index = 0;
             index < count;
             index++)
        {
            int xIndex =
                index %
                side;
            int zIndex =
                index /
                side;
            Vector3 position =
                new(
                    xIndex * spacing -
                    halfSpan,
                    0.0f,
                    zIndex * spacing -
                    halfSpan);
            var entity =
                new EntityId(
                    checked(
                        (uint)index +
                        1U),
                    1);
            RenderInstance instance =
                new(
                    entity,
                    new RenderTransform(
                        position,
                        Quaternion.Identity,
                        new Vector3(
                            4.0f)),
                    new RenderMeshHandle(
                        1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityMask.World,
                    entity.Index);

            instance =
                (index % 10) switch
                {
                    0 or 1 or 2 =>
                        instance with
                        {
                            UnitFeature =
                                new UnitFeaturePresentationMetadata(
                                    UnitIds.MainBattleTank,
                                    UnitPresentationDamageState.Intact)
                        },
                    3 =>
                        instance with
                        {
                            UnitFeature =
                                new UnitFeaturePresentationMetadata(
                                    UnitIds.ScoutVehicle,
                                    UnitPresentationDamageState.Intact)
                        },
                    4 =>
                        instance with
                        {
                            BuildingFeature =
                                new BuildingFeaturePresentationMetadata(
                                    BuildingIds.CommandCore,
                                    BuildingPresentationState.Operational)
                        },
                    5 =>
                        instance with
                        {
                            BuildingFeature =
                                new BuildingFeaturePresentationMetadata(
                                    BuildingIds.VehicleFactory,
                                    BuildingPresentationState.Operational)
                        },
                    6 =>
                        instance with
                        {
                            WorldFeature =
                                new WorldFeaturePresentationMetadata(
                                    WorldVisualId.ResourceFerrousOre,
                                    WorldPresentationKind.ResourceDeposit,
                                    ResourceDepositPresentationState.Active,
                                    Inspectable: true)
                        },
                    7 =>
                        instance with
                        {
                            WorldFeature =
                                new WorldFeaturePresentationMetadata(
                                    WorldVisualId.VegetationConifer,
                                    WorldPresentationKind.Vegetation,
                                    ResourceDepositPresentationState.None,
                                    Inspectable: false)
                        },
                    8 =>
                        instance with
                        {
                            VfxFeature =
                                new VfxFeaturePresentationMetadata(
                                    VfxEffectKind.ExplosionMedium)
                        },
                    _ =>
                        instance with
                        {
                            WorldFeature =
                                new WorldFeaturePresentationMetadata(
                                    WorldVisualId.PropBarrier,
                                    WorldPresentationKind.Prop,
                                    ResourceDepositPresentationState.None,
                                    Inspectable: false)
                        }
                };

            instances[index] =
                instance;
        }

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(
                    1),
                TimeSpan.FromMilliseconds(
                    50),
                count,
                instances));

        var world =
            new RenderWorld();
        _ =
            world.Update(
                buffer);
        return world;
    }

    private static RenderWorld CreateRoadReadabilityWorld(
        int count)
    {
        var instances =
            new RenderInstance[
                count];
        int side =
            checked(
                (int)Math.Ceiling(
                    Math.Sqrt(
                        count)));
        const float spacing =
            28.0f;
        float halfSpan =
            (side - 1) *
            spacing *
            0.5f;

        for (int index = 0;
             index < count;
             index++)
        {
            int xIndex =
                index %
                side;
            int zIndex =
                index /
                side;
            Vector3 position =
                new(
                    xIndex *
                        spacing -
                    halfSpan,
                    0.0f,
                    zIndex *
                        spacing -
                    halfSpan);
            InfrastructurePresentationKind kind =
                (index % 5) switch
                {
                    0 =>
                        InfrastructurePresentationKind.RoadSegment,
                    1 =>
                        InfrastructurePresentationKind.RoadShoulder,
                    2 =>
                        InfrastructurePresentationKind.RoadCurveShort,
                    3 =>
                        InfrastructurePresentationKind.RoadJunctionT,
                    _ =>
                        InfrastructurePresentationKind.RoadJunctionCross
                };
            Vector3 scale =
                kind switch
                {
                    InfrastructurePresentationKind.RoadSegment =>
                        new Vector3(
                            12.0f,
                            0.35f,
                            36.0f),
                    InfrastructurePresentationKind.RoadShoulder =>
                        new Vector3(
                            15.0f,
                            0.30f,
                            36.0f),
                    InfrastructurePresentationKind.RoadCurveShort =>
                        new Vector3(
                            16.0f,
                            0.35f,
                            16.0f),
                    _ =>
                        new Vector3(
                            18.0f,
                            0.35f,
                            18.0f)
                };
            var entity =
                new EntityId(
                    checked(
                        (uint)index +
                        1U),
                    1);

            instances[index] =
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        position,
                        Quaternion.Identity,
                        scale),
                    new RenderMeshHandle(
                        1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityMask.World,
                    entity.Index,
                    InfrastructureFeature:
                        new InfrastructureFeaturePresentationMetadata(
                            kind,
                            InfrastructurePresentationState.Operational));
        }

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(
                    1),
                TimeSpan.FromMilliseconds(
                    50),
                count,
                instances));

        var world =
            new RenderWorld();
        _ =
            world.Update(
                buffer);
        return world;
    }

    private static RenderWorld CreateWorld(
        int count,
        bool includeFarField)
    {
        var instances = new RenderInstance[count];
        int side = checked((int)Math.Ceiling(Math.Sqrt(count)));
        const float nearSpacing = 5.0f;
        float nearHalfSpan = (side - 1) * nearSpacing * 0.5f;

        for (int index = 0; index < count; index++)
        {
            int xIndex = index % side;
            int zIndex = index / side;

            Vector3 position;
            if (includeFarField && index >= 1_000)
            {
                float farX = 10_000.0f + (index % 100) * 20.0f;
                float farZ = 10_000.0f + (index / 100) * 20.0f;
                position = new Vector3(farX, 0.0f, farZ);
            }
            else
            {
                position = new Vector3(
                    xIndex * nearSpacing - nearHalfSpan,
                    0.0f,
                    zIndex * nearSpacing - nearHalfSpan);
            }

            var entity = new EntityId((uint)(index + 1), 1);
            instances[index] = new RenderInstance(
                entity,
                new RenderTransform(
                    position,
                    Quaternion.Identity,
                    new Vector3(3.0f)),
                new RenderMeshHandle(1),
                RenderMaterialHandle.Default,
                RenderVisibilityMask.World,
                entity.Index);
        }

        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                count,
                instances));

        var world = new RenderWorld();
        _ = world.Update(buffer);
        return world;
    }

    private sealed class NullGraphicsDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new NullGraphicsPipeline(description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new NullGraphicsBuffer(description);

        public IGraphicsTexture CreateTexture(
            GraphicsTextureData texture) =>
            new NullGraphicsTexture(
                texture.Description);

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(
                new NullGraphicsCommandContext());
        }

        public void Resize(int width, int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsPipeline : IGraphicsPipeline
    {
        public NullGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description = description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsTexture : IGraphicsTexture
    {
        public NullGraphicsTexture(
            GraphicsTextureDescription description)
        {
            Description =
                description;
        }

        public GraphicsTextureDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsBuffer : IGraphicsBuffer
    {
        public NullGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description = description;
        }

        public GraphicsBufferDescription Description { get; }

        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public void SetViewport(
            float x,
            float y,
            float width,
            float height)
        {
        }

        public void SetScissor(
            int left,
            int top,
            int right,
            int bottom)
        {
        }

        public void SetPipeline(IGraphicsPipeline pipeline)
        {
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
        }

        public void SetVertexConstants(
            ReadOnlySpan<float> values)
        {
        }

        public void SetPixelTexture(
            int slot,
            IGraphicsTexture texture)
        {
        }

        public void Draw(
            int vertexCount,
            int startVertex = 0)
        {
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
        }
    }
}
