namespace ForgeLine.Game;

internal static class DeveloperPersistenceGuard
{
    internal static bool Requires(PersistedMatchConfiguration configuration, IReadOnlyList<RecordedSimulationCommand> commands,
        IReadOnlyList<DeveloperParameterAuditData>? changes = null, IReadOnlyList<AuthoritativeDomainSnapshot>? domains = null) =>
        configuration.DeveloperInitialProfile is not null ||
        commands.Any(c => c.Kind == ReplayCommandKind.DeveloperAction || c.DeveloperPayload is not null) ||
        changes is not null || domains?.Any(d => d?.Name?.StartsWith("developer.", StringComparison.Ordinal) == true) == true;
    internal static void Validate(int schema, PersistedMatchConfiguration configuration, IReadOnlyList<RecordedSimulationCommand> commands,
        IReadOnlyList<DeveloperParameterAuditData>? changes = null, IReadOnlyList<AuthoritativeDomainSnapshot>? domains = null,
        bool developerModified = false)
    {
        bool containsDeveloperData = Requires(configuration, commands, changes, domains);
        bool required = developerModified || containsDeveloperData;
        if (required && (schema < 8 || !DeveloperToolsCapability.Current.IsCompiled))
            throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion, "Developer payloads require schema 8 and a developer build.");
        MatchPersistenceValidation.Require(!developerModified || containsDeveloperData, "Developer modification marker has no corresponding developer state.");
        if (configuration.DeveloperInitialProfile is { } initial)
        {
            try
            {
                var profile = initial.ToProfile();
                MatchPersistenceValidation.Require(profile.Metadata.CompositionKey == configuration.CompositionKey &&
                    profile.Metadata.BaseIdentity == configuration.Identity && profile.Metadata.Seed == configuration.Seed &&
                    profile.Metadata.Profile == configuration.Scenario.Profile && profile.Metadata.Participants.SequenceEqual(configuration.Participants),
                    "Initial overrides do not match their canonical base configuration.");
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Invalid initial developer profile.", error); }
        }
        int pending = 0;
        foreach (var command in commands)
        {
            if (command.Kind != ReplayCommandKind.DeveloperAction && command.DeveloperPayload is null) continue;
            MatchPersistenceValidation.Require(command.Kind == ReplayCommandKind.DeveloperAction && command.DeveloperPayload is not null && !command.IsControl,
                "Developer payloads require their explicit normal-tick command kind.");
            try { _ = command.DeveloperPayload!.ToRequest(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
            { throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Invalid bounded developer command payload.", error); }
            if (command.DeveloperPayload!.ExecutionActivated is null && ++pending > 128)
                throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Too many pending developer commands.");
        }
        if (changes is null) return;
        MatchPersistenceValidation.Require(changes.Count <= DeveloperParameterAuditData.MaximumEntries, "Parameter audit exceeds its fixed limit.");
        MatchPersistenceValidation.Require((long)changes.Count <= (long)commands.Count * DeveloperRequest.MaximumValues, "Unbounded parameter audit.");
        ulong sequence = 0, tick = 0;
        foreach (var change in changes)
        {
            MatchPersistenceValidation.Require(change is not null && change.Sequence != 0 && change.Sequence >= sequence && change.Tick >= tick &&
                change.Correlation != Guid.Empty && change.Revision != 0 && DeveloperParameterCatalog.TryGet(change.Id, out _), "Invalid parameter audit order.");
            try
            {
                var target = change!.Target.ToTarget();
                DeveloperParameterCatalog.TryGet(change.Id, out var descriptor);
                MatchPersistenceValidation.Require((descriptor.Targets & target.Kind) != 0 && change.Before.ToValue().Kind == descriptor.Kind &&
                    change.After.ToValue().Kind == descriptor.Kind && (!change.PendingBefore.HasValue || change.PendingBefore.Value.ToValue().Kind == descriptor.Kind) &&
                    (!change.PendingAfter.HasValue || change.PendingAfter.Value.ToValue().Kind == descriptor.Kind), "Invalid parameter audit values.");
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
            { throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Invalid parameter audit data.", error); }
            sequence = change!.Sequence; tick = change.Tick;
        }
    }
}
