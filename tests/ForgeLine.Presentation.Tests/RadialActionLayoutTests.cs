using System.Numerics;
using Xunit;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.Economy;

namespace ForgeLine.Presentation.Tests;

public sealed class RadialActionLayoutTests
{
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void PaintedSectorsUseExactAnnularOwnership(int count)
    {
        var geometry = new RadialActionLayout(new(500,400), new(500,400), 60, 150, count);
        Assert.Equal(-1, geometry.Hit(geometry.Center));
        Assert.Equal(-1, geometry.Hit(new(float.NaN, 400)));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, geometry.Hit(geometry.LabelCenter(i)));
            float seam = geometry.Start(i) - RadialActionLayout.GapRadians / 2;
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(seam) * 100));
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(geometry.Start(i) + .1f) * 59));
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(geometry.Start(i) + .1f) * 151));
        }
    }
    [Theory]
    [InlineData(80f, 176f, .75f)]
    [InlineData(80f, 176f, 1f)]
    [InlineData(80f, 176f, 2f)]
    [InlineData(60f, 156f, 2f)]
    public void HoverArcRemainsInsideThePublishedAnnularHitSector(float inner, float outer, float scale)
    {
        var ring = new RadialActionLayout(new(500, 400), new(500, 400), inner, outer, 8);
        float thickness = RadialActionVisuals.HoverArcWidth(scale);
        float arcOuter = ring.OuterRadius - 1;
        float arcInner = MathF.Max(ring.InnerRadius + 1, arcOuter - thickness);
        Assert.True(arcInner > ring.InnerRadius);
        Assert.True(arcOuter < ring.OuterRadius);
        for (int slot = 0; slot < ring.Count; slot++)
            for (int segment = 0; segment < 20; segment++)
            {
                float angle = ring.Start(slot) + (ring.End(slot) - ring.Start(slot)) * (segment + .5f) / 20;
                foreach (float radius in new[] { arcInner, arcOuter })
                    Assert.Equal(slot, ring.Hit(ring.Center + RadialActionLayout.Direction(angle) * radius));
            }
    }

    [Theory] [InlineData(96,1)] [InlineData(144,1.25f)] [InlineData(192,2)]
    public void FitKeepsCompleteRingInsideSafeAreaAndAvoidsControls(uint dpi, float scale)
    {
        var hud = GameplayHudLayout.Create(3840,2160,dpi,scale);
        HudRect[] obstacles = [hud.TopStatusBar, hud.Minimap, hud.OnboardingBounds];
        foreach (Vector2 anchor in new Vector2[] { new(hud.SafeArea.X + 1,hud.SafeArea.Y + 1), new(1900,1000), new(hud.SafeArea.Right-1,hud.SafeArea.Bottom-1) })
        {
            var fit = RadialActionLayout.Fit(anchor, 500, hud, obstacles);
            Assert.True(fit.IsValid);
            Assert.True(hud.SafeArea.Contains(fit.Center - new Vector2(fit.OuterRadius)));
            Assert.True(hud.SafeArea.Contains(fit.Center + new Vector2(fit.OuterRadius)));
            foreach (var rect in obstacles)
            {
                Vector2 nearest = new(Math.Clamp(fit.Center.X,rect.X,rect.Right),Math.Clamp(fit.Center.Y,rect.Y,rect.Bottom));
                Assert.True(Vector2.Distance(nearest,fit.Center) >= fit.OuterRadius);
            }
        }
    }
    [Theory]
    [InlineData(1280,720,96u,.75f)]
    [InlineData(1920,1080,144u,1f)]
    [InlineData(3440,1440,192u,2f)]
    public void InspectorAwareFitMovesRingLeftOnlyWhenPhysicalSafeAreaPermits(
        int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width,height,dpi,scale);
        var anchor = new Vector2(layout.SafeArea.Right - 12, layout.SafeArea.Y + layout.SafeArea.Height * .55f);
        float cardWidth = 330 * Math.Clamp(layout.Scale,1,1.25f);
        var standard = RadialActionLayout.Fit(anchor,0,layout,[]);
        var adjusted = RadialActionLayout.FitWithRightInspector(anchor,0,layout,[],cardWidth);
        Assert.True(standard.IsValid);
        Assert.True(adjusted.IsValid);
        Assert.True(layout.SafeArea.Contains(adjusted.Center - new Vector2(adjusted.OuterRadius)));
        Assert.True(layout.SafeArea.Contains(adjusted.Center + new Vector2(adjusted.OuterRadius)));
        Assert.Equal(anchor,adjusted.Anchor);
        Assert.Equal(0,adjusted.Hit(adjusted.LabelCenter(0)));
        Assert.True(adjusted.Center.X <= standard.Center.X);
        if (layout.SafeArea.Width >= cardWidth + 2*adjusted.OuterRadius + 8)
            Assert.True(adjusted.Center.X+adjusted.OuterRadius+MathF.Max(8,layout.Gap)+cardWidth <= layout.SafeArea.Right+1);
    }

    [Fact]
    public void CompactTacticalTitlesRemainDistinctBeforeTooltipExpansion()
    {
        var titles=Enumerable.Range(0,8).Select(i=>PlayerActionDockHudModel.ResolveRingItemTitle(PlayerActionPanelMode.Tactical,i,null,true)).ToArray();
        Assert.Equal(8,titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(titles,title=>Assert.InRange(title.Length,1,5));
    }

    [Fact]
    public void ExpandedOnboardingAtCompactMaximumDensityKeepsNativeTypeAndExactSectorLabels()
    {
        var hud=GameplayHudLayout.Create(1280,720,192,2);
        var ring=RadialActionLayout.Fit(new(640,360),0,hud,[hud.TopStatusBar,hud.Minimap,
            hud.NotificationBounds(false),hud.OnboardingBounds]);
        Assert.True(ring.IsValid);
        Assert.True(ring.OuterRadius-ring.InnerRadius>=96);
        for(int slot=0;slot<8;slot++)
        foreach(float x in new[]{-ring.LabelHalfWidth,ring.LabelHalfWidth}) foreach(float y in new[]{-28f,28f})
            Assert.Equal(slot,ring.Hit(ring.LabelCenter(slot)+new Vector2(x,y)));
    }

    [Fact]
    public void CompactHighDpiRingKeepsReadableLabelsAndAvoidsTheActualHud()
    {
        var hud=GameplayHudLayout.Create(1280,720,192,2);
        var fit=RadialActionLayout.Fit(new(640,360),500,hud,[hud.TopStatusBar,hud.Minimap,hud.NotificationBounds(false)]);
        Assert.True(fit.IsValid);
        Assert.InRange((fit.OuterRadius-fit.InnerRadius)/96,1,1.75f);
    }
    [Theory]
    [InlineData(1280, 720, 96u, .75f)]
    [InlineData(1920, 1080, 144u, 1f)]
    [InlineData(3440, 1440, 192u, 2f)]
    public void OperationsObstacleExistsOnlyWhileThePanelIsVisible(
        int width, int height, uint dpi, float uiScale)
    {
        var hud = GameplayHudLayout.Create(width, height, dpi, uiScale);
        Span<HudRect> occupied = stackalloc HudRect[8];

        RadialActionLayout.ResolveObstacles(occupied, hud);
        Assert.True(occupied[7].IsEmpty);

        RadialActionLayout.ResolveObstacles(occupied, hud,
            operations: new OperationsView(true));
        Assert.Equal(OperationsLayout.Panel(hud), occupied[7]);

        RadialActionLayout.ResolveObstacles(occupied, hud,
            operations: new OperationsView(true, Suppressed: true));
        Assert.True(occupied[7].IsEmpty);
    }

    [Theory] [InlineData(80)] [InlineData(100)]
    public void HorizontalIconLabelAndValueBoundsRemainInsideEverySector(float inner)
    {
        var ring=new RadialActionLayout(new(500,500),new(500,500),inner,inner+96,8);
        for(int slot=0;slot<8;slot++)
        {
            var center=ring.LabelCenter(slot);
            foreach(float x in new[] {-38f,38f}) foreach(float y in new[] {-28f,28f})
                Assert.Equal(slot,ring.Hit(center+new Vector2(x,y)));
        }
    }

    [Fact]
    public void InvalidOrUnavailableGeometryNeverOwnsInput()
    {
        var hud = GameplayHudLayout.Create(1920,1080,96);
        Assert.False(RadialActionLayout.Fit(new(-1,0),0,hud,[]).IsValid);
        Assert.False(RadialActionLayout.Fit(new(500,500),float.NaN,hud,[]).IsValid);
        Assert.False(RadialActionLayout.Fit(new(500,500),0,hud,[hud.SafeArea]).IsValid);
        Assert.Equal(-1, new RadialActionLayout(default, default, 100, 90, 8).Hit(new(90,0)));
        Assert.Equal(-1, default(RadialActionLayout).Hit(default));
    }
    [Fact]
    public void ProjectUsesTheActualInterpolatedTransformAndRejectsForeignOrReusedEntities()
    {
        var entity=new EntityId(1,1); var session=new SimulationSessionId(1); var buffer=new PresentationSnapshotBuffer(); var world=new RenderWorld();
        PresentationSnapshot Snapshot(ulong tick,Vector3 position,PlayerId owner) => new(new(tick),TimeSpan.FromMilliseconds(50),1,
            [new RenderInstance(entity,RenderTransform.Identity with {Position=position},default,default,RenderVisibilityMask.World,
                Selectable:new(owner,ControllableEntityCategory.Unit))],sessionId:session,
            playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(tick)});
        buffer.Publish(Snapshot(1,Vector3.Zero,new(1)));world.Update(buffer);
        var current=Snapshot(2,new(20,0,0),new(1));buffer.Publish(current);world.Update(buffer);
        var camera=new RtsCamera();camera.CenterOn(new(10,0,0));var layout=GameplayHudLayout.Create(1920,1080,96);
        var ring=new ObjectActionRingView(entity,session,1,1,true,true);
        var projected=ring.Project(world,camera,.5f,current,layout,[]);
        Assert.True(projected.Geometry.IsValid);
        Assert.Equal(camera.WorldToScreen(new(10,0,0),1920,1080).Position,projected.Geometry.Anchor);
        Assert.False((ring with {Entity=new(1,2)}).Project(world,camera,.5f,current,layout,[]).Geometry.IsValid);
        var foreign=Snapshot(3,new(20,0,0),new(2));buffer.Publish(foreign);world.Update(buffer);
        Assert.False(ring.Project(world,camera,1,foreign,layout,[]).Geometry.IsValid);
        camera.CenterOn(new(10000,0,0));
        Assert.False(ring.Project(world,camera,1,current,layout,[]).Geometry.IsValid);
    }

    [Fact]
    public void RenderSnapshotResolvesStableItemKeysInsteadOfReusingAnOldQueueIndex()
    {
        var entity=new EntityId(1,1);var session=new SimulationSessionId(1);var request=new EntityId(20,1);
        var facility=new PlayerProductionFacilityActionReadModel(entity,default,default,default,default,0,[],
            [new(request,new RecipeId(1),"Existing request",ProductionPriority.Normal,ProductionRequestMode.OneShot,false,false)]);
        var snapshot=new PresentationSnapshot(new(5),TimeSpan.FromMilliseconds(50),1,
            [new RenderInstance(entity,RenderTransform.Identity,default,default,RenderVisibilityMask.World,Selectable:new(new(1),ControllableEntityCategory.Building))],
            sessionId:session,playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(5)},
            playerActions:new(session,new(5),[],0,facility,null));
        var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
        ActionRingSlots slots=default;
        slots[0]=new(new(PlayerActionDockControlKind.Item,PlayerActionPanelMode.Production,2),new(2,20,1));
        slots[1]=new(new(PlayerActionDockControlKind.Item,PlayerActionPanelMode.Production,1),new(2,19,1));
        var ring=new ObjectActionRingView(entity,session,1,1,true,true) {Slots=slots};
        var camera=new RtsCamera();camera.CenterOn(Vector3.Zero);
        var resolved=ring.Project(world,camera,1,snapshot,GameplayHudLayout.Create(1920,1080,96),[]);
        Assert.Equal(0,resolved.Slots[0].Target.ItemIndex);
        Assert.False(resolved.Slots[1].Present);
        Assert.Equal(slots[0].Key,resolved.Slots[0].Key);
    }

    [Fact]
    public void SettledClosedDecorationPreservesSingleObjectMigrationAndOwnsNoInput()
    {
        var motion=new ObjectActionRingMotion();
        var current=new ObjectActionRingView(new(1,1),new(1),1,1,true,false);
        var view=motion.Update(current,TimeSpan.Zero,true);
        Assert.True(view.SingleOwned);Assert.False(view.Open);Assert.Equal(current.Entity,view.Entity);
        Assert.False(view.TryHit(new(500,500),out _));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void FirstOwnedContextOpensWithTheSame160MillisecondAlphaTrack(bool reduced)
    {
        var motion=new ObjectActionRingMotion();var start=TimeSpan.FromSeconds(1);
        motion.Update(default,start,!reduced);
        var ring=new ObjectActionRingView(new(1,1),new(1),1,1,true,true,new(new(500,500),new(500,500),80,176,8),true);
        var opened=start+TimeSpan.FromMilliseconds(16);
        Assert.Equal(reduced ? 1:0,motion.Update(ring,opened,!reduced).Opacity);
        Assert.Equal(reduced ? 1:.5f,motion.Update(ring,opened+TimeSpan.FromMilliseconds(80),!reduced).Opacity);
        Assert.Equal(1,motion.Update(ring,opened+TimeSpan.FromMilliseconds(160),!reduced).Opacity);
    }

    [Fact]
    public void CloseReleasesOwnershipAtOnceWhileAlphaFadesFor160Milliseconds()
    {
        var motion = new ObjectActionRingMotion();
        var open = new ObjectActionRingView(default,default,1,1,true,true,new(new(500,500),new(500,500),60,150,8),true);
        var start = TimeSpan.FromSeconds(1);
        motion.Update(open,start,true);
        var closing = motion.Update(open with { Open=false },start + TimeSpan.FromMilliseconds(16),true);
        Assert.False(closing.Open); Assert.Equal(1,closing.Opacity);
        Assert.InRange(motion.Update(open with { Open=false },start+TimeSpan.FromMilliseconds(96),true).Opacity,.49f,.51f);
        Assert.Equal(0,motion.Update(open with { Open=false },start+TimeSpan.FromMilliseconds(176),true).Opacity);
        Assert.Equal(1,motion.Update(open,start+TimeSpan.FromMilliseconds(192),false).Opacity);
    }
    [Fact]
    public void GlobalRingUsesCopiedEmptySelectionAndRejectsReplacementSessionOrSelection()
    {
        var layout = GameplayHudLayout.Create(1920,1080,96);
        var experience = default(PlayerExperienceSnapshot) with { Player=new(1), Tick=new(1) };
        PresentationSnapshot Snapshot(PlayerExperienceSnapshot value, SimulationSessionId session) =>
            new(new(1),TimeSpan.FromMilliseconds(50),1,[],sessionId:session,playerExperience:value);
        var snapshot=Snapshot(experience,new(1));
        var ring=new ObjectActionRingView(default,new(1),1,1,false,true) { Global=true, GlobalAnchor=new(900,500) };
        var world=new RenderWorld(); var camera=new RtsCamera();
        Assert.True(ring.UsesRing);
        Assert.True(ring.Project(world,camera,1,snapshot,layout,[]).Geometry.IsValid);
        Assert.False(ring.Project(world,camera,1,Snapshot(experience with {Player=default},new(1)),layout,[]).Geometry.IsValid);
        Assert.False((ring with {Session=default}).Project(world,camera,1,Snapshot(experience,default),layout,[]).Geometry.IsValid);
        Assert.False(ring.Project(world,camera,1,Snapshot(experience,new(2)),layout,[]).Geometry.IsValid);
        Assert.False(ring.Project(world,camera,1,Snapshot(experience with {Selection=PlayerSelectionSummary.Empty with {Count=1}},new(1)),layout,[]).Geometry.IsValid);
        Assert.False(ring.Project(world,camera,1,Snapshot(experience with {Tick=new(2)},new(1)),layout,[]).Geometry.IsValid);
    }

}
