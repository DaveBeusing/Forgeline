using System.Numerics;
using ForgeLine.Economy;

namespace ForgeLine.Presentation;

public static class ResourceDepositDebugVisualization
{
    public static void DrawDeposits(
        DebugDraw debugDraw,
        ResourceExtractionDebugSnapshot snapshot,
        Vector4 availableColor,
        Vector4 depletedColor,
        int maximumDeposits = 256,
        int maximumLabels = 32)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDeposits);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int depositCount = Math.Min(
            snapshot.Deposits.Count,
            maximumDeposits);

        for (int index = 0; index < depositCount; index++)
        {
            ResourceDepositReadModel deposit = snapshot.Deposits[index];
            Vector4 color =
                deposit.State == ResourceDepositState.Depleted
                    ? depletedColor
                    : availableColor;

            debugDraw.Box(deposit.Bounds, color);

            if (index >= maximumLabels)
            {
                continue;
            }

            string label = FormattableString.Invariant(
                $"{deposit.ResourceKey} {deposit.RemainingQuantity:F1}/{deposit.TotalQuantity:F1} r={deposit.Richness:F2}");

            debugDraw.Label(
                deposit.Bounds.Center,
                label,
                color);
        }
    }
}
