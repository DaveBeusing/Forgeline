using System.Diagnostics.CodeAnalysis;
using ForgeLine.Core;

namespace ForgeLine.Economy;

public readonly record struct RecipeId(uint Value) : IComparable<RecipeId>
{
    public static RecipeId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(RecipeId other) => Value.CompareTo(other.Value);

    public static bool operator <(RecipeId left, RecipeId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(RecipeId left, RecipeId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(RecipeId left, RecipeId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(RecipeId left, RecipeId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class RecipeIds
{
    public static readonly RecipeId Steel = new(1);

    public static readonly RecipeId Fuel = new(2);

    public static readonly RecipeId Electronics = new(3);

    public static readonly RecipeId Ammunition = new(4);
}

[Flags]
public enum ProductionCapability : uint
{
    None = 0,
    SteelProcessing = 1 << 0,
    FuelProcessing = 1 << 1,
    ElectronicsProcessing = 1 << 2,
    AmmunitionProcessing = 1 << 3
}

public readonly record struct ProductionIngredient
{
    public ProductionIngredient(ResourceId resourceId, double quantity)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Production ingredients require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(quantity) || quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ResourceId = resourceId;
        Quantity = quantity;
    }

    public ResourceId ResourceId { get; }

    public double Quantity { get; }
}

public sealed record ProductionRecipeDefinition
{
    public required RecipeId Id { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<ProductionIngredient> Inputs { get; init; }

    public required IReadOnlyList<ProductionIngredient> Outputs { get; init; }

    public required uint DurationTicks { get; init; }

    public required ProductionCapability RequiredCapability { get; init; }

    public double MinimumPowerFraction { get; init; } = 1.0;

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new InvalidOperationException(
                "Production recipes require a stable ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (DurationTicks == 0)
        {
            throw new InvalidOperationException(
                $"Production recipe '{Key}' requires at least one simulation tick.");
        }

        uint capability = (uint)RequiredCapability;
        const uint supportedCapabilities =
            (uint)(ProductionCapability.SteelProcessing |
                   ProductionCapability.FuelProcessing |
                   ProductionCapability.ElectronicsProcessing |
                   ProductionCapability.AmmunitionProcessing);

        if (capability == 0 ||
            (capability & supportedCapabilities) != capability ||
            (capability & (capability - 1)) != 0)
        {
            throw new InvalidOperationException(
                $"Production recipe '{Key}' requires exactly one supported facility capability.");
        }

        if (!double.IsFinite(MinimumPowerFraction) ||
            MinimumPowerFraction < 0.0 ||
            MinimumPowerFraction > 1.0)
        {
            throw new InvalidOperationException(
                $"Production recipe '{Key}' has an invalid minimum power fraction.");
        }

        ValidateIngredients(Inputs, nameof(Inputs));
        ValidateIngredients(Outputs, nameof(Outputs));
    }

    private static void ValidateIngredients(
        IReadOnlyList<ProductionIngredient> ingredients,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(ingredients);

        if (ingredients.Count == 0)
        {
            throw new InvalidOperationException(
                $"Production recipe '{propertyName}' must contain at least one resource.");
        }

        var resourceIds = new HashSet<ResourceId>();
        for (int index = 0; index < ingredients.Count; index++)
        {
            if (!resourceIds.Add(ingredients[index].ResourceId))
            {
                throw new InvalidOperationException(
                    $"Production recipe '{propertyName}' contains duplicate resources.");
            }
        }
    }
}

public sealed class ProductionRecipeCatalog
{
    private readonly Dictionary<RecipeId, ProductionRecipeDefinition> _byId;
    private readonly Dictionary<string, RecipeId> _byKey;

    public ProductionRecipeCatalog(
        IEnumerable<ProductionRecipeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byId = new Dictionary<RecipeId, ProductionRecipeDefinition>();
        _byKey = new Dictionary<string, RecipeId>(StringComparer.Ordinal);

        foreach (ProductionRecipeDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate production recipe ID '{definition.Id}'.",
                    nameof(definitions));
            }

            if (!_byKey.TryAdd(definition.Key, definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate production recipe key '{definition.Key}'.",
                    nameof(definitions));
            }
        }
    }

    public int Count => _byId.Count;

    public IEnumerable<ProductionRecipeDefinition> Definitions =>
        _byId.OrderBy(static pair => pair.Key).Select(static pair => pair.Value);

    public ProductionRecipeDefinition this[RecipeId id] =>
        _byId.TryGetValue(id, out ProductionRecipeDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown production recipe ID '{id}'.");

    public bool TryGet(
        RecipeId id,
        [NotNullWhen(true)] out ProductionRecipeDefinition? definition) =>
        _byId.TryGetValue(id, out definition);

    public bool TryResolve(string key, out RecipeId id)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(key, out id);
    }
}

public static class InitialProductionRecipes
{
    public static ProductionRecipeCatalog CreateCatalog()
    {
        return new ProductionRecipeCatalog(
            [
                new ProductionRecipeDefinition
                {
                    Id = RecipeIds.Steel,
                    Key = "recipe.steel",
                    DisplayName = "Steel",
                    Inputs =
                    [
                        new ProductionIngredient(ResourceIds.FerrousOre, 10.0)
                    ],
                    Outputs =
                    [
                        new ProductionIngredient(ResourceIds.Steel, 10.0)
                    ],
                    DurationTicks = 40,
                    RequiredCapability = ProductionCapability.SteelProcessing,
                    MinimumPowerFraction = 1.0
                },
                new ProductionRecipeDefinition
                {
                    Id = RecipeIds.Fuel,
                    Key = "recipe.fuel",
                    DisplayName = "Fuel",
                    Inputs =
                    [
                        new ProductionIngredient(ResourceIds.Volatiles, 10.0)
                    ],
                    Outputs =
                    [
                        new ProductionIngredient(ResourceIds.Fuel, 10.0)
                    ],
                    DurationTicks = 30,
                    RequiredCapability = ProductionCapability.FuelProcessing,
                    MinimumPowerFraction = 1.0
                },
                new ProductionRecipeDefinition
                {
                    Id = RecipeIds.Electronics,
                    Key = "recipe.electronics",
                    DisplayName = "Electronics",
                    Inputs =
                    [
                        new ProductionIngredient(ResourceIds.Silicates, 10.0)
                    ],
                    Outputs =
                    [
                        new ProductionIngredient(ResourceIds.Electronics, 10.0)
                    ],
                    DurationTicks = 50,
                    RequiredCapability = ProductionCapability.ElectronicsProcessing,
                    MinimumPowerFraction = 1.0
                },
                new ProductionRecipeDefinition
                {
                    Id = RecipeIds.Ammunition,
                    Key = "recipe.ammunition",
                    DisplayName = "Ammunition",
                    Inputs =
                    [
                        new ProductionIngredient(ResourceIds.Steel, 6.0),
                        new ProductionIngredient(ResourceIds.Electronics, 2.0)
                    ],
                    Outputs =
                    [
                        new ProductionIngredient(ResourceIds.Ammunition, 10.0)
                    ],
                    DurationTicks = 40,
                    RequiredCapability = ProductionCapability.AmmunitionProcessing,
                    MinimumPowerFraction = 1.0
                }
            ]);
    }
}
