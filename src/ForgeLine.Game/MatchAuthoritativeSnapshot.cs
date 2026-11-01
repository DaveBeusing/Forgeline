using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record AuthoritativeDomainSnapshot(
    string Name,
    string Json);

public sealed record MatchAuthoritativeSnapshot(
    int SchemaVersion,
    string BattlefieldKey,
    ulong Tick,
    ulong RandomState,
    MatchState MatchState,
    EntityRegistrySnapshot Entities,
    InventoryStoreSnapshot Inventories,
    IReadOnlyList<AuthoritativeDomainSnapshot> Domains)
{
    public const int CurrentSchemaVersion = 20;

    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            IncludeFields = true,
            NumberHandling =
                JsonNumberHandling
                    .AllowNamedFloatingPointLiterals
        };

    public static MatchAuthoritativeSnapshot Capture(
        MatchRuntime scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var domains =
            new List<AuthoritativeDomainSnapshot>
            {
                CaptureDomain("world.tactical_areas", scenario.Battlefield.TacticalAreas.Areas),
                CaptureDomain(
                    "simulation.pending_commands",
                    scenario.Simulation.PendingCommandCount),
                CaptureDomain(
                    "logistics.version",
                    scenario.Logistics.Version),
                CaptureDomain(
                    "logistics.nodes",
                    scenario.Logistics.GetNodes()),
                CaptureDomain(
                    "logistics.edges",
                    scenario.Logistics.GetEdges())
            };

        foreach (var participant in scenario.MatchConfiguration.Participants)
        {
            domains.Add(CaptureDomain($"intelligence.player_{participant.Player.Value}",
                scenario.Intelligence.Capture(participant.Faction)));
        }

        IReadOnlyList<OperationsRuleReadModel> operations = scenario.OperationsRules.CaptureAll();
        // Keep the legacy no-rules snapshot unchanged for existing save compatibility.
        if (operations.Count > 0)
            domains.Add(CaptureDomain("operations.rules", operations));

        if (scenario.Services.Navigation.VehicleRoutingConnected)
            domains.Add(CaptureDomain("navigation.routing.v1", scenario.Services.Navigation.CaptureRoutingState()));

        if (scenario.Services.GroundMovement.NavigationConnected)
            domains.Add(CaptureDomain("navigation.traffic", scenario.Services.GroundMovement.CaptureNavigationState()));

#if FORGELINE_DEVELOPER_TOOLS
        if (scenario.Services.DeveloperActions is DeveloperCommandGateway developer && developer.HasAuthoritativeState)
            domains.Add(CaptureDomain("developer.state.v1", developer.CaptureAuthoritative()));
#endif

        return new MatchAuthoritativeSnapshot(
            CurrentSchemaVersion,
            scenario.Battlefield.Metadata.Key,
            scenario.Simulation.CurrentTick.Value,
            scenario.Simulation.Random.State,
            scenario.GetMatchState(),
            scenario.Simulation.Entities.CaptureSnapshot(),
            scenario.Inventories.CaptureSnapshot(),
            domains);
    }

    public string ComputeSha256()
    {
        string json =
            JsonSerializer.Serialize(
                this,
                s_jsonOptions);
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                json);
        byte[] digest =
            SHA256.HashData(
                bytes);

        return Convert.ToHexString(
            digest);
    }

    private static AuthoritativeDomainSnapshot CaptureDomain<T>(
        string name,
        T value) =>
        new(
            name,
            JsonSerializer.Serialize(
                value,
                s_jsonOptions));
}
