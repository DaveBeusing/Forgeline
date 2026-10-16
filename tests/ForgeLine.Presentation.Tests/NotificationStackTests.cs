using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class NotificationStackTests
{
    [Fact]
    public void DisplayMatrixUsesVisibleDockAndLeavesEmptySpaceAndGapsForWorld()
    {
        foreach (var display in new[] { (1280,720), (1920,1080), (2560,1440), (3440,1440), (3840,2160) })
        foreach (uint dpi in new uint[] { 96,144,192 })
        foreach (float scale in new[] { .75f,1,2 })
        foreach (bool expanded in new[] { false,true })
        {
            var layout = GameplayHudLayout.Create(display.Item1, display.Item2, dpi, scale);
            var stack = ActionableAlertLayout.Resolve(Snapshot(), layout, expanded);
            var dock = layout.VisibleActionDock(expanded);
            Assert.Equal(dock.Right, stack.Bounds.Right, 3);
            Assert.Equal(dock.Bottom + layout.Gap, stack.Bounds.Y, 3);
            Assert.Equal(14*MathF.Max(1,layout.DockContentScale),stack.FontSize);
            Assert.InRange(stack.Count,0,4);
            Assert.False(stack.Bounds.Intersects(layout.Minimap));
            int detailRows=OperationsLayout.NotificationRows(layout);
            Assert.InRange(detailRows,1,6);
            float detailSize=OperationsLayout.NotificationTextSize(layout);
            Assert.Equal(stack.FontSize,detailSize);
            Assert.True(UiFontMetrics.Default.Measure("CLOSE",detailSize) <= OperationsLayout.NotificationControl(layout,0).Width-8);
            var detail=OperationsLayout.NotificationControl(layout,detailRows+2);
            Assert.True(detail.Height>=detailSize+4);
            Assert.True(detail.Bottom<OperationsLayout.NotificationControl(layout,1).Y);
            Assert.True(OperationsLayout.Panel(layout).Contains(new(detail.Right,detail.Bottom)));
            for (int i = 0; i < stack.Count; i++)
            {
                var card = stack.Card(i);
                Assert.Equal(dock.Right, card.Bounds.Right,3);
                Assert.True(stack.Bounds.Contains(new(card.Bounds.X,card.Bounds.Y)));
                Assert.True(stack.Bounds.Contains(new(card.Bounds.Right,card.Bounds.Bottom)));
                Assert.False(card.Bounds.Intersects(dock));
                Assert.False(card.Bounds.Intersects(layout.Minimap));
                Assert.False(card.Bounds.Intersects(OperationsLayout.Panel(layout)));
                Assert.Equal(i,stack.Hit(new(card.Bounds.X+1,card.Bounds.Y+1)));
                if (i+1 < stack.Count)
                    Assert.False(stack.Contains(new(card.Bounds.X+1,(card.Bounds.Bottom+stack.Card(i+1).Bounds.Y)/2)));
            }
            Assert.False(stack.Contains(new(stack.Bounds.Right+1,stack.Bounds.Y+1)));
            Assert.Equal(0,ActionableAlertLayout.Resolve(Experience() with { Alerts = 0 },layout,expanded).Count);
        }
    }

    [Fact]
    public void CriticalSeveritySurvivesSummaryOnlySpace()
    {
        var snapshot=Snapshot(); var layout=GameplayHudLayout.Create(1280,720,192,2);
        var stack=ActionableAlertLayout.Resolve(snapshot,layout,true);
        Assert.Equal(1,stack.Count); Assert.Equal(5,stack.First.Item.Hidden);
        Assert.Equal(HudAlertSeverity.Critical,NotificationText.Severity(snapshot.PlayerExperience!.Value,stack.First.Item));
        Span<char> text=stackalloc char[256];
        Assert.StartsWith("CRITICAL / MORE",NotificationText.Format(snapshot.PlayerExperience.Value,snapshot.Alerts,stack.First.Item,text).ToString());
    }

    [Fact]
    public void ThreeCardsAndOverflowKeepPriorityAndReachHiddenContent()
    {
        var snapshot = Snapshot(); var layout = GameplayHudLayout.Create(1920,1080,96);
        var stack = ActionableAlertLayout.Resolve(snapshot,layout);
        Assert.Equal(4,stack.Count);
        Assert.Equal(PlayerAlertState.CommandCoreDestroyed,stack.First.Item.Kind);
        Assert.Equal(PlayerAlertState.CommandCoreDamaged,stack.Second.Item.Kind);
        Assert.Equal(PlayerAlertState.SupplyCritical,stack.Third.Item.Kind);
        Assert.True(stack.Third.Truncated);
        Assert.Equal(2,stack.Overflow.Item.Hidden);
        var input = new InputState(); var controller = new ActionableAlertController();
        controller.Update(input,snapshot,layout);
        Click(input,stack.Overflow.Bounds);
        var result = controller.Update(input,snapshot,layout);
        Assert.True(result.Captured); Assert.True(result.NotificationDetails);
        Assert.False(controller.Update(input,snapshot,layout).NotificationDetails);
        Click(input,stack.Third.Details);
        Assert.True(controller.Update(input,snapshot,layout).NotificationDetails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenIdentityAndTargetGenerationChangesRejectHeldOverflow(bool generation)
    {
        var before = Snapshot(); var layout = GameplayHudLayout.Create(1920,1080,96);
        var rows = before.Alerts!.Alerts.ToArray();
        rows[4] = generation ? rows[4] with { Target = new EntityId(8,2) } :
            rows[4] with { Identity = rows[4].Identity with { Started = new(5) } };
        var after = Snapshot(rows);
        var input = new InputState(); var controller = new ActionableAlertController();
        controller.Update(input,before,layout);
        var rect = ActionableAlertLayout.Resolve(before,layout).Overflow.Bounds;
        Press(input,rect,true); controller.Update(input,before,layout);
        input.BeginFrame(); Press(input,rect,false);
        Assert.False(controller.Update(input,after,layout).NotificationDetails);
    }

    [Fact]
    public void HiddenCountChangeRejectsOverflowGesture()
    {
        var snapshot=Snapshot(); var layout=GameplayHudLayout.Create(1920,1080,96);
        var input=new InputState(); var controller=new ActionableAlertController();
        controller.Update(input,snapshot,layout);
        var rect=ActionableAlertLayout.Resolve(snapshot,layout).Overflow.Bounds;
        Press(input,rect,true); controller.Update(input,snapshot,layout);
        input.BeginFrame(); Press(input,rect,false);
        var changed=Snapshot(experience:Experience() with { BlockedProductionFacilities=2 });
        Assert.False(controller.Update(input,changed,layout).NotificationDetails);
    }

    [Fact]
    public void ExpiredCommandCannotTransferGestureToNextCardAndRepeatedResultDoesNotExtendLifetime()
    {
        var experience = Experience() with { Alerts = PlayerAlertState.LowPower,
            Feedback = PlayerCommandFeedback.None with { Kind=PlayerCommandFeedbackKind.Movement,
                State=PlayerCommandFeedbackState.Accepted,ResolvedAtTick=new(4),AcceptedTargets=1 } };
        var visible = Snapshot(experience:experience with { Tick=new(84) });
        var expired = Snapshot(experience:experience with { Tick=new(85) });
        var layout=GameplayHudLayout.Create(1920,1080,96);
        var stack=ActionableAlertLayout.Resolve(visible,layout);
        Assert.True(stack.First.Item.Feedback);
        Assert.False(ActionableAlertLayout.Resolve(expired,layout).First.Item.Feedback);
        var input=new InputState(); var controller=new ActionableAlertController();
        controller.Update(input,visible,layout);
        Press(input,stack.Second.Bounds,true); controller.Update(input,visible,layout);
        input.BeginFrame(); Press(input,stack.Second.Bounds,false);
        var result=controller.Update(input,expired,layout);
        Assert.False(result.Target.IsValid); Assert.False(result.OpenOperations);
    }

    [Fact]
    public void NewSessionAndChangedDockRejectPendingGesture()
    {
        var snapshot=Snapshot(); var layout=GameplayHudLayout.Create(1920,1080,96);
        var input=new InputState(); var controller=new ActionableAlertController();
        controller.Update(input,snapshot,layout);
        var rect=ActionableAlertLayout.Resolve(snapshot,layout).Overflow.Bounds;
        Press(input,rect,true); controller.Update(input,snapshot,layout);
        input.BeginFrame(); Press(input,rect,false);
        Assert.False(controller.Update(input,snapshot,layout,dockExpanded:true).OpenOperations);
        var next=Snapshot(session:2);
        controller.Update(input,next,layout);
        Click(input,rect);
        Assert.False(controller.Update(input,snapshot,layout).OpenOperations);
        Assert.False(controller.Update(input,next,layout).OpenOperations);
    }

    [Fact]
    public void DefaultPresentationFeedbackIsEmptyInsteadOfThrowing()
    {
        var layout=GameplayHudLayout.Create(1920,1080,96); var snapshot=Snapshot();
        var stack=ActionableAlertLayout.Resolve(snapshot,layout,localFeedback:default(PreAlphaUxView).InteractionHint);
        Assert.Equal(4,stack.Count);
        var details=new NotificationDetailText(); details.Update(snapshot,240,14,default(PreAlphaUxView).InteractionHint);
        Assert.True(details.PageCount>0);
    }

    [Fact]
    public void FullDetailPaginationPreservesLongLabelsBeyondSharedWrapLimitAndCachesWarmFrames()
    {
        string label=string.Join(" ",Enumerable.Repeat("Ammunition shortage at the assigned depot.",500))+" END OF AUTHORIZED MESSAGE";
        var rows=Snapshot().Alerts!.Alerts.ToArray(); rows[2]=rows[2] with { Label=label };
        var snapshot=Snapshot(rows); var details=new NotificationDetailText();
        details.Update(snapshot,240,14,"");
        Assert.True(details.PageCount>43);
        int pages=details.PageCount;
        var text=new System.Text.StringBuilder();
        for (int page=0;page<pages;page++)
        { details.Update(snapshot,240,14,"",page); foreach (string line in details.Lines) text.Append(line).Append(' '); }
        Assert.Contains("END OF AUTHORIZED MESSAGE",text.ToString());
        for(int i=0;i<128;i++) details.Update(snapshot,240,14,"",pages-1);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<256;i++) details.Update(snapshot,240,14,"",pages-1);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }

    [Fact]
    public void MoreOpensDetailsImmediatelyWhilePointerRemainsOverStack()
    {
        var input=new InputState(); var alerts=new ActionableAlertController(); var operations=new OperationsController();
        var interaction=new PresentationInteractionState(); var snapshot=Snapshot();
        var layout=GameplayHudLayout.Create(1920,1080,96);
        alerts.Update(input,snapshot,layout); operations.Update(input,snapshot,layout,interaction);
        Click(input,ActionableAlertLayout.Resolve(snapshot,layout).Overflow.Bounds);
        var result=alerts.Update(input,snapshot,layout);
        Assert.True(result.NotificationDetails);
        operations.OpenNotifications(snapshot.SessionId,interaction);
        operations.Update(input,snapshot,layout,interaction,inputCaptured:result.Captured);
        Assert.True(operations.View.Open); Assert.False(operations.View.Suppressed);
        input.BeginFrame(); result=alerts.Update(input,snapshot,layout);
        Assert.True(result.Captured); Assert.False(result.OpenOperations);
        operations.Update(input,snapshot,layout,interaction,inputCaptured:result.Captured);
        Assert.True(operations.View.Notifications); Assert.False(operations.View.Suppressed);
    }

    [Fact]
    public void OperationsOffersNotificationsWithoutVisibleStackOrFacilityData()
    {
        var input=new InputState(); var controller=new OperationsController();
        var state=new PresentationInteractionState(); var snapshot=Snapshot();
        var layout=GameplayHudLayout.Create(1280,720,192,2);
        controller.Update(input,snapshot,layout,state);
        controller.OpenCategory(OperationsCategory.All,snapshot.SessionId,state);
        Click(input,OperationsLayout.Control(layout,12)); controller.Update(input,snapshot,layout,state);
        Assert.True(controller.View.Notifications);
        Click(input,OperationsLayout.NotificationControl(layout,1)); controller.Update(input,snapshot,layout,state);
        Assert.False(controller.View.Notifications); Assert.True(controller.View.Open);
    }

    private static PlayerExperienceSnapshot Experience() => default(PlayerExperienceSnapshot) with
        { Tick=new(4),Player=new(1),Alerts=(PlayerAlertState)31,MatchStatus=PlayerMatchStatus.Active };
    private static PresentationSnapshot Snapshot(ActionableAlert[]? rows=null, PlayerExperienceSnapshot? experience=null,ulong session=1)
    {
        var current=experience??Experience();
        rows??=new AlertLifecycleTracker().Capture(new(session),current,[]).Alerts.Select(alert=>
            alert.Identity.Kind==PlayerAlertState.SupplyCritical ? alert with
            { Label="Supply Critical / " + new string('W',120) + " end of message" } :alert).ToArray();
        return new(current.Tick,TimeSpan.FromSeconds(.05),0,[],sessionId:new(session),playerExperience:current,
            alerts:new(new(session),current.Tick,current.Player,rows));
    }
    private static void Press(InputState input,HudRect rect,bool down) => input.Apply(PlatformInputEvent.MouseButtonChanged(
        down?PlatformInputEventKind.MouseButtonDown:PlatformInputEventKind.MouseButtonUp,
        PlatformMouseButton.Left,(int)(rect.X+rect.Width/2),(int)(rect.Y+rect.Height/2)));
    private static void Click(InputState input,HudRect rect)
    { input.BeginFrame(); Press(input,rect,true); Press(input,rect,false); }
}
