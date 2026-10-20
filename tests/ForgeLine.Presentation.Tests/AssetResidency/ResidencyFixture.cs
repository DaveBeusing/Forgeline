using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation.Tests.AssetResidency;

internal sealed class ResidencyFixture : IDisposable
{
    internal readonly string Root = Path.Combine(Path.GetTempPath(), "visual-residency-" + Guid.NewGuid().ToString("N"));
    internal readonly List<RuntimeAssetRecord> Records = [];
    internal ResidencyFixture(bool sealedManifest = true)
    {
        Directory.CreateDirectory(Root);
        byte[] pixels = Enumerable.Repeat((byte)127, 256).ToArray();
        Add("texture.residency.color", RuntimeAssetType.Texture, new RuntimeTextureData(8, 8, RuntimeTextureFormat.Rgba8Unorm,
            RuntimeTextureColorSpace.Srgb, RuntimeTextureUsage.Color,
            [new(8,8,32,pixels),new(4,4,16,new byte[64]),new(2,2,8,new byte[16]),new(1,1,4,new byte[4])]).ToPayload());
        AddMaterial("material.residency.main");
        Add("mesh.residency.main", RuntimeAssetType.Mesh, new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0,
            [new(-1,-1,0,0,0,1,0,0,1,0,0,1),new(-1,1,0,0,0,1,0,1,1,0,0,1),new(1,-1,0,0,0,1,1,0,1,0,0,1)],
            [0u,1u,2u],["material.residency.main"],[new(0,3,0)]).ToPayload(), ["material.residency.main"]);
        Sealed = sealedManifest;
    }
    internal bool Sealed { get; }
    internal void AddMaterial(string id) => Add(id, RuntimeAssetType.Material,
        """{"baseColorTexture":"texture.residency.color","baseColorFactor":[1,1,1,1],"metallicFactor":0,"roughnessFactor":1,"emissiveMultiplier":0,"uvScale":[1,1]}"""u8.ToArray(), ["texture.residency.color"]);
    internal void Add(string id, RuntimeAssetType type, byte[] payload, string[]? dependencies = null)
    {
        string relative = id + ".flasset";
        string path = Path.Combine(Root, relative);
        RuntimeAssetFile.Write(path, type, [], payload);
        Records.Add(RuntimeAssetIntegrity.SealRecord(path, new()
        { Id = id, Type = type, SourcePath = "visuals/source", RuntimePath = relative, SourceHash = new('a',64),
            BuildHash = new('b',64), CompilerVersion = "1.12.0", RuntimeVersion = 4, Dependencies = dependencies ?? [] }));
    }
    internal RuntimeAssetCatalog Catalog()
    {
        var manifest = new RuntimeAssetManifest { CompilerVersion = "1.12.0", Assets = Records };
        if (Sealed) manifest = RuntimeAssetIntegrity.SealManifest(manifest);
        File.WriteAllText(Path.Combine(Root, "manifest.json"), JsonSerializer.Serialize(manifest, RuntimeAssetCatalog.CreateJsonOptions()));
        return RuntimeAssetCatalog.Load(Root);
    }
    internal void DeleteContainers() { foreach (var record in Records) File.Delete(Path.Combine(Root, record.RuntimePath)); }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed class ResidencyDevice : IGraphicsDevice
{
    internal readonly List<Texture> Textures = [];
    internal readonly List<Buffer> Buffers = [];
    public bool IsRecording { get; set; }
    public GraphicsDiagnostics Diagnostics => new(new("residency",0,true,"test",false), default);
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => throw new NotSupportedException();
    public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description)
    { var buffer = new Buffer(description); Buffers.Add(buffer); return buffer; }
    public IGraphicsTexture CreateTexture(GraphicsTextureData texture)
    { var resource = new Texture(texture.Description); Textures.Add(resource); return resource; }
    public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null) => throw new NotSupportedException();
    public void Resize(int width, int height) { }
    public void WaitForIdle() { }
    public void Dispose() { }
    internal sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        internal bool Disposed;
        internal bool ThrowOnDispose;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { }
        public void Dispose() { Disposed = true; if (ThrowOnDispose) throw new InvalidOperationException("Injected retirement failure."); }
    }
    internal sealed class Texture(GraphicsTextureDescription description) : IGraphicsTexture
    {
        public GraphicsTextureDescription Description => description;
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
