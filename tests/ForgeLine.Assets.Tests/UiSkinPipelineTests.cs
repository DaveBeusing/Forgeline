using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class UiSkinPipelineTests
{
    [Fact]
    public void BinaryContractRoundTripsAndRejectsTruncationVersionAndTrailingBytes()
    {
        var skin = Fixture(); var payload = skin.ToPayload(); var round = RuntimeUiSkinData.FromPayload(payload);
        Assert.Equal(skin.Pages, round.Pages); Assert.Equal(skin.Sprites, round.Sprites);
        Assert.Equal(payload, round.ToPayload());
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.FromPayload(payload[..^1]));
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.FromPayload([.. payload, 0]));
        var malformed = (byte[])payload.Clone(); malformed.AsSpan(12,5).Fill(255);
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.FromPayload(malformed));
        payload[0] = 255;
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.FromPayload(payload));
    }
    [Theory]
    [InlineData("region")]
    [InlineData("insets")]
    [InlineData("hotspot")]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    [InlineData("nan")]
    public void RejectsInvalidRegionsInsetsHotspotsIdentityAndPadding(string invalid)
    {
        var fixture = Fixture(); var s = fixture.Sprites[0];
        var sprites = invalid switch
        {
            "region" => new[] { s with { X = 31 } }, "insets" => [s with { Insets = new(30,0,30,0) }],
            "hotspot" => [s with { HotspotX = 100, HotspotY = 0 }], "duplicate" => [s,s],
            "overlap" => [s,s with { Id = "ui.other" }], "nan" => [s with { LogicalWidth = float.NaN }],
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<InvalidDataException>(() => new RuntimeUiSkinData(fixture.Pages, sprites));
    }
    [Fact]
    public void PageRequiresDisplayRgbaExactDimensionsAndOneMip()
    {
        var page = Fixture().Pages[0];
        var valid = RuntimeTextureData.FromRgba8(32,32,new byte[4096],RuntimeTextureColorSpace.Linear,RuntimeTextureUsage.DisplayImage);
        RuntimeUiSkinData.ValidatePage(page, valid);
        var wrong = RuntimeTextureData.FromRgba8(32,32,new byte[4096],RuntimeTextureColorSpace.Srgb,RuntimeTextureUsage.BaseColor);
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.ValidatePage(page, wrong));
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.ValidatePage(page with { Width = 31 }, valid));
    }
    [Fact]
    public void CanonicalAtlasCompilesDeterministicallyWithSealedDependenciesAndExactSourcePixels()
    {
        string source = Path.Combine(Repository(),"assets/source/ui/directorate/atlas");
        string root = Path.Combine(Path.GetTempPath(),"forgeline-atlas-"+Guid.NewGuid().ToString("N"));
        try
        {
            
            Assert.True(AssetPipelineCompiler.Compile(source,Path.Combine(root,"a")).Success);
            Assert.True(AssetPipelineCompiler.Compile(source,Path.Combine(root,"b")).Success);
            var a = RuntimeAssetCatalog.LoadCancellable(Path.Combine(root,"a"),TestContext.Current.CancellationToken,true);
            var b = RuntimeAssetCatalog.LoadCancellable(Path.Combine(root,"b"),TestContext.Current.CancellationToken,true);
            Assert.Equal(a.Manifest.ContentSignature,b.Manifest.ContentSignature);
            var id = AssetId.Parse("ui.skin.directorate.compiled");
            var data = RuntimeUiSkinData.FromPayload(a.Read(id).Payload);
            Assert.Equal(836,data.Sprites.Count); Assert.Equal(7,data.Pages.Count); Assert.Equal(28*1024*1024,data.PageBytes);
            Assert.DoesNotContain(data.Sprites,s=>s.Id.Contains("patrol",StringComparison.Ordinal));
            Assert.Equal(data.Pages.Select(p=>p.Id).Order(StringComparer.Ordinal), a.Get(id).Dependencies.Order(StringComparer.Ordinal));
            Assert.True(RuntimeAssetQualification.Run(Path.Combine(root,"a"),true).Success);
            // Verify packed pixels/extrusion with the independently compiled original texture family.
            string originalSource = Path.Combine(root,"original"); Directory.CreateDirectory(originalSource);
            var sprite = data.Sprites.Single(s=>s.Id=="ui.skin.directorate.control.button");
            string directorate = Path.GetDirectoryName(source)!;
            foreach (string file in new[] { "control_button.png", "control_button.asset.json" })
                File.Copy(Path.Combine(directorate,file),Path.Combine(originalSource,file));
            Assert.True(AssetPipelineCompiler.Compile(originalSource,Path.Combine(root,"original-runtime")).Success);
            var original = RuntimeTextureData.FromPayload(RuntimeAssetCatalog.Load(Path.Combine(root,"original-runtime")).Read(AssetId.Parse(sprite.Id)).Payload);
            var packed = RuntimeTextureData.FromPayload(a.Read(AssetId.Parse(data.Pages[sprite.Page].Id)).Payload);
            var pixels=packed.Mips[0]; var src=original.Mips[0];
            for(int y=0;y<sprite.Height;y++)
                Assert.Equal(src.Pixels.AsSpan(y*src.RowPitch,sprite.Width*4).ToArray(),pixels.Pixels.AsSpan((sprite.Y+y)*pixels.RowPitch+sprite.X*4,sprite.Width*4).ToArray());
            for(int offset=1;offset<=sprite.Padding;offset++)
                Assert.Equal(src.Pixels.AsSpan(0,sprite.Width*4).ToArray(),pixels.Pixels.AsSpan((sprite.Y-offset)*pixels.RowPitch+sprite.X*4,sprite.Width*4).ToArray());
            File.Delete(Path.Combine(root,"a",a.Get(AssetId.Parse(data.Pages[0].Id)).RuntimePath));
            Assert.False(RuntimeAssetQualification.Run(Path.Combine(root,"a"),true).Success);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact]
    public void CompilerRejectsMissingPageAndInvalidAuthoredRegions()
    {
        string root=Path.Combine(Path.GetTempPath(),"forgeline-atlas-invalid-"+Guid.NewGuid().ToString("N"));
        try
        {
            string source=Path.Combine(root,"source");Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source,"skin.asset.json"),"""{"id":"ui.skin.test","type":"uiSkin","source":"skin.json"}""");
            var skin=Fixture();
            string Json(UiSkinSprite sprite)=>JsonSerializer.Serialize(new{schemaVersion=1,pages=skin.Pages,sprites=new[]{sprite}},RuntimeAssetCatalog.CreateJsonOptions());
            File.WriteAllText(Path.Combine(source,"skin.json"),Json(skin.Sprites[0]));
            Assert.False(AssetPipelineCompiler.Compile(source,Path.Combine(root,"runtime")).Success);
            File.WriteAllText(Path.Combine(source,"skin.json"),Json(skin.Sprites[0] with{X=999}));
            Assert.False(AssetPipelineCompiler.Compile(source,Path.Combine(root,"runtime")).Success);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static RuntimeUiSkinData Fixture()=>new([new("ui.page.test",32,32,UiSkinGroup.Core)],
        [new("ui.sprite.test",0,2,2,20,20,20,20,2,new(4,4,4,4),new(4,4,4,4),true)]);
    private static string Repository()
    {for(var p=new DirectoryInfo(AppContext.BaseDirectory);p is not null;p=p.Parent)if(File.Exists(Path.Combine(p.FullName,"ForgeLine.sln")))return p.FullName;throw new DirectoryNotFoundException();}
}
