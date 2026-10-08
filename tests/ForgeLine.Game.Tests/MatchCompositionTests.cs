using System.Numerics;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Jobs;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchCompositionTests
{
    [Fact]
    public void IndependentMapContentAndThreeParticipantsRunWithoutPresetAssumptions()
    {
        MatchRuntimeSettings settings = CreateIndependentSettings();
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.Equal("test.three-starts", runtime.Battlefield.Metadata.Key);
        Assert.Equal(3, runtime.Initialization.Bases.Count);
        Assert.Equal(0, runtime.Services.UnitDefinitions.Count);
        Assert.Equal(3, runtime.Simulation.Entities.GetComponentCount<CommandCoreObjective>());
        Assert.All(settings.Participants, participant =>
            Assert.Equal(participant.Player, runtime.GetBase(participant.Player).Player));
        Assert.Equal(["intelligence.player_7", "intelligence.player_11", "intelligence.player_19"],
            MatchAuthoritativeSnapshot.Capture(runtime).Domains
                .Where(domain => domain.Name.StartsWith("intelligence.", StringComparison.Ordinal))
                .Select(domain => domain.Name));
        Assert.Single(runtime.Services.RegisteredSystemTypes);
        Assert.Equal(typeof(MatchObjectiveSystem), runtime.Services.RegisteredSystemTypes[0]);
    }

    [Fact]
    public void IndependentCompositionSaveAndReplayUseExplicitResolverAndVerifyState()
    {
        MatchRuntimeSettings settings = CreateIndependentSettings();
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
        MatchSaveData save = MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
        MatchReplayData replay = MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
        Func<string, MatchComposition> resolver = key => key == settings.Composition.Key
            ? settings.Composition : throw new InvalidOperationException();
        using MatchRuntime restored = MatchPersistenceService.Restore(save, resolver);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(replay, resolver);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save));
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save,
            _ => CentralDivideScenario.CreateComposition()));
    }

    [Fact]
    public void SchemaOnePresetPayloadsMigrateAfterChecksumValidation()
    {
        using MatchRuntime runtime = CentralDivideScenario.Create(seed: 831);
        runtime.Simulation.RunTicks(4, TestContext.Current.CancellationToken);
        string oldSave = ToSchemaOne(MatchPersistenceSerializer.SerializeSave(
            MatchPersistenceService.CaptureSave(runtime)));
        string oldReplay = ToSchemaOne(MatchPersistenceSerializer.SerializeReplay(
            MatchPersistenceService.CaptureReplay(runtime)));
        using MatchRuntime restored = MatchPersistenceService.Restore(
            MatchPersistenceSerializer.DeserializeSave(oldSave));
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(
            MatchPersistenceSerializer.DeserializeReplay(oldReplay));
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        var corrupt = JsonNode.Parse(oldSave)!;
        corrupt["Payload"] = corrupt["Payload"]!.GetValue<string>() + " ";
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(corrupt.ToJsonString()));
    }

    [Fact]
    public void HostOwnedAndHeadlessExecutionProduceEquivalentIndependentCheckpoints()
    {
        MatchRuntimeSettings settings = CreateIndependentSettings();
        using var scheduler = new JobScheduler();
        using MatchRuntime headless = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        using MatchRuntime hosted = MatchRuntime.Create(settings with
        {
            Scheduler = scheduler,
            SchedulerOwnership = MatchSchedulerOwnership.Host,
            EnableSpatialQueryTiming = true
        }, TestContext.Current.CancellationToken);
        headless.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        hosted.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        Assert.False(hosted.OwnsScheduler);
        Assert.Equal(headless.Services.RegisteredSystemTypes, hosted.Services.RegisteredSystemTypes);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(headless).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(hosted).ComputeSha256());
    }

    [Fact]
    public void RecordedSchemaOneReplayRetainsItsAuthoritativeCheckpoint()
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "schema-1-central-divide.replay.json"));
        MatchReplayData replay = MatchPersistenceSerializer.DeserializeReplay(document);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(replay);
        Assert.Equal(4UL, playback.Simulation.CurrentTick.Value);
        Assert.Equal(831UL, playback.Simulation.Random.State);
        Assert.Equal("848FABF78754FA4136EB56D03B5059B5FAA2EC91F2C145CA2FFA022C7251549B",
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Fact]
    public void ParticipantSubsetAndAdditionalSystemRemainExplicitCompositionInputs()
    {
        MatchRuntimeSettings settings = CreateIndependentSettings();
        var counter = new CountingSystem();
        settings = settings with
        {
            Participants = settings.Participants.Take(2).ToArray(),
            Composition = settings.Composition with
            {
                ConfigureSystems = systems => [.. systems.Where(system => system is MatchObjectiveSystem), counter]
            }
        };
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        Assert.Equal(2, runtime.Initialization.Bases.Count);
        Assert.Equal(2, runtime.Simulation.Entities.GetComponentCount<CommandCoreObjective>());
        Assert.Equal(3, counter.Executions);
        Assert.Contains(typeof(CountingSystem), runtime.Services.RegisteredSystemTypes);
    }

    [Fact]
    public void SchemaTwoCannotSilentlyDefaultMissingCompositionIdentity()
    {
        using MatchRuntime runtime = MatchRuntime.Create(CreateIndependentSettings(), TestContext.Current.CancellationToken);
        JsonNode envelope = JsonNode.Parse(MatchPersistenceSerializer.SerializeSave(
            MatchPersistenceService.CaptureSave(runtime)))!;
        JsonNode payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!;
        payload["Configuration"]!.AsObject().Remove("CompositionKey");
        string json = payload.ToJsonString();
        envelope["Payload"] = json;
        envelope["PayloadSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Throws<MatchPersistenceException>(() =>
            MatchPersistenceSerializer.DeserializeSave(envelope.ToJsonString()));
    }

    private sealed class CountingSystem : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.SnapshotEvents;
        public int Executions { get; private set; }
        public void Execute(SimulationContext context) => Executions++;
    }

    private static string ToSchemaOne(string document)
    {
        JsonNode envelope = JsonNode.Parse(document)!;
        JsonNode payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!;
        payload["SchemaVersion"] = 1;
        payload["Configuration"]!.AsObject().Remove("CompositionKey");
        JsonNode scenario = payload["Configuration"]!["Scenario"]!;
        scenario["WestOpponent"] = scenario["OpponentConfigurations"]!["1"]!.DeepClone();
        scenario["EastOpponent"] = scenario["OpponentConfigurations"]!["2"]!.DeepClone();
        scenario.AsObject().Remove("OpponentConfigurations");
        string json = payload.ToJsonString();
        envelope["Payload"] = json;
        envelope["PayloadSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return envelope.ToJsonString();
    }

    private static MatchRuntimeSettings CreateIndependentSettings()
    {
        uint[] players = [7, 11, 19];
        BattlefieldStartPosition[] starts = players.Select((player, index) =>
        {
            var position = new Vector3(40 + index * 75, 0, 80);
            return new BattlefieldStartPosition(new PlayerId(player), position, position,
                new AxisAlignedBounds(position - new Vector3(25, 10, 25), position + new Vector3(25, 10, 25)));
        }).ToArray();
        var map = new BattlefieldDefinition(
            new BattlefieldMapMetadata("test.three-starts", "Three Starts", 256, 256, 3),
            starts, [], [], [], [], [], [],
            starts.Select(start => new BattlefieldObjectiveDefinition($"core.{start.Player.Value}", start.Player, start.CommandCorePosition)).ToArray(),
            [], CentralDivideBattlefield.Create().TerrainVisual);
        var content = new MatchContent(new ResourceCatalog([]), new BuildingDefinitionCatalog([]),
            new UnitDefinitionCatalog([]), new TechnologyDefinitionCatalog([]), new ProductionRecipeCatalog([]),
            new WeaponCatalog(), new ArmorCatalog(), new ArtilleryWeaponCatalog());
        var composition = new MatchComposition("test.three-starts.empty-content.v1", map, content,
            static _ =>
            {
                var grid = new WorldGridSettings();
                return new TerrainWorld(grid, [new TerrainChunk(new ChunkCoordinate(0, 0),
                    new TerrainHeightfield(grid.HeightSamplesPerSide, grid.ChunkSizeMeters,
                        new float[grid.HeightSamplesPerSide * grid.HeightSamplesPerSide]))]);
            },
            static (entities, inventories, _, _, start, _, _) =>
            {
                EntityId core = entities.CreateEntity();
                entities.AddComponent(core, new CompletedBuilding(BuildingIds.CommandCore, start.Player, SimulationTick.Zero));
                entities.AddComponent(core, new CommandFacility());
                return new SkirmishStartingBase(start.Player, new FactionId((uint)start.Player.Value), core,
                    EntityId.Invalid, inventories.CreateInventory(new InventorySpecification(1)), []);
            },
            static systems => systems.Where(system => system is MatchObjectiveSystem).ToArray());
        return new MatchRuntimeSettings
        {
            Composition = composition,
            Seed = 43,
            Participants = players.Select((player, index) =>
                new MatchParticipantConfiguration(new PlayerId(player), new FactionId(player), index, false)).ToArray(),
            Scenario = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay) with
            {
                OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>()
            }
        };
    }
}
