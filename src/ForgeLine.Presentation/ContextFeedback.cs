using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum PlacementContextState : byte { None, Pending, Stale, Invalid, Valid, MissingMaterials }

public readonly record struct PlacementContextFeedbackView(
    PlacementContextState State, string Title, string Reason, string Hint,
    IReadOnlyList<PlayerActionResourceAmount>? Costs = null,
    SimulationSessionId SessionId = default, SimulationTick Tick = default)
{
    public bool Visible => State != PlacementContextState.None;
}

public static class PlacementContextFeedback
{
    public static PlacementContextFeedbackView Resolve(RtsBuildingPlacementController controller, PresentationSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.AwaitingResult)
            return new(PlacementContextState.Pending, "BUILDING PLACEMENT", "BUILD REQUEST PENDING",
                "WAIT FOR THE AUTHORITATIVE RESULT", SessionId: snapshot?.SessionId ?? default, Tick: snapshot?.Tick ?? default);
        return Resolve(controller.ActiveBuilding, controller.PreviewFreshness, controller.Preview, snapshot);
    }

    public static PlacementContextFeedbackView Resolve(BuildingId building, PlacementPreviewFreshness freshness,
        BuildingPlacementPreview? preview, PresentationSnapshot? snapshot)
    {
        var view = ResolveCore(building, freshness, preview, snapshot);
        return view with { SessionId = snapshot?.SessionId ?? default, Tick = snapshot?.Tick ?? default };
    }

    private static PlacementContextFeedbackView ResolveCore(BuildingId building, PlacementPreviewFreshness freshness,
        BuildingPlacementPreview? preview, PresentationSnapshot? snapshot)
    {
        if (!building.IsSpecified) return default;
        PlayerConstructionActionReadModel? action = null;
        if (snapshot?.PlayerActions is { } actions && actions.SessionId == snapshot.SessionId && actions.Tick == snapshot.Tick)
            for (int i = 0; i < actions.Construction.Count; i++)
                if (actions.Construction[i].BuildingId == building) { action = actions.Construction[i]; break; }
        string title = action?.DisplayName ?? "BUILDING PLACEMENT";
        if (freshness == PlacementPreviewFreshness.Stale)
            return new(PlacementContextState.Stale, title, "STALE PREVIEW", "WAIT FOR THE CURRENT LOCATION CHECK");
        if (freshness != PlacementPreviewFreshness.Current || preview is not { } current || current.BuildingId != building)
            return new(PlacementContextState.Pending, title, "PLACEMENT CHECK PENDING", "MOVE THE POINTER TO A BUILDABLE LOCATION");
        if (!current.IsValid)
            return new(PlacementContextState.Invalid, title, PlayerActionDockHudModel.ResolvePlacementFailureLabel(current.Failure),
                current.Failure == BuildingPlacementFailureReason.ResourceDepositRequired
                    ? "PLACE THE EXTRACTOR ON A RESOURCE DEPOSIT" : "CHOOSE ANOTHER LOCATION / F9 ROTATE");
        if (action is { HasRequiredResources: false })
            return new(PlacementContextState.MissingMaterials, title, "MISSING CORE MATERIALS", "REPLENISH COMMAND CORE CONSTRUCTION STOCK", action.Costs);
        return new(PlacementContextState.Valid, title, "LOCATION VALID AT CAPTURED TICK",
            "LEFT CLICK REQUEST / ESC CANCEL / F9 ROTATE", action?.Costs);
    }
}

internal static class ProductionContextFeedback
{
    public static string Explain(string reason) => reason switch
    {
        "NoInput" or "NO INPUT" or "MISSING INPUT" => "DELIVER INPUTS WITH CARGO / L LOGISTICS",
        "NoPower" or "NO POWER" => "ADD POWER GENERATION; CHECK THE POWER HUD",
        "OutputFull" or "OUTPUT FULL" => "MOVE OUTPUT TO STORAGE / L LOGISTICS",
        "Paused" or "PAUSED" => "CHECK PAUSED REQUEST / CANCEL IF NEEDED",
        "DesiredStockReached" or "TARGET REACHED" => "STOCK TARGET MET; CHANGE IT IF NEEDED",
        "MISSING MATERIALS" => "REPLENISH COMMAND CORE CONSTRUCTION STOCK",
        "TECH REQUIRED" => "H TECHNOLOGY / CHECK THE REQUIRED UNLOCK",
        _ => string.Empty
    };

    public static string WorkLabel(string reason) => reason switch
    {
        "NO INPUT" => "CURRENT WORK: NO INPUT",
        "NO POWER" => "CURRENT WORK: NO POWER",
        "OUTPUT FULL" => "CURRENT WORK: OUTPUT FULL",
        "PAUSED" => "CURRENT WORK: PAUSED",
        "TARGET REACHED" => "CURRENT WORK: STOCK TARGET MET",
        "UNSUPPORTED" => "CURRENT WORK: UNSUPPORTED",
        "INVENTORY" or "INVALID FACILITY" => "CURRENT WORK: INVALID FACILITY / INVENTORY",
        _ => string.Empty
    };
}
