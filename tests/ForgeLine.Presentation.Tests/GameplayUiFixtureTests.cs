using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Core;
using ForgeLine.Input;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

[CollectionDefinition("Gameplay UI geometry", DisableParallelization = true)]
public sealed class GameplayUiFixtureTestGroup;

[Collection("Gameplay UI geometry")]
public sealed class GameplayUiFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [Fact]
    public void ReboundLongLabelAndFeedbackGeometryMatchesReviewedFixtures()
    {
        var fixtures = new SortedDictionary<string, Fixture>(StringComparer.Ordinal);
        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Build, ForgeLine.Platform.PlatformKey.G));
        using var device = new RecordingDevice();
        using var renderer = new PlayerActionDockHudRenderer(device, null);
        foreach (var profile in new[] { (1024, 720, 96u), (1920, 1080, 144u), (1920, 1200, 192u), (3440, 1440, 144u), (3840, 2160, 192u) })
            foreach (float scale in new[] { .75f, 1f, 2f })
                foreach (string state in new[] { "Ready", "Focus", "Hover", "Pressed", "Disabled", "Pending", "Accepted", "Rejected", "Cancelled" })
                {
                    var snapshot = GameplayRebindingTests.Snapshot();
                    bool disabled = state == "Disabled";
                    if (disabled)
                        snapshot = new(snapshot.Tick, TimeSpan.Zero, 1, snapshot.Instances,
                            sessionId: snapshot.SessionId, playerExperience: snapshot.PlayerExperience,
                            playerActions: new(snapshot.SessionId, snapshot.Tick,
                                [new(BuildingIds.PowerPlant, "Power Plant", [new(ResourceIds.Steel, "Steel", 10, 0)], false),
                         new(BuildingIds.VehicleFactory, "Vehicle Factory with a deliberately long qualification label", [new(ResourceIds.Steel, "Steel", 10, 0)], false)], 0, null, null));
                    var controller = new PlayerActionPanelController(bindings);
                    var panel = controller.CreateView(profile.Item1, profile.Item2, snapshot.PlayerActions, profile.Item3, scale) with
                    {
                        Mode = state == "Cancelled" ? PlayerActionPanelMode.Closed : PlayerActionPanelMode.Construction,
                        ContextualHoveredIndex = state is "Hover" or "Pressed" ? 0 : -1,
                        ContextualPressed = state == "Pressed",
                        ContextualPending = state == "Pending",
                        ContextualActivationTick = snapshot.Tick,
                        ContextualSessionId = snapshot.SessionId,
                        SelectedIndex = state == "Focus" ? 1 : 0,
                        HoveredIndex = state == "Hover" ? 1 : -1,
                        PointerPressed = state == "Pressed"
                    };
                    if (state is "Accepted" or "Rejected")
                        snapshot = new(snapshot.Tick, TimeSpan.Zero, 1, snapshot.Instances,
                            sessionId: snapshot.SessionId, playerActions: snapshot.PlayerActions,
                            playerExperience: snapshot.PlayerExperience!.Value with
                            {
                                Feedback = PlayerCommandFeedback.None with
                                {
                                    Kind = PlayerCommandFeedbackKind.Construction,
                                    State = state == "Accepted" ? PlayerCommandFeedbackState.Accepted : PlayerCommandFeedbackState.Rejected,
                                    AcceptedTargets = state == "Accepted" ? 1 : 0,
                                    RejectedTargets = state == "Rejected" ? 1 : 0,
                                    ResolvedAtTick = snapshot.Tick
                                }
                            });
                    var context = new RecordingContext { Width = profile.Item1, Height = profile.Item2 };
                    var layout = GameplayHudLayout.Create(profile.Item1, profile.Item2, profile.Item3, scale);
                    renderer.Render(context, snapshot, panel, default, FormationTemplate.Compact, layout);
                    var vertices = device.Buffers.SelectMany(x => x.Vertices).ToArray();
                    Assert.InRange(vertices.Length, 1, 262144);
                    Assert.All(vertices, vertex =>
                    {
                        Assert.True(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y));
                        Assert.InRange(vertex.Position.X, -1, 1); Assert.InRange(vertex.Position.Y, -1, 1);
                    });
                    string name = FormattableString.Invariant($"{profile.Item1}x{profile.Item2}-{profile.Item3}dpi-{scale:0.00}-{state}");
                    string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(vertices.AsSpan())));
                    fixtures.Add(name, new(vertices.Length, hash));
                    renderer.Render(context, snapshot, panel, default, FormationTemplate.Compact, layout);
                    Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(device.Buffers.SelectMany(x => x.Vertices).ToArray().AsSpan()))));
                    string? visualRoot = Environment.GetEnvironmentVariable("FORGELINE_UI_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(visualRoot) && scale == 1 && state != "Cancelled")
                        WriteSvg(Path.Combine(visualRoot, name + ".svg"), vertices, profile.Item1, profile.Item2);
                }
        Assert.NotEqual(fixtures["1920x1080-144dpi-1.00-Ready"], fixtures["1920x1080-144dpi-1.00-Disabled"]);
        Assert.NotEqual(fixtures["1920x1080-144dpi-1.00-Ready"], fixtures["1920x1080-144dpi-1.00-Accepted"]);
        string actual = JsonSerializer.Serialize(fixtures, JsonOptions);
        string? baselineOutput = Environment.GetEnvironmentVariable("FORGELINE_UI_FIXTURE_BASELINE");
        if (!string.IsNullOrEmpty(baselineOutput))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(baselineOutput))!);
            File.WriteAllText(baselineOutput, actual + Environment.NewLine);
        }
        else
        {
            string baseline = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gameplay-ui-geometry.json"));
            var expected = JsonSerializer.Deserialize<SortedDictionary<string, Fixture>>(baseline, JsonOptions)!;
            Assert.Equal(expected.Count, fixtures.Count);
            foreach (var fixture in fixtures) Assert.Equal(expected[fixture.Key], fixture.Value);
        }
    }
    private static void WriteSvg(string path, Vertex[] vertices, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var svg = new StringBuilder(FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><title>Command geometry fixture; excludes native terrain and textures</title><rect width=\"100%\" height=\"100%\" fill=\"#171b1b\"/>"));
        for (int i = 0; i < vertices.Length; i += 3)
        {
            var color = vertices[i].Color;
            svg.Append("<polygon points=\"");
            for (int j = 0; j < 3; j++)
            {
                Vector2 p = vertices[i + j].Position;
                svg.Append(FormattableString.Invariant($"{(p.X + 1) * width / 2:0.###},{(1 - p.Y) * height / 2:0.###} "));
            }
            svg.Append(FormattableString.Invariant($"\" fill=\"rgb({Math.Clamp((int)(color.X * 255), 0, 255)},{Math.Clamp((int)(color.Y * 255), 0, 255)},{Math.Clamp((int)(color.Z * 255), 0, 255)})\" fill-opacity=\"{color.W:0.###}\"/>"));
        }
        File.WriteAllText(path, svg.Append("</svg>").ToString());
    }
    private sealed record Fixture(int Vertices, string GeometrySha256);
}
