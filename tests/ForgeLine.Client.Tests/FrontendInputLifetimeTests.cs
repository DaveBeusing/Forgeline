using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendInputLifetimeTests
{
    [Fact]
    public void ResizeDpiScaleFocusOwnerAndMinimizeCancelGesturesUntilRelease()
    {
        var input = new InputState(); var lifetime = new FrontendInputLifetime();
        var normal = FrontendGeometry.Resolve(1920, 1080);
        lifetime.Update(input, normal, 1, true);
        foreach (var state in new[] { (FrontendGeometry.Resolve(1280, 720), 1, true),
            (FrontendGeometry.Resolve(1280, 720, 2, 192), 1, true),
            (FrontendGeometry.Resolve(1280, 720, 2, 192), 1, false),
            (default(FrontendGeometry), 1, false), (normal, 1, true), (normal, 2, true) })
        {
            input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 900, 860));
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Enter));
            Assert.True(lifetime.Update(input, state.Item1, state.Item2, state.Item3));
            Assert.False(input.HasPointerPosition);
            Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
            Assert.False(input.IsKeyDown(PlatformKey.Enter));
            // A repeated native down is still suppressed; only release starts another gesture.
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Enter));
            Assert.False(input.IsKeyDown(PlatformKey.Enter));
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Enter));
            input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, 900, 860));
        }
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.True(lifetime.Update(input, normal, 2, true));
    }

    [Fact]
    public void StaticLabelsDisabledAdjustmentsAndEmptySavesHaveNoActions()
    {
        var layout = FrontendDesign.ResolveLayout(1920, 1080);
        var newGame = FrontendPresentationAdapter.NewGame(new NewGameModel());
        var staticMinus = layout.Geometry.Decrease(0);
        Assert.Null(FrontendHitTesting.DetailAdjust(staticMinus.X + 1, staticMinus.Y + 1, layout, newGame));
        var seed = layout.Geometry.Increase(2);
        Assert.Equal(1, FrontendHitTesting.DetailAdjust(seed.X + 1, seed.Y + 1, layout, newGame));
        var disabled = newGame with { DetailLines = [new("SCALE", "MAX", CanDecrease: true, CanIncrease: false)] };
        var plus = layout.Geometry.Increase(0);
        Assert.Null(FrontendHitTesting.DetailAdjust(plus.X + 1, plus.Y + 1, layout, disabled));
        var load = FrontendPresentationAdapter.LoadGame(new LoadGameModel([]));
        Assert.Empty(load.PrimaryAction);
    }

    [Fact]
    public void HoverAndPressUseTheSameDrawnDetailRowAndStaticRowsRemainNeutral()
    {
        var layout = FrontendDesign.ResolveLayout(1280, 720, 2, 192);
        var view = FrontendPresentationAdapter.NewGame(new NewGameModel());
        var seed = layout.Geometry.DetailRow(2);
        var hovered = FrontendHitTesting.WithDetailPointer(view, layout, seed.X + 1, seed.Y + 1, true);
        Assert.True(hovered.DetailLines[2].IsHovered);
        Assert.True(hovered.DetailLines[2].IsPressed);
        Assert.Single(hovered.DetailLines, line => line.IsHovered);
        var label = layout.Geometry.DetailRow(0);
        var neutral = FrontendHitTesting.WithDetailPointer(view, layout, label.X + 1, label.Y + 1, true);
        Assert.DoesNotContain(neutral.DetailLines, line => line.IsHovered || line.IsPressed);
    }

    [Fact]
    public void AdaptiveSettingsAndSaveWindowsKeepKeyboardFocusReachable()
    {
        foreach (int capacity in new[] { 1, 3, 5, 7 })
        {
            var interaction = new SettingsInteractionModel { VisibleCapacity = capacity };
            for (int i = 0; i < 18; i++)
            {
                int focused = interaction.FocusedSectionIndex;
                Assert.InRange(focused - interaction.VisibleStart, 0, interaction.VisibleRowCount - 1);
                interaction.MoveNext();
            }
            var saves = new LoadGameModel(Enumerable.Range(0, 30).Select(i => new LoadGameEntry($"s{i}", new string('W', 500), $"s{i}.json", (ulong)i, LoadGameEntryState.Available)));
            saves.Focus(29);
            var view = FrontendPresentationAdapter.LoadGame(saves, capacity);
            Assert.Equal(capacity, view.DetailLines.Count);
            Assert.Single(view.DetailLines, line => line.IsFocused);
            Assert.Equal("LOAD", view.PrimaryAction);
        }
    }
}
