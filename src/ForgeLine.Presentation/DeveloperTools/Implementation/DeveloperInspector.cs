using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct DeveloperInspector(SimulationSessionId Session, SimulationTick Tick,
    EntityId Entity, PlayerId Owner, string Type, PlayerSelectionSummary Details, string Status)
{
    public static DeveloperInspector Capture(PresentationSnapshot? snapshot, SelectionSet selection)
    {
        if (snapshot is null) return new(default, default, default, default, "", default, "Snapshot unavailable");
        if (selection.Count != 1) return new(snapshot.SessionId, snapshot.Tick, default, default, "", default,
            selection.Count == 0 ? "No selection" : "Multiple selection; select exactly one entity");
        var id = selection.Entities.Single();
        foreach (var instance in snapshot.Instances)
            if (instance.Entity == id)
            {
                var detail = snapshot.PlayerExperience?.Selection ?? default;
                if (detail.Count != 1 || detail.PrimaryEntity != id) detail = default;
                return new(snapshot.SessionId, snapshot.Tick, id, instance.Selectable.Owner,
                    detail.Count == 1 ? detail.DisplayName : instance.Selectable.Category.ToString(), detail, "Observed");
            }
        return new(snapshot.SessionId, snapshot.Tick, default, default, "", default, "Target unavailable in current snapshot");
    }
}
