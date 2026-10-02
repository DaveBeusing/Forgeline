using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PlayerTacticalActionOperation : byte
{
    Attack = 1,
    AttackMove = 2,
    Stop = 3,
    HoldPosition = 4,
    Retreat = 5,
    FireMissionCoordinate = 6,
    FireMissionContact = 7,
    CancelFireMission = 8,
    RetreatToRecovery = 9
}

public enum PlayerTacticalActionFailureReason : byte
{
    None = 0,
    NoEligibleUnits = 1,
    TargetUnavailable = 2,
    TargetNotIdentified = 3,
    FriendlyTarget = 4,
    TargetNotTargetable = 5,
    UnsupportedTargetClass = 6,
    NoEligibleArtillery = 7,
    ArtilleryTargetUnavailable = 8,
    ArtilleryOutOfRange = 9,
    NoRecoveryProvider = 10
}

public sealed class PlayerTacticalActionCommand : ISimulationCommand
{
    private readonly EntityId[] _units;
    private readonly FactionIntelligenceStore _intelligence;
    private readonly WeaponCatalog _weapons;
    private readonly ArtilleryWeaponCatalog _artilleryWeapons;

    private PlayerTacticalActionCommand(
        PlayerTacticalActionOperation operation,
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        EntityId target,
        Vector3 worldTarget,
        IntelligenceContactKey contactKey,
        FormationTemplate formation,
        int requestedRounds,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (units.IsEmpty)
        {
            throw new ArgumentException(
                "Player tactical actions require at least one selected entity.",
                nameof(units));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        if (operation is
                PlayerTacticalActionOperation.AttackMove or
                PlayerTacticalActionOperation.Retreat or
                PlayerTacticalActionOperation.FireMissionCoordinate)
        {
            TacticalCommandUtilities.ValidatePosition(
                worldTarget,
                nameof(worldTarget));
        }

        if (operation == PlayerTacticalActionOperation.Attack &&
            !target.IsValid)
        {
            throw new ArgumentException(
                "Attack actions require a target entity.",
                nameof(target));
        }

        if (operation == PlayerTacticalActionOperation.FireMissionContact &&
            !contactKey.IsSpecified)
        {
            throw new ArgumentException(
                "Contact fire missions require a contact key.",
                nameof(contactKey));
        }

        if (operation is
                PlayerTacticalActionOperation.FireMissionCoordinate or
                PlayerTacticalActionOperation.FireMissionContact)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                requestedRounds,
                1);
        }

