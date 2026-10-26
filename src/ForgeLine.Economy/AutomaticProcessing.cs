namespace ForgeLine.Economy;

/// <summary>Single-purpose raw-material processors run continuously in normal matches.</summary>
public static class AutomaticProcessing
{
    public static RecipeId RecipeFor(ProductionCapability capabilities) => capabilities switch
    {
        ProductionCapability.SteelProcessing => RecipeIds.Steel,
        ProductionCapability.FuelProcessing => RecipeIds.Fuel,
        ProductionCapability.ElectronicsProcessing => RecipeIds.Electronics,
        _ => RecipeId.None
    };
}
