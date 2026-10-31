using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// Immutable evidence handoff from actual session composition; never an external JSON context.
internal sealed record LiveSessionOrigin(string Fingerprint, string InputBytesBase64, JsonElement Inputs)
{
    internal static LiveSessionOrigin Create(ClientSessionRequest request, MatchRuntimeSettings actual, bool smoke,
        int declaredAdditionalInstances, RenderQualitySettings quality, SceneLightingSettings lighting,
        SceneAtmosphereSettings atmosphere, ParticleSettings particles, RtsCameraSettings camera)
    {
        string? saveHash = null;
        if (request.Save is { } save)
        {
            using var file = File.OpenRead(save.Path);
            saveHash = Convert.ToHexString(SHA256.HashData(file));
        }
        var s = actual.Scenario;
        byte[] bytes = ReferenceFixtureRecipe.SerializeIdentityInputs(new
        {
            originVersion = 1, request.Kind, actual.Seed, scenarioKey = actual.Composition.Key,
            scenarioIdentity = actual.Composition.Identity, request.GeneratedManifestSha256, request.ReloadScenario,
            load = request.Save is { } saved ? new { saved.Id, saved.SavedTick, saved.State, currentFileSha256 = saveHash, loadedStateByteIdentityAvailable = false, identityScope = "Current file reopened after load; not authoritative loaded save bytes" } : null,
            scenario = new { s.Profile, s.StartingStock, s.NavigationCellSizeMeters, s.NavigationSectorSizeCells,
                s.DistributionRetryDelayTicks, s.DistributionMaximumTransportAttempts, s.DistributionFairnessAgingTicks,
                opponents = s.OpponentConfigurations.OrderBy(p => p.Key).Select(p => new { player = p.Key, settings = p.Value }).ToArray() },
            participants = actual.Participants.ToArray(), actual.EnableDiagnostics, actual.EnablePhaseTiming,
            actual.EnableDebugCapture, actual.EnableSpatialQueryTiming, actual.InitialEntityCapacity,
            developerInitialProfile = actual.InitialDeveloperProfile is not { } developer ? null : new
            {
                metadata = new { developer.Metadata.Schema, developer.Metadata.CompositionKey, developer.Metadata.BaseIdentity,
                    developer.Metadata.Seed, developer.Metadata.Profile, participants = developer.Metadata.Participants.ToArray(), developer.Metadata.RequiresDeveloperTools },
                parameters = developer.Parameters.Select(p => new { p.Id, p.Target, value = DeveloperStoredValue.From(p.Value), p.Revision }).ToArray()
            },
            smoke, declaredAdditionalInstances, quality, lighting, atmosphere, particles, camera,
            scope = "Actual composed session origin, not gameplay-success proof; per-frame callback/snapshot session and completed-tick progression are separately required"
        });
        using var document = JsonDocument.Parse(bytes);
        return new(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes), document.RootElement.Clone());
    }
}
