using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PracticeGuidanceTests
{
    [Theory]
    [InlineData(PracticeLessons.ReconKey)]
    [InlineData(PracticeLessons.IndustryKey)]
    [InlineData(PracticeLessons.SupplyKey)]
    public void ReadingDeferringAndDisablingNeverCompletePracticeGoals(string key)
    {
        using var runtime=MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay,17,
            composition:ScenarioRegistry.Default.Resolve(key)),TestContext.Current.CancellationToken);
        var buffer=WorldHoverExtractionTests.Observe(runtime,new PresentationInteractionState());
        runtime.Simulation.RunTicks(4,TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var guide=new EarlyGameGuidanceController();
        var first=guide.Update(snapshot,true);
        Assert.True(first.Visible);Assert.StartsWith(key,first.ContentKey);
        Assert.False(first.GoalConfirmed);
        Assert.True(PracticeLessons.TryGet(key,out var lesson));
        Assert.Equal(lesson!.Objectives.Count,first.SectionCount);
        for(int i=0;i<24;i++){guide.Navigate(GuidanceNavigation.Read);guide.Update(snapshot,true);}
        guide.Navigate(GuidanceNavigation.Later);
        Assert.False(guide.Update(snapshot,false).Visible);
        Assert.False(guide.Update(snapshot,true).GoalConfirmed);
        Assert.All(runtime.Practice!.Capture(new(1),runtime.Simulation.CurrentTick)!.CompletedAtTicks,t=>Assert.Null(t));
        Assert.False(guide.Update(snapshot,true,blocked:true).Visible);
        Assert.True(guide.Update(snapshot,true).Visible);
    }
}
