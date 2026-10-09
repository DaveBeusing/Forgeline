using System.Diagnostics;
using System.Numerics;
using ForgeLine.Benchmarks;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class RenderingScalabilityMeasurements
{
    public static void Run(string output, int samples, bool native)
    {
        using WindowsPlatform? platform = native ? new WindowsPlatform() : null;
        using IWindow? window = platform?.CreateWindow(new WindowConfiguration("Renderer scale qualification", 1600, 900));
        using IGraphicsDevice graphics = window is null ? new PresentationBenchmarks.NullGraphicsDevice()
            : GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
            {
                EnableVSync = false,
                EnableMemoryDiagnostics = true,
                AllowSoftwareAdapterFallback = false,
                EnableDebugLayer = Environment.GetEnvironmentVariable("FORGELINE_D3D12_DEBUG_LAYER") == "1"
            });
        var assets = PresentationBenchmarks.LoadRuntimeAssets();
        var context = new PresentationBenchmarks.NullGraphicsCommandContext();
        var results = new List<object>();
        int? initialLiveResources = native ? graphics.Diagnostics.Health.LiveResourceCount : null;
        foreach (var scene in new[]
        {
            new Scene("empty", 0, -1, 420, false, false),
            new Scene("medium", 1000, 3, 420, false, false),
            new Scene("large-strategic", 10000, 8, 900, false, false),
            new Scene("mixed-effects-debug-ui", 1200, 3, 120, true, false),
            new Scene("rapid-camera-lod", 1200, 3, 420, true, true),
            new Scene("lod-boundary", 1, 0, 140, false, false),
            new Scene("lod-hysteresis", 1, 0, 140, false, false)
        })
        {
            var settings = new WorldGridSettings();
            var terrainWorld = scene.Radius <= 0
                ? new TerrainWorld(settings, [DevelopmentTerrainFactory.CreateChunk(new ChunkCoordinate(0, 0), settings)])
                : DevelopmentTerrainFactory.CreateRepresentativeWorld(settings, scene.Radius);
            var world = PresentationBenchmarks.CreateRepresentativeWorld(scene.Instances);
            var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = scene.Distance, MaximumDistance = 1200 });
            float lodBoundaryDistance = 0;
            if (scene.Name is "lod-boundary" or "lod-hysteresis")
            {
                RenderInstance fixture = world.GetInterpolatedInstance(0, 1);
                float radius = PresentationBounds.ResolveLocalHalfExtents(fixture).Length();
                lodBoundaryDistance = radius * camera.GetMatrices(1600, 900).Projection.M22 * RtsVisualReference.Height / 90.0f;
            }
            using var instances = new SimpleInstanceRenderer(graphics, assets);
            using var terrain = scene.Radius < 0 ? null : new TerrainRenderer(graphics, terrainWorld, runtimeAssets: assets);
            using var lines = new DebugDrawRenderer(graphics);
            using var overlay = new DevelopmentOverlayRenderer(graphics);
            var debug = new DebugDraw { Enabled = scene.Overlay };
            for (int i = 0; i < 128; i++) debug.Line(new Vector3(i, 1, 0), new Vector3(i, 1, 128), Vector4.One);
            var terrainMs = new double[samples];
            var instanceMs = new double[samples];
            var overlayMs = new double[samples];
            var frameMs = new double[samples];
            var gpuMs = new double?[samples];
            var visible = new int[samples];
            var highLod = new int[samples];
            var upload = new long[samples];
            double lastTerrain = 0, lastInstance = 0, lastOverlay = 0, lastFrame = 0;
            GraphicsDiagnostics? lastGraphics = null;
            var resourceWindows = new List<object>();
            int frame = 0;
            void Record(IGraphicsCommandContext commands)
            {
                long clock = Stopwatch.GetTimestamp();
                terrain?.Render(commands, camera);
                lastTerrain = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
                clock = Stopwatch.GetTimestamp();
                instances.Render(commands, camera, world, 1);
                lastInstance = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
                clock = Stopwatch.GetTimestamp();
                if (scene.Overlay)
                {
                    lines.Render(commands, camera, debug);
                    overlay.Render(commands, default, camera, debug);
                }
                lastOverlay = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
            }
            int Submit()
            {
                long started = Stopwatch.GetTimestamp();
                if (scene.Name == "lod-boundary")
                    // Cross both sides of the projected-size hysteresis band.
                    camera.ApplyState(camera.CaptureState() with { Distance = lodBoundaryDistance * (frame % 2 == 0 ? 0.8f : 1.3f) });
                if (scene.Name == "lod-hysteresis")
                    // Tiny oscillation around the nominal threshold must retain LOD.
                    camera.ApplyState(camera.CaptureState() with { Distance = lodBoundaryDistance * (frame % 2 == 0 ? 0.999f : 1.001f) });
                if (scene.Motion)
                {
                    var state = camera.CaptureState();
                    camera.ApplyState(state with
                    {
                        Target = new Vector3(MathF.Sin(frame * .2f) * 200, 0, 0),
                        Distance = frame % 2 == 0 ? 419 : 421,
                        YawRadians = frame * .03f
                    });
                }
                if (native)
                {
                    if (!platform!.PumpEvents() || !window!.IsOpen) throw new InvalidOperationException("Qualification window closed early.");
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                    // Serial completion makes diagnostics correspond to this frame.
                    // This is throughput qualification, not asynchronous client pacing.
                    graphics.WaitForIdle();
                }
                else Record(context);
                lastFrame = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return ++frame;
            }
            results.Add(ScalabilityMeasurement.Measure(scene.Name, native
                ? "D3D12 serial RenderFrame + WaitForIdle; excludes simulation; no asynchronous 60-FPS claim"
                : "Null graphics CPU terrain/instance/debug UI submission; excludes native uploads and presentation",
                Submit, samples, budgetMilliseconds: 1000.0 / 60,
                observe: index =>
                {
                    terrainMs[index] = lastTerrain; instanceMs[index] = lastInstance; overlayMs[index] = lastOverlay; frameMs[index] = lastFrame;
                    visible[index] = instances.LastDiagnostics.VisibleInstances; highLod[index] = instances.LastDiagnostics.HighLodInstances;
                    upload[index] = instances.SubmissionMetrics.UploadBytes;
                    if (native)
                    {
                        lastGraphics = graphics.Diagnostics; gpuMs[index] = lastGraphics.GpuFrameMilliseconds;
                        if ((index + 1) % Math.Max(1, samples / 4) == 0)
                            resourceWindows.Add(new { CompletedSamples = index + 1, lastGraphics.Resources, lastGraphics.Health, lastGraphics.Memory });
                    }
                }, counters: () => new
                {
                    Scene = scene,
                    Viewport = "1600x900",
                    Alpha = 1,
                    InstanceDiagnostics = instances.LastDiagnostics,
                    instances.SubmissionMetrics,
                    TerrainDiagnostics = terrain?.LastDiagnostics,
                    DebugDiagnostics = lines.LastDiagnostics,
                    OverlayVertices = overlay.LastRenderedVertexCount,
                    Graphics = lastGraphics,
                    ResourceWindows = resourceWindows,
                    InstancePayloadBytesPerSecond = upload.Sum() / (frameMs.Sum() / 1000)
                }));
            double[] availableGpu = gpuMs.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            if (scene.Name == "lod-boundary" && (visible.Any(x => x != 1) || highLod.Zip(highLod.Skip(1)).Any(x => x.First == x.Second)))
                throw new InvalidOperationException("The LOD boundary fixture did not exercise alternating visible LODs.");
            if (scene.Name == "lod-hysteresis" && (visible.Any(x => x != 1) || highLod.Any(x => x != 1)))
                throw new InvalidOperationException("The LOD hysteresis fixture did not retain visible high detail inside its stability band.");
            results.Add(new
            {
                Scene = scene.Name,
                Correlation = "Array index is the same sampled serial frame; GPU time null means unavailable",
                CpuTerrain = ScalabilityMeasurement.Summarize(terrainMs),
                CpuInstances = ScalabilityMeasurement.Summarize(instanceMs),
                CpuOverlay = ScalabilityMeasurement.Summarize(overlayMs),
                GpuSampleCount = availableGpu.Length,
                GpuTiming = availableGpu.Length > 0 ? ScalabilityMeasurement.Summarize(availableGpu, 1000.0 / 60) : null,
                RawTerrainMilliseconds = terrainMs,
                RawInstanceMilliseconds = instanceMs,
                RawOverlayMilliseconds = overlayMs,
                RawGpuMilliseconds = gpuMs,
                VisibleInstances = visible,
                HighLodInstances = highLod,
                LodTransitionCount = highLod.Zip(highLod.Skip(1)).Count(x => x.First != x.Second),
                UploadedInstanceBytes = upload
            });
            graphics.WaitForIdle();
        }
        var reloadWorld = PresentationBenchmarks.CreateRepresentativeWorld(1000);
        var reloadCamera = new RtsCamera();
        results.Add(ScalabilityMeasurement.Measure("instance-scene-reload", "Fresh instance renderer + first submission + disposal; includes lazy mesh/material loads; no terrain or gameplay tick",
            () =>
            {
                using (var renderer = new SimpleInstanceRenderer(graphics, assets))
                {
                    if (native) graphics.RenderFrame(GraphicsColor.ForgeLineClear, commands => renderer.Render(commands, reloadCamera, reloadWorld, 1));
                    else renderer.Render(context, reloadCamera, reloadWorld, 1);
                    graphics.WaitForIdle();
                }
                graphics.WaitForIdle();
                if (native && graphics.Diagnostics.Health.LiveResourceCount != initialLiveResources)
                    throw new InvalidOperationException("Scene unload did not return to the device resource baseline.");
                return reloadWorld.InstanceCount;
            }, samples: 16, warmup: 4));
        if (native)
        {
            graphics.Resize(0, 0);
            graphics.RenderFrame(GraphicsColor.ForgeLineClear);
            bool suspended = graphics.Diagnostics.Surface.IsSuspended;
            graphics.Resize(800, 450); graphics.RenderFrame(GraphicsColor.ForgeLineClear); graphics.WaitForIdle();
            graphics.Resize(1600, 900); graphics.RenderFrame(GraphicsColor.ForgeLineClear); graphics.WaitForIdle();
            GraphicsDiagnostics final = graphics.Diagnostics;
            if (!suspended || final.Surface.IsSuspended || final.Surface.ResizePending || final.Debug.ErrorCount > 0)
                throw new InvalidOperationException("Native surface recovery or debug-layer contract failed.");
            results.Add(new { NativeSurfaceRecovery = true, Final = final });
        }
        ScalabilityMeasurement.Write(output, native ? "D3D12, VSync off, hardware adapter, serial completed frames" : "CPU null graphics",
            results, [typeof(SimpleInstanceRenderer).Assembly.Location, typeof(RenderingScalabilityMeasurements).Assembly.Location]);
    }

    private sealed record Scene(string Name, int Instances, int Radius, float Distance, bool Overlay, bool Motion);
}
