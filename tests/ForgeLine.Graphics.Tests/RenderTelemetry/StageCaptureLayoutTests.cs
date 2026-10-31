using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class StageCaptureLayoutTests
{
    [Fact]
    public void LogicalRowsExcludeBothOffsetAndPaddingIncludingShortLastRow()
    {
        var layout = new GraphicsTextureReadbackLayout(2, 2, 256, 512, "R8G8B8A8_UNorm");
        byte[] raw = Enumerable.Repeat((byte)205, 776).ToArray();
        for (int i = 0; i < 8; i++) { raw[512 + i] = (byte)i; raw[768 + i] = (byte)(i + 8); }
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), GraphicsReadbackRows.LogicalBytes(layout, raw));
        raw[0] = 0; raw[767] = 0;
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), GraphicsReadbackRows.LogicalBytes(layout, raw));
    }
    [Theory]
    [InlineData("R16G16B16A16_Float", 8)]
    [InlineData("R8G8B8A8_UNorm", 4)]
    [InlineData("R32_Typeless", 4)]
    [InlineData("R32_Float", 4)]
    [InlineData("D32_Float", 4)]
    public void OnlyDeclaredCaptureFormatsHaveAnExactByteWidth(string format, int width)
    { Assert.Equal(width, GraphicsReadbackRows.BytesPerTexel(format)); }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void MalformedFootprintsCannotTrimOrInventLogicalTexels(int defect)
    {
        var layout = new GraphicsTextureReadbackLayout(2, 2, 256, 0, "R8G8B8A8_UNorm");
        layout = defect switch
        {
            0 => layout with { Width = 0 }, 1 => layout with { Height = -1 },
            2 => layout with { RowPitch = 4 }, 3 => layout with { Offset = ulong.MaxValue },
            _ => layout with { Offset = 256 }
        };
        Assert.Throws<ArgumentException>(() => GraphicsReadbackRows.LogicalBytes(layout, new byte[264]));
        Assert.Throws<NotSupportedException>(() => GraphicsReadbackRows.BytesPerTexel("R8_UNorm"));
    }
    [Fact]
    public void OversizedLogicalRowFailsCheckedArithmetic()
    { Assert.Throws<OverflowException>(() => GraphicsReadbackRows.LogicalBytes(new(int.MaxValue, 1, int.MaxValue, 0, "R16G16B16A16_Float"), [])); }
    [Fact]
    public void LegacyImplementationsDoNotSilentlyEmulateNewDiagnosticCalls()
    {
        using var legacy = new LegacyDevice(); IGraphicsDevice device = legacy;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, device.CaptureFrame(default, _ => { }, true));
        Assert.Equal(1, legacy.Captures);
        Assert.Throws<NotSupportedException>(() => device.CaptureFrameDiagnostics(default, _ => { }));
        Assert.Throws<NotSupportedException>(() => device.CaptureStageFootprints());
        Assert.Equal(1, legacy.Captures);
    }
    private sealed class LegacyDevice : IGraphicsDevice
    {
        internal int Captures;
        public byte[] CaptureFrame(GraphicsColor clear, Action<IGraphicsCommandContext> record, bool captureShadowDepth = false)
        { Assert.True(captureShadowDepth); Captures++; return [1, 2, 3, 4]; }
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription value) => throw new NotSupportedException();
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription value) => throw new NotSupportedException();
        public void RenderFrame(GraphicsColor clear, Action<IGraphicsCommandContext>? record = null) => throw new NotSupportedException();
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void WaitForIdle() { }
        public void Dispose() { }
    }
}
