using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed record VerticalSliceAuthoritativeSnapshot(
    int SchemaVersion,
    string BattlefieldKey,
    ulong Tick,
    ulong RandomState,
    MatchState MatchState,
    EntityRegistrySnapshot Entities,
    InventoryStoreSnapshot Inventories,
    IReadOnlyList<AuthoritativeDomainSnapshot> Domains)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            IncludeFields = true
        };

    public static VerticalSliceAuthoritativeSnapshot Capture(
        VerticalSliceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var domains =
            new List<AuthoritativeDomainSnapshot>
            {
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
                    scenario.Logistics.GetEdges()),
                CaptureDomain(
                    "intelligence.player_1",
                    scenario.Intelligence.Capture(
                        new FactionId(1))),
                CaptureDomain(
                    "intelligence.player_2",
                    scenario.Intelligence.Capture(
                        new FactionId(2)))
            };

        return new VerticalSliceAuthoritativeSnapshot(
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
