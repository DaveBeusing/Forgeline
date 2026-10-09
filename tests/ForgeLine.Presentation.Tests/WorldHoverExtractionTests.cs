using ForgeLine.Core;
using System.Numerics;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldHoverExtractionTests
{
    [Fact]
    public void IdentifiedEnemyPublishesIdentityOnlyAndDetectedOrLostContactsPublishNoPrivateFacts()
    {
        using var scenario = CreateScenario();
        var interaction = new PresentationInteractionState();
        var enemy = scenario.GetBase(new PlayerId(2)).CommandCore;
        var entities = scenario.Simulation.Entities;
        var position = entities.GetComponent<WorldTransform>(enemy).Position;
        var signature = entities.GetComponent<IntelligenceSignature>(enemy);
        var state = IntelligenceState.Detected;
        scenario.Simulation.RegisterTickObserver(new CallbackObserver(context =>
        {
            scenario.Intelligence.BeginTick(context.Tick);
            if (state is IntelligenceState.Detected or IntelligenceState.Identified)
                scenario.Intelligence.Observe(new FactionId(1), enemy, signature, position, state, context.Tick);
        }));
        var buffer = Observe(scenario, interaction);
        interaction.SetHover(enemy, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var detected));
        Assert.Null(detected.Hover);
        state = IntelligenceState.Identified;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var identified));
        var summary = Assert.IsType<PlayerHoverSummary>(identified.Hover);
        Assert.Equal(PlayerHoverCategory.IdentifiedContact, summary.Category);
        Assert.Equal("Command Core", summary.DisplayName);
        Assert.Equal(PlayerSelectionSummary.Empty, summary.OwnedDetails);
        Assert.Null(summary.RemainingQuantity);
        Assert.Empty(summary.ExtractionState);
        Assert.Empty(summary.Requirement);
        state = IntelligenceState.Explored;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var lost));
        Assert.Null(lost.Hover);
        Assert.Equal(summary, identified.Hover);
    }

    [Fact]
    public void DepositRequiresCurrentVisibilityAndDoesNotExposeForeignExtractionState()
    {
        using var scenario = CreateScenario();
        var entities = scenario.Simulation.Entities;
        EntityId deposit = entities.CreateEntity();
        var position = scenario.Terrain.WorldBounds.Maximum - new Vector3(64, 0, 64);
        entities.AddComponent(deposit, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        entities.AddComponent(deposit, new VisualIdentity(1));
        entities.AddComponent(deposit, WorldPresentationIdentity.ForResource(ResourceIds.FerrousOre));
        entities.AddComponent(deposit, new ResourceDeposit(ResourceIds.FerrousOre,
            new ForgeLine.World.AxisAlignedBounds(position - Vector3.One, position + Vector3.One), 1234, 1));
        EntityId extractor = entities.CreateEntity();
        entities.AddComponent(extractor, new ResourceExtractor(deposit, ResourceIds.FerrousOre, 1,
            new FactionId(2), state: ResourceExtractorState.OutputBlocked));
        bool visible = false;
        scenario.Simulation.RegisterTickObserver(new CallbackObserver(context =>
        {
            scenario.Intelligence.BeginTick(context.Tick);
            if (visible) scenario.Intelligence.MarkVisibleCircle(new FactionId(1), position, 100);
        }));
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        interaction.SetHover(deposit, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.Null(hidden.Hover);
        visible = true;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var seen));
        var summary = Assert.IsType<PlayerHoverSummary>(seen.Hover);
        Assert.Equal(PlayerHoverCategory.Deposit, summary.Category);
        Assert.Equal("Ferrous Ore", summary.DisplayName);
        Assert.Equal(entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity, summary.RemainingQuantity);
        Assert.Equal("AVAILABLE", summary.ExtractionState);
        Assert.Equal("Mine / Extractor", summary.Requirement);
        visible = false;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var lost));
        Assert.Null(lost.Hover);
    }

    [Fact]
    public void UnitAndConstructionNamesAndProgressUseAuthoritativeCatalogs()
    {
        using var scenario = CreateScenario();
        var entities = scenario.Simulation.Entities;
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        var entity = entities.CreateEntity();
        entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        entities.AddComponent(entity, new VisualIdentity(1));
        entities.AddComponent(entity, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Unit));
        entities.AddComponent(entity, new UnitIdentity(UnitIds.MainBattleTank, new FactionId(1)));
        interaction.SetHover(entity, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var unit));
        Assert.Equal("Main Battle Tank", unit.Hover!.Value.DisplayName);
        Assert.Equal(PlayerHoverCategory.Unit, unit.Hover.Value.Category);
        Assert.False(unit.Hover.Value.OwnedDetails.HasHealth); // Missing is not a fabricated zero.
        entities.RemoveComponent<UnitIdentity>(entity);
        entities.SetComponent(entity, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Building));
        entities.AddComponent(entity, new ConstructionSite(BuildingIds.Extractor, new PlayerId(1),
            scenario.GetBase(new PlayerId(1)).StartingInventory, scenario.Simulation.CurrentTick, 100, 25));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var construction));
        Assert.Equal(PlayerHoverCategory.Construction, construction.Hover!.Value.Category);
        Assert.Equal("Mine / Extractor", construction.Hover.Value.DisplayName);
        Assert.Equal(PlayerWorkKind.Construction, construction.Hover.Value.OwnedDetails.Work.Kind);
        Assert.InRange(construction.Hover.Value.OwnedDetails.Work.Progress, .25, .27);
    }

    [Fact]
    public void OwnedHoverIsCopiedWithoutSelectionAndClearsOnDestruction()
    {
        using var scenario = CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        EntityId entity = scenario.GetBase(new PlayerId(1)).CommandCore;
        interaction.SetHover(entity, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var first));
        var hover = Assert.IsType<PlayerHoverSummary>(first.Hover);
        Assert.Equal(entity, hover.Entity);
        Assert.Equal(first.Tick, hover.Tick);
        Assert.Equal(first.SessionId, hover.SessionId);
        Assert.Equal(PlayerHoverCategory.Building, hover.Category);
        Assert.True(hover.OwnedDetails.HasHealth);
        Assert.Equal(0, first.PlayerExperience!.Value.Selection.Count);
        scenario.Simulation.Entities.DestroyEntity(entity);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var second));
        Assert.Null(second.Hover);
        Assert.Equal(hover, first.Hover);
    }

    [Fact]
    public void HiddenEnemyAndStaleGenerationCannotPublishFacts()
    {
        using var scenario = CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        interaction.SetHover(scenario.GetBase(new PlayerId(2)).CommandCore, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.Null(hidden.Hover);
        var entity = scenario.GetBase(new PlayerId(1)).CommandCore;
        interaction.SetHover(new EntityId(entity.Index, entity.Generation + 1), scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var stale));
        Assert.Null(stale.Hover);
    }

    internal static MatchRuntime CreateScenario() => CentralDivideScenario.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 4209) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        }, TestContext.Current.CancellationToken);

    internal static PresentationSnapshotBuffer Observe(MatchRuntime scenario, PresentationInteractionState interaction)
    {
        var buffer = new PresentationSnapshotBuffer();
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,
            new PresentationExtractionContext(scenario, new PlayerId(1), interaction, gateway)));
        return buffer;
    }

    private sealed class CallbackObserver(Action<SimulationContext> action) : ISimulationTickObserver
    {
        public void OnTickCompleted(SimulationContext context) => action(context);
    }
}
