using System.Reflection;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DeveloperToolsFoundationTests
{
    private static readonly DeveloperStateRevision Revision = new(DeveloperRevisionDomain.Parameters, 7);
    private static DeveloperRequest Request(SimulationSessionId? session = null, DeveloperStateRevision? revision = null,
        DeveloperTarget? target = null, DeveloperParameterId parameter = DeveloperParameterId.ConveyorThroughputMultiplier,
        DeveloperOperation operation = DeveloperOperation.SetParameter, DeveloperValue[]? values = null) =>
        new(session ?? new(1), Guid.NewGuid(), revision ?? Revision, operation, target ?? DeveloperTarget.Global,
            parameter, values ?? [DeveloperValue.FromDouble(2)]);
    private static DeveloperExecutionContext Context(SimulationSessionId? session = null, DeveloperStateRevision? revision = null) =>
        new(session ?? new(1), revision ?? Revision, p => p == new PlayerId(2), e => e == new EntityId(10, 2));

    [Fact]
    public void BuildAuthorityMatchesMetadataAndHasNoPersistedOrPublicConstructionPath()
    {
        var assembly = typeof(DeveloperToolsCapability).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        var capability = DeveloperToolsCapability.Current;
        Assert.Equal(capability.IsCompiled, bool.Parse(metadata["DeveloperToolsEnabled"]!));
        Assert.Equal(capability.BuildVariant, metadata["BuildVariant"]);
        Assert.Equal(capability.IsCompiled, assembly.GetType("ForgeLine.Game.CompiledDeveloperToolsHost") is not null);
        Assert.Empty(typeof(DeveloperToolsCapability).GetConstructors());
        Assert.Empty(typeof(DeveloperSessionAccess).GetConstructors());
        Assert.DoesNotContain(typeof(PersistedMatchConfiguration).GetProperties(), p => p.PropertyType == typeof(DeveloperToolsCapability));
        Assert.DoesNotContain(typeof(MatchRuntimeSettings).GetProperties(), p => p.PropertyType == typeof(DeveloperSessionAccess));
        Assert.False(capability.BindSession(new(1), activated: false).IsActive);
        Assert.Equal(capability.IsCompiled, capability.BindSession(new(1), activated: true).IsActive);
        Assert.Throws<ArgumentException>(() => capability.BindSession(default, true));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteNumbersCannotEnterAnyRequest(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DeveloperValue.FromDouble(value));

    [Fact]
    public void ExplicitValueConversionsRetainLargeIntegersWithoutCoercion()
    {
        Assert.Equal(uint.MaxValue, DeveloperValue.FromUInt32(uint.MaxValue).AsUInt32());
        Assert.Equal(int.MinValue, DeveloperValue.FromInt32(int.MinValue).AsInt32());
        Assert.Throws<InvalidOperationException>(() => DeveloperValue.FromUInt32(1).AsDouble());
        Assert.Throws<InvalidOperationException>(() => DeveloperValue.FromDouble(1).AsBoolean());
        Assert.False(default(DeveloperValue).IsValid);
    }

    [Fact]
    public void ExplicitCatalogHasOneTickParameterAndPreservesExistingDomainLimits()
    {
        var descriptors = DeveloperParameterCatalog.Descriptors;
        Assert.Equal(Enum.GetValues<DeveloperParameterId>().Length - 1, descriptors.Count);
        Assert.Equal(descriptors.Count, descriptors.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(DeveloperParameterId.ConveyorThroughputMultiplier, Assert.Single(descriptors, d => d.ApplyMode == DeveloperApplyMode.NextTick).Id);
        foreach (var descriptor in descriptors)
        {
            var target = descriptor.Targets == DeveloperTargetKinds.Global ? DeveloperTarget.Global : DeveloperTarget.ForPlayer(new(2));
            Assert.True(descriptor.Accepts(descriptor.Default, target));
            Assert.True(DeveloperParameterCatalog.TryGet(descriptor.Id, out var found));
            Assert.Same(descriptor, found);
            if (descriptor.Id != DeveloperParameterId.ConveyorThroughputMultiplier) Assert.Equal(DeveloperApplyMode.NextMatch, descriptor.ApplyMode);
        }
        Assert.Equal("attempts", descriptors.Single(d => d.Id == DeveloperParameterId.DistributionMaximumAttempts).Unit);
        Assert.Equal("contacts", descriptors.Single(d => d.Id == DeveloperParameterId.RaidMaximumContacts).Unit);
        Assert.Equal("shots", descriptors.Single(d => d.Id == DeveloperParameterId.RaidMinimumShots).Unit);
        Assert.Equal("threat", descriptors.Single(d => d.Id == DeveloperParameterId.RaidMaximumKnownThreat).Unit);
        Assert.Equal("Raids", descriptors.Single(d => d.Id == DeveloperParameterId.RaidReadiness).Category);
        Assert.False(DeveloperParameterCatalog.TryGet((DeveloperParameterId)ushort.MaxValue, out _));
        var throughput = descriptors.Single(d => d.Id == DeveloperParameterId.ConveyorThroughputMultiplier);
        Assert.True(throughput.Accepts(DeveloperValue.FromDouble(.25), DeveloperTarget.Global));
        Assert.True(throughput.Accepts(DeveloperValue.FromDouble(4), DeveloperTarget.Global));
        Assert.False(throughput.Accepts(DeveloperValue.FromDouble(.249), DeveloperTarget.Global));
        Assert.False(throughput.Accepts(DeveloperValue.FromDouble(4.001), DeveloperTarget.Global));
        Assert.False(throughput.Accepts(DeveloperValue.FromUInt32(1), DeveloperTarget.Global));
        Assert.False(throughput.Accepts(DeveloperValue.FromDouble(1), DeveloperTarget.ForEntity(new(10, 2))));
        Assert.Equal(1200U, descriptors.Single(d => d.Id == DeveloperParameterId.RaidActiveTicks).Maximum.AsUInt32());
        Assert.Equal(32, descriptors.Single(d => d.Id == DeveloperParameterId.RaidMaximumContacts).Maximum.AsInt32());
    }

    [Fact]
    public void RequestBoundsAndCopiesPreventPayloadReplacement()
    {
        DeveloperValue[] values = [DeveloperValue.FromDouble(2)];
        var request = Request(values: values);
        values[0] = DeveloperValue.FromDouble(3);
        Assert.Equal(2, request.Values[0].AsDouble());
        Assert.Throws<ArgumentException>(() => Request(session: default(SimulationSessionId)));
        Assert.Throws<ArgumentException>(() => Request(values: Enumerable.Repeat(DeveloperValue.FromDouble(1), 17).ToArray()));
        Assert.Throws<ArgumentException>(() => Request(values: [default]));
    }

    [Theory]
    [InlineData(0, DeveloperFailureReason.None)]
    [InlineData(1, DeveloperFailureReason.StaleSession)]
    [InlineData(2, DeveloperFailureReason.StaleRevision)]
    [InlineData(3, DeveloperFailureReason.InvalidTarget)]
    [InlineData(4, DeveloperFailureReason.UnsupportedOperation)]
    [InlineData(5, DeveloperFailureReason.UnsupportedParameter)]
    [InlineData(6, DeveloperFailureReason.InvalidValue)]
    public void GateRechecksCurrentSessionRevisionFullEntityAndSupportedPayload(int variant, DeveloperFailureReason expected)
    {
        var permission = DeveloperToolsCapability.Current.BindSession(new(1), true);
        var request = variant switch
        {
            1 => Request(session: new(2)),
            2 => Request(revision: new(DeveloperRevisionDomain.Parameters, 6)),
            3 => Request(target: DeveloperTarget.ForEntity(new(10, 1))),
            4 => Request(operation: (DeveloperOperation)byte.MaxValue),
            5 => Request(parameter: (DeveloperParameterId)ushort.MaxValue),
            6 => Request(values: [DeveloperValue.FromDouble(5)]),
            _ => Request()
        };
        var actual = DeveloperRequestGate.Check(permission, Context(), request, new HashSet<DeveloperOperation> { DeveloperOperation.SetParameter });
        Assert.Equal(permission.Capability.IsCompiled ? expected : DeveloperFailureReason.CapabilityUnavailable, actual);
        var inactive = DeveloperToolsCapability.Current.BindSession(new(1), false);
        Assert.Equal(inactive.Capability.IsCompiled ? DeveloperFailureReason.SessionNotActivated : DeveloperFailureReason.CapabilityUnavailable,
            DeveloperRequestGate.Check(inactive, Context(), Request(), new HashSet<DeveloperOperation> { DeveloperOperation.SetParameter }));
        // Submission approval cannot survive a revision advance before execution.
        Assert.Equal(permission.Capability.IsCompiled ? DeveloperFailureReason.StaleRevision : DeveloperFailureReason.CapabilityUnavailable,
            DeveloperRequestGate.Check(permission, Context(revision: new(DeveloperRevisionDomain.Parameters, 8)), Request(), new HashSet<DeveloperOperation> { DeveloperOperation.SetParameter }));
    }

    [Fact]
    public void MissingProvidersNeverReportAppliedOrAvailable()
    {
        var providers = DeveloperToolsComposition.Create();
        IDeveloperProvider[] all = [providers.Menu, providers.Sessions, providers.Actions, providers.Parameters, providers.Presets];
        Assert.All(all, p => Assert.Equal(DeveloperProviderStatus.Missing, p.Status));
        Assert.Empty(providers.Menu.Operations);
        var read = providers.Parameters.Capture();
        Assert.Equal(DeveloperProviderStatus.Missing, read.Status); Assert.Empty(read.Values);
        var result = providers.Actions.Submit(providers.Capability.BindSession(new(1), true), Context(), Request());
        Assert.Equal(DeveloperRequestStatus.Rejected, result.Status);
        Assert.Equal(providers.Capability.IsCompiled ? DeveloperFailureReason.ProviderUnavailable : DeveloperFailureReason.CapabilityUnavailable, result.Failure);
        Assert.Null(result.AppliedTick); Assert.Empty(result.AffectedValues);
    }

    [Fact]
    public void ResultsSeparateEnqueueFromActualTypedEffectsAndCopyTheirFacts()
    {
        var request = Request();
        Assert.Null(DeveloperResult.Accepted(request, Revision).AppliedTick);
        Assert.Null(DeveloperResult.Pending(request, Revision).AppliedTick);
        Assert.Throws<ArgumentException>(() => DeveloperResult.Rejected(request, Revision, DeveloperFailureReason.None));
        DeveloperAppliedValue[] effects = [DeveloperAppliedValue.ForResource(ResourceIds.Fuel, DeveloperTarget.ForEntity(new(10, 2)), 3.5)];
        var result = DeveloperResult.Applied(request, Revision, new(9), effects);
        effects[0] = DeveloperAppliedValue.ForSpawnedEntity(new(11, 3));
        Assert.Equal(DeveloperRequestStatus.Applied, result.Status);
        Assert.Equal(new SimulationTick(9), result.AppliedTick);
        Assert.Equal(3.5, Assert.Single(result.AffectedValues).Value.AsDouble());
        Assert.Equal(new EntityId(10, 2), result.AffectedValues[0].Target.Entity);
        Assert.Throws<ArgumentException>(() => DeveloperResult.Applied(request, Revision, new(9), [default]));
    }

    [Fact]
    public void ParameterObservationsAreCopiedAndUnavailableCannotContainEffectiveValues()
    {
        DeveloperEffectiveParameter[] values = [new(DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperTarget.Global, DeveloperValue.FromDouble(1), Revision)];
        var read = new DeveloperParameterReadResult(new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None), values);
        values[0] = values[0] with { Value = DeveloperValue.FromDouble(2) };
        Assert.Equal(1, Assert.Single(read.Values).Value.AsDouble());
        Assert.Throws<ArgumentException>(() => new DeveloperParameterReadResult(DeveloperProviderStatus.Missing, values));
    }

    [Fact]
    public void PresetIdentityAndInitialValuesAreCopiedDataWithoutPermission()
    {
        var descriptor = ScenarioRegistry.CentralDivideDescriptor;
        var participants = CentralDivideScenario.CreateDefaultParticipants(false, true).ToArray();
        var metadata = new DeveloperPresetMetadata(1, descriptor.Key, descriptor.Identity, 2026, MatchScenarioProfile.Gameplay, participants, true);
        participants[0] = participants[1];
        Assert.NotEqual(metadata.Participants[0].Player, metadata.Participants[1].Player);
        DeveloperEffectiveParameter[] values = [new(DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperTarget.Global,
            DeveloperValue.FromDouble(1.25), new(DeveloperRevisionDomain.Parameters, 0))];
        var profile = new DeveloperInitialProfile(metadata, values);
        values[0] = default;
        Assert.Equal(1.25, Assert.Single(profile.Parameters).Value.AsDouble());
        Assert.Throws<ArgumentException>(() => new DeveloperPresetMetadata(2, descriptor.Key, descriptor.Identity, 2026, MatchScenarioProfile.Gameplay, metadata.Participants.ToArray(), true));
        Assert.Throws<ArgumentException>(() => new DeveloperPresetMetadata(1, descriptor.Key, descriptor.Identity with { MapSha256 = new string('0', 64) }, 2026, MatchScenarioProfile.Gameplay, metadata.Participants.ToArray(), true));
        Assert.False(DeveloperToolsCapability.Current.BindSession(new(1), false).IsActive);
    }

    [Fact]
    public void ResetIsAnotherOrderedChangeAndCannotClearStickyAuthoritativeModification()
    {
        var before = DeveloperValue.FromDouble(1.25); var after = DeveloperValue.FromDouble(2);
        var change = new DeveloperParameterChange(1, Guid.NewGuid(), new(5), DeveloperParameterId.ConveyorThroughputMultiplier,
            DeveloperTarget.Global, before, after, new(DeveloperRevisionDomain.Parameters, 1), false);
        var modified = DeveloperModificationState.Unmodified.AfterApplied(change);
        var reset = new DeveloperParameterChange(2, Guid.NewGuid(), new(6), change.Parameter, change.Target,
            after, before, new(DeveloperRevisionDomain.Parameters, 2), true);
        var restored = modified.AfterApplied(reset);
        Assert.True(restored.DeveloperModified); Assert.Equal(2UL, restored.LastSequence);
        Assert.True(reset.IsReset); Assert.Equal(before, reset.After);
        Assert.Throws<InvalidOperationException>(() => restored.AfterApplied(reset));
        var noChange = new DeveloperParameterChange(1, Guid.NewGuid(), new(5), change.Parameter, change.Target,
            before, before, new(DeveloperRevisionDomain.Parameters, 1), false);
        Assert.False(DeveloperModificationState.Unmodified.AfterApplied(noChange).DeveloperModified);
    }
}
