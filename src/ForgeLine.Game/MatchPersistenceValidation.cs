using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ForgeLine.Game;

// Content checks shared by file discovery, writing and direct reconstruction.
// This preflight creates no runtime and does not resolve custom content.
internal static class MatchPersistenceValidation
{
    internal static void Configuration(PersistedMatchConfiguration? configuration)
    {
        Require(configuration is not null, "Missing match configuration.");
        var value = configuration!;
        Require(value.Scenario is not null && value.Participants is not null &&
            !string.IsNullOrWhiteSpace(value.CompositionKey), "Missing scenario, participants or composition identity.");
        if (value.Identity is ScenarioIdentity identity)
            Require(!string.IsNullOrWhiteSpace(identity.MapKey) && identity.MapRevision > 0 &&
                Hash(identity.MapSha256) && identity.TerrainVersion > 0 &&
                !string.IsNullOrWhiteSpace(identity.ContentKey) && Hash(identity.ContentSha256) &&
                !string.IsNullOrWhiteSpace(identity.StartRule) && !string.IsNullOrWhiteSpace(identity.ObjectiveRule),
                "Invalid saved scenario identity.");
        Require(value.InitialEntityCapacity > 0, "Invalid initial entity capacity.");
        Require(value.Participants!.Count >= 2 && value.Participants.All(p => p is not null), "Invalid participants.");
        Require(value.Participants.Select(p => p.Player.Value).Distinct().Count() == value.Participants.Count &&
            value.Participants.All(p => p.Player.Value != 0), "Invalid participant identity.");
        Require(value.Scenario!.OpponentConfigurations is not null &&
            value.Scenario.OpponentConfigurations.Values.All(p => p is not null), "Missing opponent policies.");
        var stock = value.Scenario.StartingStock;
        Require(new[] { stock.FerrousOre, stock.Volatiles, stock.Silicates, stock.Steel,
            stock.Fuel, stock.Electronics, stock.Ammunition }.All(amount => double.IsFinite(amount) && amount >= 0),
            "Invalid starting resource quantity.");
        try { value.Scenario.Validate(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            throw new MatchPersistenceException(MatchPersistenceFailureReason.InvalidConfiguration,
                "The saved scenario configuration is invalid.", error);
        }
    }

    internal static void Commands(IReadOnlyList<RecordedSimulationCommand>? commands,
        int payloadSchema = MatchPersistenceSerializer.CurrentSchemaVersion)
    {
        Require(commands is not null, "Missing command history.");
        foreach (var command in commands!)
        {
            Require(command is not null, "Null command in history.");
            Require(Enum.IsDefined(command!.Kind) && command.Entities is not null,
                "Invalid command kind or entity collection.");
            Require(command.Entities!.All(entity => entity.IsValid), "Invalid command entity identity.");
            if (command.Kind == ReplayCommandKind.QueuedOrders)
                Require(payloadSchema >= 11 && command.Issuer.IsSpecified && command.Entities.Length is > 0 and <= QueuedUnitOrders.MaximumUnits &&
                    Enum.IsDefined(command.QueueEdit) && Enum.IsDefined(command.QueueKind) && Enum.IsDefined(command.Formation) &&
                    (!command.Patrol || command.QueueKind == QueuedOrderKind.Move) &&
                    (command.QueueKind != QueuedOrderKind.Attack || command.TargetEntity.IsValid), "Invalid queued order command.");
            if (command.Kind == ReplayCommandKind.BridgeRepair)
                Require(command.Issuer.IsSpecified && command.Entities.Length == 1,
                    "Invalid bridge repair command.");
            if (command.Kind == ReplayCommandKind.ConveyorRouting)
                Require(command.Issuer.IsSpecified && command.TargetEntity.IsValid &&
                    Enum.IsDefined(command.RoutingPreference), "Invalid conveyor routing command.");
            Require(Finite(command.Position) && double.IsFinite(command.Value1) &&
                double.IsFinite(command.Value2) && double.IsFinite(command.Value3) &&
                Finite(command.ConveyorRequest.Start) && Finite(command.ConveyorRequest.End),
                "Non-finite command coordinates or values.");
        }
    }

    internal static void State(MatchAuthoritativeSnapshot? state)
    {
        Require(state is not null, "Missing authoritative checkpoint.");
        var value = state!;
        Require(!string.IsNullOrWhiteSpace(value.BattlefieldKey) && value.Entities is not null &&
            value.Inventories is not null && value.Domains is not null, "Incomplete authoritative checkpoint.");
        Lifecycle(value.MatchState);
        var entities = value.Entities!;
        Require(entities.AliveEntities is not null && entities.ComponentStores is not null,
            "Missing entity collections.");
        Require(entities.EntityCount >= 0 && entities.Capacity >= entities.EntityCount &&
            entities.EntityCount == entities.AliveEntities!.Count &&
            entities.AliveEntities.All(e => e.IsValid) &&
            entities.AliveEntities.Distinct().Count() == entities.EntityCount, "Invalid checkpoint entity identities.");
        var alive = entities.AliveEntities!.ToHashSet();
        foreach (var store in entities.ComponentStores!)
        {
            Require(store is not null && !string.IsNullOrWhiteSpace(store.ComponentType) &&
                store.Components is not null, "Invalid component store.");
            foreach (var component in store!.Components!)
            {
                Require(component is not null && alive.Contains(component.Entity),
                    "Invalid component entity identity.");
                Json(component!.Json);
            }
        }
        Require(value.Inventories!.Inventories is not null, "Missing inventory collection.");
        Require(value.Inventories.Inventories!.Select(i => i?.InventoryId).Distinct().Count() ==
            value.Inventories.Inventories.Count, "Duplicate inventory identities.");
        foreach (var inventory in value.Inventories.Inventories!)
        {
            Require(inventory is not null && inventory.Resources is not null, "Invalid inventory checkpoint.");
            Require(!double.IsNaN(inventory!.TotalCapacity) && inventory.TotalCapacity >= 0 &&
                double.IsFinite(inventory.TotalQuantity) && inventory.TotalQuantity >= 0,
                "Invalid inventory capacity or quantity.");
            Require(inventory.InventoryId.IsSpecified, "Invalid inventory identity.");
            foreach (var resource in inventory.Resources!)
                Require(resource.ResourceId.IsSpecified && double.IsFinite(resource.Quantity) && resource.Quantity >= 0 &&
                    double.IsFinite(resource.ReservedQuantity) && resource.ReservedQuantity >= 0 &&
                    double.IsFinite(resource.AvailableQuantity) && resource.AvailableQuantity >= 0,
                    "Invalid inventory resource quantity or identity.");
        }
        foreach (var domain in value.Domains!)
        {
            Require(domain is not null && !string.IsNullOrWhiteSpace(domain.Name), "Invalid checkpoint domain.");
            Json(domain!.Json);
        }
    }

    internal static void Lifecycle(MatchState value)
    {
        Require(Enum.IsDefined(value.Lifecycle) && Enum.IsDefined(value.Outcome) &&
            Enum.IsDefined(value.TerminationReason) &&
            (!value.IsTerminal || value.Lifecycle == MatchLifecyclePhase.Completed || value.Outcome != MatchOutcome.None),
            "Invalid match lifecycle metadata.");
    }

    internal static void Require([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid) throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, message);
    }

    internal static bool Hash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void Json(string? json)
    {
        Require(!string.IsNullOrWhiteSpace(json), "Missing checkpoint JSON.");
        try { using var document = JsonDocument.Parse(json!); }
        catch (JsonException error)
        {
            throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument,
                "Invalid checkpoint JSON.", error);
        }
    }
}
