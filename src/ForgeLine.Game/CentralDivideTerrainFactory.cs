using ForgeLine.World;

namespace ForgeLine.Game;

// Authoring convenience. Productive sessions use the recipe from their compiled map.
public static class CentralDivideTerrainFactory
{
    public static TerrainWorld Create(BattlefieldDefinition definition, WorldGridSettings? settings = null) =>
        BattlefieldTerrainFactory.Create(definition, BattlefieldTerrainRecipe.CentralDivide() with
        {
            Grid = settings ?? new WorldGridSettings()
        });
}
