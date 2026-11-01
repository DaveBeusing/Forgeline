using ForgeLine.Presentation.RenderContracts;
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

internal enum ReferenceCollectorMode { Full, CollectorOn, CollectorOff }

internal sealed record VisualReferenceCaptureRequest
{
    public required string OutputRoot { get; init; }
    public required string Commit { get; init; }
    public required string RuntimeRoot { get; init; }
    public string? Driver { get; init; }
    public string? AdapterBindingDirectory { get; init; }
    public string? OwnerTraceControlDirectory { get; init; }
    public int OwnerObservationVersion { get; init; }
    public int OwnerCycleCapacity { get; init; } = 8192;
    public string? PostWindowControlDirectory { get; init; }
    public bool Software { get; init; }
    public int Warmups { get; init; } = 32;
    public int Samples { get; init; } = 128;
    public int RepeatCaptures { get; init; } = 8;
    public bool ContinueAfterMismatch { get; init; }
    public bool CaptureShadowDiagnostics { get; init; }
    public bool CaptureStageDiagnostics { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CaptureBoundInputProvenanceVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CapturePostFrameResourceSnapshotVersion { get; init; }
    public ReferenceCollectorMode CollectorMode { get; init; }
    public uint RasterDpi { get; init; }
    public float YawDegrees { get; init; } = 45;
    public double? MechanicalSeconds { get; init; }
    public float InspectionDistanceMeters { get; init; }
    public ReferenceRunProfile RunProfile { get; init; }
    public ReferenceCameraPath CameraPath { get; init; }
    public UiReferenceScenario[] UiScenarios { get; init; } = [UiReferenceScenario.World];
    public float[] UiScales { get; init; } = [1];
    public bool CinematicFrontend { get; init; }
    public VisualReferenceSceneKind[] Scenes { get; init; } =
    [
        VisualReferenceSceneKind.DaylightTerrain, VisualReferenceSceneKind.IndustrialBase,
        VisualReferenceSceneKind.FormationConvoy, VisualReferenceSceneKind.LogisticsCorridor,
        VisualReferenceSceneKind.DamageDestruction, VisualReferenceSceneKind.BattlefieldCombat
    ];
    public RenderQualitySettings? GraphicsSettings { get; init; }
    public GraphicsLocalHazeSettings? LocalHaze { get; init; }
    public TerrainSurfaceMark[] SurfaceMarks { get; init; } = [];
    public RenderQuality[] Profiles { get; init; } = Enum.GetValues<RenderQuality>();
    public RtsReferenceDisplay[] Displays { get; init; } = RtsVisualReference.Displays.ToArray();
    public RtsReferenceZoom[] Zooms { get; init; } = Enum.GetValues<RtsReferenceZoom>();
    public void Validate() => Validate(allowBoundInputProvenance: false, allowPostFrameSnapshot: false);
    internal void Validate(bool allowBoundInputProvenance, bool allowPostFrameSnapshot = false)
    {
        if (CaptureBoundInputProvenanceVersion is not null)
        {
            if (!allowBoundInputProvenance || CaptureBoundInputProvenanceVersion != 1)
                throw new NotSupportedException("CPU-source provenance is native-only version1; shared host/preflight modes reject selection.");
            if (CaptureStageDiagnostics || CaptureShadowDiagnostics || OwnerTraceControlDirectory is not null ||
                CollectorMode != ReferenceCollectorMode.Full || RunProfile != ReferenceRunProfile.Warm || CameraPath != ReferenceCameraPath.Fixed ||
                Scenes is not { Length: 1 } || Profiles is not { Length: 1 } || Zooms is not { Length: 1 } || Displays is not { Length: 1 } ||
                Displays[0] != new RtsReferenceDisplay(1920, 1080) || UiScenarios is not [UiReferenceScenario.World] || UiScales is not [1])
                throw new NotSupportedException("CPU-source provenance requires one fixed1920World native Full case with the stage/shadow/trace bundle disabled.");
        }
        if (CapturePostFrameResourceSnapshotVersion is not null)
        {
            if (!allowPostFrameSnapshot || CapturePostFrameResourceSnapshotVersion != 1 || CaptureBoundInputProvenanceVersion is not null ||
                CaptureStageDiagnostics || CaptureShadowDiagnostics || OwnerTraceControlDirectory is not null || CollectorMode != ReferenceCollectorMode.Full ||
                RunProfile != ReferenceRunProfile.Warm || CameraPath != ReferenceCameraPath.Fixed || Warmups != 64 || Samples != 512 || RepeatCaptures != 8 ||
                OwnerObservationVersion != 1 || OwnerCycleCapacity != 8192 || RasterDpi != 96 || !ContinueAfterMismatch ||
                Scenes is not [VisualReferenceSceneKind.IndustrialBase] || Profiles is not [RenderQuality.Medium] || Zooms is not [RtsReferenceZoom.NormalGameplay] ||
                Displays is not { Length: 1 } || Displays[0] != new RtsReferenceDisplay(1920, 1080) || UiScenarios is not [UiReferenceScenario.World] || UiScales is not [1])
                throw new NotSupportedException("Post-frame snapshot v1 is native-only fixed IndustrialBase/Medium1920World64/512/8 with pre-draw diagnostics disabled.");
        }
        ValidateCommon();
    }
    private void ValidateCommon()
    {
        GraphicsSettings?.Validate();
        LocalHaze?.Validate();
        ArgumentNullException.ThrowIfNull(SurfaceMarks);
        _ = new TerrainSurfaceMarks(SurfaceMarks);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(RuntimeRoot);
        if (UiScenarios is null || UiScenarios.Length is < 1 or > 30 || UiScenarios.Any(x => !Enum.IsDefined(x)) ||
            UiScenarios.Distinct().Count() != UiScenarios.Length || UiScales is null || UiScales.Length is < 1 or > 3 ||
            UiScales.Any(x => x is not (.75f or 1f or 2f)) || UiScales.Distinct().Count() != UiScales.Length)
            throw new ArgumentException("Unsupported or duplicate UI scenario/scale.");
        bool ui = UiScenarios.Any(x => x != UiReferenceScenario.World);
        if (ui && (Scenes is null || Scenes.Length != 1 || Scenes[0] != VisualReferenceSceneKind.BattlefieldCombat ||
            Zooms is null || Zooms.Length != 1 || Zooms[0] != RtsReferenceZoom.NormalGameplay || CameraPath != ReferenceCameraPath.Fixed))
            throw new ArgumentException("Named UI states require the fixed BattlefieldCombat normal-zoom fixture.");
        if (!Enum.IsDefined(CollectorMode) || !Enum.IsDefined(RunProfile) || !Enum.IsDefined(CameraPath)) throw new ArgumentException("Unsupported performance profile or camera path.");
        if (Commit is null || Commit.Length != 40 || Commit.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Capture requires a complete source commit.");
        if (MechanicalSeconds is double seconds && (!double.IsFinite(seconds) || seconds is < 0 or > RuntimeAnimationData.MaximumDuration ||
            Scenes is null || Scenes.Any(s => s is not (VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation or VisualReferenceSceneKind.MainBattleTankSingle or VisualReferenceSceneKind.MainBattleTankHundred or VisualReferenceSceneKind.MainBattleTankThousand or VisualReferenceSceneKind.InfantrySkinning or VisualReferenceSceneKind.InfantrySkinningRepeated or VisualReferenceSceneKind.InfantrySkinningEngineer) &&
                s is not (>= VisualReferenceSceneKind.InfantryRifleSingle and <= VisualReferenceSceneKind.InfantryEngineerThousand))))
            throw new ArgumentOutOfRangeException(nameof(MechanicalSeconds));
        if(OwnerObservationVersion is not (0 or 1) || OwnerCycleCapacity is < 1 or > 65536)throw new ArgumentOutOfRangeException(nameof(OwnerObservationVersion));
        if (Warmups is < 3 or > 4096 || Samples is < 8 or > 8192) throw new ArgumentOutOfRangeException(nameof(Samples));
        if (RepeatCaptures is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(RepeatCaptures));
        if (RasterDpi is not (0 or 96 or 120 or 144 or 192)) throw new ArgumentOutOfRangeException(nameof(RasterDpi));
        if (!float.IsFinite(InspectionDistanceMeters) || InspectionDistanceMeters != 0 && InspectionDistanceMeters is < 20 or > 120)
            throw new ArgumentOutOfRangeException(nameof(InspectionDistanceMeters));
        if (!float.IsFinite(YawDegrees) || YawDegrees is < 0 or > 360) throw new ArgumentOutOfRangeException(nameof(YawDegrees));
        if (Scenes is null || Profiles is null || Displays is null || Zooms is null ||
            Scenes.Length is < 1 or > 14 || Profiles.Length is < 1 or > 4 || Displays.Length is < 1 or > 5 || Zooms.Length is < 1 or > 3 ||
            Scenes.Any(x => !Enum.IsDefined(x)) || Profiles.Any(x => !Enum.IsDefined(x)) || Zooms.Any(x => !Enum.IsDefined(x)) ||
            Displays.Any(x => !RtsVisualReference.Displays.Contains(x) && !(ui && x == new RtsReferenceDisplay(1280, 720)) && !(Software && x == new RtsReferenceDisplay(640, 360))) ||
            Scenes.Distinct().Count() != Scenes.Length || Profiles.Distinct().Count() != Profiles.Length ||
            Zooms.Distinct().Count() != Zooms.Length || Displays.Distinct().Count() != Displays.Length)
            throw new ArgumentException("Capture matrix contains an unsupported or duplicated entry.");
        if(!string.IsNullOrWhiteSpace(PostWindowControlDirectory) && (OwnerObservationVersion!=1 || Scenes.Length!=1 || Profiles.Length!=1 || Displays.Length!=1 || Zooms.Length!=1 || UiScenarios.Length!=1 || UiScales.Length!=1))throw new ArgumentException("Ordinary post-window control v1 requires one immutable case per actual command.");
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
        GraphicsBoundInputProvenanceScope? provenanceScope = null;
        bool outputCreated = false;
        Exception? selectedSnapshotPrimary = null;
        void RememberSelectedFailure(Exception error)
        {
            if (request?.CapturePostFrameResourceSnapshotVersion == 1)
                selectedSnapshotPrimary = ReferencePostFrameSnapshot.Remember(selectedSnapshotPrimary, error);
        }
        var captures = new List<object>();
        var captureInputs = new List<object>();
        var failedCaptures = new List<string>();
        var uiPixelHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        object? captureState = null;
        object? timedWindowState = null;
        long startupStart = Stopwatch.GetTimestamp();
        long startupAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            request = JsonSerializer.Deserialize<VisualReferenceCaptureRequest>(File.ReadAllText(requestPath), JsonOptions)
                ?? throw new ArgumentException("Empty capture request.");
            request.Validate(allowBoundInputProvenance: true, allowPostFrameSnapshot: true);
            if (request.OwnerTraceControlDirectory is not null) throw new NotSupportedException("Owner trace supervision requires the actual render-host producer.");
            if (request.CollectorMode != ReferenceCollectorMode.Full) throw new NotSupportedException("Collector controls are separate actual-host experiments, not native reference modes.");
            string identity = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            if (!identity.EndsWith(request.Commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Requested commit does not match the built client. Rebuild this revision.");
            if (Directory.Exists(request.OutputRoot) && Directory.EnumerateFileSystemEntries(request.OutputRoot).Any())
                throw new IOException("Evidence output must be empty; existing baselines are never overwritten.");
            Directory.CreateDirectory(request.OutputRoot);
            outputCreated = true;
            string runtimeFingerprint = RuntimeFingerprint(request.RuntimeRoot);
            var catalog = ReferenceRuntimeSeal.Load(request.RuntimeRoot, out object runtimeSeal);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixtures = request.Scenes.Select(x => VisualReferenceScene.Create(x, terrain, runtimeAssets: catalog, mechanicalSeconds: request.MechanicalSeconds)).ToArray();
            provenanceScope = request.CaptureBoundInputProvenanceVersion == 1 ? new GraphicsBoundInputProvenanceScope() : null;
            using var capturePower = new ReferenceCapturePowerLease();
            try
            {
            using var platform = new WindowsPlatform();
            try
            {
            using IWindow window = platform.CreateWindow(new("FORGELINE Reference Capture", 1280, 720));
            try
            {
            using var assetPreparation = new AssetPreparationScope(() => !platform.PumpEvents());
            try
            {
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
            { EnableVSync = false, EnableMemoryDiagnostics = true, ForceSoftwareAdapter = request.Software,
                AllowSoftwareAdapterFallback = request.Software });
            try
            {
            if (graphics.Diagnostics.Device.IsSoftwareAdapter != request.Software)
                throw new InvalidOperationException("Capture adapter does not match the requested lane.");
            var adapterCheckpoint = ReferenceAdapterCheckpoint.PublishAndWait(request.AdapterBindingDirectory, graphics, runtimeFingerprint, window.NativeHandle.Value.ToInt64(), () => platform.PumpEvents());
            var postWindowControl=ReferencePostWindowControl.Create(request.PostWindowControlDirectory,request.AdapterBindingDirectory);
            if(request.OwnerObservationVersion==1)platform.EnableWorkloadObservations(window);
            double startupMilliseconds = Stopwatch.GetElapsedTime(startupStart).TotalMilliseconds;
            long startupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startupAllocationStart;
            var lighting = SceneLightingSettings.Default;
            foreach (var display in request.Displays)
            foreach (var quality in request.Profiles)
            {
                ClientRenderConfiguration.Reset(graphics);
                graphics.ConfigureSceneOutput(new(false, lighting.Exposure, true));
                graphics.Resize(display.Width, display.Height);
                graphics.RenderFrame(GraphicsColor.ForgeLineClear);
                var settings = (request.GraphicsSettings ?? new RenderQualitySettings()) with { Quality = quality };
                var composition = ClientRenderConfiguration.Resolve(graphics, settings, display.Width, display.Height,
                    SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default with { LocalHaze = request.LocalHaze });
                composition.Apply(graphics);
                var profile = composition.Profile;
                lighting = composition.Lighting;
                foreach (var fixture in fixtures)
                foreach (var zoom in request.Zooms)
                foreach (var uiScenario in request.UiScenarios)
                foreach (float uiScale in request.UiScales)
                {
                    graphics.WaitForIdle();
                    timedWindowState = null;
                    long setupStart = Stopwatch.GetTimestamp();
                    long setupAllocationStart = GC.GetAllocatedBytesForCurrentThread();
                    var beforeSetup = graphics.Diagnostics;
                    uint resolvedRasterDpi = request.RasterDpi != 0 ? request.RasterDpi : display.Height == 2160 ? 192u : display.Height == 1440 ? 144u : 96u;
                    ReferenceFixtureRecipe? sharedFixture = uiScenario == UiReferenceScenario.World
                        ? new(fixture, terrain, catalog, request, display, quality, zoom, resolvedRasterDpi, uiScale, runtimeFingerprint) : null;
                    var terrainProfile = sharedFixture?.TerrainProfile ?? fixture.ResolveTerrainProfile(TerrainPresentationProfile.CreateCentralDivide(catalog));
                    if (sharedFixture is null && request.SurfaceMarks.Length > 0) terrainProfile = terrainProfile.WithSurfaceMarks(new TerrainSurfaceMarks(request.SurfaceMarks));
                    using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog, lighting: lighting,
                        surfaceDetail: profile.TerrainDetail, textureDetail: profile.Textures, residency: profile.TerrainResidency,
                        environment: profile.Environment, presentationProfile: terrainProfile) { DebugChunksEnabled = false };
                    try
                    {
                    using var meshes = new SimpleInstanceRenderer(graphics, catalog, lighting: lighting,
                        lodDiameterScale: profile.LodDiameterScale, textureDetail: profile.Textures, vegetationMotion: profile.VegetationMotion,
                        environment: profile.Environment, vegetation: profile.Vegetation);
                    try
                    {
                    if (sharedFixture is not null) meshes.EnableContentEvidence();
                    using var particles = new ParticleVfxRenderer(graphics, catalog, profile.Particles, lighting);
                    try
                    {
                    using var hud = new GameplayHudRenderer(graphics, catalog);
                    try
                    {
                    using var frontend = UiReferenceStates.IsFrontend(uiScenario) ? new FrontendOverlayRenderer(graphics) : null;
                    try
                    {
                    if (frontend is not null)
                    {
                        frontend.PrepareSkin(catalog);
                        if (!frontend.SkinReady) throw new InvalidDataException("Required production frontend skin is unavailable: " + frontend.SkinFailure);
                    }
                    if (frontend is not null && request.CinematicFrontend)
                    {
                        var deadline = Stopwatch.StartNew();
                        do { frontend.PrepareBackground(catalog, quality); Thread.Sleep(1); }
                        while (frontend.BackgroundDiagnostics.State == "Loading" && deadline.Elapsed < TimeSpan.FromSeconds(15));
                        if (frontend.BackgroundDiagnostics.State != "Ready") throw new InvalidDataException("Requested cinematic background is unavailable.");
                    }
                    using var selection = new DebugDrawRenderer(graphics, depthEnabled: false, lineWidthPixels: 3);
                    try
                    {
                    using var materialReference = fixture.Kind == VisualReferenceSceneKind.MaterialFoundation ? new MaterialReferenceRenderer(graphics, lighting) : null;
                    try
                    {
                    if (!platform.PumpEvents()) throw new OperationCanceledException("Reference window was closed.");
                    string key = $"{fixture.Kind}-{display.Width}x{display.Height}-{quality}-{zoom}";
                    if (request.RasterDpi != 0) key += $"-Dpi{request.RasterDpi}";
                    if (request.YawDegrees != 45) key += $"-Yaw{request.YawDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    if (request.MechanicalSeconds is double poseTime) key += $"-MechanicalT{poseTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    if (request.InspectionDistanceMeters != 0) key += $"-Inspection{request.InspectionDistanceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    if (request.RunProfile != ReferenceRunProfile.Warm) key += $"-{request.RunProfile}";
                    if (request.CameraPath != ReferenceCameraPath.Fixed) key += $"-{request.CameraPath}";
                    if (uiScenario != UiReferenceScenario.World || uiScale != 1)
                        key += FormattableString.Invariant($"-Ui{uiScenario}-Scale{uiScale:0.00}");
                    if (sharedFixture is not null && key != sharedFixture.CaseKey) throw new InvalidDataException("Shared world recipe key differs from native case inputs.");
                    Console.WriteLine($"[reference:capture] {key}");
                    var effectiveZoom = uiScenario == UiReferenceScenario.MinimapStrategic ? RtsReferenceZoom.Strategic : zoom;
                    var camera = sharedFixture is not null ? new RtsCamera(sharedFixture.CameraSettings) : new RtsCamera(RtsVisualReference.CreateCameraSettings(fixture.Target, effectiveZoom) with
                        { InitialYawRadians = request.YawDegrees == 45 ? RtsVisualReference.YawRadians : request.YawDegrees * MathF.PI / 180,
                          InitialDistance = request.InspectionDistanceMeters == 0 ? RtsVisualReference.GetDistance(effectiveZoom) : request.InspectionDistanceMeters });
                    double captureVisualSeconds = sharedFixture?.VisualSeconds ?? fixture.Snapshot.Tick.Value * fixture.Snapshot.TickDuration.TotalSeconds;
                    uint rasterDpi = resolvedRasterDpi;
                    var (snapshot, panel) = sharedFixture is not null ? (sharedFixture.Snapshot, sharedFixture.Panel) : UiReferenceStates.Hud(fixture.Snapshot, uiScenario, display.Width, display.Height, rasterDpi, uiScale);
                    FrontendSurfaceView? frontendView = frontend is null ? null : UiReferenceStates.Frontend(uiScenario, display.Width, display.Height, rasterDpi, uiScale);
                    var world = sharedFixture?.CreateWorld() ?? ReferenceSnapshotHistory.Create(snapshot, fixture.PreviousSnapshot);
                    string fixtureFingerprint = ReferenceSnapshotHistory.Fingerprint(fixture.Fingerprint, fixture.PreviousSnapshot);
                    if (ObjectActionRingCapture.IsRing(uiScenario))
                        panel=ObjectActionRingCapture.View(snapshot,uiScenario,world,camera,GameplayHudLayout.Create(display.Width,display.Height,rasterDpi,uiScale),rasterDpi,uiScale);
                    else if (panel.IsOpen)
                        panel=ObjectActionRingCapture.Present(snapshot,panel,world,camera,
                            GameplayHudLayout.Create(display.Width,display.Height,rasterDpi,uiScale),rasterDpi,uiScale);
                    particles.Prepare(world, camera, display.Width, display.Height, 0);
                    particles.Prepare(world, camera, display.Width, display.Height, .1);
                    particles.Prepare(world, camera, display.Width, display.Height, .1);
                    var selected = panel.Ring.Group is not null ? snapshot.PlayerActions!.Tactical!.SelectedEntities.ToArray() : fixture.Snapshot.Instances.ToArray().Where(x => x.Selectable.IsSelectable).Take(3).Select(x => x.Entity).ToArray();
                    var indicators = sharedFixture?.Indicators ?? new DebugDraw { Enabled = true };
                    if (sharedFixture is null) foreach (var instance in fixture.Snapshot.Instances.ToArray().Where(x => selected.Contains(x.Entity)))
                    {
                        if (fixture.Kind is VisualReferenceSceneKind.CommandCoreProduction or VisualReferenceSceneKind.InfantryRifleProduction or VisualReferenceSceneKind.InfantryEngineerProduction or VisualReferenceSceneKind.InfantryRepeated or VisualReferenceSceneKind.InfantryBattleProduction)
                            RtsWorldMarkerVisualization.DrawSelected(indicators, instance, new(0, .8f, .2f, 1), terrain);
                        else
                            indicators.Circle(instance.Transform.Position + Vector3.UnitY, 5, new(0, .8f, .2f, 1));
                    }
                    var information = sharedFixture?.Information ?? RtsInformationLayerView.Empty with { SelectedEntities = selected,
                        Cursor = uiScenario == UiReferenceScenario.Targeting ? RtsCursorKind.Attack : RtsCursorKind.Default,
                        HasPointer = uiScenario == UiReferenceScenario.Targeting, PointerPosition = new(display.Width * .5f, display.Height * .5f) };
                    var targeting = uiScenario == UiReferenceScenario.Targeting ? new TacticalTargetingView(TacticalTargetingMode.Attack, 2, 1, 1, 0)
                    { HasPointerTarget = true, PointerTargetValid = true, PointerWorldTarget = fixture.Target } : default;
                    var captureUx = UiReferenceStates.IsOnboarding(uiScenario) || uiScenario is >= UiReferenceScenario.GroupRing ? default(PreAlphaUxView) with
                    {
                        ShowOnboarding = true,
                        Guidance = new EarlyGameGuidanceController().Update(snapshot, true, layout: GameplayHudLayout.Create(display.Width, display.Height, rasterDpi, uiScale)),
                        Onboarding = OnboardingPresentation.Resolve(GameplayHudLayout.Create(display.Width, display.Height, rasterDpi, uiScale),
                            UiReferenceStates.OnboardingCollapse(uiScenario), detailed: true)
                    } : default;
                    var hover = uiScenario == UiReferenceScenario.GameplayTooltip ? new HoverTooltipView(snapshot.SessionId, default, default,
                        new(display.Width * .65f, display.Height * .6f), true, display.Width, display.Height,
                        GameplayHudLayout.Create(display.Width, display.Height, rasterDpi, uiScale).Scale,
                        panel.Mode, DockControl: 2, DockIndex: 0) : default;
                    if (uiScenario is UiReferenceScenario.DockQueueTooltip or UiReferenceScenario.DockTechnologyTooltip or UiReferenceScenario.LogisticsPolicyTooltip)
                        hover = new(snapshot.SessionId,default,default,new(display.Width * .65f,display.Height * .45f),true,display.Width,display.Height,
                            GameplayHudLayout.Create(display.Width,display.Height,rasterDpi,uiScale).Scale,
                            DockMode:panel.Mode,DockControl:2,DockIndex:panel.SelectedIndex);
                    if (uiScenario==UiReferenceScenario.RingTooltip && panel.Ring.Geometry.IsValid)
                        hover=new(snapshot.SessionId,default,default,panel.Ring.Geometry.LabelCenter(0),true,display.Width,display.Height,
                            GameplayHudLayout.Create(display.Width,display.Height,rasterDpi,uiScale).Scale,panel.Mode,DockControl:2,DockIndex:0);
                    var operations = uiScenario == UiReferenceScenario.NotificationDetails
                        ? new OperationsView(true, Session: snapshot.SessionId, Notifications: true) : uiScenario is UiReferenceScenario.Operations or UiReferenceScenario.OperationsEmpty or UiReferenceScenario.OperationsMissing
                        ? new OperationsView(true,Session:snapshot.SessionId,Selected:snapshot.Operations is { Facilities.Count: > 0 } ops ? ops.Facilities[0].Entity : default) : default;
                    if (UiReferenceStates.IsRules(uiScenario)) operations = UiReferenceStates.RuleView(snapshot, uiScenario);
                    var groups = uiScenario == UiReferenceScenario.GroupSummary ? new CombatGroupOverviewView(
                        [default(CombatGroupSummaryReadModel) with { Slot = 1, Label = "Armoured support", MemberCount = 3,
                            IsActive = true, IsSelected = true, HasHealth = true, Health = .4, HasSupply = true,
                            SupplyStatus = BattlefieldSupplyStatus.Critical, Fuel = .12, Ammunition = .08,
                            HasReadiness = true, Readiness = .31, HasFormation = true, Formation = FormationTemplate.Wedge }], []) : CombatGroupOverviewView.Empty;
                    if ((uiScenario == UiReferenceScenario.SelectedGroup || panel.Ring.Group is not null) && SelectedCombatGroup.Resolve(snapshot) is { } groupMembers)
                    {
                        var selectedUnits = new SelectionSet(); selectedUnits.Replace(groupMembers.EligibleEntities.ToArray());
                        groups = new CombatGroupOverviewView([], [], SelectedCombatGroup.Create(groupMembers, selectedUnits));
                    }
                    int recordFrameIndex = -1;
                    captureInputs.Clear();
                    Action<IGraphicsCommandContext> record = context =>
                    {
                        recordFrameIndex = context.FrameIndex;
                        if (frontend is not null && frontendView is { Kind: not FrontendSurfaceKind.PauseMenu, PauseContext: false } view)
                        {
                            context.BeginPass(GraphicsFramePass.Overlay); frontend.Render(context, view); return;
                        }
                        if (materialReference is not null)
                        {
                            context.BeginPass(GraphicsFramePass.World);
                            materialReference.Render(context, camera);
                            return;
                        }
                        var worldPasses = new ClientWorldPassRecorder(ground, meshes, particles, world, camera, 1,
                            captureVisualSeconds, 0, true);
                        ClientWorldPassSequence.Record(context,
                            composition.Atmosphere.Enabled ? SceneAtmosphereSettings.CreateView(camera, context.Width, context.Height) : null,
                            profile.Shadows.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(), lighting, profile.Shadows) : null,
                            profile.Shadows, ref worldPasses);
                        selection.Render(context, camera, indicators);
                        if (panel.Ring.UsesRing)
                        {
                            var ringLayout=GameplayHudLayout.Create(context.Width,context.Height,rasterDpi,uiScale);
                            Span<HudRect> ringOccupied=stackalloc HudRect[8];
                            RadialActionLayout.ResolveObstacles(ringOccupied,ringLayout,captureUx.Onboarding,operations,groups,ActionableAlertLayout.Resolve(snapshot,ringLayout));
                            panel=panel with { Ring=panel.Ring.Project(world,camera,1,snapshot,ringLayout,ringOccupied) };
                        }
                        if (panel.Ring is { Open:true,Resolved:true,Geometry.IsValid:true } ring &&
                            (hover.DockControl==0 || hover.SelectionStatus))
                            hover=new(snapshot.SessionId,default,default,ring.Geometry.Center,true,context.Width,context.Height,
                                GameplayHudLayout.Create(context.Width,context.Height,rasterDpi,uiScale).Scale,
                                SelectionStatus:true,SelectionRevision:ring.SelectionRevision,MenuRevision:ring.Revision);
                        hud.Render(context, camera, snapshot, terrain.WorldBounds, information, panel, targeting,
                            FormationTemplate.Compact, groups, captureUx, rasterDpi, uiScale,
                            hoverTooltip: hover,
                            runtimeMetrics: new RuntimeMetricsView(60, 20, RuntimeSimulationState.Running),
                            showRuntimeMetrics: false, operations: operations);
                        if(hover.SelectionStatus && targeting.Mode==TacticalTargetingMode.None && !information.IsDragSelecting &&
                            hud.LastHoverTooltipVertexCount==0)
                            throw new InvalidDataException("Visible ring status has no nonoverlapping tooltip placement.");
                        if (frontend is not null && frontendView is { } pause)
                            frontend.Render(context, pause);
                    };
                    void PrepareTerrain()
                    {
                        if (frontend is not null && frontendView?.Kind != FrontendSurfaceKind.PauseMenu && frontendView?.PauseContext != true) return;
                        ground.Prepare(camera,
                            Math.Max(1, (int)Math.Ceiling(display.Width * (double)profile.ResolutionScale)),
                            Math.Max(1, (int)Math.Ceiling(display.Height * (double)profile.ResolutionScale)),
                            profile.Shadows.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(), lighting, profile.Shadows) : null);
                        if (materialReference is null && !meshes.AssetsPrepared) meshes.Prepare(camera, world, new(
                            Math.Max(1, (int)Math.Ceiling(display.Width * (double)profile.ResolutionScale)),
                            Math.Max(1, (int)Math.Ceiling(display.Height * (double)profile.ResolutionScale)), 1,
                            profile.Shadows.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(), lighting, profile.Shadows) : null,
                            profile.Shadows.MaximumCasters, captureVisualSeconds, true), particles);
                    }
                    void RecordFrame()
                    {
                        if (!meshes.AssetsPrepared) PrepareTerrain();
                        ulong beforePresent = graphics.PresentedFrameCount;
                        var beforeSubmission = (graphics as IGraphicsRenderTelemetry)?.LastSubmission;
                        graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                        meshes.Submit(); meshes.Retire();
                        ReferenceFixturePresentation.RequireSuccessful(beforePresent, graphics.PresentedFrameCount, beforeSubmission, (graphics as IGraphicsRenderTelemetry)?.LastSubmission);
                    }
                    ReferenceWorldInputDiagnostics? worldInputInspector=request.CaptureStageDiagnostics && sharedFixture is not null
                        ? ReferenceWorldInputDiagnostics.Create(request.OutputRoot,key,"native-reference",runtimeFingerprint,sharedFixture.Fingerprint,
                            sharedFixture.InputBytesBase64,profile,request.RepeatCaptures,ground,meshes,particles,graphics,camera) : null;
                    GraphicsStageCapture? stageCapture = null;
                    object? postFrameReference = null;
                    byte[]? snapshotFirstPixels = null;
                    bool snapshotFirstDuplicateWritten = false;
                    byte[] CaptureFrame(int repetition = -1)
                    {
                        stageCapture=null;
                        postFrameReference = null;
                        GraphicsPostFrameReceipt? postReceipt = null;
                        ReferencePostFrameSnapshot.Metadata? postMetadata = null;
                        bool assetsCleaned = false;
                        string postPhase = "capture-setup";
                        string? postImage = null;
                        GraphicsPostFrameSnapshotResult? postSnapshot = null;
                        GraphicsWorldBindingAttempt? worldAttempt = null;
                        try
                        {
                        worldInputInspector?.BeforeSite(WorldInputSite.CaptureEnsurePrepared);
                        bool invokePreparation=!meshes.AssetsPrepared;
                        if (invokePreparation) PrepareTerrain();
                        worldInputInspector?.AfterSite(WorldInputSite.CaptureEnsurePrepared,invokePreparation);
                        ulong beforePresent = graphics.PresentedFrameCount;
                        var beforeSubmission = (graphics as IGraphicsRenderTelemetry)?.LastSubmission;
                        byte[] pixels;
                        if (request.CaptureStageDiagnostics || request.CaptureBoundInputProvenanceVersion == 1)
                        {
                            using var attemptScope = worldAttempt = GraphicsWorldBindingJournal.BeginAttempt();
                            stageCapture = graphics.CaptureFrameDiagnostics(GraphicsColor.ForgeLineClear, record,
                                request.CaptureBoundInputProvenanceVersion == 1 ? GraphicsCaptureStages.FinalColor : GraphicsCaptureStages.All);
                            pixels = stageCapture.FinalRgba;
                        }
                        else pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record, captureShadowDepth: request.CaptureShadowDiagnostics);
                        if (request.CapturePostFrameResourceSnapshotVersion == 1)
                        {
                            if (repetition is < 0 or > 7) throw new InvalidDataException("Selected late capture repetition unavailable.");
                            postPhase = "png-publication";
                            string imageFile = key + ".png";
                            postImage = imageFile;
                            if (repetition == 0)
                            {
                                ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, imageFile), display.Width, display.Height, pixels);
                                snapshotFirstPixels = pixels;
                            }
                            else if (!(snapshotFirstPixels ?? throw new InvalidDataException("First selected image unavailable.")).AsSpan().SequenceEqual(pixels))
                            {
                                if (!snapshotFirstDuplicateWritten)
                                {
                                    ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, $"failed-{key}-first.png"), display.Width, display.Height, snapshotFirstPixels);
                                    snapshotFirstDuplicateWritten = true;
                                }
                                imageFile = $"failed-{key}-repeat-{repetition:D2}.png";
                                postImage = imageFile;
                                ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, imageFile), display.Width, display.Height, pixels);
                            }
                            // All new enumeration/native-copy work follows closed PNG publication.
                            postPhase = "receipt-issue";
                            postReceipt = GraphicsPostFrameSnapshots.IssueReceipt(graphics, recordFrameIndex);
                            postPhase = "late-intent";
                            var roster = new GraphicsPostFrameRoster();
                            var intent = new GraphicsPostFrameIntent(ground.CapturePostFrameIntent(roster),
                                meshes.CapturePostFrameMainIntent(roster, recordFrameIndex), meshes.CapturePostFrameDecorationIntent(roster, recordFrameIndex));
                            postMetadata = ReferencePostFrameSnapshot.Context(runtimeFingerprint, sharedFixture?.Fingerprint ?? throw new InvalidDataException("Authored recipe unavailable."),
                                sharedFixture.InputBytesBase64, repetition, camera.CaptureState(), profile, new ClientRenderConfiguration(profile, lighting, graphics.Atmosphere),
                                pixels, graphics.CaptureInputFingerprint, request.OutputRoot, imageFile);
                            postPhase = "native-copy";
                            postSnapshot = GraphicsPostFrameSnapshots.Capture(graphics, postReceipt, roster, intent);
                            postPhase = "asset-cleanup";
                            assetsCleaned = true;
                            ReferencePostFrameSnapshot.CleanupAssets(meshes.Submit, meshes.Retire);
                            postPhase = "export";
                            postFrameReference = ReferencePostFrameSnapshot.Write(postSnapshot, postMetadata, request.OutputRoot, key, repetition);
                        }
                        else { meshes.Submit(); meshes.Retire(); assetsCleaned = true; }
                        ReferenceFixturePresentation.RequireSuccessful(beforePresent, graphics.PresentedFrameCount, beforeSubmission, (graphics as IGraphicsRenderTelemetry)?.LastSubmission);
                        worldInputInspector?.AfterCapture(stageCapture);
                        return pixels;
                        }
                        catch(Exception error)
                        {
                            if (request.CapturePostFrameResourceSnapshotVersion == 1)
                            {
                                RememberSelectedFailure(error);
                                if (!assetsCleaned)
                                {
                                    try { meshes.Submit(); } catch (Exception cleanup) { try { error.Data["postFrameSubmitCleanup"] = cleanup.ToString(); } catch { } }
                                    try { meshes.Retire(); } catch (Exception cleanup) { try { error.Data["postFrameRetireCleanup"] = cleanup.ToString(); } catch { } }
                                }
                                ReferencePostFrameSnapshot.RetainFailure(error, request.OutputRoot, key + "-readback-" + repetition.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), postReceipt, postMetadata, postPhase, postImage, postSnapshot);
                            }
                            else if (request.CaptureBoundInputProvenanceVersion == 1)
                                ReferenceBoundInputProvenance.RetainCaptureFailure(error, request.OutputRoot, key,
                                    () => new { runtimeFingerprint, recipeFingerprint = sharedFixture?.Fingerprint, captureState }, stageCapture, worldAttempt);
                            else ReferenceStageDiagnostics.RetainWorldBindingFailure(error,request.OutputRoot,key,()=>new {runtimeFingerprint,recipeFingerprint=sharedFixture?.Fingerprint,recipeInputBytesBase64=sharedFixture?.InputBytesBase64,profile,captureState},stageCapture,worldAttempt);
                            throw;
                        }
                    }
                    object CaptureInputRecord(int repetition, byte[] pixels)
                    {
                        ReadOnlyMemory<byte> depth = graphics.CaptureShadowDepth;
                        string? file = null;
                        if (request.CaptureShadowDiagnostics && !depth.IsEmpty)
                        {
                            file = $"{key}-readback-{repetition:D2}.depth";
                            using var stream = new FileStream(Path.Combine(request.OutputRoot, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                            stream.Write(depth.Span);
                        }
                        if (request.CapturePostFrameResourceSnapshotVersion == 1)
                            return new { repetition, worldInputDiagnostics = (object?)null, frameIndex = recordFrameIndex,
                                fingerprint = graphics.CaptureInputFingerprint, actualSubmission = (graphics as IGraphicsRenderTelemetry)?.LastSubmission,
                                pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)), shadowFingerprint = (string?)null, shadowFile = (string?)null,
                                shadowLayout = (GraphicsTextureReadbackLayout?)null, stageDiagnostics = (object?)null,
                                postFrameResourceSnapshot = postFrameReference ?? throw new InvalidDataException("Current late snapshot reference unavailable.") };
                        if (request.CaptureBoundInputProvenanceVersion == 1)
                        {
                            var bound = ReferenceBoundInputProvenance.Write(stageCapture ?? throw new InvalidDataException("Current FinalColor capture unavailable."),
                                request.OutputRoot, key, repetition, runtimeFingerprint, sharedFixture?.Fingerprint ?? throw new InvalidDataException("Authored recipe unavailable."),
                                sharedFixture.InputBytesBase64, camera.CaptureState(), profile, graphics.CaptureInputFingerprint);
                            return new { repetition, worldInputDiagnostics = (object?)null, frameIndex = recordFrameIndex,
                                fingerprint = graphics.CaptureInputFingerprint, actualSubmission = (graphics as IGraphicsRenderTelemetry)?.LastSubmission,
                                pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)), shadowFingerprint = (string?)null, shadowFile = (string?)null,
                                shadowLayout = (GraphicsTextureReadbackLayout?)null, stageDiagnostics = (object?)null, boundInputProvenance = bound };
                        }
                        object? stages=stageCapture is null ? null : ReferenceStageDiagnostics.Write(stageCapture, request.OutputRoot, key,
                            repetition, "native-reference", runtimeFingerprint, sharedFixture?.Fingerprint ?? throw new InvalidDataException("Stage diagnostics require an authored World recipe."),
                            sharedFixture.InputBytesBase64, camera.CaptureState(), profile, graphics.CaptureInputFingerprint);
                        object? worldInputs=worldInputInspector?.Persist(stages);
                        return new { repetition, worldInputDiagnostics=worldInputs, frameIndex = recordFrameIndex, fingerprint = graphics.CaptureInputFingerprint,
                            actualSubmission = (graphics as IGraphicsRenderTelemetry)?.LastSubmission,
                            pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                            shadowFingerprint = depth.IsEmpty ? null : Convert.ToHexString(SHA256.HashData(depth.Span)),
                            shadowFile = file, shadowLayout = (graphics as IGraphicsRenderTelemetry)?.CaptureShadowLayout,
                            stageDiagnostics = stages };
                    }
                    PrepareTerrain();
                    if (request.CaptureBoundInputProvenanceVersion == 1)
                        ReferenceBoundInputProvenance.Preflight(request.OutputRoot, key, display.Width, display.Height, request.RepeatCaptures);
                    if (request.CaptureStageDiagnostics)
                    {
                        if (sharedFixture is null) throw new NotSupportedException("Stage diagnostics require the shared authored World recipe.");
                        ReferenceStageDiagnostics.Preflight(graphics, request.OutputRoot, key, request.RepeatCaptures);
                    }
                    double setupMilliseconds = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds;
                    long setupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - setupAllocationStart;
                    int warmupFrames = request.RunProfile == ReferenceRunProfile.RendererCold ? 0 : request.Warmups;
                    for (int i = 0; i < warmupFrames; i++)
                    {
                        if (!platform.PumpEvents()) throw new OperationCanceledException("Reference window was closed.");
                        RecordFrame();
                    }
                    graphics.WaitForIdle();
                    var beforeSamples = graphics.Diagnostics;
                    if (warmupFrames > 0) meshes.ObserveFrameDiagnostics(beforeSamples);
                    (graphics as IGraphicsRenderTelemetry)?.EnableRenderTelemetry(Math.Min(65536, request.Samples + 8));
                    var performance = new ReferencePerformanceSamples(request.Samples, beforeSamples.Frame?.GpuSubmissionFence ?? 0);
                    var initialCamera = camera.CaptureState();
                    var cpu = new double[request.Samples]; var gpu = new List<double>(); ulong lastFence = 0;long lastMeasuredEndTick=0;
                    lastFence = beforeSamples.Frame?.GpuSubmissionFence ?? 0;
                    for (int i = 0; i < cpu.Length; i++)
                    {
                        if (!platform.PumpEvents()) throw new OperationCanceledException("Reference window was closed.");
                        camera.ApplyState(sharedFixture?.SampleCameras[i] ?? ReferencePerformanceSamples.CameraPose(initialCamera, request.CameraPath, i, request.Samples));
                        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                        long start = Stopwatch.GetTimestamp(); PrepareTerrain(); RecordFrame();
                        long cpuEndedAt = Stopwatch.GetTimestamp();lastMeasuredEndTick=cpuEndedAt;
                        cpu[i] = Stopwatch.GetElapsedTime(start, cpuEndedAt).TotalMilliseconds;
                        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                        var sampleDiagnostics = graphics.Diagnostics;
                        meshes.ObserveFrameDiagnostics(sampleDiagnostics);
                        performance.DrainRawGpu(graphics);
                        performance.AddCpu(i, cpu[i], allocatedBytes, sampleDiagnostics,
                            new { content = sharedFixture is not null ? meshes.CaptureContentEvidence() : (RenderContentEvidence?)null, objects = meshes.SubmissionMetrics, assetPreparation = meshes.AssetFrameMetrics, decals = fixture.Snapshot.DecalMetrics, terrain = ground.LastDiagnostics, instances = meshes.LastDiagnostics, particles = particles.Diagnostics }, resourceMemory: (graphics as IGraphicsRenderTelemetry)?.ResourceMemory, actualSubmission: (graphics as IGraphicsRenderTelemetry)?.CpuIntervals, measuredInterval: new GraphicsTimestampInterval(start, cpuEndedAt), actualCamera: camera.CaptureState());
                        if (sampleDiagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                        {
                            lastFence = frame.GpuSubmissionFence;
                            if (sampleDiagnostics.GpuFrameMilliseconds is { } milliseconds) gpu.Add(milliseconds);
                        }
                    }
                    (graphics as IGraphicsRenderTelemetry)?.StopRenderTelemetry();
                    if(postWindowControl is not null)
                    {
                        byte[] profileBytes=ReferenceFixtureRecipe.SerializeIdentityInputs(new ClientRenderConfiguration(profile,lighting,graphics.Atmosphere));
                        string actualProfile=Convert.ToHexString(SHA256.HashData(profileBytes));
                        postWindowControl.PublishReady(new("native-reference",key,sharedFixture is not null,sharedFixture?.Fingerprint,actualProfile,ProfileInputBytesBase64:Convert.ToBase64String(profileBytes)),request.Samples,lastMeasuredEndTick,null,((IGraphicsRenderTelemetry)graphics).LastSubmission ?? throw new InvalidDataException("Actual final native submission missing."));
                        postWindowControl.WaitForAck(platform.PumpEvents);
                    }
                    graphics.WaitForIdle();
                    var measured = graphics.Diagnostics;
                    performance.AddGpu(measured);
                    performance.DrainRawGpu(graphics);
                    if (measured.Frame is { } finalFrame && finalFrame.GpuSubmissionFence > lastFence && measured.GpuFrameMilliseconds is { } finalGpu)
                        gpu.Add(finalGpu);
                    timedWindowState = new { sourceSha = ClientBuildIdentity.SourceRevision, runtimeFingerprint, key,
                        recipeFingerprint = sharedFixture?.Fingerprint, recipeInputs = sharedFixture?.IdentityInputs,
                        recipeInputBytesBase64 = sharedFixture?.InputBytesBase64, camera = initialCamera, profile, lighting,
                        observedComposition = new ClientRenderConfiguration(profile,lighting,graphics.Atmosphere),
                        performance = performance.Report(measured) };
                    byte[] rgba;bool repeatPixelsEqual;List<object> repeatFailures;ReadOnlyMemory<byte> shadowDepth;
                    using(worldInputInspector?.Arm(0))
                    {
                    camera.ApplyState(initialCamera);
                    worldInputInspector?.BeforeSite(WorldInputSite.NativeFixedPoseRestore);
                    PrepareTerrain();
                    worldInputInspector?.AfterSite(WorldInputSite.NativeFixedPoseRestore,true);
                    // Restore fixed pose and settle history only after the timed window.
                    if (request.CameraPath != ReferenceCameraPath.Fixed)
                        for (int i = 0; i < 3; i++) RecordFrame();
                    if (measured.Surface.Width != display.Width || measured.Surface.Height != display.Height)
                        throw new InvalidOperationException("Actual native surface dimensions differ from the capture request.");
                    if (measured.Resources.TextureBindingFailureCount != 0 || meshes.MaterialDiagnostics.BindingFailureCount != 0 ||
                        meshes.LastDiagnostics.ResourceDeposits.MissingResources != 0 ||
                        meshes.LastDiagnostics.ResourceDeposits.CanonicalFallbacks != 0 ||
                        meshes.LastDiagnostics.ResourceDeposits.InvalidSelections != 0 ||
                        meshes.LastDiagnostics.ResourceDeposits.InvalidTransforms != 0 ||
                        meshes.LastDiagnostics.FallbackMeshInstances != 0 || measured.Health.PendingRetirementCount != 0 ||
                        measured.Health.SubmissionFaultCount != 0 || measured.Health.FailureReasonCode is not null ||
                        (measured.Debug.Enabled && measured.Debug.ErrorCount != 0))
                        throw new InvalidOperationException("Capture contains failed bindings, missing resource visuals or fallback meshes.");
                    captureState = new { key, stage = "first-readback", surface = graphics.Diagnostics.Surface, hostWindowDpi = window.Dpi };
                    rgba = CaptureFrame(0);
                    repeatPixelsEqual = true;
                    repeatFailures = new List<object>();
                    shadowDepth = graphics.CaptureShadowDepth;
                    captureInputs.Add(CaptureInputRecord(0, rgba));
                    }
                    for (int repetition = 1; repetition < request.RepeatCaptures; repetition++)
                    {
                        byte[] repeated;
                        using(worldInputInspector?.Arm(repetition))
                        {
                        captureState = new { key, stage = "repeat-readback", repetition, surface = graphics.Diagnostics.Surface, hostWindowDpi = window.Dpi };
                        repeated = CaptureFrame(repetition);
                        captureInputs.Add(CaptureInputRecord(repetition, repeated));
                        }
                        if (!rgba.AsSpan().SequenceEqual(repeated))
                        {
                            if (repeatPixelsEqual && request.CapturePostFrameResourceSnapshotVersion is null)
                                ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, $"failed-{key}-first.png"), display.Width, display.Height, rgba);
                            string failedImage = $"failed-{key}-repeat-{repetition:D2}.png";
                            if (request.CapturePostFrameResourceSnapshotVersion is null)
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
                    if (request.CapturePostFrameResourceSnapshotVersion is null)
                        ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, image), display.Width, display.Height, rgba);
                    if (uiScenario != UiReferenceScenario.World)
                        UiReferenceStates.ConfirmDistinct(uiPixelHashes, FormattableString.Invariant($"{display.Width}x{display.Height}-{quality}-{rasterDpi}-{uiScale:0.00}-"), uiScenario, rgba);
                    var motionImages = new List<object>();
                    if (uiScenario == UiReferenceScenario.World && display.Width == RtsVisualReference.Width && quality == RenderQuality.Medium && zoom == RtsReferenceZoom.NormalGameplay)
                    {
                        var original = camera.CaptureState();
                        for (int step = 0; step < 8; step++)
                        {
                            camera.ApplyState(original with { Target = original.Target + new Vector3(step * .5f, 0, step * .5f), YawRadians = original.YawRadians + step * .005f });
                            PrepareTerrain();
                            string motionImage = $"{key}-motion-{step:D2}.png";
                            captureState = new { key, stage = "camera-pose-readback", step, surface = graphics.Diagnostics.Surface, hostWindowDpi = window.Dpi };
                            byte[] motion = CaptureFrame();
                            ReferenceCaptureImage.WritePng(Path.Combine(request.OutputRoot, motionImage), display.Width, display.Height, motion);
                            motionImages.Add(new { image = motionImage, imageSha256 = FileHash(Path.Combine(request.OutputRoot, motionImage)), camera = camera.CaptureState() });
                        }
                        camera.ApplyState(original);
                        PrepareTerrain();
                    }
                    var text = frontend?.TextDiagnostics ?? hud.TextDiagnostics;
                    if (uiScenario != UiReferenceScenario.World && (!text.AtlasReady || text.DroppedGlyphs != 0 || text.MissingGlyphs != 0))
                        throw new InvalidDataException("Named UI state contains incomplete text.");
                    object[] regions = uiScenario == UiReferenceScenario.World ? [] : UiReferenceStates.Regions(rgba, display.Width, display.Height,
                        frontendView?.Layout, GameplayHudLayout.Create(display.Width, display.Height, rasterDpi, uiScale), panel.IsOpen);
                    captures.Add(new { submission = meshes.SubmissionMetrics, key, image, imageSha256 = FileHash(Path.Combine(request.OutputRoot, image)),
                        pixelSha256 = Convert.ToHexString(SHA256.HashData(rgba)), fixture.Kind, fingerprint = fixtureFingerprint,
                        recipeFingerprint = sharedFixture?.Fingerprint, recipeInputs = sharedFixture?.IdentityInputs, recipeInputBytesBase64 = sharedFixture?.InputBytesBase64,
                        ui = new { scenario = uiScenario, fixtureVersion = 3, layoutContract = GameplayHudLayout.ContractVersion,
                            notifications = frontend is null ? ActionableAlertLayout.Resolve(snapshot,
                                GameplayHudLayout.Create(display.Width, display.Height, rasterDpi, uiScale), panel.IsOpen) : (NotificationStackLayout?)null,
                            skin = frontend?.SkinDiagnostics, battlefieldSkin = frontend is null ? hud.SkinDiagnostics : (GameplayHudSkinDiagnostics?)null, skinState = frontend?.SkinState, skinnedControls = frontend?.LastSkinnedControlCount, layoutId = frontend is null ? "GameplayHudLayout" : "FrontendGeometry",
                            tooltipBounds = hud.LastHoverTooltipBounds, ringGeometry = panel.Ring.Geometry.IsValid ? panel.Ring.Geometry : (RadialActionLayout?)null,
                            interaction = new { panel.Mode, panel.SelectedIndex, panel.HoveredIndex, panel.PointerPressed,
                                panel.ContextualHoveredIndex, panel.ContextualPressed, panel.ContextualPending, targetingMode = targeting.Mode },
                            captureOffset = uiScenario == UiReferenceScenario.MenuReveal ? "Authored half-reveal; live motion reviewed separately" : "Settled fixed state",
                            regions, text, vertices = frontend?.LastRenderedVertexCount ?? hud.LastRenderedVertexCount,
                            background = frontend?.BackgroundDiagnostics },
                        seed = VisualReferenceScene.Seed, tick = fixture.Snapshot.Tick.Value, captureVisualSeconds, mechanicalSeconds = request.MechanicalSeconds, camera = camera.CaptureState(),
                        requestedDisplay = display, actualSurface = measured.Surface, hostWindow = window.ClientSize, hostWindowDpi = window.Dpi,
                        rasterDpi, uiScale, requestedQuality = quality, profile, lighting,
                        observedComposition=request.OwnerObservationVersion==1 ? (object)new ClientRenderConfiguration(profile,lighting,graphics.Atmosphere) : null,
                        localHaze = graphics.Atmosphere.Enabled ? graphics.Atmosphere.LocalHaze : null, surfaceMarks = request.SurfaceMarks,
                        cpu = Distribution(cpu), gpu = Distribution(gpu.ToArray()), fps = Distribution(cpu.Select(x => 1000 / x).ToArray()),
                        performance = performance.Report(measured),
                        run = new { request.RunProfile, request.CameraPath, warmupFrames, setupMilliseconds, setupAllocatedBytes,
                            resourcesBeforeSetup = beforeSetup.Resources, resourcesBeforeSamples = beforeSamples.Resources,
                            cameraPathVersion = request.CameraPath == ReferenceCameraPath.Traverse ? 2 : 1, cameraSamples = request.Samples,
                            coldScope = "Fresh per-case renderers; process/device/runtime catalog and OS caches can already be warm. No OS/driver cache flush." },
                        decals = fixture.Snapshot.DecalMetrics, terrain = ground.LastDiagnostics, instances = meshes.LastDiagnostics, materials = meshes.MaterialDiagnostics, particles = particles.Diagnostics,
                        resourceBatches = meshes.GetResourceBatchDiagnostics(),
                        materialReference = materialReference is null ? null : MaterialReferenceRenderer.Materials,
                        hudVertices = hud.LastRenderedVertexCount, graphics = measured, repeatPixelsEqual, request.RepeatCaptures, repeatFailures,
                        worldInputInspectorVersion=request.CaptureStageDiagnostics ? 1 : (int?)null,captureInputs = captureInputs.ToArray(),
                        motionImages, motionMethod = "Eight adjacent camera poses, no timing claim; readback stalls prevent judder acceptance.", visualReview = "pending" });
                    WriteManifest("in-progress", null);
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                    }
                    catch (Exception error) { RememberSelectedFailure(error); throw; }
                }
                graphics.WaitForIdle();
            }
            if (runtimeFingerprint != RuntimeFingerprint(request.RuntimeRoot)) throw new InvalidOperationException("Runtime assets changed during qualification.");
            string? aggregateFailure = failedCaptures.Count == 0 ? null : $"{failedCaptures.Count} fixed-state cases contain unequal native pixels.";
            WriteManifest(aggregateFailure is null ? "structural-pass" : "structural-failure", aggregateFailure);
            if (aggregateFailure is not null)
                ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new
                { status = "structural-failure", error = aggregateFailure, completedCaptures = captures.Count, failedCaptures }, JsonOptions));
            return aggregateFailure is null ? 0 : 1;

            void WriteManifest(string status, string? error) => ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "manifest.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, status, error, request.Commit, buildIdentity = identity,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    environment = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        logicalProcessors = Environment.ProcessorCount, cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                        configuration = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration },
                    runtimeFingerprint, runtimeSeal, request.Driver, software = request.Software, fixtureVersion = VisualReferenceScene.FixtureVersion,
                    adapterIdentity = (graphics as IGraphicsRenderTelemetry)?.DeviceIdentity, adapterBinding = adapterCheckpoint?.Evidence,
                    hostWindowDpi = window.Dpi, executionRequirement = "Thread-scoped display/system requirement during explicit capture; user sleep/lock and occlusion checks remain authoritative.",
                    clientAssemblySha256 = FileHash(typeof(Program).Assembly.Location),
                    presentationAssemblySha256 = FileHash(typeof(GameplayHudRenderer).Assembly.Location),
                    startupMilliseconds, startupAllocatedBytes,
                    capturePath = "D3D12 swap-chain backbuffer readback before Present; full-resolution native output, independent of host-window clamp",
                    method = "Immutable presentation states; completed extraction tick and frozen visual seconds recorded per case, VSync off, particle age.2s. CPU includes frame reuse/Present. Distinct completed GPU fences. Readbacks/PNG export excluded. No live gameplay or sustained-motion FPS claim.",
                    request.Warmups, request.Samples, request.ContinueAfterMismatch,
                    postWindowControl=postWindowControl?.Evidence(),ownerCycles=new {schemaVersion=1,available=false,reason="Native reference has no ClientRenderHost owner cycle or exact process-allocation collector"},
                    platformWorkload=request.OwnerObservationVersion==1 ? (object)platform.CaptureWorkloadTelemetry() : null,
                    failedCaptures, captures, humanVisualAcceptance = "pending" }, JsonOptions));
            }
            catch (Exception error) { RememberSelectedFailure(error); throw; }
            }
            catch (Exception error) { RememberSelectedFailure(error); throw; }
            }
            catch (Exception error) { RememberSelectedFailure(error); throw; }
            }
            catch (Exception error) { RememberSelectedFailure(error); throw; }
            }
            catch (Exception error) { RememberSelectedFailure(error); throw; }
        }
        catch (Exception caught)
        {
            Exception error = ReferencePostFrameSnapshot.SelectPrimary(caught, selectedSnapshotPrimary);
            if (request?.CapturePostFrameResourceSnapshotVersion == 1)
            {
                try { Console.Error.WriteLine($"[reference:failure] {error.GetType().Name}: {error.Message}"); }
                catch (Exception reporting) { RememberSelectedFailure(reporting); }
            }
            else Console.Error.WriteLine($"[reference:failure] {error.GetType().Name}: {error.Message}");
            if (request is not null && outputCreated)
                ReferenceBoundInputProvenance.RetainScopeFailure(error, provenanceScope, request.OutputRoot);
            // Never overwrite an existing evidence directory after a rejected request.
            if (request is not null && outputCreated)
            {
                if (request.CapturePostFrameResourceSnapshotVersion == 1)
                {
                    try
                    {
                        ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new
                        {
                            status = "structural-failure", error = error.ToString(), completedCaptures = captures.Count, captureState, captureInputs,
                            timedWindow = timedWindowState, postFrameFailure = ReferencePostFrameSnapshot.FailureFacts(error, "outer-report", null, [])
                        }, JsonOptions));
                    }
                    catch (Exception reporting)
                    {
                        RememberSelectedFailure(reporting);
                        ReferencePostFrameSnapshot.RetainReportingFailure(error, request.OutputRoot);
                    }
                }
                else ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new { status = "structural-failure", error = error.ToString(), completedCaptures = captures.Count, captureState, captureInputs, timedWindow = timedWindowState }, JsonOptions));
            }
            return 1;
        }
        finally { provenanceScope?.Dispose(); }
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

