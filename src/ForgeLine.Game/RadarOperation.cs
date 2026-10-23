using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Intelligence;

namespace ForgeLine.Game;

public static class RadarOperation
{
    public const double MinimumOperationalScale = 1.0;

    public static bool IsOperational(EntityRegistry entities, EntityId sensor)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!entities.IsAlive(sensor) || !entities.HasComponent<RadarSensorState>(sensor))
            return false;

        if (!entities.TryGetComponent(sensor, out PowerConsumer consumer))
        {
            // Completed radar structures fail closed if their economic binding is missing.
            // Bare mobile sensors retain the existing independent sensor contract.
            return !entities.HasComponent<CompletedBuilding>(sensor);
        }

        return consumer.Enabled &&
            entities.TryGetComponent(sensor, out PowerNetworkMembership membership) &&
            membership.NetworkId.IsSpecified &&
            consumer.OperationalScale >= MinimumOperationalScale;
    }
}
