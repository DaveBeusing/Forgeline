using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ReferenceFixtureHostCapture
{
    internal static int Run(string requestPath)
    {
        VisualReferenceCaptureRequest? request = null;
        bool created = false;
        try
        {
            request = JsonSerializer.Deserialize<VisualReferenceCaptureRequest>(File.ReadAllText(requestPath), VisualReferenceCapture.JsonOptions)
                ?? throw new InvalidDataException("Empty host fixture request.");
            request.Validate(); ValidateSingleCase(request);
            if (!string.Equals(request.Commit, ClientBuildIdentity.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Requested fixture source does not match the actual built host.");
            if (Directory.Exists(request.OutputRoot)) throw new IOException("Host evidence directory must be new; failed attempts are never overwritten.");
            Directory.CreateDirectory(request.OutputRoot); created = true;
            long setupStarted = Stopwatch.GetTimestamp();
            long setupAllocated = GC.GetTotalAllocatedBytes(precise: true);
            var catalog = ReferenceRuntimeSeal.Load(request.RuntimeRoot, out object runtimeSeal);
            string runtimeFingerprint = VisualReferenceCapture.RuntimeFingerprint(request.RuntimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixture = VisualReferenceScene.Create(request.Scenes[0], terrain, runtimeAssets: catalog, mechanicalSeconds: request.MechanicalSeconds);
            var display = request.Displays[0];
            uint dpi = request.RasterDpi != 0 ? request.RasterDpi : display.Height == 2160 ? 192u : display.Height == 1440 ? 144u : 96u;
            var recipe = new ReferenceFixtureRecipe(fixture, terrain, catalog, request, display, request.Profiles[0], request.Zooms[0], dpi, request.UiScales[0], runtimeFingerprint);
            using var power = new ReferenceCapturePowerLease();
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Actual Render Host Fixture", 1280, 720));
            var target = new GraphicsWindowTarget(window.NativeHandle.Value, display.Width, display.Height, false);
            var snapshots = new PresentationSnapshotBuffer();
            using var host = new ClientRenderHost(target, terrain, snapshots, recipe.CameraSettings, catalog,
                asynchronousStartup: true, renderQuality: recipe.Settings, terrainProfile: recipe.TerrainProfile, referenceFixture: recipe);
            var deadline = Stopwatch.StartNew();
            void WaitUntil(Func<bool> done)
            {
                while (!done())
                {
                    host.CheckFixtureFailure();
                    if (!platform.PumpEvents()) throw new OperationCanceledException("Host fixture window closed.");
                    if (deadline.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Declared 10-minute host fixture command deadline exceeded.");
                    Thread.Sleep(1);
                }
                host.CheckFixtureFailure();
            }
            WaitUntil(() => host.RendererInitialized);
            double setupMilliseconds = Stopwatch.GetElapsedTime(setupStarted).TotalMilliseconds;
            long setupProcessAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - setupAllocated;
            ReferenceOwnerTraceHandoff? traceHandoff = null;
            if (request.OwnerTraceControlDirectory is not null)
            {
                traceHandoff = ReferenceOwnerTraceHandoff.PublishReady(request.OwnerTraceControlDirectory,
                    request.AdapterBindingDirectory ?? throw new InvalidDataException("Owner trace requires the actual checkpoint."), recipe.Fingerprint);
                WaitUntil(traceHandoff.Attached);
            }
            for (int ordinal = 0; ordinal < recipe.FrameCount; ordinal++)
            {
                if (!platform.PumpEvents()) throw new OperationCanceledException("Host fixture window closed.");
                host.PublishFixtureFrame(ordinal);
                int expected = ordinal;
                WaitUntil(() => host.FixtureAcknowledged == expected);
            }
            if (traceHandoff is not null)
            {
                traceHandoff.PublishWindowComplete(); WaitUntil(traceHandoff.Released);
            }
            host.RequestFixtureReadback(); WaitUntil(() => host.FixtureReadbackCompleted);
            host.RequestStop(); host.Dispose();
            if (runtimeFingerprint != VisualReferenceCapture.RuntimeFingerprint(request.RuntimeRoot)) throw new InvalidDataException("Runtime assets changed during actual host fixture capture.");
            ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "fixture-manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, kind = "actual-client-render-host-reference-fixture-run", status = "structural-pass",
                sourceSha = ClientBuildIdentity.SourceRevision, runtimeFingerprint, runtimeSeal, setupMilliseconds, setupProcessAllocatedBytes,
                owner = request.CollectorMode == ReferenceCollectorMode.CollectorOff ? null : "owner.json",
                ownerSha256 = request.CollectorMode == ReferenceCollectorMode.CollectorOff ? null : VisualReferenceCapture.FileHash(Path.Combine(request.OutputRoot, "owner.json")),
                clientAssemblySha256 = VisualReferenceCapture.FileHash(typeof(Program).Assembly.Location),
                hostWindow = window.ClientSize, hostWindowDpi = window.Dpi, nativeHandle = window.NativeHandle.Value.ToInt64(),
                fixture = host.FixtureEvidence(), physicalAdmission = "Requires separately verified exact device/provider and foreground conditions"
            }, VisualReferenceCapture.JsonOptions));
            return 0;
        }
        catch (Exception error)
        {
            if (created && request is not null)
                ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "failure.json"), JsonSerializer.Serialize(new
                { status = "structural-failure", sourceSha = ClientBuildIdentity.SourceRevision, error = error.ToString() }, VisualReferenceCapture.JsonOptions));
            Console.Error.WriteLine(error); return 1;
        }
    }
    internal static void ValidateSingleCase(VisualReferenceCaptureRequest request)
    {
        if (request.Scenes.Length != 1 || request.Displays.Length != 1 || request.Profiles.Length != 1 || request.Zooms.Length != 1 ||
            request.UiScenarios.Length != 1 || request.UiScenarios[0] != UiReferenceScenario.World || request.UiScales.Length != 1 ||
            request.CinematicFrontend || request.Scenes[0] == VisualReferenceSceneKind.MaterialFoundation)
            throw new NotSupportedException("Actual host fixture commands require one authored World case; special UI/material-reference modes are explicitly unsupported.");
        if (request.CollectorMode != ReferenceCollectorMode.Full && (request.RunProfile != ReferenceRunProfile.Warm || request.Warmups != 64 || request.Samples != 512))
            throw new NotSupportedException("Collector on/off is a separate fixed64/512 warm experiment.");
        if (request.OwnerTraceControlDirectory is not null && (string.IsNullOrWhiteSpace(request.OwnerTraceControlDirectory) ||
            request.AdapterBindingDirectory is null || request.CollectorMode != ReferenceCollectorMode.Full ||
            request.RunProfile != ReferenceRunProfile.Warm || request.Warmups != 64 || request.Samples != 512 || request.CaptureStageDiagnostics))
            throw new NotSupportedException("Owner trace is a separate actual-host Full64/512 warm diagnostic with stage copies disabled.");
        _ = LiveRenderTelemetry.CompletionCapacityFor(request.Samples, request.RunProfile == ReferenceRunProfile.RendererCold ? 0 : request.Warmups);
    }
}
