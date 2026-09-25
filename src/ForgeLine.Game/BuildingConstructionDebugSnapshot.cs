using System.Runtime.InteropServices;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct ConstructionSiteReadModel(
    EntityId Entity,
    BuildingId BuildingId,
    string BuildingKey,
    PlayerId Owner,
    AxisAlignedBounds Bounds,
    float Progress,
    uint ProgressTicks,
    uint RequiredTicks);

public readonly record struct CompletedBuildingReadModel(
    EntityId Entity,
    BuildingId BuildingId,
    string BuildingKey,
    PlayerId Owner,
    AxisAlignedBounds Bounds,
    SimulationTick CompletedAtTick);

public sealed class BuildingConstructionDebugSnapshot
{
    public BuildingConstructionDebugSnapshot(
        ReadOnlySpan<ConstructionSiteReadModel> sites,
        ReadOnlySpan<CompletedBuildingReadModel> completedBuildings)
    {
        Sites = sites.ToArray();
        CompletedBuildings = completedBuildings.ToArray();
    }

    public IReadOnlyList<ConstructionSiteReadModel> Sites { get; }

    public IReadOnlyList<CompletedBuildingReadModel> CompletedBuildings { get; }

    public static BuildingConstructionDebugSnapshot Capture(
        EntityRegistry entities,
        BuildingDefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(definitions);

        var sites = new List<ConstructionSiteReadModel>();
        foreach (EntityId entity in
                 entities.Query<ConstructionSite>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!TryResolveBounds(entities, entity, out AxisAlignedBounds bounds))
            {
                continue;
            }

            ConstructionSite site =
                entities.GetComponent<ConstructionSite>(entity);
            string key = definitions.TryGet(
                site.BuildingId,
                out BuildingDefinition? definition)
                ? definition.Key
                : site.BuildingId.ToString();

            sites.Add(
                new ConstructionSiteReadModel(
                    entity,
                    site.BuildingId,
                    key,
                    site.Owner,
                    bounds,
                    site.Progress,
                    site.ProgressTicks,
                    site.RequiredTicks));
        }

        var completed = new List<CompletedBuildingReadModel>();
        foreach (EntityId entity in
                 entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!TryResolveBounds(entities, entity, out AxisAlignedBounds bounds))
            {
                continue;
            }

            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(entity);
            string key = definitions.TryGet(
                building.BuildingId,
                out BuildingDefinition? definition)
                ? definition.Key
                : building.BuildingId.ToString();

            completed.Add(
                new CompletedBuildingReadModel(
                    entity,
                    building.BuildingId,
                    key,
                    building.Owner,
                    bounds,
                    building.CompletedAtTick));
        }

        return new BuildingConstructionDebugSnapshot(
            CollectionsMarshal.AsSpan(sites),
            CollectionsMarshal.AsSpan(completed));
    }

    private static bool TryResolveBounds(
        EntityRegistry entities,
        EntityId entity,
        out AxisAlignedBounds bounds)
    {
        if (!entities.TryGetComponent(entity, out WorldTransform transform) ||
            !entities.TryGetComponent(entity, out SpatialPresence presence))
        {
            bounds = default;
            return false;
        }

        bounds = presence.CreateEntry(entity, transform).Bounds;
        return true;
    }
}
