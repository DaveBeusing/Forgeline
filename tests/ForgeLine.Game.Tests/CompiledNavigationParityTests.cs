using System.Buffers.Binary;
using System.Security.Cryptography;
using ForgeLine.Navigation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CompiledNavigationParityTests
{
    // Complete cell samples, flags and three movement-class costs from master cb58c3ad.
    [Theory]
    [InlineData(16f, "", "0ee76deadf24346257c196939b59cdbfe07f0662a54ead26e38617132470c8d5")]
    [InlineData(16f, "crossing.north_bridge", "7929579eb20e1c36042d68a88a6ec7581626b9e809c72b5e65538c1d0a7293f2")]
    [InlineData(16f, "crossing.south_ford", "0c08f9cd9d1b37abd4145faff17d97a5e938b90d4f69c01d6e0825511690e43c")]
    [InlineData(32f, "", "2a9f76d07e4dde6285dec7d85283c68bdfb3ba9ac8056c8fe20f48dc9539111a")]
    [InlineData(32f, "crossing.north_bridge", "6299f13648c4041765a4c318d9b824e0cdc9b49c0a90ce852d7b605e910f0871")]
    [InlineData(32f, "crossing.south_ford", "b0e937cfaa78c216cdc93a1a0ad72ae77f8e3e3151f39db17e7cf9220ceadc5c")]
    public void LoadedTerrainAndObstaclesPreserveEveryNavigationCell(float cellSize, string closedCrossing, string expected)
    {
        var composition = ScenarioRegistry.Default.Resolve(CentralDivideScenario.CompositionKey);
        var terrain = composition.CreateTerrain(composition.Battlefield);
        IReadOnlyDictionary<string, bool>? availability = closedCrossing.Length == 0 ? null :
            new Dictionary<string, bool> { [closedCrossing] = false };
        var grid = NavigationWorld.Build(terrain, composition.Battlefield.CreateNavigationObstacles(availability),
            new NavigationGridSettings { CellSizeMeters = cellSize, StaticObstacleClearanceMeters = 0.5f }).Grid;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[25];
        NavigationMovementClass[] movements = [NavigationMovementClass.Infantry, NavigationMovementClass.Wheeled, NavigationMovementClass.Tracked];
        for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coordinate = new NavigationCellCoordinate(x, z);
                var cell = grid.GetCell(coordinate);
                BinaryPrimitives.WriteSingleLittleEndian(bytes, cell.Height);
                BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], cell.SlopeDegrees);
                bytes[8] = cell.StaticBlocked ? (byte)1 : (byte)0;
                bytes[9] = cell.HasTerrain ? (byte)1 : (byte)0;
                int offset = 10;
                foreach (var movement in movements)
                {
                    var capabilities = NavigationCapabilities.For(movement);
                    bytes[offset++] = grid.IsTraversable(coordinate, capabilities) ? (byte)1 : (byte)0;
                    BinaryPrimitives.WriteSingleLittleEndian(bytes[offset..], grid.GetTraversalCost(coordinate, capabilities));
                    offset += 4;
                }
                hash.AppendData(bytes);
            }
        Assert.Equal(expected, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
