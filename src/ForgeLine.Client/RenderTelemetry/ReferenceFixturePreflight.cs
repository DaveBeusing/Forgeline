using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Game;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// Proposal construction from real runtime data/capability reads, not a rendering or policy approval.
internal static class ReferenceFixturePreflight
{
    internal static int Run(string requestPath)
    {
        VisualReferenceCaptureRequest? request = null;
        bool created = false;
        try
        {
            byte[] requestBytes = File.ReadAllBytes(requestPath);
            request = JsonSerializer.Deserialize<VisualReferenceCaptureRequest>(requestBytes, VisualReferenceCapture.JsonOptions)
                ?? throw new InvalidDataException("Empty fixture preflight request.");
            request.Validate(); ReferenceFixtureHostCapture.ValidateSingleCase(request);
            if (request.OwnerTraceControlDirectory is not null || request.CaptureStageDiagnostics || request.CaptureShadowDiagnostics || request.ContinueAfterMismatch)
                throw new NotSupportedException("No-frame proposal preflight must not carry separate trace/stage/mismatch diagnostic options.");
            if (!string.Equals(request.Commit, ClientBuildIdentity.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Preflight must use the requested actual built producer source.");
            if (Directory.Exists(request.OutputRoot)) throw new IOException("Preflight output must be new; previous proposals remain immutable.");
            string payloadRoot = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            if (Path.GetFullPath(request.OutputRoot).StartsWith(payloadRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Preflight evidence cannot mutate the executable payload directory.");
            Directory.CreateDirectory(request.OutputRoot); created = true;
            var payloadBefore = Inventory(payloadRoot);
            var catalog = ReferenceRuntimeSeal.Load(request.RuntimeRoot, out object seal);
            string runtime = VisualReferenceCapture.RuntimeFingerprint(request.RuntimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixture = VisualReferenceScene.Create(request.Scenes[0], terrain, runtimeAssets: catalog, mechanicalSeconds: request.MechanicalSeconds);
            var display = request.Displays[0];
            uint dpi = request.RasterDpi != 0 ? request.RasterDpi : display.Height == 2160 ? 192u : display.Height == 1440 ? 144u : 96u;
            var recipe = new ReferenceFixtureRecipe(fixture, terrain, catalog, request, display, request.Profiles[0], request.Zooms[0], dpi, request.UiScales[0], runtime);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Recipe Preflight (No Frames)", display.Width, display.Height));
            using var graphics = GraphicsDeviceFactory.CreateForWindowTarget(new(window.NativeHandle.Value, display.Width, display.Height, false), new()
            { EnableVSync = false, EnableMemoryDiagnostics = true, ForceSoftwareAdapter = request.Software, AllowSoftwareAdapterFallback = request.Software });
            var capabilities = new CapabilitySnapshot(graphics);
            var configuration = ClientRenderConfiguration.Resolve(capabilities, recipe.Settings, display.Width, display.Height,
                SceneLightingSettings.Default, recipe.Atmosphere);
            if (graphics.Diagnostics.Surface.SubmittedFrameCount != 0 || graphics.PresentedFrameCount != 0) throw new InvalidDataException("Recipe preflight must not present a frame.");
            var payloadAfter = Inventory(payloadRoot);
            if (!payloadBefore.SequenceEqual(payloadAfter) || runtime != VisualReferenceCapture.RuntimeFingerprint(request.RuntimeRoot))
                throw new InvalidDataException("Producer/runtime inputs changed while assembling the proposal.");
            var value = new { schemaVersion = 1, kind = "actual-fixture-preflight-proposal", accepted = false, qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, buildIdentity = ClientBuildIdentity.InformationalVersion,
                runtimeFingerprint = runtime, runtimeSeal = seal, payloadBefore, payloadAfter,
                requestSha256 = Hash(requestBytes), requestBytesBase64 = Convert.ToBase64String(requestBytes),
                clientAssemblySha256 = VisualReferenceCapture.FileHash(typeof(Program).Assembly.Location),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                device = graphics.Diagnostics.Device, adapterIdentity = (graphics as IGraphicsRenderTelemetry)?.DeviceIdentity,
                capabilityInputs = capabilities.Evidence, compositionApplied = false, renderingValidated = false,
                submittedOrPresentedFrames = 0, createdAtUtc = DateTimeOffset.UtcNow,
                origin = new { kind = "authored-reference-fixture", actualGameplayAttestationAvailable = false,
                    scene = fixture.Kind, seed = VisualReferenceScene.Seed, fixtureVersion = VisualReferenceScene.FixtureVersion, recipe.SnapshotFingerprint },
                @case = new { id = recipe.CaseKey, scene = request.Scenes[0], quality = request.Profiles[0], zoom = request.Zooms[0],
                    width = display.Width, height = display.Height, runProfile = request.RunProfile, cameraPath = request.CameraPath,
                    rasterDpi = dpi, uiScenario = request.UiScenarios[0], warmups = recipe.Warmups, samples = recipe.Samples,
                    recipeFingerprint = recipe.Fingerprint, recipe = recipe.IdentityInputs, recipeInputBytesBase64 = recipe.InputBytesBase64,
                    initialCamera = recipe.InitialCamera, sampleCameras = recipe.SampleCameras,
                    effectiveProfile = configuration.Profile, lighting = configuration.Lighting, atmosphere = configuration.Atmosphere },
                limitations = "Device creation/capability selection only: no Apply/renderer/RenderFrame/CaptureFrame/Present; effective allocation, consumed mixed content, physical controls and policy acceptance remain unproven." };
            ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "preflight.json"), JsonSerializer.Serialize(value, VisualReferenceCapture.JsonOptions));
            return 0;
        }
        catch (Exception error)
        {
            if (created && request is not null) ReferenceCaptureManifest.Write(Path.Combine(request.OutputRoot, "failure.json"),
                JsonSerializer.Serialize(new { kind = "actual-fixture-preflight-proposal", accepted = false, qualified = false,
                    status = "failed", sourceSha = ClientBuildIdentity.SourceRevision, error = error.ToString() }, VisualReferenceCapture.JsonOptions));
            Console.Error.WriteLine(error); return 1;
        }
    }
    private static string Hash(ReadOnlySpan<byte> raw) => Convert.ToHexString(SHA256.HashData(raw));
    internal static InventoryEntry[] Inventory(string root)
    {
        int visited = 1; var paths = new List<string>(); var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Payload cannot traverse reparse points.");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visited > 16384) throw new IOException("Payload inventory entry count exceeds declared bound.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Payload cannot contain reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path); else paths.Add(path);
                if (paths.Count + directories.Count > 16384) throw new IOException("Payload inventory exceeds bounded entry count.");
            }
        }
        return paths.OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .Select(path => new InventoryEntry(Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length, VisualReferenceCapture.FileHash(path).ToLowerInvariant())).ToArray();
    }
    internal sealed record InventoryEntry(string Path, long Bytes, string Sha256);

