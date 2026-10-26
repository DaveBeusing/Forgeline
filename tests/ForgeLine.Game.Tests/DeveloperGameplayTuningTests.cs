#if FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DeveloperGameplayTuningTests
{
    private static MatchRuntimeSettings Basis() => CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 17) with
    { Participants = CentralDivideScenario.CreateDefaultParticipants(false, true) };
    private static DeveloperGameplayTuningProvider Bind(ConveyorTestFixture fixture, DeveloperRestartPlan? plan = null)
    {
        var basis = Basis();
        basis = basis with { Scenario = basis.Scenario with { DistributionRetryDelayTicks = 5, DistributionFairnessAgingTicks = 20 } };
        var provider = new DeveloperGameplayTuningProvider(fixture.Simulation, plan ?? DeveloperRestartBuilder.ForExisting(basis));
        fixture.Conveyor.BindDeveloperTuning(provider); return provider;
    }
    private static DeveloperRequest Request(DeveloperGameplayTuningProvider provider, DeveloperParameterId id, DeveloperValue value,
        DeveloperTarget? target = null, DeveloperOperation operation = DeveloperOperation.SetParameter, ulong? revision = null,
        Guid? correlation = null, SimulationSessionId? session = null) => new(session ?? provider.Session, correlation ?? Guid.NewGuid(),
        new(DeveloperRevisionDomain.Parameters, revision ?? provider.CaptureContribution().Revision.Value), operation,
        target ?? DeveloperTarget.Global, id, operation == DeveloperOperation.SetParameter ? [value] : []);
    private sealed class ApplyCommand(DeveloperGameplayTuningProvider provider, DeveloperSessionAccess access, DeveloperRequest request,
        Action<DeveloperTuningApplication> completed) : ISimulationCommand
    { public void Execute(SimulationContext context) => completed(provider.ApplyAtTick(context, access, request)); }
    private static DeveloperTuningApplication Apply(SimulationCoordinator simulation, DeveloperGameplayTuningProvider provider,
        DeveloperRequest request, bool active = true)
    {
        DeveloperTuningApplication? result = null;
        simulation.SubmitCommand(new ApplyCommand(provider, DeveloperToolsCapability.Current.BindSession(simulation.SessionId, active), request,
            value => result = value), new(simulation.CurrentTick.Value + 1));
        simulation.AdvanceOneTick(); return Assert.IsType<DeveloperTuningApplication>(result);
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(1)]
    [InlineData(4)]
    public void LiveRateChangesRealModuleAndBothPortBudgetsWithoutChangingTotalStock(double multiplier)
    {
        // At10Hz,4x needs40 transfer rounds; the old32-round bound cannot supply it.
        var fixture = new ConveyorTestFixture(17, 10);
        var source = fixture.AddStorage(new(41, 0, 80)); var destination = fixture.AddStorage(new(103, 0, 80));
        var line = fixture.Connect(source, destination); fixture.Give(source, ResourceIds.FerrousOre, 900);
        var provider = Bind(fixture);
        var result = Apply(fixture.Simulation, provider, Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(multiplier)));
        Assert.Equal(DeveloperRequestStatus.Applied, result.Result.Status);
        double expected = 100 * multiplier * fixture.TickSeconds;
        for (int tick = 0; tick < line.Length + 2; tick++)
        {
            double before = fixture.Stock(source, ResourceIds.FerrousOre);
            fixture.Step();
            Assert.InRange(before - fixture.Stock(source, ResourceIds.FerrousOre), expected - 1e-8, expected + 1e-8);
            Assert.InRange(fixture.Flow(line[0]).OutputPerSecond, 100 * multiplier - 1e-8, 100 * multiplier + 1e-8);
            Assert.InRange(fixture.Total(ResourceIds.FerrousOre), 900 - 1e-8, 900 + 1e-8);
        }
        double delivered = fixture.Stock(destination, ResourceIds.FerrousOre); fixture.Step();
        Assert.InRange(fixture.Stock(destination, ResourceIds.FerrousOre) - delivered, expected - 1e-8, expected + 1e-8);
        Assert.Equal(100 * multiplier, fixture.Conveyor.EffectiveThroughput);
        Assert.Equal(1UL, fixture.Conveyor.TuningRevision); Assert.Equal(result.Result.AppliedTick, fixture.Conveyor.TuningAppliedTick);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 20)]
    [InlineData(2, 0)]
    public void PowerDamageAndFullOutputsKeepTheirPhysicalAuthority(int control, double expected)
    {
        var fixture = new ConveyorTestFixture(17, 10);
        var source = fixture.AddStorage(new(41, 0, 80)); var destination = fixture.AddStorage(new(103, 0, 80));
        var line = fixture.Connect(source, destination); fixture.Give(source, ResourceIds.FerrousOre, 900);
        if (control == 0) fixture.SetPower(line[0], 0);
        if (control == 1)
        {
            var health = fixture.Entities.GetComponent<HealthState>(line[0]);
            fixture.Entities.SetComponent(line[0], new HealthState(health.Maximum * .5, health.Maximum));
        }
        if (control == 2)
        {
            foreach (var entity in line) fixture.Give(entity, ResourceIds.FerrousOre, fixture.Flow(entity).Capacity);
            fixture.Give(destination, ResourceIds.FerrousOre, 1000);
        }
        double total = fixture.Total(ResourceIds.FerrousOre), before = fixture.Stock(source, ResourceIds.FerrousOre);
        var provider = Bind(fixture);
        Apply(fixture.Simulation, provider, Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(4)));
        Assert.InRange(before - fixture.Stock(source, ResourceIds.FerrousOre), expected - 1e-8, expected + 1e-8);
        Assert.InRange(fixture.Total(ResourceIds.FerrousOre), total - 1e-8, total + 1e-8);
    }

    [Fact]
    public void TinyPositiveCargoStillMovesExactlyOneLinkPerTickAtFourTimesRate()
    {
        var fixture = new ConveyorTestFixture();
        var source = fixture.AddStorage(new(41, 0, 80)); var destination = fixture.AddStorage(new(103, 0, 80));
        var line = fixture.Connect(source, destination); const double cargo = 1e-10;
        fixture.Give(source, ResourceIds.FerrousOre, cargo); var provider = Bind(fixture);
        Apply(fixture.Simulation, provider, Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(4)));
        Assert.Equal(cargo, fixture.Stock(line[0], ResourceIds.FerrousOre));
        for (int i = 1; i < line.Length; i++)
        { fixture.Step(); Assert.Equal(cargo, fixture.Stock(line[i], ResourceIds.FerrousOre)); Assert.Equal(cargo, fixture.Total(ResourceIds.FerrousOre)); }
        fixture.Step(); Assert.Equal(cargo, fixture.Stock(destination, ResourceIds.FerrousOre));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void InvalidRequestsCannotChangeStateOrRevision(int control)
    {
        var fixture = new ConveyorTestFixture(); var provider = Bind(fixture); var before = provider.CaptureContribution();
        var request = control switch
        {
            0 => Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(.3)),
            1 => Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(4.25)),
            2 => Request(provider, (DeveloperParameterId)ushort.MaxValue, DeveloperValue.FromDouble(1)),
            3 => Request(provider, DeveloperParameterId.OpponentAggression, DeveloperValue.FromDouble(.8), DeveloperTarget.ForPlayer(new(1))),
            4 => Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(2), revision: 7),
            5 => Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(2), session: new(ulong.MaxValue)),
            6 => Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(2), DeveloperTarget.ForEntity(fixture.Payer)),
            7 => Request(provider, DeveloperParameterId.RaidMinimumUnits, DeveloperValue.FromInt32(2), DeveloperTarget.ForPlayer(new(2))),
            8 => Request(provider, DeveloperParameterId.OpponentReactionCadence, DeveloperValue.FromUInt32(1201), DeveloperTarget.ForPlayer(new(2))),
            _ => Request(provider, DeveloperParameterId.DistributionMaximumAttempts, DeveloperValue.FromUInt32(33))
        };
        var result = Apply(fixture.Simulation, provider, request);
        Assert.Equal(DeveloperRequestStatus.Rejected, result.Result.Status); Assert.Empty(result.Changes);
        Assert.Same(before, provider.CaptureContribution()); Assert.Equal(0UL, before.Revision.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeveloperValue.FromDouble(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeveloperValue.FromDouble(double.PositiveInfinity));
    }

    [Fact]
    public void InactiveAccessAndUnavailableSubmissionNeverPretendToApply()
    {
        var fixture = new ConveyorTestFixture(); var provider = Bind(fixture); var before = provider.CaptureContribution();
        var request = Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(2));
        var result = Apply(fixture.Simulation, provider, request, active: false);
        Assert.Equal(DeveloperFailureReason.SessionNotActivated, result.Result.Failure); Assert.Same(before, provider.CaptureContribution());
        var submitted = provider.Submit(DeveloperToolsCapability.Current.BindSession(provider.Session, true),
            new(provider.Session, before.Revision, _ => true, _ => true), request);
        Assert.Equal(DeveloperFailureReason.ProviderUnavailable, submitted.Failure); Assert.Null(submitted.AppliedTick);
    }

    [Fact]
    public void RepeatCorrelationIsExactlyOnceAndResetRetainsTheOriginalOverride()
    {
        var fixture = new ConveyorTestFixture(); var basis = Basis();
        basis = basis with { Scenario = basis.Scenario with { DistributionRetryDelayTicks = 5, DistributionFairnessAgingTicks = 20 } };
        var metadata = DeveloperRestartBuilder.ForExisting(basis).InitialOverrides.Metadata;
        Assert.True(DeveloperRestartBuilder.TryBuild(basis, metadata,
            [new(DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperTarget.Global, DeveloperValue.FromDouble(.5))], out var plan, out _));
        var provider = Bind(fixture, plan); var initial = provider.CaptureContribution().InitialOverrides;
        var first = Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(2));
        var applied = Apply(fixture.Simulation, provider, first);
        var second = Apply(fixture.Simulation, provider, Request(provider, DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperValue.FromDouble(3)));
        var latest = provider.CaptureContribution(); var repeated = Apply(fixture.Simulation, provider, first);
        Assert.True(repeated.IsReplay); Assert.Empty(repeated.Changes); Assert.Same(applied.Result, repeated.Result);
        Assert.Same(latest, provider.CaptureContribution()); Assert.Same(latest, repeated.Contribution);
        var collision = Request(provider, first.Parameter, DeveloperValue.FromDouble(4), revision: first.ExpectedRevision.Value, correlation: first.Correlation);
        Assert.Equal(DeveloperRequestStatus.Rejected, Apply(fixture.Simulation, provider, collision).Result.Status);
        var reset = Apply(fixture.Simulation, provider, Request(provider, first.Parameter, default, operation: DeveloperOperation.ResetParameter));
        Assert.True(Assert.Single(reset.Changes).IsReset); Assert.Equal(.5, provider.CaptureContribution().Conveyor.Multiplier);
        Assert.True(provider.CaptureContribution().DeveloperModified); Assert.Same(initial, provider.CaptureContribution().InitialOverrides);
        Assert.Equal(3UL, reset.Result.Revision.Value); Assert.Equal(2UL, second.Result.Revision.Value);
    }

    [Fact]
    public void RestartValuesRemainPendingAndBuilderDoesNotReplaceTheRunningSession()
    {
        using var runtime = CentralDivideScenario.Create(Basis(), TestContext.Current.CancellationToken);
        var original = runtime.RuntimeSettings; var plan = DeveloperRestartBuilder.ForExisting(original);
        var provider = Assert.IsType<DeveloperGameplayTuningProvider>(runtime.Services.DeveloperParameters);
        var request = Request(provider, DeveloperParameterId.OpponentAggression, DeveloperValue.FromDouble(.9), DeveloperTarget.ForPlayer(new(2)));
        var result = Apply(runtime.Simulation, provider, request);
        Assert.Equal(DeveloperRequestStatus.Pending, result.Result.Status); Assert.Null(result.Result.AppliedTick);
        Assert.False(provider.CaptureContribution().DeveloperModified); Assert.Same(original, runtime.RuntimeSettings);
        var row = provider.CaptureContribution().Values.Single(v => v.Id == request.Parameter && v.Target == request.Target);
        Assert.Equal(original.Scenario.OpponentConfigurations[2].Aggression, row.Effective.AsDouble()); Assert.Equal(.9, row.Pending!.Value.AsDouble());
        Assert.True(provider.TryCreateRestartPlan(plan.InitialOverrides.Metadata, out var restart, out _));
        Assert.Equal(.9, restart!.Settings.Scenario.OpponentConfigurations[2].Aggression);
        Assert.Equal(original.Composition.Identity, restart.Settings.Composition.Identity); Assert.Same(original, runtime.RuntimeSettings);
        Assert.Equal(DeveloperRestartStatus.WaitingForNewSession, provider.CaptureContribution().RestartStatus);
        var reset = Request(provider, request.Parameter, default, request.Target, DeveloperOperation.ResetParameter);
        Apply(runtime.Simulation, provider, reset); Assert.Equal(DeveloperRestartStatus.None, provider.CaptureContribution().RestartStatus);
    }

    private sealed class ChangingDispatcher : IDeveloperActionDispatcher
    {
        private int _reads;
        public DeveloperProviderStatus Status => ++_reads == 1
            ? new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None) : DeveloperProviderStatus.Missing;
        public DeveloperResult Submit(DeveloperSessionAccess access, DeveloperExecutionContext context, DeveloperRequest request) =>
            DeveloperResult.Rejected(request, context.Revision, DeveloperFailureReason.ProviderUnavailable);
    }
    [Fact]
    public void CaptureUsesOneAvailabilitySnapshot()
    {
        var fixture = new ConveyorTestFixture();
        var provider = new DeveloperGameplayTuningProvider(fixture.Simulation, DeveloperRestartBuilder.ForExisting(Basis()), new ChangingDispatcher());
        var snapshot = provider.Capture();
        Assert.Equal(DeveloperProviderAvailability.Available, snapshot.Status.Availability);
        Assert.NotEmpty(snapshot.Values);
    }

    [Fact]
    public void CompleteRestartProfilesValidateAtomicallyAndPreserveUnchangedFields()
    {
        var basis = Basis(); var metadata = DeveloperRestartBuilder.ForExisting(basis).InitialOverrides.Metadata;
        var player = DeveloperTarget.ForPlayer(new(2)); var original = basis.Scenario.OpponentConfigurations[2];
        DeveloperTuningOverride[] valid =
        [new(DeveloperParameterId.OpponentOffensiveReadiness, player, DeveloperValue.FromDouble(.2)),
         new(DeveloperParameterId.OpponentRetreat, player, DeveloperValue.FromDouble(.1)),
         new(DeveloperParameterId.RaidEnabled, player, DeveloperValue.FromBoolean(false)),
         new(DeveloperParameterId.DistributionMaximumAttempts, DeveloperTarget.Global, DeveloperValue.FromUInt32(32))];
        Assert.True(DeveloperRestartBuilder.TryBuild(basis, metadata, valid, out var plan, out _));
        Assert.Equal(.2, plan!.Settings.Scenario.OpponentConfigurations[2].OffensiveReadinessThreshold);
        Assert.Equal(.1, plan.Settings.Scenario.OpponentConfigurations[2].RetreatThreshold);
        Assert.Equal(original.MinimumAttackUnits, plan.Settings.Scenario.OpponentConfigurations[2].MinimumAttackUnits);
        Assert.NotSame(original.Raids, plan.Settings.Scenario.OpponentConfigurations[2].Raids);
        Assert.Equal(basis.Scenario.NavigationCellSizeMeters, plan.Settings.Scenario.NavigationCellSizeMeters);
        var invalid = valid.Append(new DeveloperTuningOverride(DeveloperParameterId.DistributionRetryDelay, DeveloperTarget.Global, DeveloperValue.FromUInt32(1201))).ToArray();
        Assert.False(DeveloperRestartBuilder.TryBuild(basis, metadata, invalid, out var rejected, out _)); Assert.Null(rejected);
        Assert.Same(original, basis.Scenario.OpponentConfigurations[2]);
        Assert.False(DeveloperRestartBuilder.TryBuild(basis, metadata,
            [new(DeveloperParameterId.OpponentOffensiveReadiness, player, DeveloperValue.FromDouble(.1))], out _, out _));
    }
}
#endif
