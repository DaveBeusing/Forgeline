using Xunit;
namespace ForgeLine.Presentation.Tests;
public sealed class FrontendMenuGeometryTests
{
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(3440,1440)]
    [InlineData(3840,2160)]
    [InlineData(5120,2160)]
    public void BackgroundFillsViewportAndContentIsNotLetterboxed(int width,int height)
    {
        var g=FrontendGeometry.Resolve(width,height);
        Assert.Equal(new FrontendRegion(0,0,width,height),g.Background);
        float margin=82*MathF.Min(width/1920f,height/1080f);
        Assert.InRange(MathF.Abs(g.SafeArea.X-margin),0,.51f);
        Assert.InRange(MathF.Abs(g.SafeArea.Right-(width-margin)),0,1.01f);
    }
    [Theory]
    [InlineData(1280,720,192,2)]
    [InlineData(1920,1080,96,1)]
    [InlineData(3440,1440,144,1)]
    [InlineData(3840,2160,192,2)]
    public void ControlsTableHasSeparateColumnsAndBoundedRows(int width,int height,uint dpi,float scale)
    {
        var g=FrontendGeometry.Resolve(width,height,scale,dpi,true);
        Assert.Equal(52,g.DetailStride);
        for(int i=0;i<g.VisibleDetailRows;i++)
        {
            var label=g.DetailLabel(i);var value=g.DetailValue(i,false);
            Assert.True(label.Right<value.X);Assert.Equal(label.Y,value.Y);
            Assert.True(value.Right<=g.Details.Right);
            Assert.True(g.DetailRow(i).Bottom<g.PrimaryAction.Y);
        }
    }
    [Fact]
    public void EmbeddedWordmarkIsBoundedVectorArtwork()
    {
        Assert.InRange(FrontendBrandMark.Triangles.Length,3,4096);
        Assert.Equal(0,FrontendBrandMark.Triangles.Length%3);
        Assert.All(FrontendBrandMark.Triangles,v=>{
            Assert.InRange(v.Position.X,0,FrontendBrandMark.Width);
            Assert.InRange(v.Position.Y,0,FrontendBrandMark.Height);
        });
        Assert.True(FrontendBrandMark.Triangles.Select(v=>v.Color).Distinct().Count()>1);
    }
}
