using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class UiSkinBatchTests
{
    private static readonly int[] Counts=[12,6,6];
    private static readonly string[] Order=["draw","draw","draw","text","draw"];
    [Fact]
    public void SharedLeasesReleaseAtLastOwnerAndFailedPreparationRollsBack()
    {
        using var fixture=new Fixture(); using var device=new Device();
        var first=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken); var second=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken);
        Assert.Equal(2,device.Textures.Count); Assert.Equal(2,first.LeaseCount);
        first.Dispose();Assert.All(device.Textures,t=>Assert.False(t.Disposed));
        second.Dispose();Assert.All(device.Textures,t=>Assert.True(t.Disposed));
        Assert.Throws<ObjectDisposedException>(()=>first.Sprite("ui.sprite.a"));
        using var again=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken);Assert.Equal(4,device.Textures.Count);
        using var failure=new Device{FailTextureAt=2};
        Assert.Throws<InvalidOperationException>(()=>new UiSkinResources(failure,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken));
        Assert.True(failure.Textures[0].Disposed);
    }
    [Fact]
    public void CancellationMissingAndDeletedPagesRemainFailures()
    {
        using var fixture=new Fixture();using var device=new Device();
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>new UiSkinResources(device,fixture.Catalog,cancellationToken:cancel.Token));
        Assert.Empty(device.Textures);
        File.Delete(Path.Combine(fixture.Root,"page-b.flasset"));
        Assert.Throws<FileNotFoundException>(()=>new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken));
        Assert.All(device.Textures,t=>Assert.True(t.Disposed));
    }
    [Fact]
    public void ContiguousBatchingPreservesPageOrderClipAndExternalTextBarriers()
    {
        using var fixture=new Fixture();using var device=new Device();using var resources=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken);
        using var batch=new UiSkinBatch(device,resources,maximumFlushes:2);var context=new Context();
        batch.Begin(200,100,0);
        batch.Draw("ui.sprite.a",new(0,0,20,20),Vector4.One);
        batch.Draw("ui.sprite.a",new(20,0,20,20),Vector4.One);
        batch.Draw("ui.sprite.b",new(40,0,20,20),Vector4.One);
        batch.Draw("ui.sprite.a",new(60,0,20,20),Vector4.One);
        batch.Flush(context);
        Assert.Equal(Counts,context.Draws.Select(d=>d.Count));
        Assert.Same(context.Draws[0].Texture,context.Draws[2].Texture);
        context.Events.Add("text");
        batch.Draw("ui.sprite.a",new(80,0,20,20),Vector4.One);batch.Flush(context);
        Assert.Equal(Order,context.Events);
        Assert.Equal(0,context.Draws[3].Start);
        Assert.Equal(24*32,device.Buffers[0].Bytes.Length);
        Assert.Equal(6*32,device.Buffers[1].Bytes.Length); // Distinct buffers preserve native whole-resource write guards.
        Assert.Equal(4,batch.Diagnostics.Draws);Assert.Equal(30,batch.Diagnostics.Vertices);
        Assert.Equal(30*32,batch.Diagnostics.VertexUploadBytes);
        Assert.Equal((0,0,200,100),context.Scissors[^1]);
    }
    [Fact]
    public void SmallNineSlicePartialFillAndScissorHaveFiniteBoundedUvs()
    {
        using var fixture=new Fixture();using var device=new Device();using var resources=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken);
        using var batch=new UiSkinBatch(device,resources);var context=new Context();
        batch.Begin(200,100,0);batch.SetClip(new(10,10,50,50));
        batch.Draw("ui.sprite.a",new(5,5,80,80),Vector4.One,true,.5f);
        batch.Draw("ui.sprite.a",new(20,20,1,1),Vector4.One,true);
        batch.Tile("ui.sprite.b",new(30,30,25,15),Vector4.One);batch.Flush(context);
        foreach(var v in MemoryMarshal.Cast<byte,Vertex>(device.Buffers[0].Bytes))
        {Assert.True(float.IsFinite(v.P.X)&&float.IsFinite(v.P.Y));Assert.InRange(v.U.X,0,1);Assert.InRange(v.U.Y,0,1);Assert.InRange(v.P.X,-.9f,-.4f);}
        Assert.Throws<KeyNotFoundException>(()=>batch.Draw("ui.missing",new(0,0,20,20),Vector4.One));
        Assert.Equal(1,batch.Diagnostics.MissingAssets);
        batch.Dispose();Assert.Throws<ObjectDisposedException>(()=>batch.Begin(200,100,0));
    }
    [Fact]
    public void WarmGeometryHasNoManagedAllocationsAndGrowthIsBounded()
    {
        using var fixture=new Fixture();using var device=new Device();using var resources=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken);
        using var batch=new UiSkinBatch(device,resources);
        void Draw(){batch.Begin(200,100,0);for(int i=0;i<16;i++)batch.Draw("ui.sprite.a",new(0,0,30,30),Vector4.One,true);}
        Draw();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100;i++)Draw();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        batch.Begin(200,100,0);for(int i=0;i<UiSkinBatch.MaximumQuads+1;i++)batch.Draw("ui.sprite.a",new(0,0,20,20),Vector4.One);
        Assert.Equal(UiSkinBatch.MaximumQuads*6,batch.Diagnostics.Vertices);Assert.Equal(1,batch.Diagnostics.DroppedQuads);
        Assert.Equal(4,device.Buffers.Count);
        batch.Begin(200,100,0);var context=new Context();
        batch.Draw("ui.sprite.a",new(0,0,20,20),Vector4.One);batch.Flush(context);
        batch.Draw("ui.sprite.a",new(20,0,20,20),Vector4.One);
        Assert.Throws<InvalidOperationException>(()=>batch.Flush(context));
        Assert.Equal(4,device.Buffers.Count);
    }
    [Theory]
    [InlineData(1280)]
    [InlineData(1028)]
    public void NativeStraightAlphaIsAppliedOnceOverDarkAndBrightOutput(int requestedWidth)
    {
        using var fixture=new Fixture();using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE skin alpha",requestedWidth,720));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=true});
        using(var resources=new UiSkinResources(device,fixture.Catalog,cancellationToken:TestContext.Current.CancellationToken))
        using(var batch=new UiSkinBatch(device,resources,maximumFlushes:2))
        {
            int capturedWidth=0;
            byte[] Capture(float clear)=>device.CaptureFrame(new(clear,clear,clear,1),c=>
            {capturedWidth=c.Width;c.BeginPass(GraphicsFramePass.Overlay);batch.Begin(c.Width,c.Height,c.FrameIndex);batch.Draw("ui.sprite.a",new(8,8,64,64),Vector4.One);batch.Flush(c);batch.Draw("ui.sprite.b",new(80,8,64,64),Vector4.One);batch.Flush(c);});
            var dark=Capture(0);var bright=Capture(1);int pixel=(32*capturedWidth+32)*4;
            Assert.InRange(dark[pixel],118,122);Assert.InRange(dark[pixel+1],58,62);Assert.InRange(dark[pixel+2],28,32);
            Assert.InRange(bright[pixel],245,249);Assert.InRange(bright[pixel+1],185,189);Assert.InRange(bright[pixel+2],155,159);
            Assert.Equal(dark,Capture(0));
        }
        device.WaitForIdle();Assert.Equal(0,device.Diagnostics.Resources.LoadedTextureCount);
        Assert.Equal(0,device.Diagnostics.Health.SubmissionFaultCount);Assert.Equal(0,device.Diagnostics.Debug.ErrorCount);
    }
    [Fact]
    public void NativePortraitBc7UploadPreservesLinearDisplaySamplingAndReleasesItsTextures()
    {
        using var fixture = new Fixture(portraits: true); using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE portrait BC7", 640, 360));
        using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = true });
        using (var resources = new UiSkinResources(device, fixture.Catalog, UiSkinGroup.Portraits, TestContext.Current.CancellationToken))
        using (var batch = new UiSkinBatch(device, resources))
        {
            Assert.Equal(2048, resources.PageBytes); int width = 0;
            var image = device.CaptureFrame(new(0, 0, 0, 1), context =>
            {
                width = context.Width; context.BeginPass(GraphicsFramePass.Overlay); batch.Begin(context.Width, context.Height, context.FrameIndex);
                batch.Draw("ui.sprite.a", new(8, 8, 64, 64), new(1, .5f, .25f, 1)); batch.Flush(context);
            });
            int pixel = (32 * width + 32) * 4;
            Assert.InRange((int)image[pixel], 253, 255); Assert.InRange((int)image[pixel + 1], 126, 129); Assert.InRange((int)image[pixel + 2], 62, 65);
        }
        device.WaitForIdle(); Assert.Equal(0, device.Diagnostics.Resources.LoadedTextureCount);
        Assert.Equal(0, device.Diagnostics.Health.SubmissionFaultCount); Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vertex(Vector2 P,Vector4 C,Vector2 U);
    private sealed class Fixture:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"forgeline-skin-batch-"+Guid.NewGuid().ToString("N"));
        public RuntimeAssetCatalog Catalog{get;}
        public Fixture(bool portraits = false)
        {
            Directory.CreateDirectory(Root);
            var pages=new[]{new UiSkinPage("ui.page.a",32,32,portraits ? UiSkinGroup.Portraits : UiSkinGroup.Core),new UiSkinPage("ui.page.b",32,32,portraits ? UiSkinGroup.Portraits : UiSkinGroup.Core)};
            var skin=new RuntimeUiSkinData(pages,[new("ui.sprite.a",0,2,2,20,20,20,20,2,new(8,8,8,8),default,true),new("ui.sprite.b",1,2,2,20,20,20,20,2,default,default,false)]);
            var records=new List<RuntimeAssetRecord>();
            byte[] pixels=new byte[4096];for(int i=0;i<pixels.Length;i+=4){pixels[i]=240;pixels[i+1]=120;pixels[i+2]=60;pixels[i+3]=128;}
            RuntimeTextureData texture;
            if (portraits)
            {
                byte[] blocks = new byte[1024];
                // BC7 mode 6: eight 7-bit 127 endpoints plus p-bits one, all indices zero => opaque white.
                for (int i = 0; i < blocks.Length; i += 16)
                { blocks[i] = 0xc0; for (int j = 1; j < 8; j++) blocks[i + j] = 0xff; blocks[i + 8] = 1; }
                texture = new(32, 32, RuntimeTextureFormat.Bc7Unorm, RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage, [new(32, 32, 128, blocks)]);
            }
            else texture = RuntimeTextureData.FromRgba8(32,32,pixels,RuntimeTextureColorSpace.Linear,RuntimeTextureUsage.DisplayImage);
            Add("ui.page.a",RuntimeAssetType.Texture,"page-a.flasset",texture.ToPayload(),[]);
            Add("ui.page.b",RuntimeAssetType.Texture,"page-b.flasset",texture.ToPayload(),[]);
            Add(UiSkinResources.AssetName,RuntimeAssetType.UiSkin,"skin.flasset",skin.ToPayload(),pages.Select(p=>p.Id).ToArray());
            var manifest=RuntimeAssetIntegrity.SealManifest(new(){CompilerVersion="test",Assets=records});
            File.WriteAllText(Path.Combine(Root,"manifest.json"),JsonSerializer.Serialize(manifest,RuntimeAssetCatalog.CreateJsonOptions()));
            Catalog=RuntimeAssetCatalog.Load(Root);
            void Add(string id,RuntimeAssetType type,string file,byte[] payload,string[] dependencies)
            {
                RuntimeAssetFile.Write(Path.Combine(Root,file),type,[],payload);
                records.Add(RuntimeAssetIntegrity.SealRecord(Path.Combine(Root,file),new(){Id=id,Type=type,RuntimePath=file,SourcePath="test",SourceHash=new('a',64),BuildHash=new('b',64),CompilerVersion="test",RuntimeVersion=RuntimeAssetIntegrity.SupportedRuntimeVersion,Dependencies=dependencies}));
            }
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    private sealed class Device:IGraphicsDevice
    {
        public bool SupportsUiText=>true;public int FailTextureAt{get;init;}
        public List<Texture> Textures{get;}=[];public List<Buffer> Buffers{get;}=[];
        public GraphicsDiagnostics Diagnostics=>throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription d)=>new Pipeline(d);
        public IGraphicsTexture CreateTexture(GraphicsTextureData d){if(Textures.Count+1==FailTextureAt)throw new InvalidOperationException("upload failure");var t=new Texture(d.Description);Textures.Add(t);return t;}
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription d){var b=new Buffer(d);Buffers.Add(b);return b;}
        public void RenderFrame(GraphicsColor c,Action<IGraphicsCommandContext>? commands=null)=>commands?.Invoke(new Context());
        public void Resize(int w,int h){}public void WaitForIdle(){}public void Dispose(){}
    }
    private sealed class Texture(GraphicsTextureDescription d):IGraphicsTexture
    {public GraphicsTextureDescription Description=>d;public bool Disposed{get;private set;}public void Dispose()=>Disposed=true;}
    private sealed class Pipeline(GraphicsPipelineDescription d):IGraphicsPipeline
    {public GraphicsPipelineDescription Description=>d;public void Dispose(){}}
    private sealed class Buffer(GraphicsBufferDescription d):IGraphicsBuffer
    {
        public GraphicsBufferDescription Description=>d;private byte[] _bytes=[]; public byte[] Bytes=>_bytes;
        public void SetData<T>(ReadOnlySpan<T> data,int offsetInBytes=0)where T:unmanaged
        {var source=MemoryMarshal.AsBytes(data);if(Bytes.Length<offsetInBytes+source.Length)Array.Resize(ref _bytes,offsetInBytes+source.Length);source.CopyTo(Bytes.AsSpan(offsetInBytes));}
        public void Dispose(){}
    }
    private sealed class Context:IGraphicsCommandContext
    {
        public int Width=>200;public int Height=>100;public int FrameIndex=>0;
        public List<(int Count,int Start,IGraphicsTexture? Texture)> Draws{get;}=[];
        public List<string> Events{get;}=[];public List<(int,int,int,int)> Scissors{get;}=[];private IGraphicsTexture? _texture;
        public void SetViewport(float x,float y,float w,float h){}public void SetScissor(int l,int t,int r,int b)=>Scissors.Add((l,t,r,b));
        public void SetPipeline(IGraphicsPipeline p){}public void SetPixelTexture(int slot,IGraphicsTexture t)=>_texture=t;
        public void SetVertexBuffer(IGraphicsBuffer b,int strideInBytes,int offsetInBytes=0,int inputSlot=0){}
        public void SetIndexBuffer(IGraphicsBuffer b,GraphicsIndexFormat f,int offsetInBytes=0){}public void SetVertexConstants(ReadOnlySpan<float> v){}
        public void Draw(int vertexCount,int startVertex=0){Draws.Add((vertexCount,startVertex,_texture));Events.Add("draw");}
        public void DrawIndexed(int indexCount,int startIndex=0,int baseVertex=0){}public void DrawIndexedInstanced(int indexCount,int instanceCount,int startIndex=0,int baseVertex=0,int startInstance=0){}
    }
}
