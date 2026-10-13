using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed record VisualReferenceCaptureRequest
{
    public required string OutputRoot { get; init; }
    public required string Commit { get; init; }
    public required string RuntimeRoot { get; init; }
    public string? Driver { get; init; }
    public bool Software { get; init; }
    public int Warmups { get; init; } = 32;
    public int Samples { get; init; } = 128;
    public int RepeatCaptures { get; init; } = 8;
    public bool ContinueAfterMismatch { get; init; }
    public uint RasterDpi { get; init; }
    public float YawDegrees { get; init; } = 45;
    public float InspectionDistanceMeters { get; init; }
    public ReferenceRunProfile RunProfile { get; init; }
    public ReferenceCameraPath CameraPath { get; init; }
    public VisualReferenceSceneKind[] Scenes { get; init; } =
    [
        VisualReferenceSceneKind.DaylightTerrain, VisualReferenceSceneKind.IndustrialBase,
        VisualReferenceSceneKind.FormationConvoy, VisualReferenceSceneKind.LogisticsCorridor,
        VisualReferenceSceneKind.DamageDestruction, VisualReferenceSceneKind.BattlefieldCombat
    ];
    public RenderQuality[] Profiles { get; init; } = Enum.GetValues<RenderQuality>();
    public RtsReferenceDisplay[] Displays { get; init; } = RtsVisualReference.Displays.ToArray();
    public RtsReferenceZoom[] Zooms { get; init; } = Enum.GetValues<RtsReferenceZoom>();
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(RuntimeRoot);
        if (!Enum.IsDefined(RunProfile) || !Enum.IsDefined(CameraPath)) throw new ArgumentException("Unsupported performance profile or camera path.");
        if (Commit is null || Commit.Length != 40 || Commit.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Capture requires a complete source commit.");
        if (Warmups is < 3 or > 4096 || Samples is < 8 or > 8192) throw new ArgumentOutOfRangeException(nameof(Samples));
        if (RepeatCaptures is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(RepeatCaptures));
        if (RasterDpi is not (0 or 96 or 120 or 144 or 192)) throw new ArgumentOutOfRangeException(nameof(RasterDpi));
        if (!float.IsFinite(InspectionDistanceMeters) || InspectionDistanceMeters != 0 && InspectionDistanceMeters is < 20 or > 120)
            throw new ArgumentOutOfRangeException(nameof(InspectionDistanceMeters));
        if (!float.IsFinite(YawDegrees) || YawDegrees is < 0 or > 360) throw new ArgumentOutOfRangeException(nameof(YawDegrees));
        if (Scenes is null || Profiles is null || Displays is null || Zooms is null ||
            Scenes.Length is < 1 or > 14 || Profiles.Length is < 1 or > 3 || Displays.Length is < 1 or > 4 || Zooms.Length is < 1 or > 3 ||
            Scenes.Any(x => !Enum.IsDefined(x)) || Profiles.Any(x => !Enum.IsDefined(x)) || Zooms.Any(x => !Enum.IsDefined(x)) ||
            Displays.Any(x => !RtsVisualReference.Displays.Contains(x) && !(Software && x == new RtsReferenceDisplay(640, 360))) ||
            Scenes.Distinct().Count() != Scenes.Length || Profiles.Distinct().Count() != Profiles.Length ||
            Zooms.Distinct().Count() != Zooms.Length || Displays.Distinct().Count() != Displays.Length)
            throw new ArgumentException("Capture matrix contains an unsupported or duplicated entry.");
    }
}

