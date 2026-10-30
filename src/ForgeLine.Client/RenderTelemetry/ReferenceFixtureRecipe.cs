using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.World;
using ForgeLine.Game;

namespace ForgeLine.Client;

// One authored native world state, shared by the reference runner and real render owner.
// No simulation is started and no authoritative commands or world state are modified.
internal sealed class ReferenceFixtureRecipe
{
    private readonly DebugLine[] _lines;
    private readonly DebugLabel[] _labels;
    internal VisualReferenceScene Fixture { get; }
    internal PresentationSnapshot Snapshot { get; }
    internal PlayerActionPanelView Panel { get; }
    internal RtsCameraSettings CameraSettings { get; }
    internal RtsCameraState InitialCamera { get; }
    internal RtsInformationLayerView Information { get; }
    internal DebugDraw Indicators { get; }
    internal TerrainPresentationProfile TerrainProfile { get; }
    internal RenderQualitySettings Settings { get; }
    internal GraphicsAtmosphereSettings Atmosphere { get; }
    internal RtsReferenceDisplay Display { get; }
    internal uint RasterDpi { get; }
    internal float UiScale { get; }
    internal double VisualSeconds { get; }
    internal string Fingerprint { get; }
    internal object IdentityInputs { get; }
    internal string InputBytesBase64 { get; }
    internal string CaseKey { get; }
    internal int RepeatCaptures { get; }
    internal string? AdapterBindingDirectory { get; }
    internal string SnapshotFingerprint { get; }
    internal ReferenceCameraPath CameraPath { get; }
    internal int Warmups { get; }
    internal int Samples { get; }
    internal bool Software { get; }
    internal string OutputRoot { get; }
    internal string RuntimeFingerprint { get; }
    internal RtsCameraState[] SampleCameras { get; }

