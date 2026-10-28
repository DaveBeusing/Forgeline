using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

public sealed record GameplayPacingMilestone(string Name, string Owner, ulong? Tick);

public static class GameplayPacingNames
{
    public const string ConveyorDelivery = "progression.first_conveyor_delivery";
    public const string ProcessingOutput = "progression.first_processing_output";
    public const string MobileSupply = "progression.first_mobile_supply";
    public const string ResearchCompleted = "progression.first_research_completed";
    public const string AuthorizedContact = "progression.first_authorized_contact";
    public const string ChainDisruption = "progression.first_chain_disruption";
    public const string ChainRecovery = "progression.first_chain_recovery";
    public const string StrategicExpansion = "progression.first_strategic_expansion";
    public const string CombatDamage = "progression.first_combat_damage";
    public static IReadOnlyList<string> ParticipantMilestones { get; } = Array.AsReadOnly(new[]
    { ConveyorDelivery, ProcessingOutput, MobileSupply, ResearchCompleted, AuthorizedContact,
        ChainDisruption, ChainRecovery, StrategicExpansion });
}

public sealed partial class GameplayTelemetryCollector
{
    private void ObservePacing()
    {
        var tick = _scenario.Simulation.CurrentTick;
        foreach (var participant in _scenario.MatchConfiguration.Participants)
        {
            string owner = OwnerForPlayer(participant.Player);
            foreach (var item in _scenario.Progress.CaptureFirst(participant.Player, tick))
            {
                if (item.SessionId != _scenario.Simulation.SessionId || item.Player != participant.Player ||
                    item.Tick.Value > tick.Value || item.Quantity <= 0) continue;
                string? name = item.Kind switch
                {
                    GameplayProgressKind.ConveyorDelivered => GameplayPacingNames.ConveyorDelivery,
                    GameplayProgressKind.ProductionCompleted => GameplayPacingNames.ProcessingOutput,
                    GameplayProgressKind.FieldFuelTransferred or GameplayProgressKind.FieldAmmunitionTransferred => GameplayPacingNames.MobileSupply,
                    GameplayProgressKind.ResearchCompleted => GameplayPacingNames.ResearchCompleted,
                    GameplayProgressKind.DisruptionObserved => GameplayPacingNames.ChainDisruption,
                    GameplayProgressKind.DisruptionResolved => GameplayPacingNames.ChainRecovery,
                    _ => null
                };
                if (name is not null) RecordMilestone(name, owner, "", item.Tick.Value);
            }
            if (!_milestones.ContainsKey(new(GameplayPacingNames.AuthorizedContact, owner, "")) &&
                _scenario.Intelligence.CurrentTick == tick && _scenario.Intelligence.HasCurrentContact(participant.Faction))
                RecordMilestone(GameplayPacingNames.AuthorizedContact, owner, "");
        }
        foreach (var entity in _scenario.Simulation.Entities.Query<CompletedBuilding, WorldTransform>(QueryIterationOrder.StableByEntityIndex))
        {
            var building = _scenario.Simulation.Entities.GetComponent<CompletedBuilding>(entity);
            if (building.CompletedAtTick.Value == 0 || building.CompletedAtTick > tick) continue;
            var position = _scenario.Simulation.Entities.GetComponent<WorldTransform>(entity).Position;
            foreach (var participant in _scenario.MatchConfiguration.Participants)
            {
                if (participant.Player != building.Owner) continue;
                var area = _scenario.Battlefield.Starts[participant.StartIndex].BuildArea;
                if (position.X < area.Minimum.X || position.X > area.Maximum.X ||
                    position.Z < area.Minimum.Z || position.Z > area.Maximum.Z)
                    RecordMilestone(GameplayPacingNames.StrategicExpansion, OwnerForPlayer(building.Owner), "");
            }
        }
        // Match-wide damage is combat evidence, not faction-authorized scouting evidence.
        if (_scenario.Services.CombatRuntime.Metrics.TotalDamageApplied > 0)
            RecordMilestone(GameplayPacingNames.CombatDamage, MatchOwner, "");
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<GameplayPacingMilestone> CapturePacing()
    {
        var result = new List<GameplayPacingMilestone>();
        foreach (var participant in _scenario.MatchConfiguration.Participants)
        {
            string owner = OwnerForPlayer(participant.Player);
            foreach (string name in GameplayPacingNames.ParticipantMilestones)
                result.Add(new(name, owner, _milestones.TryGetValue(new(name, owner, ""), out var value) ? value.Tick : null));
        }
        result.Add(new(GameplayPacingNames.CombatDamage, MatchOwner,
            _milestones.TryGetValue(new(GameplayPacingNames.CombatDamage, MatchOwner, ""), out var combat) ? combat.Tick : null));
        return result.AsReadOnly();
    }
}
