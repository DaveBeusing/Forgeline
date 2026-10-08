using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12GraphicsResizeIntegrationTests
{
    [Fact]
    public void BorderlessResizeSuspensionAndRestoreResumePresentation()
    {
        using var platform =
            new WindowsPlatform();
        using IWindow window =
            platform.CreateWindow(
                new WindowConfiguration(
                    "FORGELINE Graphics Resize Test",
                    1_280,
                    720,
                    resizable: true,
                    WindowMode.Windowed));

        using IGraphicsDevice graphics =
            GraphicsDeviceFactory.CreateForWindow(
                window,
                new GraphicsConfiguration
                {
                    AllowSoftwareAdapterFallback = true,
                    EnableDebugLayer = false,
                    EnableVSync = false,
                    EnableMemoryDiagnostics = true
                });

        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear);

        GraphicsSurfaceInfo initial =
            graphics.Diagnostics.Surface;

        var memory = graphics.Diagnostics.Memory;
        Assert.NotNull(memory);
        if (memory.Available)
            Assert.True(memory.LocalBudgetBytes > 0 || memory.NonLocalBudgetBytes > 0);
        else
            Assert.False(string.IsNullOrWhiteSpace(memory.FailureReason));

        Assert.True(
            initial.SubmittedFrameCount > 0);
        Assert.True(
            initial.PresentedFrameCount > 0);

        window.SetMode(
            WindowMode.BorderlessFullscreen);
        graphics.Resize(
            window.ClientSize.Width,
            window.ClientSize.Height);

        int renderedWidth = 0;
        int renderedHeight = 0;
        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear,
            context =>
            {
                renderedWidth =
                    context.Width;
                renderedHeight =
                    context.Height;
            });

        GraphicsSurfaceInfo borderless =
            graphics.Diagnostics.Surface;

        Assert.False(
            borderless.IsSuspended);
        Assert.False(
            borderless.ResizePending);
        Assert.Equal(
            window.ClientSize.Width,
            borderless.Width);
        Assert.Equal(
            window.ClientSize.Height,
            borderless.Height);
        Assert.InRange(
            borderless.FrameIndex,
            0,
            borderless.BufferCount - 1);
        Assert.Equal(
            borderless.Width,
            renderedWidth);
        Assert.Equal(
            borderless.Height,
            renderedHeight);
        Assert.True(
            borderless.SubmittedFrameCount >
            initial.SubmittedFrameCount);
        Assert.True(
            borderless.PresentedFrameCount >
            initial.PresentedFrameCount);
        Assert.False(
            borderless.IsOccluded);

        graphics.Resize(
            0,
            0);
        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear);

        GraphicsSurfaceInfo suspended =
            graphics.Diagnostics.Surface;

        Assert.True(
            suspended.IsSuspended);
        Assert.False(
            suspended.ResizePending);
        Assert.Equal(
            borderless.SubmittedFrameCount,
            suspended.SubmittedFrameCount);
        Assert.Equal(
            borderless.PresentedFrameCount,
            suspended.PresentedFrameCount);

        graphics.Resize(
            window.ClientSize.Width,
            window.ClientSize.Height);

        GraphicsSurfaceInfo restorePending =
            graphics.Diagnostics.Surface;

        Assert.True(
            restorePending.IsSuspended);
        Assert.True(
            restorePending.ResizePending);

        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear);

        GraphicsSurfaceInfo restored =
            graphics.Diagnostics.Surface;

        Assert.False(
            restored.IsSuspended);
        Assert.False(
            restored.ResizePending);
        Assert.Equal(
            restored.ResizeGeneration,
            restored.AppliedResizeGeneration);
        Assert.InRange(
            restored.FrameIndex,
            0,
            restored.BufferCount - 1);
        Assert.True(
            restored.SubmittedFrameCount >
            suspended.SubmittedFrameCount);
        Assert.True(
            restored.PresentedFrameCount >
            suspended.PresentedFrameCount);
        Assert.False(
            restored.IsOccluded);

        window.SetMode(
            WindowMode.Windowed);
        graphics.Resize(
            window.ClientSize.Width,
            window.ClientSize.Height);
        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear);

        GraphicsSurfaceInfo windowed =
            graphics.Diagnostics.Surface;

        Assert.False(
            windowed.IsSuspended);
        Assert.False(
            windowed.ResizePending);
        Assert.Equal(
            window.ClientSize.Width,
            windowed.Width);
        Assert.Equal(
            window.ClientSize.Height,
            windowed.Height);
        Assert.True(
            windowed.SubmittedFrameCount >
            restored.SubmittedFrameCount);
        Assert.True(
            windowed.PresentedFrameCount >
            restored.PresentedFrameCount);

        ulong previousPresentedFrames =
            windowed.PresentedFrameCount;

        for (int transition = 0;
             transition < 4;
             transition++)
        {
            WindowMode requestedMode =
                transition % 2 == 0
                    ? WindowMode.BorderlessFullscreen
                    : WindowMode.Windowed;

            window.SetMode(
                requestedMode);
            graphics.Resize(
                window.ClientSize.Width,
                window.ClientSize.Height);
            graphics.RenderFrame(
                GraphicsColor.ForgeLineClear);

            GraphicsSurfaceInfo current =
                graphics.Diagnostics.Surface;

            Assert.Equal(
                requestedMode,
                window.Mode);
            Assert.False(
                window.ClientSize.IsEmpty);
            Assert.False(
                current.IsSuspended);
            Assert.False(
                current.ResizePending);
            Assert.False(
                current.IsOccluded);
            Assert.Equal(
                window.ClientSize.Width,
                current.Width);
            Assert.Equal(
                window.ClientSize.Height,
                current.Height);
            Assert.InRange(
                current.FrameIndex,
                0,
                current.BufferCount - 1);
            Assert.True(
                current.PresentedFrameCount >
                previousPresentedFrames);

            previousPresentedFrames =
                current.PresentedFrameCount;
        }
    }
}
