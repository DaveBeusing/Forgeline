using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CompiledTerrainTests
{
    // Captured from master cb58c3ad before replacing the original terrain evaluator.
    private const string LegacyTerrainSha256 = "d9ff8d68132b7c8a48936cf751552044ca6befe8b4359b1dd0b736de2bc67a33";

    [Fact]
    public void CompiledRecipePreservesEveryLegacyHeightSample()
    {
        var artifact = BattlefieldMapArtifact.Deserialize(BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create()).Serialize());
        var terrain = BattlefieldTerrainFactory.Create(artifact.ToDefinition(), artifact.Terrain);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[4];
        foreach (var chunk in terrain.Chunks)
            for (int z = 0; z < chunk.Heightfield.SamplesPerSide; z++)
                for (int x = 0; x < chunk.Heightfield.SamplesPerSide; x++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, chunk.Heightfield.GetHeight(x, z));
                    hash.AppendData(bytes);
                }
        Assert.Equal(LegacyTerrainSha256, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    [Fact]
    public void LoadedRecipeParametersControlAuthoritativeHeights()
    {
        var artifact = BattlefieldMapArtifact.Deserialize(BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create()).Serialize());
        var definition = artifact.ToDefinition();
        var original = BattlefieldTerrainFactory.Create(definition, artifact.Terrain);
        var changed = BattlefieldTerrainFactory.Create(definition, artifact.Terrain with { RidgeAmplitude = 0 });
        Assert.True(original.TrySampleHeight(1536, 1536, out float before));
        Assert.True(changed.TrySampleHeight(1536, 1536, out float after));
        Assert.True(before - after > 50);
    }

    [Fact]
    public void InputBudgetsAndInvalidOwnersAreRejected()
    {
        string oversized = "{\"starts\":[" + string.Join(",", Enumerable.Repeat("{}", 16_385)) + "]}";
        var error = Assert.Throws<InvalidOperationException>(() => BattlefieldMapArtifact.Deserialize(System.Text.Encoding.UTF8.GetBytes(oversized)));
        Assert.Contains("collection budget", error.Message);
        string longKey = "{\"metadata\":{\"key\":\"" + new string('x', 2049) + "\"}}";
        Assert.Throws<InvalidOperationException>(() => BattlefieldMapArtifact.Deserialize(System.Text.Encoding.UTF8.GetBytes(longKey)));
        var map = BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create());
        var objectives = map.Objectives.ToArray();
        objectives[1] = objectives[1] with { Owner = objectives[0].Owner };
        Assert.Throws<InvalidOperationException>(() => (map with { Objectives = objectives }).ToDefinition());
        var starts = map.Starts.ToArray();
        starts[0] = starts[0] with { BuildArea = new AxisAlignedBounds(new Vector3(-1, 0, 0), new Vector3(500, 0, 500)) };
        Assert.Throws<InvalidOperationException>(() => (map with { Starts = starts }).ToDefinition());
        var resources = map.Resources.ToArray();
        resources[0] = resources[0] with { HalfExtents = new Vector3(float.PositiveInfinity) };
        Assert.Throws<InvalidOperationException>(() => (map with { Resources = resources }).ToDefinition());
    }

    [Fact]
    public void DefinitionCopiesArtifactCollections()
    {
        var artifact = BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create());
        var definition = artifact.ToDefinition();
        var original = definition.Resources[0];
        artifact.Resources[0] = original with { TotalQuantity = original.TotalQuantity + 1 };
        artifact.TerrainVisual.MaterialAssetIds[0] = "changed";
        Assert.Equal(original, definition.Resources[0]);
        Assert.NotEqual("changed", definition.TerrainVisual.MaterialAssetIds[0]);
    }

    [Fact]
    public void InvalidExternalDataFailsBeforeTerrainAllocation()
    {
        var map = BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create());
        Assert.Throws<InvalidOperationException>(() => (map with { FormatVersion = 2 }).ToDefinition());
        Assert.Throws<InvalidOperationException>(() => (map with { Terrain = map.Terrain with { Version = 99 } }).ToDefinition());
        Assert.Throws<InvalidOperationException>(() => (map with { Terrain = map.Terrain with { RidgeAmplitude = double.NaN } }).ToDefinition());
        Assert.Throws<InvalidOperationException>(() => (map with { Terrain = map.Terrain with { Grid = new WorldGridSettings { HeightSamplesPerSide = 100_000 } } }).ToDefinition());
        Assert.Throws<InvalidOperationException>(() => BattlefieldMapArtifact.Deserialize(new byte[BattlefieldMapArtifact.MaximumInputBytes + 1]));
        Assert.Throws<InvalidOperationException>(() => BattlefieldMapArtifact.Deserialize("{\"formatVersion\":3,\"formatVersion\":3}"u8));
        var nodes = map.RoadNodes.ToArray();
        nodes[0] = nodes[0] with { Position = new Vector3(float.NaN, 0, 0) };
        Assert.Throws<InvalidOperationException>(() => (map with { RoadNodes = nodes }).ToDefinition());
        var edges = map.RoadEdges.ToArray();
        edges[0] = edges[0] with { DestinationNodeKey = "missing" };
        Assert.Throws<InvalidOperationException>(() => (map with { RoadEdges = edges }).ToDefinition());
    }
}
