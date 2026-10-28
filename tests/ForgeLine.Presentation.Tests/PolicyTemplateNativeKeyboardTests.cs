using System.Runtime.InteropServices;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[CollectionDefinition("Policy template native input", DisableParallelization = true)]
public sealed class PolicyTemplateNativeInputTestGroup;

[Collection("Policy template native input")]
public sealed class PolicyTemplateNativeKeyboardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedNativeTapsNavigateCopySavePreviewAndApplyExactlyOnce(bool remapped)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Win32 input-message integration requires Windows.");
        using var f = new Fixture(remapped);
        f.PreparePreview();
        f.Focus(9);
        var before = f.Panel.View;
        f.Key(0x5A);
        Assert.Equal(before, f.Panel.View);
        f.Tap(GameplayAction.Activate);
        Assert.True(f.Panel.View.Pending);
        Assert.Equal(PolicyTemplateRowState.Pending, Assert.Single(f.Panel.View.Rows).State);
        Assert.True(f.Panel.TryTakeRequest(out var request));
        Assert.Equal(Fixture.ExpectedRequest, request);
        Assert.False(f.Panel.TryTakeRequest(out _));
        f.Update();
        Assert.False(f.Panel.TryTakeRequest(out _));
        // A second deliberate press while awaiting the first result cannot fan out twice.
        f.Tap(GameplayAction.Activate);
        Assert.False(f.Panel.TryTakeRequest(out _));
    }

    [Fact]
    public void NativeFocusLossConsumesAnApplyTapBeforeFocusReturns()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Win32 input-message integration requires Windows.");
        using var f = new Fixture(false);
        f.PreparePreview(); f.Focus(9);
        f.Input.BeginFrame();
        _ = SendMessage(f.Window.NativeHandle.Value, 0x0008, 0, 0); // WM_KILLFOCUS
        f.SendKey(GameplayAction.Activate);
        f.Drain(); f.Update();
        Assert.False(f.Panel.TryTakeRequest(out _));
        f.Input.BeginFrame();
        _ = SendMessage(f.Window.NativeHandle.Value, 0x0007, 0, 0); // WM_SETFOCUS
        f.Drain(); f.Update();
        Assert.False(f.Panel.TryTakeRequest(out _));
        f.Tap(GameplayAction.Activate);
        Assert.True(f.Panel.TryTakeRequest(out var request));
        Assert.Equal(Fixture.ExpectedRequest, request);
        Assert.False(f.Panel.TryTakeRequest(out _));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly WindowsPlatform _platform = new();
        private readonly GameplayBindingRegistry _bindings;
        private readonly PresentationSnapshot _snapshot;
        internal IWindow Window { get; }
        internal InputState Input { get; } = new();
        internal PolicyTemplatePanelController Panel { get; }
        private static readonly EntityId Target = new(5, 1);
        internal static PlayerActionRequest ExpectedRequest => PlayerActionRequest.SetStockPolicy(Target,
            ResourceIds.FerrousOre, 25, 50, 100, LogisticsStockPriority.Normal, true);

        internal Fixture(bool remapped)
        {
            _bindings = remapped ? new GameplayBindingRegistry(new GameplayBindings()
                .With(GameplayAction.NextItem, PlatformKey.N).With(GameplayAction.Activate, PlatformKey.G))
                : GameplayBindingRegistry.Default;
            Window = _platform.CreateWindow(new WindowConfiguration("Policy template input regression", 160, 100));
            _ = ShowWindow(Window.NativeHandle.Value, 0); // Hide the native message sink; no renderer is created.
            _platform.PumpEvents();
            while (Window.TryDequeueInputEvent(out _)) { }
            _ = SendMessage(Window.NativeHandle.Value, 0x0007, 0, 0);
            Drain(); Input.BeginFrame();
            var session = new SimulationSessionId(47);
            var stock = new PolicyTemplateStockFact(ResourceIds.FerrousOre, "Ore", new(6, 1),
                1, 25, 50, 100, LogisticsStockPriority.Normal, true, 0, 0, 1000, 0);
            var target = new PolicyTemplateTargetSnapshot(Target, "Owned smelter", "", 1000, 1000,
                [stock], null, []);
            var actions = new PlayerActionSnapshot(session, SimulationTick.Zero, [], 0, null, null)
            { PolicyTemplates = new(session, SimulationTick.Zero, [target], 0) };
            _snapshot = new PresentationSnapshot(SimulationTick.Zero, TimeSpan.Zero, 0,
                ReadOnlySpan<RenderInstance>.Empty, sessionId: session, playerActions: actions);
            Panel = new(_bindings); Panel.Reset(session); Panel.Open(); Update();
        }

        internal void PreparePreview()
        {
            Assert.True(Panel.View.HasSelection);
            Focus(3); Tap(GameplayAction.Activate); // Copy the actual eligible stock fact.
            Assert.True(Panel.View.Draft.Valid); Assert.True(Panel.View.Dirty);
            Assert.False(Panel.TryTakeRequest(out _));
            Focus(7); Tap(GameplayAction.Activate); // Save locally.
            Assert.True(Panel.View.Saved); Assert.False(Panel.View.Dirty);
            Assert.False(Panel.TryTakeRequest(out _));
            Focus(8); Tap(GameplayAction.Activate); // Preview remains command-free.
            Assert.True(Panel.View.PreviewValid);
            var row = Assert.Single(Panel.View.Rows);
            Assert.Equal(PolicyTemplateRowState.Ready, row.State);
            Assert.Equal(ExpectedRequest, row.Request);
            Assert.False(Panel.TryTakeRequest(out _));
        }

        internal void Focus(int control)
        {
            for (int i = 0; Panel.View.Focus != control && i < 12; i++)
            {
                int before = Panel.View.Focus;
                Tap(GameplayAction.NextItem);
                Assert.Equal((before + 1) % OperationsRulePanelLayout.ControlCount, Panel.View.Focus);
            }
            Assert.Equal(control, Panel.View.Focus);
        }
        internal void Tap(GameplayAction action)
        {
            Input.BeginFrame(); SendKey(action); Drain();
            Assert.False(Input.IsKeyDown(_bindings.Key(action)));
            Update();
        }
        internal void Key(uint virtualKey)
        {
            Input.BeginFrame(); Send(virtualKey); Drain(); Update();
        }
        internal void SendKey(GameplayAction action)
        {
            PlatformKey key = _bindings.Key(action);
            uint virtualKey = key switch { PlatformKey.Tab => 0x09, PlatformKey.Enter => 0x0D,
                PlatformKey.N => 0x4E, PlatformKey.G => 0x47, _ => throw new InvalidOperationException() };
            Send(virtualKey);
        }
        private void Send(uint virtualKey)
        {
            _ = SendMessage(Window.NativeHandle.Value, 0x0100, virtualKey, 1);
            _ = SendMessage(Window.NativeHandle.Value, 0x0101, virtualKey, unchecked((nint)(int)0xC0000001));
        }
        internal void Drain()
        {
            _platform.PumpEvents();
            while (Window.TryDequeueInputEvent(out var input)) Input.Apply(input);
        }
        internal void Update() => Panel.Update(Input, _snapshot, GameplayHudLayout.Create(1600, 900, 96), false, null);
        public void Dispose() { Window.Dispose(); _platform.Dispose(); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint window, int command);
}