    internal ReferenceFixtureRecipe(VisualReferenceScene fixture, TerrainWorld terrain, RuntimeAssetCatalog catalog,
        VisualReferenceCaptureRequest request, RtsReferenceDisplay display, RenderQuality quality, RtsReferenceZoom zoom,
        uint rasterDpi, float uiScale, string? runtimeFingerprint = null)
    {
        CaseKey = BuildCaseKey(fixture.Kind, request, display, quality, zoom, uiScale);
        Fixture = fixture; Display = display; RasterDpi = rasterDpi; UiScale = uiScale;
        Settings = (request.GraphicsSettings ?? new RenderQualitySettings()) with { Quality = quality };
        Atmosphere = GraphicsAtmosphereSettings.Default with { LocalHaze = request.LocalHaze };
        TerrainProfile = fixture.ResolveTerrainProfile(TerrainPresentationProfile.CreateCentralDivide(catalog));
        if (request.SurfaceMarks.Length > 0) TerrainProfile = TerrainProfile.WithSurfaceMarks(new TerrainSurfaceMarks(request.SurfaceMarks));
        CameraSettings = RtsVisualReference.CreateCameraSettings(fixture.Target, zoom) with
        {
            InitialYawRadians = request.YawDegrees == 45 ? RtsVisualReference.YawRadians : request.YawDegrees * MathF.PI / 180,
            InitialDistance = request.InspectionDistanceMeters == 0 ? RtsVisualReference.GetDistance(zoom) : request.InspectionDistanceMeters
        };
        InitialCamera = new RtsCamera(CameraSettings).CaptureState();
        (Snapshot, Panel) = UiReferenceStates.Hud(fixture.Snapshot, UiReferenceScenario.World, display.Width, display.Height, rasterDpi, uiScale);
        SnapshotFingerprint = ReferenceSnapshotHistory.Fingerprint(fixture.Fingerprint, fixture.PreviousSnapshot);
        VisualSeconds = fixture.Snapshot.Tick.Value * fixture.Snapshot.TickDuration.TotalSeconds;
        var selected = fixture.Snapshot.Instances.ToArray().Where(x => x.Selectable.IsSelectable).Take(3).Select(x => x.Entity).ToArray();
        Information = RtsInformationLayerView.Empty with { SelectedEntities = selected, PointerPosition = new(display.Width * .5f, display.Height * .5f) };
        Indicators = new DebugDraw { Enabled = true };
        foreach (var instance in fixture.Snapshot.Instances.ToArray().Where(x => selected.Contains(x.Entity)))
        {
            if (fixture.Kind is VisualReferenceSceneKind.CommandCoreProduction or VisualReferenceSceneKind.InfantryRifleProduction or VisualReferenceSceneKind.InfantryEngineerProduction or VisualReferenceSceneKind.InfantryRepeated or VisualReferenceSceneKind.InfantryBattleProduction)
                RtsWorldMarkerVisualization.DrawSelected(Indicators, instance, new(0, .8f, .2f, 1), terrain);
            else Indicators.Circle(instance.Transform.Position + Vector3.UnitY, 5, new(0, .8f, .2f, 1));
        }
        _lines = Indicators.Lines.ToArray(); _labels = Indicators.Labels.ToArray();
        CameraPath = request.CameraPath; Samples = request.Samples;
        Warmups = request.RunProfile == ReferenceRunProfile.RendererCold ? 0 : request.Warmups;
        RepeatCaptures = request.RepeatCaptures; AdapterBindingDirectory = request.AdapterBindingDirectory;
        Software = request.Software; OutputRoot = request.OutputRoot;
        RuntimeFingerprint = runtimeFingerprint ?? VisualReferenceCapture.RuntimeFingerprint(catalog.RuntimeRoot);
        SampleCameras = Enumerable.Range(0, Samples).Select(i => ReferencePerformanceSamples.CameraPose(InitialCamera, CameraPath, i, Samples)).ToArray();
        IdentityInputs = new
        {
            recipeVersion = 1, fixture.Kind, SnapshotFingerprint, VisualSeconds, request.MechanicalSeconds,
            alpha = 1, particleAgeSeconds = .2, particleDelta = 0, paused = true, Settings, Atmosphere,
            request.SurfaceMarks, Display, RasterDpi, UiScale, CameraSettings, CameraPath, Samples,
            panel = Panel, Information, selected, lines = Indicators.Lines.ToArray(), labels = Indicators.Labels.ToArray(), RuntimeFingerprint
        };
        byte[] inputBytes = JsonSerializer.SerializeToUtf8Bytes(IdentityInputs, VisualReferenceCapture.JsonOptions);
        InputBytesBase64 = Convert.ToBase64String(inputBytes);
        Fingerprint = Convert.ToHexString(SHA256.HashData(inputBytes));
    }
    internal static string BuildCaseKey(VisualReferenceSceneKind kind, VisualReferenceCaptureRequest request,
        RtsReferenceDisplay display, RenderQuality quality, RtsReferenceZoom zoom, float uiScale)
    {
        string key = $"{kind}-{display.Width}x{display.Height}-{quality}-{zoom}";
        if (request.RasterDpi != 0) key += $"-Dpi{request.RasterDpi}";
        if (request.YawDegrees != 45) key += "-Yaw" + request.YawDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (request.MechanicalSeconds is double seconds) key += "-MechanicalT" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (request.InspectionDistanceMeters != 0) key += "-Inspection" + request.InspectionDistanceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (request.RunProfile != ReferenceRunProfile.Warm) key += $"-{request.RunProfile}";
        if (request.CameraPath != ReferenceCameraPath.Fixed) key += $"-{request.CameraPath}";
        if (uiScale != 1) key += FormattableString.Invariant($"-UiWorld-Scale{uiScale:0.00}");
        return key;
    }
    internal int FrameCount => checked(Warmups + Samples + (CameraPath == ReferenceCameraPath.Fixed ? 0 : 3));
    internal RenderWorld CreateWorld() => ReferenceSnapshotHistory.Create(Snapshot, Fixture.PreviousSnapshot);
    internal RtsCameraState CameraAt(int ordinal) => ordinal < Warmups || ordinal >= Warmups + Samples ? InitialCamera : SampleCameras[ordinal - Warmups];
    internal ClientRenderFrame Frame(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, FrameCount);
        var camera = CameraAt(ordinal);
        return new(camera, Display.Width, Display.Height, false, default, Panel, default, FormationTemplate.Compact,
            _lines, _labels, [], [], RasterDpi, Information, UiScale,
            MetricsActive: false);
    }
}

// One outstanding pose request; ACK is only issued after an actual successful host presentation.
internal sealed class ReferenceFixtureSequence(int count)
{
    private readonly object _gate = new();
    private int _requested = -1, _acknowledged = -1;
    internal void Request(int ordinal)
    {
        lock (_gate)
        {
            if (ordinal != _requested + 1 || _requested != _acknowledged || ordinal >= count)
                throw new InvalidOperationException("Fixture requests must be contiguous with one outstanding presentation.");
            _requested = ordinal;
        }
    }
    internal bool TryRead(out int ordinal)
    {
        lock (_gate) { ordinal = _requested; return _requested > _acknowledged; }
    }
    internal void Acknowledge(int ordinal)
    {
        lock (_gate)
        {
            if (_requested != ordinal || _acknowledged + 1 != ordinal)
                throw new InvalidOperationException("Fixture presentation ACK is stale, duplicate or skipped.");
            _acknowledged = ordinal;
        }
    }
    internal int Acknowledged { get { lock (_gate) return _acknowledged; } }
}
