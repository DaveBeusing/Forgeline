using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.TerrainResidency;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests.TerrainResidency;

public sealed class TerrainGeometryTests
{
    [Theory]
    [InlineData(33)]
    [InlineData(65)]
    [InlineData(34)]
    public void EveryLevelTilesTheChunkWithIdenticalUnitBoundarySegments(int samples)
    {
        var patterns = new TerrainIndexPatterns(samples, 8 * 1024 * 1024);
        int cells = samples - 1;
        foreach (var range in patterns.Ranges)
        {
            var edges = new Dictionary<(uint, uint), int>(); long twiceArea = 0;
            for (int i = range.FirstIndex; i < range.FirstIndex + range.IndexCount; i += 3)
            {
                uint a = patterns.Indices[i], b = patterns.Indices[i + 1], c = patterns.Indices[i + 2];
                Assert.InRange(a, 0u, (uint)(samples * samples - 1));
                Assert.InRange(b, 0u, (uint)(samples * samples - 1));
                Assert.InRange(c, 0u, (uint)(samples * samples - 1));
                int ax = (int)a % samples, az = (int)a / samples, bx = (int)b % samples, bz = (int)b / samples,
                    cx = (int)c % samples, cz = (int)c / samples;
                int positiveY = (bz - az) * (cx - ax) - (bx - ax) * (cz - az);
                Assert.True(positiveY > 0); twiceArea += positiveY;
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            Assert.Equal(2L * cells * cells, twiceArea);
            Assert.Equal(4 * cells, edges.Count(e => e.Value == 1));
            foreach (var pair in edges)
            {
                Assert.InRange(pair.Value, 1, 2);
                if (pair.Value == 2) continue;
                int ax = (int)pair.Key.Item1 % samples, az = (int)pair.Key.Item1 / samples;
                int bx = (int)pair.Key.Item2 % samples, bz = (int)pair.Key.Item2 / samples;
                Assert.Equal(1, Math.Abs(ax - bx) + Math.Abs(az - bz));
                Assert.True(ax == bx && (ax == 0 || ax == cells) || az == bz && (az == 0 || az == cells));
            }
            void Edge(uint a, uint b)
            {
                var edge = a < b ? (a, b) : (b, a); edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        }
        if (cells % 2 == 0) Assert.True(patterns.Ranges[^1].IndexCount < patterns.Ranges[0].IndexCount);
    }

    [Fact]
    public void LargeGridKeepsThirtyTwoBitIndicesAndCheckedLimits()
    {
        var patterns = new TerrainIndexPatterns(257, 8 * 1024 * 1024);
        Assert.Equal(66048u, patterns.Indices.Max());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainIndexPatterns(65537, 8 * 1024 * 1024));
    }

    [Fact]
    public void NeighbourBoundaryPositionsNormalsAndWorldUvAgreeOnCurvedTerrain()
    {
        TerrainWorld world = CurvedWorld();
        var source = new TerrainPreparedSource(world, new(), new([]), new());
        var left = source.Build(0, CancellationToken.None); var right = source.Build(1, CancellationToken.None);
        for (int z = 0; z < 33; z++)
        {
            var a = left.Vertices[z * 33 + 32]; var b = right.Vertices[z * 33];
            Assert.Equal(a.Position, b.Position); Assert.Equal(a.Normal, b.Normal); Assert.Equal(a.MacroTint, b.MacroTint);
            Assert.Equal(1, a.LocalUv.X); Assert.Equal(0, b.LocalUv.X); Assert.Equal(a.LocalUv.Y, b.LocalUv.Y);
        }
    }

    [Fact]
    public void DifferentHeightfieldResolutionsShareTheSameBoundaryDerivative()
    {
        var chunks = new List<TerrainChunk>();
        for (int cx = 0; cx < 2; cx++)
        {
            int samples = cx == 0 ? 65 : 33; var heights = new float[samples * samples];
            for (int z = 0; z < samples; z++)
                for (int x = 0; x < samples; x++)
                    heights[z * samples + x] = 20 + MathF.Sin((cx * 256 + x * 256f / (samples - 1)) * .023f);
            chunks.Add(new(new(cx, 0), new TerrainHeightfield(samples, 256, heights)));
        }
        var source = new TerrainPreparedSource(new(new(), chunks), new(), new([]), new());
        var left = source.Build(0, CancellationToken.None); var right = source.Build(1, CancellationToken.None);
        for (int z = 0; z < 33; z++) Assert.Equal(left.Vertices[z * 33 + 32].Normal, right.Vertices[z * 33].Normal);
    }

    [Fact]
    public void ResidualBoundContainsActualFineAndCoarseTriangleInterpolation()
    {
        var source = new TerrainPreparedSource(CurvedWorld(), new(), new([]), new());
        var data = source.Build(0, CancellationToken.None);
        for (int level = 1; level < source.Patterns.Ranges.Length; level++)
            for (int z = 0; z < 32; z++)
                for (int x = 0; x < 32; x++)
                {
                    Vector2 point = new(x + .37f, z + .61f);
                    float fine = Interpolate(0, point), coarse = Interpolate(level, point);
                    Assert.InRange(Math.Abs(fine - coarse), 0, data.HeightErrors[level] + .0001f);
                }
        float Interpolate(int level, Vector2 point)
        {
            var range = source.Patterns.Ranges[level];
            for (int i = range.FirstIndex; i < range.FirstIndex + range.IndexCount; i += 3)
            {
                uint ia = source.Patterns.Indices[i], ib = source.Patterns.Indices[i + 1], ic = source.Patterns.Indices[i + 2];
                Vector2 a = new(ia % 33, ia / 33), b = new(ib % 33, ib / 33), c = new(ic % 33, ic / 33);
                float denominator = Cross(b - a, c - a);
                float wb = Cross(point - a, c - a) / denominator, wc = Cross(b - a, point - a) / denominator;
                if (wb >= -.00001f && wc >= -.00001f && wb + wc <= 1.00001f)
                    return data.Vertices[ia].Position.Y * (1 - wb - wc) + data.Vertices[ib].Position.Y * wb + data.Vertices[ic].Position.Y * wc;
            }
            throw new InvalidOperationException("Uncovered triangle sample.");
        }
        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }

    [Fact]
    public void HysteresisNeverExceedsTheConfiguredPixelError()
    {
        var settings = new TerrainPreparationSettings(); float[] normal = [0, 0], tint = [0, 0];
        var projection = new TerrainLodProjection(0, 0, 1, true);
        Assert.Equal(1, TerrainLodSelector.Select([0, .30f], normal, tint, projection, settings, 0));
        Assert.Equal(0, TerrainLodSelector.Select([0, .40f], normal, tint, projection, settings, 0));
        Assert.Equal(1, TerrainLodSelector.Select([0, .40f], normal, tint, projection, settings, 1));
        Assert.Equal(0, TerrainLodSelector.Select([0, .501f], normal, tint, projection, settings, 1));
        Assert.Equal(0, TerrainLodSelector.Select([0, 0], [0, .021f], tint, projection, settings, 1));
        Assert.Equal(0, TerrainLodSelector.Select([0, 0], normal, [0, .003f], projection, settings, 1));
        Assert.Equal(0, TerrainLodSelector.Select([0, 0], normal, tint, projection, settings, 1, true));
        Assert.Equal(0, TerrainLodSelector.Select([0, 0], normal, tint,
            TerrainLodProjection.Shadow(Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 1, 100), 2048), settings, 1));
        Assert.True(new TerrainLodProjection(10, 5, 1000, false).Pixels(.01f) > 1);
    }

