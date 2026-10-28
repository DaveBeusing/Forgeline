namespace ForgeLine.Game;

// Diagnostic comparison input only; never a saved state, command or player visibility feed.
public sealed record MatchReplayCheckpoint(ulong Tick, ulong RandomState, string StateSha256, int SchemaVersion = 1)
{
    public const int MaximumCount = 4096;

    public static MatchReplayCheckpoint Capture(MatchRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var snapshot = MatchAuthoritativeSnapshot.Capture(runtime);
        // Recovery pre-schedules future input. Its pending-count observation can differ
        // before the last original submission without changing the simulated world.
        // Final recovery still checks the complete, unfiltered authoritative hash.
        var comparable = snapshot with
        {
            Domains = snapshot.Domains.Where(d => d.Name != "simulation.pending_commands").ToArray()
        };
        return new(snapshot.Tick, snapshot.RandomState, comparable.ComputeSha256());
    }
}
