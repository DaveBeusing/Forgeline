using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;

namespace ForgeLine.Game;

public sealed class IntelligenceTargetAvailabilityPolicy :
    ITargetAvailabilityPolicy
{
    private readonly EntityRegistry _entities;
    private readonly FactionIntelligenceStore _intelligence;

    public IntelligenceTargetAvailabilityPolicy(
        EntityRegistry entities,
        FactionIntelligenceStore intelligence)
    {
        _entities = entities ??
            throw new ArgumentNullException(nameof(entities));
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
    }

    public bool IsTargetAvailable(
        EntityId observer,
        EntityId target)
    {
        if (!observer.IsValid ||
            !target.IsValid ||
            !_entities.IsAlive(observer) ||
            !_entities.IsAlive(target))
        {
            return false;
        }

        if (!TryGetObserverFaction(
                observer,
                out FactionId faction))
        {
            return false;
        }

        return _intelligence.IsEntityCurrentlyIdentified(
            faction,
            target);
    }

    private bool TryGetObserverFaction(
        EntityId observer,
        out FactionId faction)
    {
        if (_entities.TryGetComponent(
                observer,
                out IntelligenceSignature signature))
        {
            faction = signature.Faction;
            return faction.IsSpecified;
        }

        if (_entities.TryGetComponent(
                observer,
                out Combatant combatant))
        {
            faction = combatant.Faction;
            return faction.IsSpecified;
        }

        faction = FactionId.None;
        return false;
    }
}
