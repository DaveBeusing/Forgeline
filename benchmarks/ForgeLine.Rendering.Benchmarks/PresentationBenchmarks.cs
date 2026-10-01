using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
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
    private RtsCamera _strategicCamera = null!;
    private NullGraphicsCommandContext _context = null!;
    private RenderWorld _visibleWorld = null!;
    private RenderWorld _culledWorld = null!;
    private RenderWorld _representativeWorld = null!;

    [GlobalSetup]
    public void Setup()
    {
        var graphics = new NullGraphicsDevice();
        _renderer = new SimpleInstanceRenderer(graphics);
        _camera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 120.0f,
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
    public InstanceRenderDiagnostics SubmitRepresentativeVerticalSliceStrategicView()
    {
        _renderer.Render(
            _context,
            _strategicCamera,
            _representativeWorld,
            1.0f);
        return _renderer.LastDiagnostics;
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