internal static class VisualReferenceCapture
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() } };

    internal static int Run(string requestPath)
    {
        VisualReferenceCaptureRequest? request = null;
        bool outputCreated = false;
        var captures = new List<object>();
        var captureInputs = new List<object>();
        var failedCaptures = new List<string>();
        long startupStart = Stopwatch.GetTimestamp();
        long startupAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            request = JsonSerializer.Deserialize<VisualReferenceCaptureRequest>(File.ReadAllText(requestPath), JsonOptions)
                ?? throw new ArgumentException("Empty capture request.");
            request.Validate();
            string identity = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            if (!identity.EndsWith(request.Commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Requested commit does not match the built client. Rebuild this revision.");
            if (Directory.Exists(request.OutputRoot) && Directory.EnumerateFileSystemEntries(request.OutputRoot).Any())
                throw new IOException("Evidence output must be empty; existing baselines are never overwritten.");
            Directory.CreateDirectory(request.OutputRoot);
            outputCreated = true;
            string runtimeFingerprint = RuntimeFingerprint(request.RuntimeRoot);
            var catalog = RuntimeAssetCatalog.Load(request.RuntimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixtures = request.Scenes.Select(x => VisualReferenceScene.Create(x, terrain, runtimeAssets: catalog)).ToArray();
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Reference Capture", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
            { EnableVSync = false, EnableMemoryDiagnostics = true, ForceSoftwareAdapter = request.Software,
                AllowSoftwareAdapterFallback = request.Software });
            if (graphics.Diagnostics.Device.IsSoftwareAdapter != request.Software)
                throw new InvalidOperationException("Capture adapter does not match the requested lane.");
            double startupMilliseconds = Stopwatch.GetElapsedTime(startupStart).TotalMilliseconds;
            long startupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startupAllocationStart;
            var lighting = SceneLightingSettings.Default;
            foreach (var display in request.Displays)
            foreach (var quality in request.Profiles)
            {
                graphics.ConfigurePostProcessing(GraphicsPostProcessingSettings.Neutral);
                graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Disabled });
                graphics.ConfigureDirectionalShadows(default);
                graphics.ConfigureSceneOutput(new(false, lighting.Exposure, true));
                graphics.Resize(display.Width, display.Height);
                graphics.RenderFrame(GraphicsColor.ForgeLineClear);
                var profile = new RenderQualitySettings { Quality = quality }.Resolve(graphics.SupportsPostProcessing,
                    graphics.Diagnostics.Device.IsSoftwareAdapter, display.Width, display.Height, 3, graphics.AvailableMemoryBudgetBytes);
                graphics.ConfigureSceneOutput(new(profile.LinearScene, lighting.Exposure, true));
                graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = profile.Atmosphere });
                graphics.ConfigureDirectionalShadows(profile.Shadows);
                graphics.ConfigurePostProcessing(profile.PostProcessing);
                foreach (var fixture in fixtures)
                foreach (var zoom in request.Zooms)
                {
                    graphics.WaitForIdle();
                    long setupStart = Stopwatch.GetTimestamp();
                    long setupAllocationStart = GC.GetAllocatedBytesForCurrentThread();
                    var beforeSetup = graphics.Diagnostics;
                    using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog, surfaceDetail: profile.TerrainDetail) { DebugChunksEnabled = false };
                    using var meshes = new SimpleInstanceRenderer(graphics, catalog, lodDiameterScale: profile.LodDiameterScale);
                    using var particles = new ParticleVfxRenderer(graphics, catalog, profile.Particles);
                    using var hud = new GameplayHudRenderer(graphics, catalog);
                    using var selection = new DebugDrawRenderer(graphics, depthEnabled: false, lineWidthPixels: 3);
                    if (!platform.PumpEvents()) throw new OperationCanceledException("Reference window was closed.");
                    string key = $"{fixture.Kind}-{display.Width}x{display.Height}-{quality}-{zoom}";
                    if (request.RasterDpi != 0) key += $"-Dpi{request.RasterDpi}";
                    if (request.YawDegrees != 45) key += $"-Yaw{request.YawDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    if (request.InspectionDistanceMeters != 0) key += $"-Inspection{request.InspectionDistanceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    if (request.RunProfile != ReferenceRunProfile.Warm) key += $"-{request.RunProfile}";
                    if (request.CameraPath != ReferenceCameraPath.Fixed) key += $"-{request.CameraPath}";
                    Console.WriteLine($"[reference:capture] {key}");
                    var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(fixture.Target, zoom) with
                        { InitialYawRadians = request.YawDegrees == 45 ? RtsVisualReference.YawRadians : request.YawDegrees * MathF.PI / 180,
                          InitialDistance = request.InspectionDistanceMeters == 0 ? RtsVisualReference.GetDistance(zoom) : request.InspectionDistanceMeters });
                    double captureVisualSeconds = fixture.Snapshot.Tick.Value * fixture.Snapshot.TickDuration.TotalSeconds;
                    var buffer = new PresentationSnapshotBuffer(); buffer.Publish(fixture.Snapshot);
                    var world = new RenderWorld(); world.Update(buffer);
                    particles.Prepare(world, camera, display.Width, display.Height, 0);
                    particles.Prepare(world, camera, display.Width, display.Height, .1);
                    particles.Prepare(world, camera, display.Width, display.Height, .1);
                    var selected = fixture.Snapshot.Instances.ToArray().Where(x => x.Selectable.IsSelectable).Take(3).Select(x => x.Entity).ToArray();
                    var indicators = new DebugDraw { Enabled = true };
                    foreach (var instance in fixture.Snapshot.Instances.ToArray().Where(x => selected.Contains(x.Entity)))
                    {
                        if (fixture.Kind is VisualReferenceSceneKind.CommandCoreProduction or VisualReferenceSceneKind.InfantryRifleProduction or VisualReferenceSceneKind.InfantryEngineerProduction or VisualReferenceSceneKind.InfantryRepeated or VisualReferenceSceneKind.InfantryBattleProduction)
                            RtsWorldMarkerVisualization.DrawSelected(indicators, instance, new(0, .8f, .2f, 1), terrain);
                        else
                            indicators.Circle(instance.Transform.Position + Vector3.UnitY, 5, new(0, .8f, .2f, 1));
                    }
                    var information = RtsInformationLayerView.Empty with { SelectedEntities = selected };
                    uint rasterDpi = request.RasterDpi != 0 ? request.RasterDpi :
                        display.Height == 2160 ? 192u : display.Height == 1440 ? 144u : 96u;
                    int recordFrameIndex = -1;
                    captureInputs.Clear();
                    Action<IGraphicsCommandContext> record = context =>
                    {
                        recordFrameIndex = context.FrameIndex;
                        if (graphics.Atmosphere.Enabled) context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera, context.Width, context.Height));
                        if (profile.Shadows.Enabled)
                        {
                            var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), lighting, profile.Shadows);
                            context.BeginDirectionalShadowPass(shadow);
                            int draws = ground.RenderShadow(context, shadow, 128);
                            meshes.RenderShadow(context, camera, world, 1, shadow, profile.Shadows.MaximumCasters - draws, profile.Shadows.MaximumDraws - draws, captureVisualSeconds);
                            context.EndDirectionalShadowPass();
                        }
                        ground.Render(context, camera);
                        particles.Prepare(world, camera, context.Width, context.Height, 0, true);
                        meshes.Render(context, camera, world, 1, captureVisualSeconds, particles);
                        particles.Render(context, camera);
                        context.BeginPass(GraphicsFramePass.Overlay);
                        selection.Render(context, camera, indicators);
                        hud.Render(context, camera, fixture.Snapshot, terrain.WorldBounds, information, default, default,
                            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, rasterDpi, 1);
                    };
                    double setupMilliseconds = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds;
                    long setupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - setupAllocationStart;
                    int warmupFrames = request.RunProfile == ReferenceRunProfile.RendererCold ? 0 : request.Warmups;
                    for (int i = 0; i < warmupFrames; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                    graphics.WaitForIdle();
                    var beforeSamples = graphics.Diagnostics;
                    var performance = new ReferencePerformanceSamples(request.Samples, beforeSamples.Frame?.GpuSubmissionFence ?? 0);
                    var initialCamera = camera.CaptureState();
                    var cpu = new double[request.Samples]; var gpu = new List<double>(); ulong lastFence = 0;
                    lastFence = beforeSamples.Frame?.GpuSubmissionFence ?? 0;
                    for (int i = 0; i < cpu.Length; i++)
                    {
                        camera.ApplyState(ReferencePerformanceSamples.CameraPose(initialCamera, request.CameraPath, i, request.Samples));
                        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                        long start = Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                        cpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                        var sampleDiagnostics = graphics.Diagnostics;
                        performance.AddCpu(i, cpu[i], allocatedBytes, sampleDiagnostics,
                            new { objects = meshes.SubmissionMetrics, terrain = ground.LastDiagnostics, instances = meshes.LastDiagnostics, particles = particles.Diagnostics });
                        if (sampleDiagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                        {
                            lastFence = frame.GpuSubmissionFence;
                            if (sampleDiagnostics.GpuFrameMilliseconds is { } milliseconds) gpu.Add(milliseconds);
                        }
                    }
                    graphics.WaitForIdle();
                    var measured = graphics.Diagnostics;
                    performance.AddGpu(measured);
                    if (measured.Frame is { } finalFrame && finalFrame.GpuSubmissionFence > lastFence && measured.GpuFrameMilliseconds is { } finalGpu)
                        gpu.Add(finalGpu);
                    camera.ApplyState(initialCamera);
                    // Restore fixed pose and settle history only after the timed window.
                    if (request.CameraPath != ReferenceCameraPath.Fixed)
                        for (int i = 0; i < 3; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                    if (measured.Surface.Width != display.Width || measured.Surface.Height != display.Height)
                        throw new InvalidOperationException("Actual native surface dimensions differ from the capture request.");
                    if (measured.Resources.TextureBindingFailureCount != 0 || meshes.MaterialDiagnostics.BindingFailureCount != 0 ||
                        meshes.LastDiagnostics.FallbackMeshInstances != 0 || measured.Health.PendingRetirementCount != 0 ||
                        measured.Health.SubmissionFaultCount != 0 || measured.Health.FailureReasonCode is not null ||
                        (measured.Debug.Enabled && measured.Debug.ErrorCount != 0))
                        throw new InvalidOperationException("Capture contains failed bindings or fallback meshes.");
                    byte[] rgba = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
                    bool repeatPixelsEqual = true;
                    var repeatFailures = new List<object>();
                    ReadOnlyMemory<byte> shadowDepth = graphics.CaptureShadowDepth;
                    captureInputs.Add(new { frameIndex = recordFrameIndex, fingerprint = graphics.CaptureInputFingerprint,
                        shadowFingerprint = shadowDepth.IsEmpty ? null : Convert.ToHexString(SHA256.HashData(shadowDepth.Span)) });
                    for (int repetition = 1; repetition < request.RepeatCaptures; repetition++)
                    {
                        byte[] repeated = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
                        captureInputs.Add(new { frameIndex = recordFrameIndex, fingerprint = graphics.CaptureInputFingerprint,
                            shadowFingerprint = graphics.CaptureShadowDepth.IsEmpty ? null : Convert.ToHexString(SHA256.HashData(graphics.CaptureShadowDepth.Span)) });
                        if (!rgba.AsSpan().SequenceEqual(repeated))
                        {
                            if (repeatPixelsEqual)
                                ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, $"failed-{key}-first.png"), display.Width, display.Height, rgba);
                            string failedImage = $"failed-{key}-repeat-{repetition:D2}.png";
                            ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, failedImage), display.Width, display.Height, repeated);
                            if (!shadowDepth.IsEmpty)
                            {
                                File.WriteAllBytes(Path.Combine(request.OutputRoot, $"failed-{key}-first.depth"), shadowDepth.Span);
                                File.WriteAllBytes(Path.Combine(request.OutputRoot, $"failed-{key}-repeat.depth"), graphics.CaptureShadowDepth.Span);
                            }
                            int differences = 0, maximumDelta = 0;
                            for (int i = 0; i < rgba.Length; i++)
                                if (rgba[i] != repeated[i]) { differences++; maximumDelta = Math.Max(maximumDelta, Math.Abs(rgba[i] - repeated[i])); }
                            string failure = $"Repeated fixed-state pixels differ on repetition {repetition}: {differences} channels, maximum delta {maximumDelta}; failed images exported.";
                            if (!request.ContinueAfterMismatch) throw new InvalidOperationException(failure);
                            Console.Error.WriteLine($"[reference:mismatch] {key}: {failure}");
                            repeatPixelsEqual = false;
                            repeatFailures.Add(new { repetition, differences, maximumDelta, image = failedImage,
                                imageSha256 = FileHash(Path.Combine(request.OutputRoot, failedImage)) });
                        }
                    }
                    if (!repeatPixelsEqual) failedCaptures.Add(key);
                    string image = key + ".png";
                    ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, image), display.Width, display.Height, rgba);
                    var motionImages = new List<object>();
                    if (display.Width == RtsVisualReference.Width && quality == RenderQuality.Medium && zoom == RtsReferenceZoom.NormalGameplay)
                    {
                        var original = camera.CaptureState();
                        for (int step = 0; step < 8; step++)
                        {
                            camera.ApplyState(original with { Target = original.Target + new Vector3(step * .5f, 0, step * .5f), YawRadians = original.YawRadians + step * .005f });
                            string motionImage = $"{key}-motion-{step:D2}.png";
                            byte[] motion = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
                            ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, motionImage), display.Width, display.Height, motion);
                            motionImages.Add(new { image = motionImage, imageSha256 = FileHash(Path.Combine(request.OutputRoot, motionImage)), camera = camera.CaptureState() });
                        }
                        camera.ApplyState(original);
                    }
                    captures.Add(new { submission = meshes.SubmissionMetrics, key, image, imageSha256 = FileHash(Path.Combine(request.OutputRoot, image)),
                        pixelSha256 = Convert.ToHexString(SHA256.HashData(rgba)), fixture.Kind, fixture.Fingerprint,
                        seed = VisualReferenceScene.Seed, tick = fixture.Snapshot.Tick.Value, captureVisualSeconds, camera = camera.CaptureState(),
                        requestedDisplay = display, actualSurface = measured.Surface, hostWindow = window.ClientSize,
                        rasterDpi, uiScale = 1, requestedQuality = quality, profile, lighting,
                        cpu = Distribution(cpu), gpu = Distribution(gpu.ToArray()), fps = Distribution(cpu.Select(x => 1000 / x).ToArray()),
                        performance = performance.Report(measured),
                        run = new { request.RunProfile, request.CameraPath, warmupFrames, setupMilliseconds, setupAllocatedBytes,
                            resourcesBeforeSetup = beforeSetup.Resources, resourcesBeforeSamples = beforeSamples.Resources,
                            cameraPathVersion = 1, cameraSamples = request.Samples,
                            coldScope = "Fresh per-case renderers; process/device/runtime catalog and OS caches can already be warm. No OS/driver cache flush." },
                        terrain = ground.LastDiagnostics, instances = meshes.LastDiagnostics, materials = meshes.MaterialDiagnostics, particles = particles.Diagnostics,
                        hudVertices = hud.LastRenderedVertexCount, graphics = measured, repeatPixelsEqual, request.RepeatCaptures, repeatFailures,
                        captureInputs = captureInputs.ToArray(),
                        motionImages, motionMethod = "Eight adjacent camera poses, no timing claim; readback stalls prevent judder acceptance.", visualReview = "pending" });
                    WriteManifest("in-progress", null);
                }
                graphics.WaitForIdle();
            }
            if (runtimeFingerprint != RuntimeFingerprint(request.RuntimeRoot)) throw new InvalidOperationException("Runtime assets changed during qualification.");
            string? aggregateFailure = failedCaptures.Count == 0 ? null : $"{failedCaptures.Count} fixed-state cases contain unequal native pixels.";
            WriteManifest(aggregateFailure is null ? "structural-pass" : "structural-failure", aggregateFailure);
            if (aggregateFailure is not null)
                File.WriteAllText(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new
                { status = "structural-failure", error = aggregateFailure, completedCaptures = captures.Count, failedCaptures }, JsonOptions));
            return aggregateFailure is null ? 0 : 1;

            void WriteManifest(string status, string? error) => File.WriteAllText(Path.Combine(request.OutputRoot, "manifest.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, status, error, request.Commit, buildIdentity = identity,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    environment = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        logicalProcessors = Environment.ProcessorCount, cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                        configuration = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration },
                    runtimeFingerprint, request.Driver, software = request.Software, fixtureVersion = VisualReferenceScene.FixtureVersion,
                    startupMilliseconds, startupAllocatedBytes,
                    capturePath = "D3D12 swap-chain backbuffer readback before Present; full-resolution native output, independent of host-window clamp",
                    method = "Immutable presentation states; completed extraction tick and frozen visual seconds recorded per case, VSync off, particle age.2s. CPU includes frame reuse/Present. Distinct completed GPU fences. Readbacks/PNG export excluded. No live gameplay or sustained-motion FPS claim.",
                    request.Warmups, request.Samples, request.ContinueAfterMismatch, failedCaptures, captures, humanVisualAcceptance = "pending" }, JsonOptions));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[reference:failure] {error.GetType().Name}: {error.Message}");
            // Never overwrite an existing evidence directory after a rejected request.
            if (request is not null && outputCreated)
                File.WriteAllText(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new { status = "structural-failure", error = error.ToString(), completedCaptures = captures.Count, captureInputs }, JsonOptions));
            return 1;
        }
    }

    internal static object Distribution(double[] values)
    {
        Array.Sort(values);
        return values.Length == 0 ? new { samples = 0, mean = (double?)null, p50 = (double?)null, p95 = (double?)null, p99 = (double?)null }
            : new { samples = values.Length, mean = (double?)values.Average(), p50 = (double?)values[(int)((values.Length - 1) * .5)],
                p95 = (double?)values[(int)Math.Ceiling(values.Length * .95) - 1], p99 = (double?)values[(int)Math.Ceiling(values.Length * .99) - 1] };
    }
    internal static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static string RuntimeFingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace('\\', '/')));
            hash.AppendData(Convert.FromHexString(FileHash(path)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
