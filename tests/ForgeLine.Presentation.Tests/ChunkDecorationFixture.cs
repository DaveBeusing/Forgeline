using System.Numerics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation.Tests;

internal static class ChunkDecorationFixture
{
    internal static EnvironmentScatterChunk[] Generate(int count, int side = 2, ChunkCoordinate origin = default)
    {
        var recipe = new WorldGenerationRecipe { Seed = 17, Origin = origin, WidthInChunks = side, HeightInChunks = side };
        var species = new ScatterSpecies("mesh.world.vegetation.grass_clump", ScatterClassification.Grass,
            400, 16000, 400, 500, 1000, 1000, 0, new(0, 65535, 0, 0, 0));
        var generator = new EnvironmentScatterGenerator(recipe, new("scatter.render.fixture.v1", [species], densityPermille: 1000,
            maximumTotalInstances: count), new Field(), new ScatterClearanceIndex([]));
        return Enumerable.Range(0, side).SelectMany(z => Enumerable.Range(0, side).Select(x =>
            generator.GenerateChunk(new(origin.X + x, origin.Z + z)))).ToArray();
    }
    internal static EnvironmentScatterChunk[] Mixed()
    {
        var recipe = new WorldGenerationRecipe { Seed = 17, WidthInChunks = 2, HeightInChunks = 2 };
        var yard = new ScatterClearanceIndex([new(ScatterAreaKind.Yard, 10000, 10000, 246000, 246000)]);
        var generator = new EnvironmentScatterGenerator(recipe, EnvironmentScatterProfile.Temperate(1000, 4000), new Forest(), yard);
        return [generator.GenerateChunk(new(0,0)), generator.GenerateChunk(new(1,0)),
            generator.GenerateChunk(new(0,1)), generator.GenerateChunk(new(1,1))];
    }
    private sealed class Forest : IEnvironmentQuery
    {
        public bool TrySample(long x, long z, out EnvironmentSample sample, EnvironmentSurface surface = EnvironmentSurface.Natural)
        { sample = new(0, 18000, 0, 40000, 50000, EnvironmentSurface.Natural, new(45000, 8000, 4000, 4000, 4535)); return true; }
    }
    internal static RtsCamera Camera(Vector3 target, float distance) =>
        new(new() { InitialTarget = target, InitialDistance = distance, MaximumDistance = 8000 });
    internal static RenderWorld World(ChunkDecorationSnapshot snapshot)
    {
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, []) { ChunkDecorations = snapshot });
        var world = new RenderWorld(); world.Update(buffer); return world;
    }
    private sealed class Field : IEnvironmentQuery
    {
        public bool TrySample(long x, long z, out EnvironmentSample sample, EnvironmentSurface surface = EnvironmentSurface.Natural)
        { sample = new(0, 18000, 0, 40000, 50000, EnvironmentSurface.Natural, new(0, 65535, 0, 0, 0)); return true; }
    }
}
