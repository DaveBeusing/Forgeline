using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Rendering.Benchmarks;
using ForgeLine.Simulation;

internal static class SubmissionProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    public static void Main(string[] args)
    {
        string output = args[0], assets = args[1];
        using var fixtures = new PresentationBenchmarks(); fixtures.Setup();
        var original = Field<SimpleInstanceRenderer>(fixtures, "_renderer");
        var device = Field<IGraphicsDevice>(original, "_graphics");
        var world = Field<RenderWorld>(fixtures, "_mechanicalWorld");
        var camera = Field<RtsCamera>(fixtures, "_camera");
        var context = new CountingContext();
        using var renderer = new SimpleInstanceRenderer(new ShadowDevice(device), RuntimeAssetCatalog.Load(assets));
        var results = new List<object>();
        results.Add(Measure("mechanical-1000-original-fixture", i => fixtures.Submit1000MechanicalInstances(true), original));
        results.Add(Measure("near-1000-original-fixture", i => fixtures.Submit1000NearFieldInstances(), original));
        results.Add(Measure("culled-5000-original-fixture", i => fixtures.Submit5000InstancesWithCulling(), original));
        object? mainWork = null, shadowWork = null;
        results.Add(Measure("mechanical-1000-changing-alpha-main-and-light", i =>
        {
            float alpha = i % 2 == 0 ? .25f : .75f;
            renderer.Render(context, camera, world, alpha, presentationSeconds: i / 60d);
            var matrices = camera.GetMatrices(context.Width, context.Height);
            renderer.RenderShadow(context, camera, world, alpha, new(matrices.ViewProjection, camera.Target, 750, 2000), int.MaxValue, int.MaxValue, i / 60d);
        }, renderer));
        renderer.Render(context, camera, world, .5f, presentationSeconds: 17);
        mainWork = Work(renderer);
        renderer.RenderShadow(context, camera, world, .5f, new(camera.GetMatrices(context.Width, context.Height).ViewProjection, camera.Target, 750, 2000), int.MaxValue, int.MaxValue, 17);
        shadowWork = Work(renderer);
        var movingRoots = Enumerable.Range(0, world.InstanceCount).Select(index =>
        {
            RenderInstance root = world.GetInterpolatedInstance(index, 1);
            return root with { Transform = root.Transform with { Position = root.Transform.Position + new Vector3(.125f, 0, .125f),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .1f) },
                UnitFeature = root.UnitFeature with { AimYawRadians = .6f } };
        }).ToArray();
        var interpolationSnapshots = new PresentationSnapshotBuffer();
        interpolationSnapshots.Publish(new(new SimulationTick(33), TimeSpan.FromMilliseconds(50), movingRoots.Length, movingRoots));
        world.Update(interpolationSnapshots);
        results.Add(Measure("mechanical-1000-interpolated-main-and-light", i =>
        {
            float alpha = i % 2 == 0 ? .25f : .75f;
            renderer.Render(context, camera, world, alpha, presentationSeconds: i / 60d);
            renderer.RenderShadow(context, camera, world, alpha, new(camera.GetMatrices(context.Width, context.Height).ViewProjection, camera.Target, 750, 2000), int.MaxValue, int.MaxValue, i / 60d);
        }, renderer));
        renderer.Render(context, camera, world, .5f, presentationSeconds: 18);
        object? interpolatedMainWork = Work(renderer);
        renderer.RenderShadow(context, camera, world, .5f, new(camera.GetMatrices(context.Width, context.Height).ViewProjection, camera.Target, 750, 2000), int.MaxValue, int.MaxValue, 18);
        object? interpolatedLightWork = Work(renderer);
        context.DrawnInstances = 0;
        renderer.RenderShadow(context, camera, world, .5f, new(camera.GetMatrices(context.Width, context.Height).ViewProjection, camera.Target, 750, 2000), 1024, 256, 18);
        object? boundedLightWork = Work(renderer);
        long boundedDrawnParts = context.DrawnInstances;
        var random = new Random(8462);
        var roots = Enumerable.Range(0, 10000).Select(i => new RenderInstance(new EntityId((uint)i + 1, 1),
            new(new Vector3(random.Next(-1000,1000), 0, random.Next(-1000,1000)), Quaternion.Identity, Vector3.One),
            new(1), RenderMaterialHandle.Default, i % 7 == 0 ? RenderVisibilityMask.Debug : RenderVisibilityMask.World)).ToArray();
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), roots.Length, roots));
        var distributed = new RenderWorld(); distributed.Update(snapshots);
        results.Add(Measure("distributed-10000-pan-and-zoom", i =>
        {
            camera.ApplyState(camera.CaptureState() with { Target = new Vector3(i % 101 * 4 - 200, 0, 0), Distance = i % 3 == 0 ? 120 : i % 3 == 1 ? 420 : 900 });
            renderer.Render(context, camera, distributed, i % 2 == 0 ? .25f : .75f, presentationSeconds: i / 60d);
        }, renderer));
        camera.ApplyState(camera.CaptureState() with { Target = Vector3.Zero, Distance = 420 });
        var first = new PresentationSnapshot(new SimulationTick(2), TimeSpan.FromMilliseconds(50), roots.Length, roots);
        var movedRoots = roots.Select(root => root with { Transform = root.Transform with { Position = root.Transform.Position + new Vector3(.125f, 0, .125f) } }).ToArray();
        var second = new PresentationSnapshot(new SimulationTick(2), TimeSpan.FromMilliseconds(50), roots.Length, movedRoots);
        results.Add(Measure("distributed-10000-new-snapshot-pair", i =>
        {
            snapshots.Publish(i % 2 == 0 ? first : second); distributed.Update(snapshots);
            renderer.Render(context, camera, distributed, .5f, presentationSeconds: i / 60d);
        }, renderer));
        var version = typeof(SimpleInstanceRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        File.WriteAllText(output, JsonSerializer.Serialize(new { SchemaVersion = 1, SourceVersion = version,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ProcessorCount = Environment.ProcessorCount, Backend = "Null graphics; CPU only", Warmups = 256, Samples = 1024,
            AssetManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets,"manifest.json")))),
            ProbeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),
            RendererSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimpleInstanceRenderer).Assembly.Location))),
            Results = results, ChangingFrameMainWork = mainWork, ChangingFrameLightWork = shadowWork,
            InterpolatedFrameMainWork = interpolatedMainWork, InterpolatedFrameLightWork = interpolatedLightWork,
            BoundedLightWork = boundedLightWork, BoundedDrawnParts = boundedDrawnParts }, JsonOptions));
    }
    private static object? Work(SimpleInstanceRenderer renderer) => typeof(SimpleInstanceRenderer).GetProperty("WorkMetrics")?.GetValue(renderer);
    private static object Measure(string name, Action<int> frame, SimpleInstanceRenderer renderer)
    {
        for (int i = 0; i < 256; i++) frame(i);
        long[] ticks = new long[1024], allocations = new long[1024];
        for (int i = 0; i < ticks.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            frame(i);
            ticks[i] = Stopwatch.GetTimestamp() - start; allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        long[] ordered = (long[])ticks.Clone(); Array.Sort(ordered);
        double Microseconds(long value) => value * 1000000d / Stopwatch.Frequency;
        return new { Name = name, RawTicks = ticks, RawAllocatedBytes = allocations, Stopwatch.Frequency,
            P50Microseconds = Microseconds(ordered[511]), P95Microseconds = Microseconds(ordered[972]), P99Microseconds = Microseconds(ordered[1013]),
            Diagnostics = renderer.LastDiagnostics, Metrics = renderer.SubmissionMetrics, Work = Work(renderer) };
    }
    private sealed class ShadowDevice(IGraphicsDevice inner) : IGraphicsDevice
    {
        public GraphicsDirectionalShadowSettings DirectionalShadows => new(DirectionalShadowQuality.High);
        public GraphicsDiagnostics Diagnostics => inner.Diagnostics;
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription d) => inner.CreateGraphicsPipeline(d);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription d) => inner.CreateBuffer(d);
        public IGraphicsTexture CreateTexture(GraphicsTextureData d) => inner.CreateTexture(d);
        public void RenderFrame(GraphicsColor c, Action<IGraphicsCommandContext>? a = null) => inner.RenderFrame(c,a);
        public void Resize(int w,int h) => inner.Resize(w,h);
        public void WaitForIdle() => inner.WaitForIdle();
        public void Dispose() { }
    }
    private sealed class CountingContext : IGraphicsCommandContext
    {
        internal long DrawnInstances;
        public int Width => 1600; public int Height => 900; public int FrameIndex => 0;
        public void SetViewport(float x,float y,float w,float h) { }
        public void SetScissor(int l,int t,int r,int b) { }
        public void SetPipeline(IGraphicsPipeline p) { }
        public void SetVertexBuffer(IGraphicsBuffer b,int s,int offsetInBytes=0,int inputSlot=0) { }
        public void SetIndexBuffer(IGraphicsBuffer b,GraphicsIndexFormat f,int offsetInBytes=0) { }
        public void SetVertexConstants(ReadOnlySpan<float> v) { }
        public void SetPixelTexture(int s,IGraphicsTexture t) { }
        public void Draw(int c,int startVertex=0) { }
        public void DrawIndexed(int c,int startIndex=0,int baseVertex=0) { }
        public void DrawIndexedInstanced(int c,int n,int startIndex=0,int baseVertex=0,int startInstance=0) { DrawnInstances += n; }
    }
}
