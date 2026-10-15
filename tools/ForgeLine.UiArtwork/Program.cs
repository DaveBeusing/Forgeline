using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Client;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.UiArtwork;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--runtime" || args[2] != "--output")
        {
            Console.Error.WriteLine("ForgeLine.UiArtwork --runtime <compiled-assets> --output <empty-output>");
            return 2;
        }
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Portrait output must be empty; previous evidence is never replaced.");
        Directory.CreateDirectory(output);
        RuntimeAssetCatalog catalog = RuntimeAssetCatalog.Load(args[1]);
        var records = new List<object>();
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Artwork Capture", 512, 512));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
        { EnableVSync = false, EnableMemoryDiagnostics = true, AllowSoftwareAdapterFallback = false });
        if (graphics.Diagnostics.Device.IsSoftwareAdapter) throw new InvalidOperationException("Native hardware adapter required.");
        var lighting = SceneLightingSettings.Default;
        graphics.ConfigureSceneOutput(new(false, lighting.Exposure, true));
        using var meshes = new SimpleInstanceRenderer(graphics, catalog, lighting: lighting);
        var identities = new List<(string Kind, uint Id, string Key, string Mesh, string? Turret, string Material, RenderInstance Instance)>();
        foreach (UnitDefinition unit in DirectorateContent.CreateUnitCatalog().Definitions.OrderBy(static d => d.Id))
        {
            UnitPresentationDefinition presentation = UnitPresentationCatalog.Get(unit.Id);
            identities.Add(("unit", unit.Id.Value, unit.Key, presentation.MeshAssetId, presentation.TurretMeshAssetId, presentation.MaterialAssetId,
                new(new EntityId(unit.Id.Value, 1), new(Vector3.Zero, Quaternion.Identity, unit.VisualScale), new(1), new(1), RenderVisibilityMask.World,
                    UnitFeature: new(unit.Id, UnitPresentationDamageState.Intact))));
        }
        foreach (BuildingDefinition building in DirectorateContent.CreateBuildingCatalog().Definitions.OrderBy(static d => d.Id))
        {
            BuildingPresentationDefinition presentation = BuildingPresentationCatalog.Get(building.Id);
            Vector3 scale = new(building.Footprint.Width, building.Footprint.Height, building.Footprint.Depth);
            identities.Add(("building", building.Id.Value, building.Key, presentation.MeshAssetId, null, presentation.MaterialAssetId,
                new(new EntityId(building.Id.Value, 1), new(Vector3.Zero, Quaternion.Identity, scale), new(1), new(1), RenderVisibilityMask.World,
                    BuildingFeature: new(building.Id, BuildingPresentationState.Operational))));
        }
        foreach (var identity in identities)
        {
            RuntimeMeshData model = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(identity.Mesh)).Payload);
            Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
            Expand(model);
            if (identity.Turret is not null) Expand(RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(identity.Turret)).Payload));
            void Expand(RuntimeMeshData mesh)
            {
                foreach (RuntimeMeshVertex vertex in mesh.Vertices)
                {
                    Vector3 point = new Vector3(vertex.X, vertex.Y, vertex.Z) * identity.Instance.Transform.Scale;
                    minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                }
            }
            Vector3 center = (minimum + maximum) * .5f;
            float radius = Vector3.Distance(minimum, maximum) * .5f;
            var camera = new RtsCamera(new()
            {
                InitialTarget = center, InitialYawRadians = MathF.PI / 4, InitialPitchRadians = -35 * MathF.PI / 180,
                VerticalFieldOfViewRadians = MathF.PI / 4, InitialDistance = radius / MathF.Sin(MathF.PI / 8) * 1.12f,
                MinimumDistance = .1f, MaximumDistance = 10000, NearPlane = .01f
            });
            var buffer = new PresentationSnapshotBuffer();
            buffer.Publish(new PresentationSnapshot(new SimulationTick(32), TimeSpan.FromMilliseconds(50), 1, new[] { identity.Instance }));
            var world = new RenderWorld(); world.Update(buffer);
            foreach (int size in new[] { 48, 64, 96, 128, 256 })
            {
                graphics.WaitForIdle(); graphics.Resize(size, size);
                Action<IGraphicsCommandContext> record = context => meshes.Render(context, camera, world, 1, 0);
                var clear = new GraphicsColor(.025f, .035f, .04f, 1);
                for (int warmup = 0; warmup < 3; warmup++) graphics.RenderFrame(clear, record);
                byte[] pixels = graphics.CaptureFrame(clear, record);
                byte[] repeated = graphics.CaptureFrame(clear, record);
                if (!pixels.AsSpan().SequenceEqual(repeated)) throw new InvalidDataException($"Unstable portrait: {identity.Key}/{size}");
                if (meshes.LastDiagnostics.FallbackMeshInstances != 0 || meshes.MaterialDiagnostics.BindingFailureCount != 0 ||
                    graphics.Diagnostics.Resources.TextureBindingFailureCount != 0)
                    throw new InvalidDataException($"Portrait used missing/fallback content: {identity.Key}");
                string name = $"{identity.Kind}_{identity.Id}_{size}.png";
                ReferenceCaptureImage.WritePng(Path.Combine(output, name), size, size, pixels);
                records.Add(new { identity.Kind, gameplayId = identity.Id, identity.Key, meshAssetId = identity.Mesh,
                    meshRuntimeHash = catalog.Get(AssetId.Parse(identity.Mesh)).RuntimeHash, identity.Turret,
                    materialAssetId = identity.Material, size, file = name,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, name)))),
                    camera = camera.CaptureState(), lighting, minimum, maximum, repeatPixelsEqual = true,
                    rendering = meshes.LastDiagnostics });
                Console.WriteLine($"Captured {identity.Key} {size}px");
            }
        }
        graphics.WaitForIdle();
        File.WriteAllText(Path.Combine(output, "portraits.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, sourceIdentity = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            runtimeContentSignature = catalog.Manifest.ContentSignature, device = graphics.Diagnostics.Device,
            method = "Native production renderer, one identity, fixed pose/light, no HUD or terrain, independently rasterized sizes, repeated readback.",
            humanReview = "pending", captures = records
        }, Json));
        return 0;
    }
}
