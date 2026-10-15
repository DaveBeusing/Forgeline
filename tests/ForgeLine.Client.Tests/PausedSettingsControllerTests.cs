using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class PausedSettingsControllerTests
{
    [Fact]
    public void ApplyUpdatesReadableUiImmediatelyAndPersistsNextMatchGraphicsWithoutResettingMatch()
    {
        WithController((controller, store, window, current) =>
        {
            controller.Open(current);
            controller.Model.SetDisplay(1600, 900, false, 1.4f);
            controller.Model.SetOnboarding(false);
            controller.Model.SetRenderQuality(FrontendRenderQuality.Low);
            var applied = controller.Update(Press(PlatformKey.Enter), Layout(), default);
            Assert.NotNull(applied);
            var live = PausedSettingsController.LiveSettings(current, applied);
            Assert.Equal(1.4f, live.UiScale);
            Assert.False(live.ShowOnboarding);
            Assert.Equal(current.RenderQuality, live.RenderQuality);
            Assert.Equal(RenderQuality.Low, store.Load().Settings.RenderQuality.Quality);
            Assert.True(controller.Visible);
            Assert.Equal("BACK TO PAUSE", controller.Surface.SecondaryAction);
            controller.Open(live);
            Assert.Equal(FrontendRenderQuality.Low, controller.Model.Settings.RenderQuality);
        });
    }

    [Fact]
    public void EscapeDiscardsEditsAndConsumesClosingInput()
    {
        WithController((controller, store, window, current) =>
        {
            controller.Open(current);
            controller.Model.SetOnboarding(false);
            var input = Press(PlatformKey.Escape);
            Assert.Null(controller.Update(input, Layout(), default));
            Assert.False(controller.Visible);
            Assert.False(input.IsKeyDown(PlatformKey.Escape));
            Assert.True(store.Load().Settings.ShowOnboarding);
        });
    }

    [Fact]
    public void ChangedDisplayRequiresFreshPresentationAndEscapeRestoresPreviousWindow()
    {
        WithController((controller, store, window, current) =>
        {
            controller.Open(current);
            controller.Model.SetDisplay(1920, 1080, false, 1.25f);
            var old = Surface(1600, 900, 5);
            Assert.Null(controller.Update(Press(PlatformKey.Enter), Layout(), old));
            Assert.Equal(new WindowSize(1920, 1080), window.ClientSize);
            Assert.Null(controller.Update(Press(PlatformKey.Enter), Layout(), old));
            Assert.Equal(1600, store.Load().Settings.WindowWidth);
            controller.Update(Press(PlatformKey.Escape), Layout(), old);
            Assert.Equal(new WindowSize(1600, 900), window.ClientSize);
            Assert.False(controller.Visible);
        });
    }

    [Fact]
    public void FreshDisplayCanBeKeptAndFocusLossRollsBackUnconfirmedTrial()
    {
        WithController((controller, store, window, current) =>
        {
            controller.Open(current);
            controller.Model.SetDisplay(1920, 1080, false, 1);
            controller.Update(Press(PlatformKey.Enter), Layout(), Surface(1600,900,5));
            var saved = controller.Update(Press(PlatformKey.Enter), Layout(), Surface(1920,1080,6));
            Assert.NotNull(saved);
            Assert.Equal(1920, store.Load().Settings.WindowWidth);
            controller.Model.SetDisplay(2560, 1440, false, 1);
            controller.Update(Press(PlatformKey.Enter), Layout(), Surface(1920,1080,6));
            window.IsFocused = false;
            controller.Update(new(), Layout(), Surface(2560,1440,7));
            Assert.Equal(new WindowSize(1920,1080), window.ClientSize);
            Assert.Equal(1920, store.Load().Settings.WindowWidth);
        });
    }

    [Fact]
    public void ExpiredDisplayTrialRollsBackWithoutPersisting()
    {
        double seconds = 0;
        WithController((controller, store, window, current) =>
        {
            controller.Open(current);
            controller.Model.SetDisplay(1920, 1080, false, 1);
            controller.Update(Press(PlatformKey.Enter), Layout(), Surface(1600,900,5));
            seconds = 16;
            controller.Update(new(), Layout(), Surface(1920,1080,6));
            Assert.Equal(new WindowSize(1600,900), window.ClientSize);
            Assert.Equal(1600, store.Load().Settings.WindowWidth);
            Assert.True(controller.Visible);
        }, () => seconds);
    }

    [Fact]
    public void NewSessionStartsAtCloseTacticalDistance()
    {
        var camera = new ClientUserSettings().CreateCameraSettings(default);
        Assert.Equal(RtsVisualReference.CloseDistance, camera.InitialDistance);
        Assert.True(camera.InitialDistance < RtsVisualReference.NormalDistance);
    }

    private static InputState Press(PlatformKey key)
    {
        var input = new InputState();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,key));
        return input;
    }
    private static FrontendLayout Layout() => FrontendDesign.ResolveLayout(1600,900);
    private static GraphicsSurfaceInfo Surface(int width,int height,ulong count) =>
        new(width,height,3,0,false,"test") { PresentedFrameCount=count };
    private static void WithController(Action<PausedSettingsController,ClientSettingsStore,TestWindow,ClientUserSettings> test, Func<double>? seconds = null)
    {
        string root = Path.Combine(Path.GetTempPath(),"forgeline-pause-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            var current = new ClientUserSettings();
            store.Save(current);
            using var window = new TestWindow();
            using var controller = new PausedSettingsController(window,current,new(store),seconds);
            test(controller,store,window,current);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private sealed class TestWindow : IWindow
    {
        public NativeWindowHandle NativeHandle => default;
        public WindowSize ClientSize { get; private set; } = new(1600,900);
        public uint Dpi => 96;
        public bool IsFocused { get; set; } = true;
        public bool IsMinimized => false;
        public bool IsOpen => true;
        public WindowMode Mode { get; private set; }
        public void ConfigureDisplay(WindowConfiguration value) { ClientSize=new(value.Width,value.Height);Mode=value.Mode; }
        public void SetMode(WindowMode mode) => Mode=mode;
        public void RequestClose() { }
        public bool TryDequeueEvent(out WindowEvent value) { value=default;return false; }
        public bool TryDequeueInputEvent(out PlatformInputEvent value) { value=default;return false; }
        public void Dispose() { }
    }
}
