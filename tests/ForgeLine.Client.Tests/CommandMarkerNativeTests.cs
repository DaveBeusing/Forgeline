using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

[Collection("Native scene output")]
public sealed class CommandMarkerNativeTests
{
    [Fact]
    public void MarkerOutcomesHaveDistinctRepeatableNativePixelsAndCompleteLabels()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ForgeLine.sln"))) root = Directory.GetParent(root)!.FullName;
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE command acknowledgement", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false });
        using var renderer = new RtsInformationOverlayRenderer(graphics,
            RuntimeAssetCatalog.Load(Path.Combine(root, "assets/runtime")));
        var camera = new RtsCamera(new RtsCameraSettings { InitialTarget = Vector3.Zero });
        var snapshot = new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), 0, [],
            playerExperience: default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(1) });
        var view = RtsInformationLayerView.Empty with { MinimapEnabled = false };
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
        int baselineGlyphs = renderer.TextDiagnostics.Glyphs;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        string? output = Environment.GetEnvironmentVariable("FORGELINE_COMMAND_MARKER_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Command marker capture output must be empty.");
            Directory.CreateDirectory(output);
        }
        foreach (var state in Enum.GetValues<RtsCommandMarkerState>())
        {
            view = view with { TacticalFeedback = new(true, Vector3.Zero,
                state != RtsCommandMarkerState.Rejected, RtsCommandMarkerKind.Attack) { State = state } };
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
            byte[] repeated = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
            Assert.Equal(pixels, repeated);
            Assert.Equal(baselineGlyphs + (state is RtsCommandMarkerState.Pending or RtsCommandMarkerState.Partial ? 7 : 8),
                renderer.TextDiagnostics.Glyphs);
            Assert.Equal(0, renderer.TextDiagnostics.MissingGlyphs);
            Assert.Equal(0, renderer.TextDiagnostics.DroppedGlyphs);
            Assert.Equal(0, renderer.SkinDiagnostics.MissingAssets);
            Assert.Equal(0, renderer.SkinDiagnostics.DroppedQuads);
            if (state == RtsCommandMarkerState.Pending)
                Assert.True(renderer.LastRenderedVertexCount >= renderer.TextDiagnostics.Vertices + 24);
            Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(pixels))));
            if (!string.IsNullOrEmpty(output))
                ReferenceCaptureImage.WritePng(Path.Combine(output, state + ".png"), 1280, 720, pixels);
        }
        void Record(IGraphicsCommandContext context) => renderer.Render(context, camera, snapshot,
            new(new(-500), new(500)), view, CombatGroupOverviewView.Empty, 96);
    }
}
