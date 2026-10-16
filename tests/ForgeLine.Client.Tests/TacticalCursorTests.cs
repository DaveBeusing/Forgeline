using ForgeLine.Assets;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class TacticalCursorTests
{
    [Theory]
    [InlineData(96u,.75f,0)] [InlineData(96u,1f,1)] [InlineData(144u,1f,2)]
    [InlineData(192u,1f,3)] [InlineData(192u,2f,3)] [InlineData(0u,1f,0)]
    public void PointerSizeFollowsPhysicalDpiAndUserScale(uint dpi,float scale,int expected) =>
        Assert.Equal(expected,TacticalCursorSet.SizeIndex(dpi,scale));
    [Fact]
    public void AllRuntimeCursorRolesAndSizesCreateNativePointersAcrossTwoMatchSessions()
    {
        string root=AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root,"ForgeLine.sln"))) root=Directory.GetParent(root)!.FullName;
        var catalog=RuntimeAssetCatalog.Load(Path.Combine(root,"assets/runtime"));
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE runtime tactical pointers",640,360));
        for (int session=0;session<2;session++)
        {
            using var cursors=new TacticalCursorSet(window,catalog);
            foreach (var kind in Enum.GetValues<RtsCursorKind>())
                foreach (uint dpi in new uint[]{72,96,144,192}) cursors.Update(kind,dpi,1,true);
            cursors.Update(RtsCursorKind.Default,96,1,false);
        }
    }
}
