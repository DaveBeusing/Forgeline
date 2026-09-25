using ForgeLine.Core;

namespace ForgeLine.Economy;

public static class InitialResourceDefinitions
{
    public static ResourceCatalog CreateCatalog()
    {
        return new ResourceCatalog(
            [
                new ResourceDefinition
                {
                    Id = ResourceIds.FerrousOre,
                    Key = "resource.ferrous_ore",
                    DisplayName = "Ferrous Ore",
                    DefaultExtractionRatePerSecond = 10.0,
                    DefaultRichness = 1.0
                },
                new ResourceDefinition
                {
                    Id = ResourceIds.Volatiles,
                    Key = "resource.volatiles",
                    DisplayName = "Volatiles",
                    DefaultExtractionRatePerSecond = 8.0,
                    DefaultRichness = 1.0
                },
                new ResourceDefinition
                {
                    Id = ResourceIds.Silicates,
                    Key = "resource.silicates",
                    DisplayName = "Silicates",
                    DefaultExtractionRatePerSecond = 12.0,
                    DefaultRichness = 1.0
                }
            ]);
    }
}