    [Fact]
    public void ActualDrawRangesReduceTrianglesWithoutChangingMainOrShadowCoverage()
    {
        using var device = new TerrainTestDevice(); var world = TerrainPreparationTests.FlatWorld(1);
        using var full = new TerrainRenderer(device, world, presentationProfile: new([]), preparation: new() { GeometryLodEnabled = false });
        using var lod = new TerrainRenderer(device, world, presentationProfile: new([]));
        var camera = new RtsCamera(new() { InitialDistance = 260 });
        var a = new TerrainTestContext(); var b = new TerrainTestContext();
        full.Render(a, camera); lod.Render(b, camera);
        Assert.Equal(full.LastDiagnostics.VisibleChunks, lod.LastDiagnostics.VisibleChunks);
        Assert.True(b.Indices < a.Indices); Assert.True(lod.WorkMetrics.ReducedMainChunks > 0);
        Assert.Equal(b.Indices / 3, lod.WorkMetrics.MainTriangles);
        Assert.Equal(b.TextureBindings, lod.WorkMetrics.TextureBindings);
        Assert.Equal(12 * lod.WorkMetrics.MaterialBindings + lod.LastDiagnostics.VisibleChunks, b.TextureBindings);
        var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, device.DirectionalShadows);
        long fullBefore = a.Indices, lodBefore = b.Indices;
        Assert.Equal(full.RenderShadow(a, shadow, 1000), lod.RenderShadow(b, shadow, 1000));
        Assert.True(b.Indices - lodBefore < a.Indices - fullBefore);
        Assert.Equal((b.Indices - lodBefore) / 3, lod.WorkMetrics.ShadowTriangles);
    }

    [Fact]
    public void ShorelineAndAuthoredRoadChunksKeepFullGeometry()
    {
        var world = CurvedWorld();
        var road = new TerrainPresentationProfile([], [new(TerrainMaterialSlot.Concrete, new(128), new(512), 0, Strength: 1)]);
        var source = new TerrainPreparedSource(world, new(), road, new());
        Assert.True(source.Build(0, CancellationToken.None).ProtectedSurface);
        var heights = new float[65 * 65];
        for (int i = 0; i < heights.Length; i++) heights[i] = i % 65 - 32;
        var coast = new TerrainWorld(new(), [new(new(0, 0), new TerrainHeightfield(65, 256, heights))]);
        Assert.True(new TerrainPreparedSource(coast, new(), new([]), new()).Build(0, CancellationToken.None).ProtectedSurface);
    }

    private static TerrainWorld CurvedWorld()
    {
        var chunks = new List<TerrainChunk>();
        for (int cx = 0; cx < 2; cx++)
        {
            var heights = new float[65 * 65];
            for (int z = 0; z < 65; z++)
                for (int x = 0; x < 65; x++)
                {
                    float wx = cx * 256 + x * 4, wz = z * 4;
                    heights[z * 65 + x] = 20 + .004f * wx + MathF.Sin(wx * .023f) * MathF.Cos(wz * .017f);
                }
            chunks.Add(new(new(cx, 0), new TerrainHeightfield(65, 256, heights)));
        }
        return new(new(), chunks);
    }
}