        Operation = operation;
        Issuer = issuer;
        Target = target;
        WorldTarget = worldTarget;
        ContactKey = contactKey;
        Formation = formation;
        RequestedRounds = requestedRounds;
        SubmittedAtTick = submittedAtTick;
        _units = units.ToArray();
        _intelligence =
            intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
        _weapons =
            weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _artilleryWeapons =
            artilleryWeapons ??
            throw new ArgumentNullException(nameof(artilleryWeapons));
    }

    public PlayerTacticalActionOperation Operation { get; }

    public PlayerId Issuer { get; }

    public EntityId Target { get; }

    public Vector3 WorldTarget { get; }

    public IntelligenceContactKey ContactKey { get; }

    public FormationTemplate Formation { get; }

    public int RequestedRounds { get; }

    public SimulationTick SubmittedAtTick { get; }

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public PlayerTacticalActionFailureReason FailureReason { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public static PlayerTacticalActionCommand Attack(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        EntityId target,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.Attack,
            issuer,
            units,
            target,
            Vector3.Zero,
            IntelligenceContactKey.None,
            FormationTemplate.Compact,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand AttackMove(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        Vector3 destination,
        SimulationTick submittedAtTick,
        FormationTemplate formation,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.AttackMove,
            issuer,
            units,
            EntityId.Invalid,
            destination,
            IntelligenceContactKey.None,
            formation,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand Stop(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.Stop,
            issuer,
            units,
            EntityId.Invalid,
            Vector3.Zero,
            IntelligenceContactKey.None,
            FormationTemplate.Compact,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand HoldPosition(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.HoldPosition,
            issuer,
            units,
            EntityId.Invalid,
            Vector3.Zero,
            IntelligenceContactKey.None,
            FormationTemplate.Compact,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand Retreat(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        Vector3 destination,
        SimulationTick submittedAtTick,
        FormationTemplate formation,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.Retreat,
            issuer,
            units,
            EntityId.Invalid,
            destination,
            IntelligenceContactKey.None,
            formation,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand RetreatToRecovery(
        PlayerId issuer,
        ReadOnlySpan<EntityId> units,
        SimulationTick submittedAtTick,
        FormationTemplate formation,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.RetreatToRecovery,
            issuer,
            units,
            EntityId.Invalid,
            Vector3.Zero,
            IntelligenceContactKey.None,
            formation,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand FireMissionCoordinate(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery,
        Vector3 coordinate,
        int requestedRounds,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.FireMissionCoordinate,
            issuer,
            artillery,
            EntityId.Invalid,
            coordinate,
            IntelligenceContactKey.None,
            FormationTemplate.Compact,
            requestedRounds,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand FireMissionContact(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery,
        IntelligenceContactKey contactKey,
        int requestedRounds,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.FireMissionContact,
            issuer,
            artillery,
            EntityId.Invalid,
            Vector3.Zero,
            contactKey,
            FormationTemplate.Compact,
            requestedRounds,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public static PlayerTacticalActionCommand CancelFireMission(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery,
        SimulationTick submittedAtTick,
        FactionIntelligenceStore intelligence,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons) =>
        new(
            PlayerTacticalActionOperation.CancelFireMission,
            issuer,
            artillery,
            EntityId.Invalid,
            Vector3.Zero,
            IntelligenceContactKey.None,
            FormationTemplate.Compact,
            0,
            submittedAtTick,
            intelligence,
            weapons,
            artilleryWeapons);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AcceptedTargetCount = 0;
        RejectedTargetCount = 0;
        FailureReason =
            PlayerTacticalActionFailureReason.None;
        ExecutedAtTick = context.Tick;

        switch (Operation)
        {
            case PlayerTacticalActionOperation.Attack:
                ExecuteAttack(context);
                break;

            case PlayerTacticalActionOperation.AttackMove:
                ExecuteAttackMove(context);
                break;

            case PlayerTacticalActionOperation.Stop:
                ExecuteStop(context);
                break;

            case PlayerTacticalActionOperation.HoldPosition:
                ExecuteHold(context);
                break;

            case PlayerTacticalActionOperation.Retreat:
                ExecuteRetreat(context);
                break;

            case PlayerTacticalActionOperation.RetreatToRecovery:
                ExecuteRetreatToRecovery(context);
                break;

            case PlayerTacticalActionOperation.FireMissionCoordinate:
            case PlayerTacticalActionOperation.FireMissionContact:
                ExecuteFireMission(context);
                break;

            case PlayerTacticalActionOperation.CancelFireMission:
                ExecuteCancelFireMission(context);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported tactical operation '{Operation}'.");
        }
    }

    private void ExecuteAttack(SimulationContext context)
    {
        if (!TryResolveIssuerFaction(out FactionId faction) ||
            !context.Entities.IsAlive(Target))
        {
            RejectAll(
                PlayerTacticalActionFailureReason.TargetUnavailable);
            return;
        }

        if (!_intelligence.TryResolveCurrentlyIdentifiedEntity(
                faction,
                IntelligenceContactKey.FromEntity(Target),
                out EntityId identified) ||
            identified != Target)
        {
            RejectAll(
                PlayerTacticalActionFailureReason.TargetNotIdentified);
            return;
        }

        if (!context.Entities.TryGetComponent(
                Target,
                out Combatant targetCombatant))
        {
            RejectAll(
                PlayerTacticalActionFailureReason.TargetNotTargetable);
            return;
        }

        if (targetCombatant.Faction == faction)
        {
            RejectAll(
                PlayerTacticalActionFailureReason.FriendlyTarget);
            return;
        }

        if (!context.Entities.TryGetComponent(
                Target,
                out Targetable targetable) ||
            !context.Entities.TryGetComponent(
                Target,
                out HealthState health) ||
            health.IsDepleted ||
            !context.Entities.HasComponent<WorldTransform>(Target))
        {
            RejectAll(
                PlayerTacticalActionFailureReason.TargetNotTargetable);
            return;
        }

        List<EntityId> owned =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out _);
        var compatible =
            new List<EntityId>(owned.Count);

        for (int index = 0;
             index < owned.Count;
             index++)
        {
            EntityId entity =
                owned[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out WeaponState weaponState) ||
                !_weapons.TryGet(
                    weaponState.WeaponId,
                    out WeaponDefinition? weapon) ||
                weapon is null ||
                !weapon.Effectiveness.CanEngage(
                    targetable.Class))
            {
                continue;
            }

            compatible.Add(entity);
        }

        if (compatible.Count == 0)
        {
            RejectAll(
                owned.Count == 0
                    ? PlayerTacticalActionFailureReason.NoEligibleUnits
                    : PlayerTacticalActionFailureReason.UnsupportedTargetClass);
            return;
        }

        var command =
            new AttackCommand(
                Issuer,
                compatible.ToArray(),
                Target,
                SubmittedAtTick);
        command.Execute(context);

        AcceptedTargetCount =
            command.AcceptedTargetCount;
        RejectedTargetCount =
            _units.Length -
            AcceptedTargetCount;

        if (AcceptedTargetCount == 0)
        {
            FailureReason =
                PlayerTacticalActionFailureReason.NoEligibleUnits;
        }
    }

    private void ExecuteAttackMove(
        SimulationContext context)
    {
        var command =
            new AttackMoveCommand(
                Issuer,
                _units,
                WorldTarget,
                SubmittedAtTick,
                Formation);
        command.Execute(context);
        CopyCombatResult(
            command.AcceptedTargetCount,
            command.RejectedTargetCount);
    }

    private void ExecuteStop(
        SimulationContext context)
    {
        var command =
            new StopCombatCommand(
                Issuer,
                _units,
                SubmittedAtTick);
        command.Execute(context);
        CopyCombatResult(
            command.AcceptedTargetCount,
            command.RejectedTargetCount);
    }

    private void ExecuteHold(
        SimulationContext context)
    {
        var command =
            new HoldPositionCommand(
                Issuer,
                _units,
                SubmittedAtTick);
        command.Execute(context);
        CopyCombatResult(
            command.AcceptedTargetCount,
            command.RejectedTargetCount);
    }

    private void ExecuteRetreat(
        SimulationContext context)
    {
        var command =
            new RetreatCommand(
                Issuer,
                _units,
                WorldTarget,
                SubmittedAtTick,
                Formation);
        command.Execute(context);
        CopyCombatResult(
            command.AcceptedTargetCount,
            command.RejectedTargetCount);
    }

    private void ExecuteRetreatToRecovery(
        SimulationContext context)
    {
        List<EntityId> owned =
            TacticalCommandUtilities.FilterOwnedCombatUnits(
                context,
                Issuer,
                _units,
                out _);

        if (owned.Count == 0)
        {
            RejectAll(
                PlayerTacticalActionFailureReason.NoEligibleUnits);
            return;
        }

        if (!RetreatRecoveryPlanner.TryResolve(
                context,
                Issuer,
                owned,
                out EntityId provider,
                out Vector3 destination,
                out RetreatRecoveryReason reason))
        {
            RejectAll(
                PlayerTacticalActionFailureReason.NoRecoveryProvider);
            return;
        }

        var command =
            new RetreatCommand(
                Issuer,
                owned.ToArray(),
                destination,
                SubmittedAtTick,
                Formation);
        command.Execute(context);

        AcceptedTargetCount =
            command.AcceptedTargetCount;
        RejectedTargetCount =
            _units.Length -
            AcceptedTargetCount;

        for (int index = 0;
             index < owned.Count;
             index++)
        {
            EntityId entity =
                owned[index];

            var recovery =
                new RetreatRecoveryState(
                    provider,
                    reason,
                    destination,
                    context.Tick);

            if (context.Entities.HasComponent<RetreatRecoveryState>(
                    entity))
            {
                context.Entities.SetComponent(
                    entity,
                    recovery);
            }
            else
            {
                context.Entities.AddComponent(
                    entity,
                    recovery);
            }
        }
    }

    private void ExecuteFireMission(
        SimulationContext context)
    {
        if (!TryResolveIssuerFaction(
                out FactionId faction) ||
            !TryResolveArtilleryTarget(
                faction,
                out Vector3 targetPosition))
        {
            RejectAll(
                PlayerTacticalActionFailureReason.ArtilleryTargetUnavailable);
            return;
        }

        var accepted =
            new List<EntityId>(_units.Length);
        int ownedArtillery = 0;

        for (int index = 0;
             index < _units.Length;
             index++)
        {
            EntityId entity =
                _units[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != Issuer ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ArtilleryCapability capability) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform) ||
                !_artilleryWeapons.TryGet(
                    capability.WeaponId,
                    out ArtilleryWeaponDefinition? definition) ||
                definition is null)
            {
                continue;
            }

            ownedArtillery++;

            if (!definition.IsInRange(
                    transform.Position,
                    targetPosition))
            {
                continue;
            }

            accepted.Add(entity);
        }

        if (accepted.Count == 0)
        {
            RejectAll(
                ownedArtillery == 0
                    ? PlayerTacticalActionFailureReason.NoEligibleArtillery
                    : PlayerTacticalActionFailureReason.ArtilleryOutOfRange);
            return;
        }

        FireMissionCommand command =
            Operation ==
            PlayerTacticalActionOperation.FireMissionContact
                ? new FireMissionCommand(
                    Issuer,
                    accepted.ToArray(),
                    ContactKey,
                    RequestedRounds,
                    SubmittedAtTick)
                : new FireMissionCommand(
                    Issuer,
                    accepted.ToArray(),
                    WorldTarget,
                    RequestedRounds,
                    SubmittedAtTick);

        command.Execute(context);

        AcceptedTargetCount =
            command.AcceptedTargetCount;
        RejectedTargetCount =
            _units.Length -
            AcceptedTargetCount;
    }

    private void ExecuteCancelFireMission(
        SimulationContext context)
    {
        var command =
            new CancelFireMissionCommand(
                Issuer,
                _units);
        command.Execute(context);

        AcceptedTargetCount =
            command.CancelledTargetCount;
        RejectedTargetCount =
            _units.Length -
            AcceptedTargetCount;

        if (AcceptedTargetCount == 0)
        {
            FailureReason =
                PlayerTacticalActionFailureReason.NoEligibleArtillery;
        }
    }

    private bool TryResolveArtilleryTarget(
        FactionId faction,
        out Vector3 targetPosition)
    {
        if (Operation ==
            PlayerTacticalActionOperation.FireMissionContact)
        {
            if (_intelligence.TryGetContact(
                    faction,
                    ContactKey,
                    out IntelligenceContact contact))
            {
                targetPosition =
                    contact.LastKnownPosition;
                return true;
            }

            targetPosition = default;
            return false;
        }

        VisibilityCellCoordinate cell =
            _intelligence.WorldToCell(
                WorldTarget);

        if (_intelligence.GetTerrainState(
                faction,
                cell) !=
            IntelligenceState.Visible)
        {
            targetPosition = default;
            return false;
        }

        targetPosition =
            WorldTarget;
        return true;
    }

    private bool TryResolveIssuerFaction(
        out FactionId faction)
    {
        if (Issuer.Value == 0 ||
            Issuer.Value > uint.MaxValue)
        {
            faction = FactionId.None;
            return false;
        }

        faction =
            new FactionId(
                (uint)Issuer.Value);
        return faction.IsSpecified;
    }

    private void CopyCombatResult(
        int accepted,
        int rejected)
    {
        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;

        if (accepted == 0)
        {
            FailureReason =
                PlayerTacticalActionFailureReason.NoEligibleUnits;
        }
    }

    private void RejectAll(
        PlayerTacticalActionFailureReason failure)
    {
        AcceptedTargetCount = 0;
        RejectedTargetCount =
            _units.Length;
        FailureReason = failure;
    }
}
