using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsCursorTests
{
    [Theory]
    [InlineData(24,3,2)] [InlineData(32,4,3)] [InlineData(48,6,5)] [InlineData(64,8,6)]
    public void NativePointerPreservesHotspotDimensionsAndReleasesSessionHandles(int size,int x,int y)
    {
        using var platform = new WindowsPlatform();
        using IWindow owner = platform.CreateWindow(new("FORGELINE cursor ownership",640,360));
        var window = Assert.IsType<WindowsWindow>(owner);
        byte[] pixels = new byte[size*size*4];
        Array.Fill(pixels,(byte)255);
        var image = new WindowCursorImage(size,x,y,pixels);
        for (int session=0;session<2;session++)
        {
            owner.SetCursorImage(image);
            nint cursor = window.CursorHandle;
            Assert.NotEqual(0,cursor);
            Assert.Equal(1,window.CursorCount);
            owner.SetCursorImage(image);
            Assert.Equal(cursor,window.CursorHandle);
            Assert.NotEqual(0,GetIconInfo(cursor,out var info));
            try { Assert.Equal(0,info.IsIcon); Assert.Equal((uint)x,info.X); Assert.Equal((uint)y,info.Y); }
            finally { _=DeleteObject(info.Mask); _=DeleteObject(info.Color); }
            _=WindowsNative.SendMessage(owner.NativeHandle.Value,WindowsNative.WmKillFocus,0,0);
            owner.SetCursorImage(null);
            Assert.Equal(0,window.CursorHandle);
            _=WindowsNative.SendMessage(owner.NativeHandle.Value,WindowsNative.WmSetFocus,0,0);
            owner.SetCursorImage(image);
            owner.ReleaseCursorImages();
            Assert.Equal(0,window.CursorCount);
            Assert.Equal(0,GetIconInfo(cursor,out _));
        }
        owner.RequestClose();
        owner.ReleaseCursorImages(); // Leaving a closed match must not throw during cleanup.
    }
    [Fact]
    public void NativeResourceConvertsStraightRgbaToBottomUpPremultipliedBgraAndTransparentMask()
    {
        byte[] rgba = new byte[32*32*4]; rgba[0]=200;rgba[1]=100;rgba[2]=50;rgba[3]=128;
        var image = new WindowCursorImage(32,4,3,rgba);
        byte[] resource=image.Resource();
        Assert.Equal((ushort)4,BinaryPrimitives.ReadUInt16LittleEndian(resource));
        int offset=44+31*32*4;
        Assert.Equal(new byte[]{25,50,100,128},resource.AsSpan(offset,4).ToArray());
        Assert.Equal(0x7f,resource[44+rgba.Length+31*4]);
        Assert.Throws<ArgumentException>(()=>new WindowCursorImage(16,1,1,new byte[1024]));
        Assert.Throws<ArgumentException>(()=>new WindowCursorImage(32,32,0,rgba));
        Assert.Throws<ArgumentException>(()=>new WindowCursorImage(32,0,-1,rgba));
        Assert.Throws<ArgumentException>(()=>new WindowCursorImage(32,0,0,new byte[3]));
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint X,Y; public nint Mask,Color; }
    [DllImport("user32.dll")] private static extern int GetIconInfo(nint handle,out IconInfo info);
    [DllImport("gdi32.dll")] private static extern int DeleteObject(nint handle);
}
