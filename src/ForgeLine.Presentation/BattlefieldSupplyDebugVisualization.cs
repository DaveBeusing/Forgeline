using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class BattlefieldSupplyDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        BattlefieldSupplyDebugSnapshot snapshot,
        int maximumProviders = 64,
        int maximumUnits = 128,
        int maximumLabels = 20)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumProviders);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumUnits);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int labels = 0;
        int providerCount =
            Math.Min(
                snapshot.Providers.Count,
                maximumProviders);

        for (int index = 0; index < providerCount; index++)
        {
            BattlefieldSupplyProviderReadModel provider =
                snapshot.Providers[index];

            Vector4 color =
                provider.IsDepot
                    ? new Vector4(0.2f, 0.85f, 1.0f, 1.0f)
                    : new Vector4(0.25f, 1.0f, 0.45f, 1.0f);

            Vector3 center =
                provider.WorldPosition +
                Vector3.UnitY * 0.15f;

            debugDraw.Circle(
                center,
                provider.ResupplyRangeMeters,
                color,
                segments: 24);
            debugDraw.Point(
                provider.WorldPosition + Vector3.UnitY * 1.0f,
                2.5f,
                color);

            if (labels < maximumLabels)
            {
                string kind =
                    provider.IsDepot
                        ? "DEPOT"
                        : provider.IsTruck
                            ? "TRUCK"
                            : "PROVIDER";
                string fuel =
                    provider.FuelQuantity.ToString(
                        "F0",
                        System.Globalization.CultureInfo.InvariantCulture);
                string ammunition =
                    provider.AmmunitionQuantity.ToString(
                        "F0",
                        System.Globalization.CultureInfo.InvariantCulture);

                debugDraw.Label(
                    provider.WorldPosition + Vector3.UnitY * 2.0f,
                    $"SUPPLY {kind} F={fuel} A={ammunition}",
                    color);
                labels++;
            }
        }

        int unitCount =
            Math.Min(
                snapshot.Units.Count,
                maximumUnits);

        for (int index = 0; index < unitCount; index++)
        {
            BattlefieldSupplyUnitReadModel unit =
                snapshot.Units[index];

            Vector4 color =
                GetStatusColor(unit.Status);

            if (unit.Status != BattlefieldSupplyStatus.Supplied)
            {
                debugDraw.Point(
                    unit.WorldPosition + Vector3.UnitY * 1.25f,
                    2.0f,
                    color);
            }

            if (unit.ResupplyProvider.IsValid &&
                TryFindProvider(
                    snapshot,
                    unit.ResupplyProvider,
                    out BattlefieldSupplyProviderReadModel provider))
            {
                debugDraw.Line(
                    unit.WorldPosition + Vector3.UnitY * 1.0f,
                    provider.WorldPosition + Vector3.UnitY * 1.0f,
                    color);
            }

            if (labels >= maximumLabels ||
                unit.Status == BattlefieldSupplyStatus.Supplied)
            {
                continue;
            }

            string fuel =
                (unit.FuelFraction * 100.0).ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture);
            string ammunition =
                (unit.AmmunitionFraction * 100.0).ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture);

            debugDraw.Label(
                unit.WorldPosition + Vector3.UnitY * 2.0f,
                $"{unit.Status} F={fuel}% A={ammunition}%",
                color);
            labels++;
        }
    }

    private static bool TryFindProvider(
        BattlefieldSupplyDebugSnapshot snapshot,
        EntityId entity,
        out BattlefieldSupplyProviderReadModel provider)
    {
        for (int index = 0; index < snapshot.Providers.Count; index++)
        {
            BattlefieldSupplyProviderReadModel candidate =
                snapshot.Providers[index];

            if (candidate.Entity == entity)
            {
                provider = candidate;
                return true;
            }
        }

        provider = default;
        return false;
    }

    private static Vector4 GetStatusColor(
        BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.LowSupply =>
                new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
            BattlefieldSupplyStatus.Critical =>
                new Vector4(1.0f, 0.45f, 0.1f, 1.0f),
            BattlefieldSupplyStatus.Unsupplied =>
                new Vector4(1.0f, 0.15f, 0.15f, 1.0f),
            _ =>
                new Vector4(0.2f, 1.0f, 0.45f, 1.0f)
        };
}
