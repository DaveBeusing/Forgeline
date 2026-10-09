using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class SelectionHotPathMeasurements
{
    private const int Warmup = 128;
    private const int Samples = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string output)
    {
        var results = new List<Measurement>();
        foreach (var size in new[] { (1024, 720), (1600, 900), (3440, 1440) })
            foreach (uint dpi in new[] { 96u, 144u })
                foreach (float uiScale in new[] { .75f, 2f })
                    foreach (int count in new[] { 1, 1000 })
                        foreach (bool click in new[] { false, true })
                        {
                            var fixture = new Fixture(size.Item1, size.Item2, dpi / 96f * uiScale, count, click);
                            var measured = Measure(fixture);
                            results.Add(new(size.Item1, size.Item2, dpi, uiScale, count, click, measured.Bytes,
                                measured.P50, measured.P95, measured.P99,
                                fixture.Controller.Selection.Count));
                        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Backend = "CPU input and picking; idle hover or two click edges selecting visible same-type units; excludes simulation, HUD geometry, GPU and Present",
            BuildVersion = typeof(RtsSelectionController).Assembly.GetName().Version?.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            Warmup, Samples,
            TimingPolicy = "Timing is observational; zero warm allocation and expected selection count are hard gates.",
            Results = results
        }, JsonOptions));
        if (results.Any(r => r.AllocatedBytes != 0 || (r.DoubleClick && r.SelectedCount != r.EntityCount)))
            throw new InvalidOperationException("Selection hot paths violated allocation or visible selection contracts.");
        Console.WriteLine($"Selection hot paths: {results.Count} cases; zero allocation and expected selection; {Path.GetFullPath(output)}");
    }

    private sealed record Measurement(int Width, int Height, uint Dpi, float UiScale, int EntityCount,
        bool DoubleClick, long AllocatedBytes, double P50Microseconds, double P95Microseconds, double P99Microseconds, int SelectedCount);

    private static (long Bytes, double P50, double P95, double P99) Measure(Fixture fixture)
    {
        for (int i = 0; i < Warmup; i++) fixture.Frame();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var times = new double[Samples];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            fixture.Frame();
            times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(times);
        return (bytes, times[Samples / 2], times[(int)(Samples * .95)], times[(int)(Samples * .99)]);
    }

    private sealed class Fixture
    {
        private readonly InputState _input = new();
        private readonly RtsCamera _camera = new(new RtsCameraSettings { EdgeScrollEnabled = false, InitialDistance = 100 });
        private readonly RenderWorld _world = new();
        private readonly FlatTerrain _terrain = new();
        private readonly int _width;
        private readonly int _height;
        private readonly float _scale;
        private readonly bool _click;
        public RtsSelectionController Controller { get; } = new(new(new PlayerId(1), ControllableEntityCategory.Unit));

        public Fixture(int width, int height, float scale, int count, bool click)
        {
            _width = width; _height = height; _scale = scale; _click = click;
            var instances = new RenderInstance[count];
            for (int i = 0; i < count; i++)
                instances[i] = new(new EntityId((uint)i + 1, 1),
                    new(new Vector3(i % 32 - 16, 0, i / 32 - 16), Quaternion.Identity, Vector3.One),
                    new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                    Selectable: new(new PlayerId(1), ControllableEntityCategory.Unit, CanMove: true),
                    UnitFeature: new(UnitIds.ScoutVehicle, default));
            var buffer = new PresentationSnapshotBuffer();
            buffer.Publish(new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), count, instances, sessionId: new(1)));
            _world.Update(buffer);
            Vector2 pointer = _camera.WorldToScreen(instances[0].Transform.Position, width, height).Position;
            _input.Apply(PlatformInputEvent.PointerMoved((int)pointer.X, (int)pointer.Y));
        }

        public void Frame()
        {
            Update();
            if (_click) Update();
        }

        private void Update()
        {
            _input.BeginFrame();
            if (_click)
            {
                int x = (int)_input.PointerPosition.X, y = (int)_input.PointerPosition.Y;
                _input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
                _input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
            }
            Controller.Update(_input, _camera, _world, _terrain, _width, _height, 1, pointerScale: _scale,
                elapsed: TimeSpan.FromMilliseconds(16));
        }
    }

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-10_000, -100, -10_000), new(10_000, 100, 10_000));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
