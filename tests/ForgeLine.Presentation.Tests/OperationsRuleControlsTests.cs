using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsRuleControlsTests
{
    [Fact]
    public void RealInputShortageAppliesOnlyAfterReleaseAndDuplicateActivationStaysPending()
    {
        using var f = new RuleUiFixture();
        f.Open();
        Assert.Empty(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        f.Edge(7, PlatformInputEventKind.MouseButtonDown);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Edge(7, PlatformInputEventKind.MouseButtonUp);
        Assert.True(f.Controller.TryTakeRuleRequest(out var request));
        Assert.Equal(0u, request.ExpectedRuleRevision);
        Assert.Equal(0, request.RuleDefinition.Minimum);
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Dispatch(request);
        f.Tick(8);
        var row = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        Assert.Equal(1, row.ActionCount);
        Assert.Equal(OperationsRuleResult.Applied, row.Result);
        Assert.Equal(0, f.Runtime.Inventories.GetTotalQuantity(f.InputInventory));
        Assert.Equal(1u, f.View.ExpectedRevision);
        Assert.False(f.View.NewDraft);
        f.Click(6);
        Assert.True(f.View.Dirty);
        Assert.Equal(0, row.Definition.Minimum);
        Assert.Equal(1u, Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisableAndRemoveReleaseOnlyTheRuleManagedOutputPause(bool remove)
    {
        using var f = new RuleUiFixture(outputFull: true);
        f.Open(); f.Click(2); f.Apply(); f.Tick(55);
        EntityId request = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).ManagedProductionRequest;
        Assert.True(request.IsValid);
        Assert.True(f.Runtime.Simulation.Entities.GetComponent<ProductionRequest>(request).Paused);
        f.Click(remove ? 9 : 8); f.DispatchPending(); f.Tick();
        Assert.False(f.Runtime.Simulation.Entities.GetComponent<ProductionRequest>(request).Paused);
        if (remove) Assert.Empty(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        else Assert.False(Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Enabled);
    }

    [Fact]
    public void RevisionMismatchRefreshesCopiedStateWithoutAutomaticRetry()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Apply(); f.Tick();
        var current = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        f.Runtime.Simulation.SubmitCommand(new OperationsRuleCommand(f.Runtime.OperationsRules,
            OperationsRuleOperation.SetEnabled, current.Definition, current.Revision, false,
            f.Runtime.Simulation.CurrentTick), f.Runtime.Simulation.CurrentTick.Next());
        f.Click(6); f.AllowNextChange();
        f.Click(7); f.DispatchPending(); f.Tick();
        Assert.Equal(OperationsRuleResult.RevisionMismatch, f.LastResult!.Value.OperationsResult);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        Assert.Equal(2u, f.View.ExpectedRevision);
        Assert.False(f.View.Observed!.Value.Enabled);
        Assert.True(f.View.DraftEnabled);
        Assert.True(f.View.Dirty);
        f.AllowNextChange();
        f.Click(7); f.DispatchPending(); f.Tick();
        Assert.Equal(OperationsRuleResult.Applied, f.LastResult!.Value.OperationsResult);
        Assert.Equal(3u, Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Revision);
    }

    [Fact]
    public void RealOutputRecoveryContinuesIdenticallyAfterUiCommandSaveAndReplay()
    {
        using var f = new RuleUiFixture(outputFull: true); f.Open(); f.Click(2); f.Apply(); f.Tick(55);
        var paused = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        Assert.True(paused.ManagedProductionRequest.IsValid);
        // The normal downstream recipe consumes the finite shared output after the checkpoint.
        f.Gateway.SubmitProduction(new PlayerId(1), f.Downstream, RecipeIds.Ammunition,
            f.Runtime.Simulation.CurrentTick, mode: ProductionRequestMode.Repeat);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(f.Runtime)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(f.Runtime)));
        using var restored = MatchPersistenceService.Restore(save, f.Resolve);
        using var playback = MatchPersistenceService.PlayReplay(replay, f.Resolve);
        Verify();
        foreach (ulong ticks in new ulong[] { 1, 7, 60, 100 })
        {
            f.Runtime.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            Verify();
        }
        Assert.True(f.Runtime.OperationsRules.Capture(new PlayerId(1))[0].ActionCount >= 2);
        Assert.Equal(20, f.Runtime.Inventories.GetQuantity(f.AmmoInventory, ResourceIds.Ammunition));
        Assert.True(f.Runtime.Inventories.GetQuantity(f.InputInventory, ResourceIds.FerrousOre) < 200);
        void Verify()
        {
            string expected = MatchAuthoritativeSnapshot.Capture(f.Runtime).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
    }

    [Fact]
    public void RuleGesturesRejectDragModalResizeAndChangedSelection()
    {
        using var f = new RuleUiFixture(); f.Open();
        f.Edge(7, PlatformInputEventKind.MouseButtonDown);
        f.Edge(6, PlatformInputEventKind.MouseButtonUp);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Edge(7, PlatformInputEventKind.MouseButtonDown);
        f.Update(blocked: true);
        f.Edge(7, PlatformInputEventKind.MouseButtonUp);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Edge(7, PlatformInputEventKind.MouseButtonDown);
        f.Layout = GameplayHudLayout.Create(1280, 720, 192, 2);
        f.Update();
        f.Edge(7, PlatformInputEventKind.MouseButtonUp);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Selection.Clear();
        f.Update();
        Assert.Null(f.View.Data);
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
    }

    [Fact]
    public void GameplayBindingsEditAndApplyOncePerKeyPress()
    {
        using var f = new RuleUiFixture(); f.Open();
        for (int i = 0; i < 6; i++) f.Key(PlatformKey.Tab);
        Assert.Equal(7, f.View.Focus);
        f.KeyDown(PlatformKey.Enter);
        Assert.True(f.Controller.TryTakeRuleRequest(out var create));
        f.Update(); f.Update();
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.KeyUp(PlatformKey.Enter);
        f.Key(PlatformKey.Enter);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Dispatch(create); f.Tick();
        f.Key(GameplayBindingRegistry.Default.Key(GameplayAction.PrimarySetting));
        f.Key(GameplayBindingRegistry.Default.Key(GameplayAction.Increase));
        Assert.Equal(150, f.View.Draft.Target);
        Assert.Equal(100, Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Definition.Target);
        f.AllowNextChange();
        f.Key(PlatformKey.Enter);
        Assert.True(f.Controller.TryTakeRuleRequest(out var update));
        Assert.Equal(1u, update.ExpectedRuleRevision);
        Assert.Equal(create.RuleDefinition.Id, update.RuleDefinition.Id);
        Assert.Equal(150, update.RuleDefinition.Target);
    }

    [Fact]
    public void NewRulesRespectFacilityLimitWhileExistingRulesRemainEditable()
    {
        using var f = new RuleUiFixture();
        for (ulong i = 1; i <= OperationsRuleSystem.MaximumRulesPerFacility; i++)
            f.Runtime.Simulation.SubmitCommand(new OperationsRuleCommand(f.Runtime.OperationsRules,
                OperationsRuleOperation.Upsert, new OperationsRuleDefinition(i, new PlayerId(1), f.Facility,
                    OperationsRuleTemplate.EnsureInputStockPolicy, ResourceIds.FerrousOre, 0, 100, 150,
                    LogisticsStockPriority.Normal), 0, true, f.Runtime.Simulation.CurrentTick), f.Runtime.Simulation.CurrentTick.Next());
        f.Tick(); f.Open();
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        Assert.Contains("LIMIT", f.View.Feedback, StringComparison.Ordinal);
        f.Click(1);
        Assert.False(f.View.NewDraft);
        f.Click(6);
        f.Click(7); f.DispatchPending(); f.Tick();
        Assert.Equal(OperationsRuleSystem.MaximumRulesPerFacility, f.Runtime.OperationsRules.Capture(new PlayerId(1)).Count);
        Assert.Equal(2u, f.View.ExpectedRevision);
    }

    [Fact]
    public void ForeignSelectionAndReusedEntityCannotReceiveAnOldRuleDraft()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Click(7);
        Assert.True(f.Controller.TryTakeRuleRequest(out var stale));
        Assert.True(f.Runtime.Simulation.Entities.DestroyEntity(f.Facility));
        var replacement = f.Runtime.Simulation.Entities.CreateEntity();
        Assert.Equal(f.Facility.Index, replacement.Index);
        Assert.NotEqual(f.Facility, replacement);
        f.Dispatch(stale); f.Tick();
        Assert.Equal(OperationsRuleResult.InvalidDefinition, f.LastResult!.Value.OperationsResult);
        Assert.Empty(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        Assert.Null(f.View.Data);
        f.Selection.SetSingle(f.Runtime.GetBase(new PlayerId(2)).CommandCore);
        f.Interaction.SetSelection(f.Selection.Entities); f.Tick();
        Assert.Null(f.View.Data);
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
    }

    [Theory]
    [InlineData(1280, 720, 96, 0.75f)]
    [InlineData(1280, 720, 192, 2f)]
    [InlineData(1600, 900, 144, 1f)]
    [InlineData(3840, 2160, 192, 2f)]
    public void RuleControlsAndMeasuredDetailPagesStayInsideExistingPanel(int width, int height, uint dpi, float scale)
    {
        using var f = new RuleUiFixture(); f.Open();
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var panel = OperationsLayout.Panel(layout);
        for (int i = 0; i < OperationsRulePanelLayout.ControlCount; i++)
        {
            var control = OperationsRulePanelLayout.Control(layout, i);
            Assert.False(control.IsEmpty);
            Assert.True(panel.Contains(new System.Numerics.Vector2(control.X, control.Y)));
            Assert.True(control.Right <= panel.Right && control.Bottom <= panel.Bottom);
            for (int j = i + 1; j < OperationsRulePanelLayout.ControlCount; j++)
            {
                var other = OperationsRulePanelLayout.Control(layout, j);
                Assert.False(control.X < other.Right && control.Right > other.X && control.Y < other.Bottom && control.Bottom > other.Y);
            }
        }
        var details = new OperationsRuleDetailText();
        float size = MathF.Max(14 * MathF.Min(1, layout.Scale), 14 * OperationsLayout.Scale(layout));
        var region = OperationsRulePanelLayout.Details(layout);
        float panelScale = OperationsLayout.Scale(layout);
        float leading = MathF.Max(15 * MathF.Min(1, layout.Scale), 16 * panelScale);
        float lastLineBottom = region.Y + 4 * panelScale + 5 * leading + size;
        Assert.True(lastLineBottom <= region.Bottom - 2 * panelScale,
            $"All six detail lines must fit: {lastLineBottom} / {region.Bottom - 2 * panelScale}");
        Assert.True(region.Bottom < OperationsRulePanelLayout.Control(layout, 11).Y);
        details.Update(f.View, region.Width - 16 * OperationsLayout.Scale(layout), size);
        var all = new System.Text.StringBuilder();
        for (int page = 0, count = details.PageCount; page < count; page++)
        {
            details.Update(f.View with { DetailPage = page }, region.Width - 16 * OperationsLayout.Scale(layout), size);
            foreach (var line in details.Lines)
            {
                Assert.True(UiFontMetrics.Default.Measure(line, size, bold: true) <= region.Width - 16 * OperationsLayout.Scale(layout), line);
                all.Append(line).Append(' ');
            }
        }
        Assert.Contains("reservation, loading or delivery", all.ToString(), StringComparison.Ordinal);
        Assert.Contains("manually disabled", all.ToString(), StringComparison.Ordinal);
        Assert.Contains("automatically recreated", all.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerLimitIsCopiedAcrossOtherFacilitiesWithoutExposingTheirRules()
    {
        using var f = new RuleUiFixture();
        for (int facilityIndex = 0; facilityIndex < 4; facilityIndex++)
        {
            var entity = f.Runtime.Simulation.Entities.CreateEntity();
            var input = f.Runtime.Inventories.CreateInventory(new InventorySpecification(1000));
            var output = f.Runtime.Inventories.CreateInventory(new InventorySpecification(1000));
            f.Runtime.Simulation.Entities.AddComponent(entity, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Building));
            f.Runtime.Simulation.Entities.AddComponent(entity, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            for (int i = 0; i < OperationsRuleSystem.MaximumRulesPerFacility; i++)
                f.Runtime.Simulation.SubmitCommand(new OperationsRuleCommand(f.Runtime.OperationsRules,
                    OperationsRuleOperation.Upsert, new OperationsRuleDefinition((ulong)(1 + facilityIndex * 8 + i), new PlayerId(1), entity,
                        OperationsRuleTemplate.EnsureInputStockPolicy, ResourceIds.FerrousOre, 0, 100, 150, LogisticsStockPriority.Normal),
                    0, true, f.Runtime.Simulation.CurrentTick), f.Runtime.Simulation.CurrentTick.Next());
        }
        f.Tick(); f.Open(); f.Tick();
        Assert.Empty(f.View.Data!.Rules);
        Assert.Equal(OperationsRuleSystem.MaximumRulesPerPlayer, f.View.Data.UsedRuleIds.Count);
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        Assert.Contains("LIMIT", f.View.Feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void CopiedRuleReorderingNeverChangesTheEditedIdentity()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Apply(); f.Tick();
        var original = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        ulong other = original.Definition.Id > 1 ? original.Definition.Id - 1 : original.Definition.Id + 1;
        f.Runtime.Simulation.SubmitCommand(new OperationsRuleCommand(f.Runtime.OperationsRules,
            OperationsRuleOperation.Upsert, original.Definition with { Id = other }, 0, true, f.Runtime.Simulation.CurrentTick),
            f.Runtime.Simulation.CurrentTick.Next());
        f.Tick();
        Assert.Equal(original.Definition.Id, f.View.Draft.Id);
        f.Click(6); f.AllowNextChange();
        f.Click(7);
        Assert.True(f.Controller.TryTakeRuleRequest(out var request));
        Assert.Equal(original.Definition.Id, request.RuleDefinition.Id);
        Assert.Equal(original.Revision, request.ExpectedRuleRevision);
    }

    [Fact]
    public void FastConfirmationStillRejectsASecondClickWithoutSubmittingAgain()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Apply(); f.Tick();
        Assert.False(f.View.Pending);
        Assert.True(f.View.InputGuarded);
        f.Click(8);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        Assert.True(Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Enabled);
        f.AllowNextChange();
        f.Click(7);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Click(8); f.DispatchPending(); f.Tick();
        Assert.False(Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).Enabled);
    }

    [Fact]
    public void ModalInterruptionCancelsGestureButPreservesUnappliedDraftAcrossMissingRequestedData()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Click(6);
        Assert.Equal(50, f.View.Draft.Minimum);
        f.Edge(7, PlatformInputEventKind.MouseButtonDown);
        f.Update(blocked: true);
        f.Tick(blocked: true);
        Assert.Null(f.Snapshot.PlayerActions!.OperationsRules);
        f.Update(); f.Tick();
        f.Edge(7, PlatformInputEventKind.MouseButtonUp);
        Assert.Equal(50, f.View.Draft.Minimum);
        Assert.True(f.View.Dirty);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        Assert.Empty(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
    }

    [Fact]
    public void EssentialResultMayArriveBeforeTheSubmissionReceiptWithoutLosingConfirmation()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Click(7);
        Assert.True(f.Controller.TryTakeRuleRequest(out var request));
        Assert.True(PlayerActionRequestDispatcher.TryDispatch(request, new PlayerId(1), f.Gateway,
            f.Runtime.Simulation.CurrentTick, out var receipt));
        f.Controller.ExpectRuleSubmission(77);
        f.Runtime.Simulation.AdvanceOneTick();
        Assert.True(f.Gateway.Results.TryRead(out var result));
        f.Controller.ObserveRuleResult(result);
        Assert.True(f.View.Pending);
        f.Controller.ObserveRuleSubmission(77, receipt);
        f.Tick();
        Assert.False(f.View.Pending);
        Assert.False(f.View.NewDraft);
        Assert.Equal(1u, f.View.ExpectedRevision);
    }

    [Fact]
    public void InputPolicyFromUiSurvivesSaveAndReplayWithoutClaimingDelivery()
    {
        using var f = new RuleUiFixture(); f.Open(); f.Apply(); f.Tick(8);
        Assert.Equal(1, Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).ActionCount);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(f.Runtime)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(f.Runtime)));
        using var restored = MatchPersistenceService.Restore(save, f.Resolve);
        using var playback = MatchPersistenceService.PlayReplay(replay, f.Resolve);
        foreach (ulong ticks in new ulong[] { 0, 1, 7, 20 })
        {
            f.Runtime.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            string expected = MatchAuthoritativeSnapshot.Capture(f.Runtime).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
        Assert.Equal(0, f.Runtime.Inventories.GetTotalQuantity(f.InputInventory));
    }

    [Fact]
    public void ManualOverrideRemainsPausedAcrossUiDisableAndRemove()
    {
        using var f = new RuleUiFixture(outputFull: true); f.Open(); f.Click(2); f.Apply(); f.Tick(55);
        EntityId request = Assert.Single(f.Runtime.OperationsRules.Capture(new PlayerId(1))).ManagedProductionRequest;
        f.Gateway.SubmitProductionPaused(new PlayerId(1), request, true, f.Runtime.Simulation.CurrentTick);
        f.Tick();
        Assert.Equal(OperationsRuleResult.ManualOverride, f.View.Observed!.Value.Result);
        Assert.False(f.View.Observed.Value.ManagedProductionRequest.IsValid);
        f.Click(8); f.DispatchPending(); f.Tick();
        Assert.True(f.Runtime.Simulation.Entities.GetComponent<ProductionRequest>(request).Paused);
        f.AllowNextChange(); f.Click(9); f.DispatchPending(); f.Tick();
        Assert.Empty(f.Runtime.OperationsRules.Capture(new PlayerId(1)));
        Assert.True(f.Runtime.Simulation.Entities.GetComponent<ProductionRequest>(request).Paused);
    }

    [Fact]
    public void ReboundActivationUsesTheExistingGameplayRegistry()
    {
        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Activate, PlatformKey.G));
        using var f = new RuleUiFixture(bindings: bindings); f.Open();
        for (int i = 0; i < 6; i++) f.Key(bindings.Key(GameplayAction.NextItem));
        f.Key(PlatformKey.Enter);
        Assert.False(f.Controller.TryTakeRuleRequest(out _));
        f.Key(PlatformKey.G);
        Assert.True(f.Controller.TryTakeRuleRequest(out _));
        Assert.Same(bindings, f.View.Bindings);
    }

    [Fact]
    public void SessionReplacementDiscardsUnsubmittedLocalRequestAndDraft()
    {
        using var old = new RuleUiFixture(); old.Open(); old.Click(7);
        using var replacement = new RuleUiFixture();
        old.Controller.Update(old.Input, replacement.Snapshot, old.Layout, old.Interaction, selection: replacement.Selection);
        Assert.False(old.Controller.View.Open);
        Assert.False(old.Controller.TryTakeRuleRequest(out _));
        Assert.Empty(old.Runtime.OperationsRules.Capture(new PlayerId(1)));
        Assert.Empty(replacement.Runtime.OperationsRules.Capture(new PlayerId(1)));
    }

    private sealed class UiTestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

    private sealed class RuleUiFixture : IDisposable
    {
        private readonly MatchComposition _composition;
        private readonly PresentationSnapshotBuffer _buffer = new();
        private ulong _sequence;
        internal MatchRuntime Runtime { get; }
        internal PlayerCommandGateway Gateway { get; }
        private readonly UiTestClock _clock = new();
        internal OperationsController Controller { get; }
        internal InputState Input { get; } = new();
        internal PresentationInteractionState Interaction { get; } = new();
        internal SelectionSet Selection { get; } = new();
        internal GameplayHudLayout Layout { get; set; } = GameplayHudLayout.Create(1600, 900, 96);
        internal PresentationSnapshot Snapshot { get; private set; } = null!;
        internal EntityId Facility { get; private set; }
        internal EntityId Downstream { get; private set; }
        internal InventoryId InputInventory { get; private set; }
        internal InventoryId AmmoInventory { get; private set; }
        internal PlayerCommandResultReadModel? LastResult { get; private set; }
        internal OperationsRulePanelView View => Controller.View.RuleControls!.Value;

        internal RuleUiFixture(bool outputFull = false, GameplayBindingRegistry? bindings = null)
        {
            Controller = new(bindings, timeProvider: _clock);
            var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 9204);
            var basis = settings.Composition;
            _composition = basis with
            {
                Key = outputFull ? "operations-output-ui-recovery" : "operations-input-ui-controls",
                CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
                {
                    var result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                    if (start.Player != new PlayerId(1)) return result;
                    InputInventory = inventories.CreateInventory(new InventorySpecification(1000));
                    var shared = inventories.CreateInventory(new InventorySpecification(16));
                    AmmoInventory = inventories.CreateInventory(new InventorySpecification(1000));
                    if (outputFull)
                    {
                        Assert.True(inventories.Add(InputInventory, ResourceIds.FerrousOre, 200).Succeeded);
                        Assert.True(inventories.Add(shared, ResourceIds.Steel, 12).Succeeded);
                        Assert.True(inventories.Add(shared, ResourceIds.Electronics, 4).Succeeded);
                    }
                    Facility = Make(InputInventory, shared, ProductionCapability.SteelProcessing);
                    Downstream = Make(shared, AmmoInventory, ProductionCapability.AmmunitionProcessing);
                    return result;
                    EntityId Make(InventoryId input, InventoryId output, ProductionCapability capability)
                    {
                        var entity = entities.CreateEntity();
                        entities.AddComponent(entity, new ControllableEntity(start.Player, ControllableEntityCategory.Building));
                        entities.AddComponent(entity, new ProductionFacility(input, output, capability, SimulationTick.Zero));
                        entities.AddComponent(entity, new PowerNetworkMembership(new PowerNetworkId(100002)));
                        entities.AddComponent(entity, new PowerGenerator(100));
                        entities.AddComponent(entity, new PowerConsumer(1));
                        return entity;
                    }
                }
            };
            settings = settings with { Composition = _composition, Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
            Runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
            Gateway = new(Runtime.Simulation, Runtime.Services.BuildingCommands, Runtime.BattlefieldRuntime.MatchStateEntity,
                operationsRules: Runtime.OperationsRules);
            Runtime.Simulation.AttachTickObserver(Gateway);
            Selection.SetSingle(Facility);
            Interaction.SetSelection(Selection.Entities);
            Interaction.SetOperationsOpen(true);
            Runtime.Simulation.AttachTickObserver(new PresentationExtractor(_buffer,
                new PresentationExtractionContext(Runtime, new PlayerId(1), Interaction, Gateway)));
            Gateway.SubmitProduction(new PlayerId(1), Facility, RecipeIds.Steel, Runtime.Simulation.CurrentTick,
                mode: ProductionRequestMode.Repeat);
            Tick();
            Controller.Update(Input, Snapshot, Layout, Interaction, selection: Selection);
        }
        internal MatchComposition Resolve(string key) => key == _composition.Key ? _composition : throw new InvalidOperationException(key);
        internal void Open()
        {
            Controller.OpenCategory(OperationsCategory.Production, Snapshot.SessionId, Interaction);
            ClickRect(OperationsLayout.Control(Layout, 13));
            Assert.True(View.Open);
            if (View.Data is null) Tick();
        }
        internal void Update(bool blocked = false) => Controller.Update(Input, Snapshot, Layout, Interaction, blocked, selection: Selection);
        internal void Tick(ulong count = 1, bool blocked = false)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(count * 50));
            Runtime.Simulation.RunTicks(count, TestContext.Current.CancellationToken);
            while (Gateway.Results.TryRead(out var result)) { LastResult = result; Controller.ObserveRuleResult(result); }
            Assert.True(_buffer.TryReadLatest(out var snapshot)); Snapshot = snapshot;
            Input.BeginFrame(); Update(blocked);
        }
        internal void Edge(int index, PlatformInputEventKind kind)
        {
            Input.BeginFrame(); var r = OperationsRulePanelLayout.Control(Layout, index);
            Input.Apply(PlatformInputEvent.MouseButtonChanged(kind, PlatformMouseButton.Left, (int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)));
            Update();
        }
        internal void Click(int index) => ClickRect(OperationsRulePanelLayout.Control(Layout, index));
        private void ClickRect(HudRect r)
        {
            Input.BeginFrame();
            int x = (int)(r.X + r.Width / 2), y = (int)(r.Y + r.Height / 2);
            Input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
            Input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
            Update();
        }
        internal void AllowNextChange() => _clock.Advance(TimeSpan.FromMilliseconds(600));
        internal void KeyDown(PlatformKey key)
        { Input.BeginFrame(); Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key)); Update(); }
        internal void KeyUp(PlatformKey key)
        { Input.BeginFrame(); Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key)); Update(); }
        internal void Key(PlatformKey key) { KeyDown(key); KeyUp(key); }
        internal void Apply() { Click(7); DispatchPending(); }
        internal void DispatchPending() { Assert.True(Controller.TryTakeRuleRequest(out var request)); Dispatch(request); }
        internal void Dispatch(PlayerActionRequest request)
        {
            Assert.True(PlayerActionRequestDispatcher.TryDispatch(request, new PlayerId(1), Gateway, Runtime.Simulation.CurrentTick, out var receipt));
            Controller.ExpectRuleSubmission(++_sequence);
            Controller.ObserveRuleSubmission(_sequence, receipt);
        }
        public void Dispose() => Runtime.Dispose();
    }

    [Fact]
    public void RuleQuantitiesReuseManualStepsWithoutRewritingLegacyValues()
    {
        var zero = LogisticsQuantityEditor.DecideField(PlayerStockThresholdField.Minimum, 0, 100, 150, 1000, 1, allowZeroMinimum: true);
        Assert.True(zero.Allowed);
        Assert.Equal(50, zero.Next);
        Assert.Equal(0, LogisticsQuantityEditor.DecideField(PlayerStockThresholdField.Minimum, 50, 100, 150, 1000, -1, true).Next);
        Assert.False(LogisticsQuantityEditor.DecideField(PlayerStockThresholdField.Minimum, 50, 100, 150, 1000, -1).Allowed);
        Assert.Equal(17, LogisticsQuantityEditor.DecideField(PlayerStockThresholdField.Minimum, 17, 100, 150, 1000, 0, true).Next);
        Assert.Equal(150, LogisticsQuantityEditor.DecideField(PlayerStockThresholdField.Maximum, 0, 100, 150, 150, 1, true).Next);
        Assert.True(LogisticsQuantityEditor.FitsCapacity(150, double.PositiveInfinity));
        Assert.False(LogisticsQuantityEditor.FitsCapacity(200, 150));
        Assert.Equal((50d, 100d, 150d), LogisticsQuantityEditor.NewManualDefaults(double.PositiveInfinity));
    }

    [Fact]
    public void ExtractionCopiesOnlyOneOwnedFacilityRulesAndRealRecipeInputs()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        var output = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        EntityId facility = entities.CreateEntity();
        entities.AddComponent(facility, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Building));
        entities.AddComponent(facility, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
        entities.AddComponent(facility, new PowerNetworkMembership(new PowerNetworkId(100001)));
        entities.AddComponent(facility, new PowerGenerator(100));
        entities.AddComponent(facility, new PowerConsumer(1));
        var interaction = new PresentationInteractionState();
        interaction.SetSelection([facility]);
        interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var definition = new OperationsRuleDefinition(71, new PlayerId(1), facility,
            OperationsRuleTemplate.EnsureInputStockPolicy, ResourceIds.FerrousOre, 0, 100, 150, LogisticsStockPriority.Normal);
        scenario.Simulation.SubmitCommand(PlayerProductionActionCommand.Queue(new PlayerId(1), facility,
            RecipeIds.Steel, SimulationTick.Zero, mode: ProductionRequestMode.Repeat), new SimulationTick(1));
        scenario.Simulation.SubmitCommand(new OperationsRuleCommand(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, definition, 0, true, SimulationTick.Zero), new SimulationTick(1));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var selected = Assert.IsType<OperationsRuleSelectionSnapshot>(snapshot.PlayerActions!.OperationsRules);
        Assert.Equal(facility, selected.Facility);
        Assert.Equal(1000, selected.InputCapacity);
        Assert.Equal(RecipeIds.Steel, selected.ContextRecipe);
        Assert.Single(selected.Inputs, amount => amount.ResourceId == ResourceIds.FerrousOre);
        Assert.Equal(1u, Assert.Single(selected.Rules).Revision);
        scenario.Simulation.SubmitCommand(new OperationsRuleCommand(scenario.OperationsRules,
            OperationsRuleOperation.SetEnabled, definition, 1, false, scenario.Simulation.CurrentTick), scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.False(Assert.Single(snapshot.PlayerActions!.OperationsRules!.Rules).Enabled);
        Assert.True(Assert.Single(selected.Rules).Enabled);
        Assert.Equal(1u, Assert.Single(selected.Rules).Revision);
        interaction.SetSelection([facility, scenario.GetBase(new PlayerId(2)).CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions!.OperationsRules);
    }
}
