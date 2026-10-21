using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Copied mineral/extractor review snapshots, without running economic production.
internal static class ResourceDepositReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.ResourceDepositsGallery => 12,
            VisualReferenceSceneKind.ResourceDepositSingle => 1,
            VisualReferenceSceneKind.ResourceDeposits100 => 100,
            VisualReferenceSceneKind.ResourceDeposits1000 => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        bool paired = count is 1 or 12;
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.ExtractorProduction, terrain, false, assets);
        Vector3 target = basis.Target;
        var map = CentralDivideBattlefield.Create();
        ResourceId[] resources = [ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Volatiles, ResourceIds.RareElements];
        var simulation = new SimulationCoordinator(seed: VisualReferenceScene.Seed);
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots));
        var footprint = DirectorateContent.CreateBuildingCatalog()[BuildingIds.Extractor].Footprint;
        for (int i = 0; i < count; i++)
        {
            int family = count == 12 ? i / 3 : i % 4;
            int state = count == 1 ? 0 : i % 3;
            var resource = map.Resources.First(item => item.ResourceId == resources[family]);
            Vector3 offset = count switch
            {
                1 => Vector3.Zero,
                12 => new((i % 3 - 1) * 80, 0, (i / 3 - 1.5f) * 64),
                100 => new((i % 10 - 4.5f) * 32, 0, (i / 10 - 4.5f) * 32),
                _ => new((i % 40 - 19.5f) * 32, 0, (i / 40 - 12) * 32)
            };
            Vector3 center = target + offset;
            if (!terrain.TrySampleHeight(center.X, center.Z, out float ground))
                throw new InvalidOperationException("Mineral review point lies outside terrain.");
            center.Y = ground;
            double remaining = state switch { 0 => resource.TotalQuantity, 1 => resource.TotalQuantity * .65, _ => 0 };
            var bounds = new AxisAlignedBounds(center - resource.HalfExtents, center + resource.HalfExtents);
            EntityId deposit = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(deposit, ResourceDeposit.Restore(resource.ResourceId, bounds,
                resource.TotalQuantity, remaining, resource.ExtractionRatePerSecond));
            simulation.Entities.AddComponent(deposit, new WorldTransform(center + Vector3.UnitY * resource.HalfExtents.Y,
                Quaternion.Identity, new(resource.HalfExtents.X * 1.2f, resource.HalfExtents.Y * 2, resource.HalfExtents.Z * 1.2f)));
            simulation.Entities.AddComponent(deposit, WorldPresentationIdentity.ForResource(resource.ResourceId));
            simulation.Entities.AddComponent(deposit, new VisualIdentity(4000));
            if (!paired) continue;
            EntityId extractor = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(extractor, new WorldTransform(center + Vector3.UnitY * footprint.Height * .5f,
                Quaternion.Identity, new(footprint.Width, footprint.Height, footprint.Depth)));
            simulation.Entities.AddComponent(extractor, new VisualIdentity(4001));
            simulation.Entities.AddComponent(extractor, new CompletedBuilding(BuildingIds.Extractor, new(1), SimulationTick.Zero));
        }
        simulation.RunTicks(VisualReferenceScene.FixedTick);
        if (!snapshots.TryReadLatest(out PresentationSnapshot extracted))
            throw new InvalidOperationException("Mineral review snapshot was not published.");
        var instances = extracted.Instances.ToArray();
        var snapshot = new PresentationSnapshot(extracted.Tick, extracted.TickDuration, extracted.SimulationEntityCount, instances,
            sessionId: extracted.SessionId, playerExperience: basis.Snapshot.PlayerExperience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, target, instances }, FingerprintOptions)));
        return new(kind, target, snapshot, default, fingerprint);
    }
}
