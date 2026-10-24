using ForgeLine.Game;

namespace ForgeLine.Client;

internal static class ScenarioPackageValidation
{
    internal static int Run()
    {
        try
        {
            foreach (var descriptor in ScenarioRegistry.Default.Descriptors)
            {
                var composition = ScenarioRegistry.Default.Resolve(descriptor.Key);
                using var source = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
                    MatchScenarioProfile.Validation, 2026, enableDebugCapture: true, composition: composition));
                source.Simulation.RunTicks(2);
                var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(source)));
                var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(source)));
                using var restored = MatchPersistenceService.Restore(save);
                using var playback = MatchPersistenceService.PlayReplay(replay);
                Console.WriteLine($"Qualified scenario {descriptor.Key}: map={descriptor.Identity.MapSha256}; content={descriptor.Identity.ContentSha256}; state={save.StateSha256}.");
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or MatchPersistenceException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Scenario package validation failed: {error.Message}");
            return 1;
        }
    }
}
