using ForgeLine.Core;

namespace ForgeLine.Presentation;

/// <summary>Catalog inputs for additional steps, after any currently reserved step.</summary>
public sealed class PlayerProductionRequirement
{
    public PlayerProductionRequirement(byte kind, uint id, string name, IReadOnlyList<PlayerActionResourceAmount> inputs)
    {
        Kind = kind; Id = id; Name = name;
        Inputs = Array.AsReadOnly(inputs.ToArray());
    }
    public byte Kind { get; }
    public uint Id { get; }
    public string Name { get; }
    public IReadOnlyList<PlayerActionResourceAmount> Inputs { get; }
}

public readonly record struct ProductionStockThreshold(ResourceId Resource, double Minimum, double Target, double Maximum,
    double Available, double Announced, double Missing);

/// <summary>Pure copied-data preview; never creates policies, shipments or inventory.</summary>
public sealed record ProductionStockProposal(bool FitsCapacity, string Reason, int Steps,
    IReadOnlyList<ProductionStockThreshold> Thresholds)
{
    public static ProductionStockProposal Create(PlayerLogisticsActionReadModel logistics, PlayerProductionRequirement requirement, int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(steps, 100);
        var thresholds = new List<ProductionStockThreshold>();
        double additional = 0;
        foreach (var group in requirement.Inputs.GroupBy(i => i.ResourceId))
        {
            var policy = logistics.Policies.FirstOrDefault(p => p.ResourceId == group.Key);
            double required = group.Sum(i => i.RequiredQuantity) * steps;
            double target = required + policy.ReservedQuantity;
            double missing = Math.Max(0, required - policy.AvailableQuantity - policy.AnnouncedQuantity);
            additional += missing;
            thresholds.Add(new(group.Key, target, target, target, policy.AvailableQuantity, policy.AnnouncedQuantity, missing));
        }
        double committedSpace = logistics.InventoryQuantity + logistics.Policies.Sum(p => p.AnnouncedQuantity) + additional;
        bool fits = thresholds.Count > 0 && committedSpace <= logistics.InventoryCapacity + 1e-9;
        return new(fits, fits ? "PREVIEW ONLY - APPLY EACH RESOURCE" : "SHARED STORAGE FULL - CONSUME OR REMOVE STOCK", steps,
            Array.AsReadOnly(thresholds.ToArray()));
    }
}
