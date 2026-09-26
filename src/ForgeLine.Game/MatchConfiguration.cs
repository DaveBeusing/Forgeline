using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record MatchParticipantConfiguration
{
    public MatchParticipantConfiguration(
        PlayerId player,
        FactionId faction,
        int startIndex,
        bool isComputerControlled)
    {
        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        if (!faction.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(faction));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

        Player = player;
        Faction = faction;
        StartIndex = startIndex;
        IsComputerControlled = isComputerControlled;
    }

    public PlayerId Player { get; }

    public FactionId Faction { get; }

    public int StartIndex { get; }

    public bool IsComputerControlled { get; }
}

public sealed class MatchConfiguration
{
    private readonly MatchParticipantConfiguration[] _participants;

    public MatchConfiguration(
        string mapKey,
        ulong seed,
        IEnumerable<MatchParticipantConfiguration> participants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapKey);
        ArgumentNullException.ThrowIfNull(participants);

        _participants = participants.ToArray();

        if (_participants.Length < 2)
        {
            throw new ArgumentException(
                "A skirmish match requires at least two participants.",
                nameof(participants));
        }

        var players = new HashSet<PlayerId>();
        var starts = new HashSet<int>();

        for (int index = 0; index < _participants.Length; index++)
        {
            MatchParticipantConfiguration participant =
                _participants[index];

            if (!players.Add(participant.Player))
            {
                throw new ArgumentException(
                    $"Player {participant.Player} is assigned more than once.",
                    nameof(participants));
            }

            if (!starts.Add(participant.StartIndex))
            {
                throw new ArgumentException(
                    $"Start index {participant.StartIndex} is assigned more than once.",
                    nameof(participants));
            }
        }

        MapKey = mapKey;
        Seed = seed;
    }

    public string MapKey { get; }

    public ulong Seed { get; }

    public IReadOnlyList<MatchParticipantConfiguration> Participants =>
        _participants;

    public MatchParticipantConfiguration GetParticipant(PlayerId player)
    {
        for (int index = 0; index < _participants.Length; index++)
        {
            if (_participants[index].Player == player)
            {
                return _participants[index];
            }
        }

        throw new KeyNotFoundException(
            $"Player {player} is not configured for this match.");
    }

    public void ValidateAgainst(PrototypeBattlefieldDefinition battlefield)
    {
        ArgumentNullException.ThrowIfNull(battlefield);

        if (!string.Equals(
                MapKey,
                battlefield.Metadata.Key,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configured map '{MapKey}' does not match battlefield '{battlefield.Metadata.Key}'.");
        }

        for (int index = 0; index < _participants.Length; index++)
        {
            MatchParticipantConfiguration participant =
                _participants[index];

            if (participant.StartIndex >= battlefield.Starts.Count)
            {
                throw new InvalidOperationException(
                    $"Start index {participant.StartIndex} is outside battlefield start assignments.");
            }

            BattlefieldStartPosition start =
                battlefield.Starts[participant.StartIndex];

            if (start.Player != participant.Player)
            {
                throw new InvalidOperationException(
                    $"Battlefield start {participant.StartIndex} belongs to player {start.Player}, not configured player {participant.Player}.");
            }

            if (participant.Player.Value > uint.MaxValue ||
                participant.Faction.Value != (uint)participant.Player.Value)
            {
                throw new InvalidOperationException(
                    $"Player {participant.Player} must currently use its matching combat faction.");
            }
        }
    }

    public static MatchConfiguration CreateVerticalSlice(
        PrototypeBattlefieldDefinition battlefield,
        ulong seed = 17)
    {
        ArgumentNullException.ThrowIfNull(battlefield);

        var configuration =
            new MatchConfiguration(
                battlefield.Metadata.Key,
                seed,
                [
                    new MatchParticipantConfiguration(
                        new PlayerId(1),
                        new FactionId(1),
                        startIndex: 0,
                        isComputerControlled: false),
                    new MatchParticipantConfiguration(
                        new PlayerId(2),
                        new FactionId(2),
                        startIndex: 1,
                        isComputerControlled: true)
                ]);

        configuration.ValidateAgainst(battlefield);
        return configuration;
    }
}

public sealed class SkirmishMatchInitialization
{
    private readonly Dictionary<PlayerId, SkirmishStartingBase> _bases;

    internal SkirmishMatchInitialization(
        MatchConfiguration configuration,
        Dictionary<PlayerId, SkirmishStartingBase> bases)
    {
        Configuration = configuration;
        _bases = bases;
    }

    public MatchConfiguration Configuration { get; }

    public IReadOnlyDictionary<PlayerId, SkirmishStartingBase> Bases =>
        _bases;

    public SkirmishStartingBase GetBase(PlayerId player) =>
        _bases.TryGetValue(player, out SkirmishStartingBase startingBase)
            ? startingBase
            : throw new KeyNotFoundException(
                $"No starting base exists for player {player}.");
}

public static class SkirmishMatchInitializer
{
    public static SkirmishMatchInitialization Initialize(
        EntityRegistry entities,
        InventoryStore inventories,
        UnitFactory unitFactory,
        TerrainWorld terrain,
        PrototypeBattlefieldDefinition battlefield,
        PrototypeBattlefieldRuntime battlefieldRuntime,
        MatchConfiguration configuration,
        SkirmishStartingStock? startingStock = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(unitFactory);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(battlefieldRuntime);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.ValidateAgainst(battlefield);

        var bases =
            new Dictionary<PlayerId, SkirmishStartingBase>(
                configuration.Participants.Count);

        for (int index = 0;
             index < configuration.Participants.Count;
             index++)
        {
            MatchParticipantConfiguration participant =
                configuration.Participants[index];
            BattlefieldStartPosition start =
                battlefield.Starts[participant.StartIndex];

            SkirmishStartingBase startingBase =
                SkirmishStartingBaseFactory.Create(
                    entities,
                    inventories,
                    unitFactory,
                    terrain,
                    start,
                    startingStock);

            if (!participant.IsComputerControlled &&
                entities.IsAlive(startingBase.Controller))
            {
                entities.DestroyEntity(startingBase.Controller);
            }

            bases.Add(
                participant.Player,
                startingBase);
        }

        _ = battlefieldRuntime.AttachCommandCoreObjectives(
            entities,
            bases.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.CommandCore));

        return new SkirmishMatchInitialization(
            configuration,
            bases);
    }
}
