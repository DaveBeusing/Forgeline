using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GuidanceLessonTests
{
    [Theory]
    [InlineData(1280, 720, 192u, 2f)]
    [InlineData(1920, 1080, 96u, 1f)]
    [InlineData(3840, 2160, 192u, 2f)]
    [InlineData(1024, 720, 144u, .75f)]
    public void EveryLessonAndActiveBindingRemainsReachableWithoutEllipsis(int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Activate, PlatformKey.J)
            .With(GameplayAction.NextItem, PlatformKey.G));
        var guide = new EarlyGameGuidanceController();
        var snapshot = EarlyGameGuidanceTests.Snapshot(0);
        Assert.Equal(EarlyGameGuidanceCatalog.Steps.Length, EarlyGameGuidanceCatalog.Steps.Select(x => x.Key).Distinct().Count());
        foreach (var lesson in EarlyGameGuidanceCatalog.Steps)
        {
            var view = guide.Update(snapshot, true, bindings: bindings, layout: layout);
            Assert.Equal(lesson.Key, view.ContentKey);
            Assert.InRange(view.PageCount, 1, 8);
            var panel = GameplayGuidanceLayout.Resolve(layout, true);
            Assert.True(panel.Height > layout.OnboardingBounds.Height);
            Assert.True(panel.Bottom <= OperationsLayout.Entry(layout).Y - layout.Gap + .01f);
            var pages = new List<string>();
            for (int page = 0; page < view.PageCount; page++)
            {
                view = guide.Update(snapshot, true, bindings: bindings, layout: layout);
                Assert.Equal(page, view.Page);
                foreach (string line in view.Context.Split('\n'))
                    Assert.True(UiFontMetrics.Default.Measure(line, GuidanceCardLayout.TextSize(layout)) <= GameplayGuidanceLayout.Resolve(layout, true).Width - 20);
                Assert.InRange(GuidanceCardLayout.TextSize(layout), 14, 20);
                pages.Add(view.Context);
                guide.Navigate(GuidanceNavigation.NextPage);
            }
            string complete = string.Join(" ", pages).Replace('\n', ' ');
            Assert.Contains(lesson.Context, complete);
            Assert.Contains("J ACTIVATE", complete);
            Assert.Contains("G NEXT ITEM", complete);
            guide.Navigate(GuidanceNavigation.NextSection);
        }
    }

    [Fact]
    public void ReadingDeferralAndMinimizedProgressAreIndependentAndResetWithSession()
    {
        var guide = new EarlyGameGuidanceController();
        var first = EarlyGameGuidanceTests.Snapshot(0);
        var initial = guide.Update(first, true);
        guide.Navigate(GuidanceNavigation.Later);
        guide.Update(first, true);
        guide.Navigate(GuidanceNavigation.PreviousSection);
        Assert.Equal(GuidanceExplanationState.Deferred, guide.Update(first, true).ExplanationState);
        var completed = EarlyGameGuidanceTests.Snapshot(PlayerGuidanceMilestone.CommandCore, tick: 2);
        Assert.False(guide.Update(completed, false).Visible);
        var restored = guide.Update(completed, true);
        Assert.True(restored.GoalConfirmed);
        Assert.Equal(GuidanceExplanationState.Deferred, restored.ExplanationState);
        for (int i = 0; i < restored.PageCount; i++) { guide.Update(completed, true); guide.Navigate(GuidanceNavigation.Read); }
        guide.Update(completed, true);
        guide.Navigate(GuidanceNavigation.PreviousSection);
        Assert.Equal(GuidanceExplanationState.Viewed, guide.Update(completed, true).ExplanationState);
        var replacement = guide.Update(EarlyGameGuidanceTests.Snapshot(0, session: 2), true);
        Assert.False(replacement.GoalConfirmed);
        Assert.Equal(GuidanceExplanationState.Offered, replacement.ExplanationState);
        Assert.Equal(0, replacement.Page);
    }
    [Fact]
    public void StaleOrForeignEvidenceCannotAdvanceCopiedMilestones()
    {
        var valid = EarlyGameGuidanceTests.Snapshot(0);
        var entity = new ForgeLine.Core.EntityId(5, 1);
        var stale = new ForgeLine.Game.GameplayProgressEvent(1, new(2), valid.Tick, new(1),
            ForgeLine.Game.GameplayProgressKind.CargoUnloaded, entity, entity, entity, 1, ForgeLine.Core.ResourceIds.FerrousOre, 10, 1);
        var summary = valid.Guidance!.Value with { Observed = PlayerGuidanceMilestone.OreDelivered, Evidence = new[] { stale } };
        var snapshot = new PresentationSnapshot(valid.Tick, TimeSpan.Zero, 0, [], sessionId: valid.SessionId,
            playerExperience: valid.PlayerExperience, guidance: summary);
        var guide = new EarlyGameGuidanceController();
        Assert.False(guide.Update(snapshot, true).Visible);
        Assert.Equal(PlayerGuidanceMilestone.None, guide.Observed);
    }

}