    // Snapshot every actual capability once, so Resolve consumes precisely the exported inputs.
    internal sealed class CapabilitySnapshot : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics { get; }
        public bool SupportsPostProcessing { get; }
        public bool SupportsParticleRendering { get; }
        public bool SupportsDirectionalShadows { get; }
        public bool SupportsAtmosphere { get; }
        public ulong? AvailableMemoryBudgetBytes { get; }
        internal object Evidence => new { SupportsPostProcessing, SupportsParticleRendering, SupportsDirectionalShadows,
            SupportsAtmosphere, AvailableMemoryBudgetBytes, Diagnostics.Device.IsSoftwareAdapter, Diagnostics.Surface.BufferCount,
            actualSurface = Diagnostics.Surface, scope = "Actual device readbacks consumed by Resolve; configuration not applied" };
        internal CapabilitySnapshot(IGraphicsDevice source)
        {
            Diagnostics = source.Diagnostics; SupportsPostProcessing = source.SupportsPostProcessing;
            SupportsParticleRendering = source.SupportsParticleRendering; SupportsDirectionalShadows = source.SupportsDirectionalShadows;
            SupportsAtmosphere = source.SupportsAtmosphere; AvailableMemoryBudgetBytes = source.AvailableMemoryBudgetBytes;
        }
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => throw new NotSupportedException();
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => throw new NotSupportedException();
        public void RenderFrame(GraphicsColor clear, Action<IGraphicsCommandContext>? commands = null) => throw new NotSupportedException("Preflight cannot draw.");
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void WaitForIdle() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
