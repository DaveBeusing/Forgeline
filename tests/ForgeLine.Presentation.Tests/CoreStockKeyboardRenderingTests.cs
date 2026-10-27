using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class CoreStockKeyboardRenderingTests
{
    [Fact]
    public void KeyboardFieldSwitchMovesVisibleArcsWithoutPointerHover()
    {
        var policy = new PlayerStockPolicyActionReadModel(new(70, 1), ResourceIds.Steel, "Steel",
            0, 50, 100, 150, LogisticsStockPriority.Normal, true, default, default, default, default);
        var logistics = new PlayerLogisticsActionReadModel(new(11, 1), [policy], null, 500) { IsCommandCore = true };
        var actions = new PlayerActionSnapshot(new(1), new(1), [], 0, null, null, logistics);
        var snapshot = HoverTooltipTests.Snapshot(actions: actions);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Key(PlatformKey.L);
        using var graphics = new RecordingDevice();
        using var renderer = new PlayerActionRingHudRenderer(graphics, null);
        var geometry = new RadialActionLayout(new(800, 450), new(800, 450), 80, 176, 8);
        ActionRingSlots slots = default;
        var kinds = new[] { PlayerActionDockControlKind.DecreaseMinimum, PlayerActionDockControlKind.IncreaseMinimum,
            PlayerActionDockControlKind.DecreaseMaximum, PlayerActionDockControlKind.IncreaseMaximum };
        for (int i = 0; i < kinds.Length; i++)
            slots[i + 2] = new(new(kinds[i], PlayerActionPanelMode.Logistics, 0), default);
        var ring = new ObjectActionRingView(new(11, 1), new(1), 1, 1, true, true, geometry, true)
            { Slots = slots, Level = ActionRingLevel.Parameters };
        Assert.Equal(PlayerStockThresholdField.Minimum, View().StockThresholdField);
        AssertMarkers(2, 3);
        Key(PlatformKey.M);
        Assert.Equal(PlayerStockThresholdField.Maximum, View().StockThresholdField);
        Key(PlatformKey.Right);
        Assert.Equal(200, View().StockMaximum);
        Assert.Equal(50, View().StockMinimum);
        AssertMarkers(4, 5);
        Assert.False(controller.TryTakeRequest(out _));

        PlayerActionPanelView View() => controller.CreateView(1600, 900, actions);
        void Key(PlatformKey key)
        {
            input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
            controller.Update(input, snapshot, 1600, 900);
            input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
            controller.Update(input, snapshot, 1600, 900);
        }
        void AssertMarkers(int first, int second)
        {
            renderer.Render(new RecordingContext(), snapshot, View() with { Ring = ring },
                default, default, GameplayHudLayout.Create(1600, 900, 96));
            var markers = graphics.Buffers.SelectMany(b => b.Vertices)
                .Where(v => v.Color == new Vector4(.3f, .8f, 1, 1)).ToArray();
            Assert.Equal(240, markers.Length);
            var sectors = markers.Chunk(3).Select(triangle =>
            {
                var ndc = (triangle[0].Position + triangle[1].Position + triangle[2].Position) / 3;
                return geometry.Hit(new((ndc.X + 1) * 800, (1 - ndc.Y) * 450));
            }).Distinct().Order().ToArray();
            Assert.Equal(new[] { first, second }, sectors);
        }
    }
}
