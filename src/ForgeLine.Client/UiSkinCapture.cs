using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class UiSkinCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static int Run(string output,string runtimeRoot)
    {
        try
        {
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Capture output must be empty.");
            Directory.CreateDirectory(output);
            var catalog=RuntimeAssetCatalog.LoadCancellable(runtimeRoot,default,true);
            using var platform=new WindowsPlatform();
            using IWindow window=platform.CreateWindow(new("FORGELINE Skin Qualification",1280,720));
            using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new(){EnableVSync=false,EnableDebugLayer=true,EnableMemoryDiagnostics=true});
            if(graphics.Diagnostics.Device.IsSoftwareAdapter)throw new NotSupportedException("Hardware capture requires an actual hardware adapter.");
            var baseline=graphics.Diagnostics.Resources;
            var cases=new List<object>();
            for(int session=0;session<3;session++)
            {
                using(var fixture=new UiSkinFixtureRenderer(graphics,catalog))
                {
                    foreach(var display in new[]{(1280,720),(1920,1080)})
                    {
                        graphics.Resize(display.Item1,display.Item2);
                        foreach(bool bright in new[]{false,true})
                        {
                            var clear=bright?new GraphicsColor(.7f,.72f,.74f,1):new GraphicsColor(.025f,.03f,.035f,1);
                            void Draw(IGraphicsCommandContext c){c.BeginPass(GraphicsFramePass.Overlay);fixture.Render(c);}
                            for(int warmup=0;warmup<3;warmup++)graphics.RenderFrame(clear,Draw);
                            byte[] a=graphics.CaptureFrame(clear,Draw),b=graphics.CaptureFrame(clear,Draw);
                            if(!a.AsSpan().SequenceEqual(b))throw new InvalidDataException("Repeated skin pixels differ.");
                            string name=$"fixture-{session}-{display.Item1}x{display.Item2}-{(bright?"bright":"dark")}.png";
                            ReferenceCaptureImage.WritePng(Path.Combine(output,name),display.Item1,display.Item2,a);
                            if(fixture.CoreDiagnostics.DroppedQuads!=0||fixture.TextDiagnostics.MissingGlyphs!=0||fixture.TextDiagnostics.DroppedGlyphs!=0)
                                throw new InvalidDataException("Incomplete native skin fixture.");
                            cases.Add(new{image=name,sha256=Hash(Path.Combine(output,name)),repeatPixelsEqual=true,core=fixture.CoreDiagnostics,
                                icons=fixture.IconDiagnostics,portraits=fixture.PortraitDiagnostics,text=fixture.TextDiagnostics,resources=graphics.Diagnostics.Resources});
                        }
                    }
                }
                graphics.WaitForIdle();
                var after=graphics.Diagnostics.Resources;
                if(after.LoadedTextureCount!=baseline.LoadedTextureCount||after.ShaderResourceDescriptorsUsed!=baseline.ShaderResourceDescriptorsUsed)
                    throw new InvalidDataException("UI session disposal retained textures or descriptors.");
            }
            // Verify the real menu integration over the same input geometry.
            using(var frontend=new FrontendOverlayRenderer(graphics))
            {
                frontend.PrepareBackground(catalog,RenderQuality.Medium);
                var view=FrontendSurfaceView.MainMenu([new("new-game","New Game",true,true),new("settings","Settings",true,false)]);
                var image=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,c=>{c.BeginPass(GraphicsFramePass.Overlay);frontend.Render(c,view);});
                ReferenceCaptureImage.WritePng(Path.Combine(output,"menu-control.png"),1920,1080,image);
                if(frontend.SkinDiagnostics.Vertices==0)throw new InvalidDataException("Real menu fixture was not drawn.");
            }
            graphics.WaitForIdle();
            if(graphics.Diagnostics.Health.SubmissionFaultCount!=0||graphics.Diagnostics.Debug.ErrorCount!=0)throw new InvalidDataException("Native graphics diagnostics contain failures.");
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
                schemaVersion=1,source=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                runtimeSignature=catalog.Manifest.ContentSignature,adapter=graphics.Diagnostics.Device,cases,baseline,final=graphics.Diagnostics,
                menuImageSha256=Hash(Path.Combine(output,"menu-control.png")),status="structural-pass",humanReview="pending",
                limits="Three repeated sessions, two output sizes, bright/dark native readback; no physical monitor or sustained frame-time acceptance."
            },JsonOptions));
            return 0;
        }
        catch(Exception e)when(e is IOException or InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException)
        {Console.Error.WriteLine(e);return 1;}
    }
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
