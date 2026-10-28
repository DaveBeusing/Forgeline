using ForgeLine.Game;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class PracticeEntryTests
{
    [Fact]
    public void PracticeChoicesAreOptionalAndNormalSkirmishRemainsFirst()
    {
        var choices=ScenarioRegistry.Default.Inspect().Select(x=>new NewGameScenarioOption(x.Descriptor.Key,x.Descriptor.DisplayName,x.Available,x.Error)).ToArray();
        var model=new NewGameModel(choices);
        Assert.Equal(CentralDivideScenario.CompositionKey,model.SelectedScenario.Key);
        Assert.True(model.CanStartSelectedScenario);
        Assert.Equal(5,model.Scenarios.Count);
        model.MoveScenario(1);model.MoveScenario(1);
        Assert.Equal(PracticeLessons.ReconKey,model.SelectedScenario.Key);
    }

    [Fact]
    public void RetryAndSkipArePracticeOnlyAndSurviveDeveloperAvailabilityRefresh()
    {
        var menu=new PauseMenuModel();
        Assert.DoesNotContain(menu.Items,x=>x.Command==PauseMenuCommand.RetryPractice);
        menu.SetPracticeAvailable(true);menu.SetDeveloperToolsAvailable(false);
        Assert.Contains(menu.Items,x=>x.Command==PauseMenuCommand.RetryPractice);
        Assert.Contains(menu.Items,x=>x.Command==PauseMenuCommand.ReturnToMenu&&x.Label=="SKIP PRACTICE / MENU");
        Assert.True(menu.TryFocus("retry-practice"));
        Assert.Equal(PauseMenuCommand.RetryPractice,menu.ActivateFocused());
        menu.SetPracticeAvailable(false);
        Assert.DoesNotContain(menu.Items,x=>x.Command==PauseMenuCommand.RetryPractice);
        Assert.Contains(menu.Items,x=>x.Command==PauseMenuCommand.ReturnToMenu&&x.Label=="RETURN TO MENU");
    }

    [Fact]
    public void RestartConstructsFreshPracticeAndNeverTransfersIntoSkirmish()
    {
        using var first=MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay,17,
            composition:ScenarioRegistry.Default.Resolve(PracticeLessons.ReconKey)),TestContext.Current.CancellationToken);
        first.Simulation.RunTicks(2,TestContext.Current.CancellationToken);
        var request=ClientSessionRequest.Restart(first.RuntimeSettings);
        using var restarted=ClientSessionFactory.CreateOwned(request,_=>{},TestContext.Current.CancellationToken);
        Assert.NotEqual(first.Simulation.SessionId,restarted.Runtime.Simulation.SessionId);
        Assert.Equal(0UL,restarted.Runtime.Simulation.CurrentTick.Value);
        Assert.NotSame(first.Practice,restarted.Runtime.Practice);
        using var normal=ClientSessionFactory.CreateOwned(ClientSessionRequest.NewGame(17),_=>{},TestContext.Current.CancellationToken);
        Assert.Null(normal.Runtime.Practice);
    }
}
