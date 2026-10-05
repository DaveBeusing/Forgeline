using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed class DevelopmentOverlayRenderer : IDisposable
{
    private const int MaxVertices = 196_608;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 2.0f;
    private const float GlyphAdvance = 12.0f;
    private const float LineAdvance = 16.0f;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private int _vertexCount;
    private float _uiScale = 1.0f;
    private bool _disposed;

    public DevelopmentOverlayRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
        _pipeline = CreatePipeline(graphics);
    }

    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        in DevelopmentOverlayMetrics metrics,
        RtsCamera? camera = null,
        DebugDraw? debugDraw = null,
        PlayerExperienceSnapshot? playerExperience = null,
        bool showDevelopmentMetrics = true,
        PlayerActionSnapshot? playerActions = null,
        PlayerActionPanelView? actionPanel = null,
        TacticalTargetingView? tacticalTargeting = null,
        FormationTemplate activeFormation = FormationTemplate.Compact,
        PreAlphaUxView preAlphaUx = default,
        float uiScale = 1.0f)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);

        _vertexCount = 0;
        _uiScale =
            float.IsFinite(uiScale)
                ? Math.Clamp(uiScale, 0.75f, 2.0f)
                : 1.0f;

        if (showDevelopmentMetrics)
        {
        Span<char> buffer = stackalloc char[1_024];
        var builder = new OverlayTextBuilder(buffer);

        builder.Append("FORGELINE DEV");
        builder.NewLine();
        builder.Append("FPS ");
        builder.Append(metrics.FramesPerSecond, "F1");
        builder.Append(" FRAME ");
        builder.Append(metrics.FrameMilliseconds, "F2");
        builder.Append("MS CPU ");
        builder.Append(metrics.CpuRenderMilliseconds, "F2");
        builder.Append("MS");
        builder.NewLine();

        builder.Append("SIM TICK ");
        builder.Append(metrics.SimulationTick);
        builder.Append(" ");
        builder.Append(metrics.SimulationTickMilliseconds, "F2");
        builder.Append("MS ENTITIES ");
        builder.Append(metrics.SimulationEntityCount);
        builder.NewLine();

        builder.Append("CHUNKS ");
        builder.Append(metrics.VisibleTerrainChunks);
        builder.Append("/");
        builder.Append(metrics.TotalTerrainChunks);
        builder.Append(" DRAWS ");
        builder.Append(metrics.DrawCalls);
        builder.Append(" INST ");
        builder.Append(metrics.RenderedInstances);
        builder.Append("/");
        builder.Append(metrics.TotalInstances);
        builder.NewLine();

        builder.Append("JOBS ");
        builder.Append(metrics.JobExecutionMilliseconds, "F2");
        builder.Append("MS ALLOC ");
        builder.AppendBytes(metrics.TotalAllocatedBytes);
        builder.Append(" HEAP ");
        builder.AppendBytes(metrics.HeapSizeBytes);
        builder.NewLine();

        builder.Append("GC ");
        builder.Append(metrics.Gen0Collections);
        builder.Append("/");
        builder.Append(metrics.Gen1Collections);
        builder.Append("/");
        builder.Append(metrics.Gen2Collections);

        EmitReadableText(
            builder.Written,
            12.0f,
            12.0f,
            new Vector4(0.98f, 0.78f, 0.18f, 1.0f),
            context.Width,
            context.Height);
        }

        if (playerExperience.HasValue)
        {
            EmitPlayerExperience(
                playerExperience.Value,
                showDevelopmentMetrics ? 112.0f : 12.0f,
                context.Width,
                context.Height);
        }

        if (playerActions is not null &&
            actionPanel is PlayerActionPanelView panelView)
        {
            EmitPlayerActions(
                playerActions,
                panelView,
                activeFormation,
                context.Width,
                context.Height);
        }

        if (tacticalTargeting is
                TacticalTargetingView targeting &&
            targeting.IsActive)
        {
            EmitTacticalTargeting(
                targeting,
                context.Width,
                context.Height);
        }

        EmitPreAlphaUx(
            preAlphaUx,
            context.Width,
            context.Height);

        if (camera is not null &&
            debugDraw is not null &&
            debugDraw.Enabled)
        {
            foreach (DebugLabel label in debugDraw.Labels)
            {
                ScreenProjection projection =
                    camera.WorldToScreen(
                        label.Position,
                        context.Width,
                        context.Height);

                if (!projection.IsVisible)
                {
                    continue;
                }

                EmitText(
                    label.Text.AsSpan(),
                    projection.Position.X,
                    projection.Position.Y,
                    label.Color,
                    context.Width,
                    context.Height);
            }
        }

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        IGraphicsBuffer vertexBuffer = GetFrameVertexBuffer(context.FrameIndex);
        vertexBuffer.SetData<OverlayVertex>(
            _vertices.AsSpan(0, _vertexCount));

        context.SetPipeline(_pipeline);
        context.SetVertexBuffer(vertexBuffer, VertexStride);
        context.Draw(_vertexCount);

        LastRenderedVertexCount = _vertexCount;
    }

    private void EmitPlayerExperience(
        in PlayerExperienceSnapshot snapshot,
        float originY,
        int width,
        int height)
    {
        Span<char> buffer = stackalloc char[2_048];
        var builder = new OverlayTextBuilder(buffer);

        builder.Append("MATCH ");
        builder.Append(
            snapshot.MatchStatus switch
            {
                PlayerMatchStatus.Loading => "LOADING",
                PlayerMatchStatus.Active => "ACTIVE",
                PlayerMatchStatus.Victory => "VICTORY",
                PlayerMatchStatus.Defeat => "DEFEAT",
                PlayerMatchStatus.Draw => "DRAW",
                PlayerMatchStatus.Ended => "ENDED",
                _ => "UNKNOWN"
            });

        int totalSeconds =
            (int)Math.Clamp(
                snapshot.Statistics.DurationSeconds,
                0.0,
                int.MaxValue);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        builder.Append("  TIME ");
        builder.Append(minutes);
        builder.Append(":");
        if (seconds < 10)
        {
            builder.Append("0");
        }

        builder.Append(seconds);
        builder.NewLine();

        builder.Append("STEEL ");
        builder.Append(snapshot.Resources.Steel, "F0");
        builder.Append("  FUEL ");
        builder.Append(snapshot.Resources.Fuel, "F0");
        builder.Append("  ELECTRONICS ");
        builder.Append(snapshot.Resources.Electronics, "F0");
        builder.Append("  AMMO ");
        builder.Append(snapshot.Resources.Ammunition, "F0");
        builder.NewLine();

        builder.Append("RAW FE ");
        builder.Append(snapshot.Resources.FerrousOre, "F0");
        builder.Append("  VOL ");
        builder.Append(snapshot.Resources.Volatiles, "F0");
        builder.Append("  SIL ");
        builder.Append(snapshot.Resources.Silicates, "F0");
        builder.NewLine();

        builder.Append("POWER ");
        builder.Append(snapshot.Power.Generation, "F0");
        builder.Append("/");
        builder.Append(snapshot.Power.Demand, "F0");
        builder.Append("  ");
        builder.Append(
            snapshot.Power.IsConstrained
                ? "CONSTRAINED"
                : "STABLE");
        builder.NewLine();

        builder.Append("INTEL EXP ");
        builder.Append(snapshot.Intelligence.ExploredCells);
        builder.Append(" VIS ");
        builder.Append(snapshot.Intelligence.VisibleCells);
        builder.Append(" CONTACTS ");
        builder.Append(snapshot.Intelligence.KnownContacts);
        builder.NewLine();

        if (snapshot.Selection.Count > 0)
        {
            builder.Append("SELECTED ");
            builder.Append(snapshot.Selection.Count);
            builder.Append(" ");
            builder.Append(snapshot.Selection.DisplayName);
            builder.NewLine();

            if (snapshot.Selection.HasHealth)
            {
                builder.Append("HP ");
                builder.Append(
                    snapshot.Selection.HealthFraction * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasSupply)
            {
                builder.Append("FUEL ");
                builder.Append(
                    snapshot.Selection.FuelFraction * 100.0,
                    "F0");
                builder.Append("% AMMO ");
                builder.Append(
                    snapshot.Selection.AmmunitionFraction * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasReadiness)
            {
                builder.Append("READY ");
                builder.Append(
                    snapshot.Selection.Readiness * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasPower)
            {
                builder.Append("POWER ");
                builder.Append(
                    snapshot.Selection.PowerState.ToString());
            }

            builder.NewLine();

            if (snapshot.Selection.Work.Kind != PlayerWorkKind.None)
            {
                builder.Append(
                    snapshot.Selection.Work.Kind.ToString());
                builder.Append(" ");
                builder.Append(
                    snapshot.Selection.Work.Activity);
                builder.Append(" ");
                builder.Append(
                    snapshot.Selection.Work.Progress * 100.0,
                    "F0");
                builder.Append("% ");
                builder.Append(
                    snapshot.Selection.Work.State.ToString());

                if (!string.IsNullOrWhiteSpace(
                        snapshot.Selection.Work.BlockReason))
                {
                    builder.Append(" ");
                    builder.Append(
                        snapshot.Selection.Work.BlockReason);
                }

                builder.NewLine();
            }
        }

        if (snapshot.Feedback.Kind != PlayerCommandFeedbackKind.None &&
            snapshot.Tick.Value >= snapshot.Feedback.ResolvedAtTick.Value &&
            snapshot.Tick.Value - snapshot.Feedback.ResolvedAtTick.Value <= 80)
        {
            builder.Append("COMMAND ");

            if (snapshot.Feedback.Kind ==
                PlayerCommandFeedbackKind.Movement)
            {
                builder.Append("MOVE ");
                builder.Append(
                    snapshot.Feedback.AcceptedTargets);
                builder.Append(" ACCEPTED");

                if (snapshot.Feedback.RejectedTargets > 0)
                {
                    builder.Append(" ");
                    builder.Append(
                        snapshot.Feedback.RejectedTargets);
                    builder.Append(" REJECTED");
                }
            }
            else
            {
                builder.Append(
                    snapshot.Feedback.Kind switch
                    {
                        PlayerCommandFeedbackKind.Construction => "BUILD ",
                        PlayerCommandFeedbackKind.Production => "PROCESS ",
                        PlayerCommandFeedbackKind.UnitProduction => "UNITS ",
                        PlayerCommandFeedbackKind.Logistics => "LOGISTICS ",
                        PlayerCommandFeedbackKind.Supply => "SUPPLY ",
                        PlayerCommandFeedbackKind.Tactical => "TACTICAL ",
                        PlayerCommandFeedbackKind.Artillery => "ARTILLERY ",
                        _ => "ACTION "
                    });

                if (snapshot.Feedback.State ==
                    PlayerCommandFeedbackState.Accepted)
                {
                    builder.Append("ACCEPTED");
                }
                else if (snapshot.Feedback.Kind ==
                         PlayerCommandFeedbackKind.Construction)
                {
                    builder.Append("BLOCKED ");
                    builder.Append(
                        snapshot.Feedback.BuildRejection.ToString());

                    if (snapshot.Feedback.PlacementFailure !=
                        BuildingPlacementFailureReason.None)
                    {
                        builder.Append(" ");
                        builder.Append(
                            snapshot.Feedback.PlacementFailure.ToString());
                    }
                }
                else
                {
                    builder.Append("REJECTED");
                    if (snapshot.Feedback.ActionFailure !=
                        PlayerLogisticsActionFailureReason.None)
                    {
                        builder.Append(" ");
                        builder.Append(
                            snapshot.Feedback.ActionFailure.ToString());
                    }
                    else if (snapshot.Feedback.TacticalFailure !=
                             PlayerTacticalActionFailureReason.None)
                    {
                        builder.Append(" ");
                        builder.Append(
                            snapshot.Feedback.TacticalFailure.ToString());
                    }
                }
            }

            builder.NewLine();
        }

        if (snapshot.Alerts != PlayerAlertState.None)
        {
            builder.Append("ALERT ");

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertState.LowPower))
            {
                builder.Append("LOW POWER ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertState.ProductionBlocked))
            {
                builder.Append("PRODUCTION BLOCKED ");
                builder.Append(
                    snapshot.BlockedProductionFacilities);
                builder.Append(" ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertState.SupplyCritical))
            {
                builder.Append("SUPPLY CRITICAL ");
                builder.Append(snapshot.CriticalSupplyUnits);
                builder.Append(" ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertState.CommandCoreDamaged))
            {
                builder.Append("COMMAND CORE DAMAGED ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertState.CommandCoreDestroyed))
            {
                builder.Append("COMMAND CORE DESTROYED ");
            }

            builder.NewLine();
        }

        EmitText(
            builder.Written,
            12.0f,
            originY,
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);

        if (!snapshot.IsMatchComplete)
        {
            return;
        }

        string result =
            snapshot.MatchStatus switch
            {
                PlayerMatchStatus.Victory => "VICTORY",
                PlayerMatchStatus.Defeat => "DEFEAT",
                PlayerMatchStatus.Draw => "DRAW",
                _ => "MATCH ENDED"
            };

        EmitText(
            result.AsSpan(),
            MathF.Max(12.0f, width * 0.5f - 48.0f),
            MathF.Max(12.0f, height * 0.32f),
            new Vector4(0.98f, 0.78f, 0.18f, 1.0f),
            width,
            height);
        EmitText(
            "R RESTART  ESC RETURN".AsSpan(),
            MathF.Max(12.0f, width * 0.5f - 126.0f),
            MathF.Max(30.0f, height * 0.32f + 28.0f),
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);

        Span<char> statisticsBuffer =
            stackalloc char[256];
        var statistics =
            new OverlayTextBuilder(
                statisticsBuffer);
        statistics.Append("UNITS PRODUCED ");
        statistics.Append(
            snapshot.Statistics.UnitsProduced);
        statistics.Append("  BUILDINGS ");
        statistics.Append(
            snapshot.Statistics.BuildingsConstructed);
        statistics.Append("  OUTPUT ");
        statistics.Append(
            snapshot.Statistics.ProcessedOutput,
            "F0");

        EmitText(
            statistics.Written,
            MathF.Max(12.0f, width * 0.5f - 156.0f),
            MathF.Max(48.0f, height * 0.32f + 52.0f),
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);
    }

    private void EmitPlayerActions(
        PlayerActionSnapshot actions,
        in PlayerActionPanelView panel,
        FormationTemplate activeFormation,
        int width,
        int height)
    {
        if (!panel.IsOpen)
        {
            EmitText(
                "B BUILD  P PROCESS  U UNITS  L LOGISTICS  Y SUPPLY  K COMBAT".AsSpan(),
                panel.OriginX,
                panel.OriginY,
                new Vector4(0.78f, 0.86f, 0.95f, 1.0f),
                width,
                height);
            return;
        }

        Span<char> buffer =
            stackalloc char[8_192];
        var builder =
            new OverlayTextBuilder(buffer);

        builder.Append("ACTIONS ");
        builder.Append(
            panel.Mode switch
            {
                PlayerActionPanelMode.Construction => "BUILD",
                PlayerActionPanelMode.Production => "PROCESS",
                PlayerActionPanelMode.UnitProduction => "UNITS",
                PlayerActionPanelMode.Logistics => "LOGISTICS",
                PlayerActionPanelMode.Supply => "SUPPLY",
                PlayerActionPanelMode.Tactical => "COMBAT",
                _ => "CLOSED"
            });
        builder.NewLine();

        builder.Append("TAB SELECT  ENTER ACT  C CANCEL");
        builder.NewLine();

        builder.Append("PENDING ");
        builder.Append(actions.PendingCommandCount);
        if (panel.Mode == PlayerActionPanelMode.Logistics)
        {
            builder.Append("  PRIORITY ");
            builder.Append(panel.LogisticsPriority.ToString());
            builder.Append("  EDIT ");
            builder.Append(panel.StockThresholdField.ToString());
        }
        else if (panel.Mode == PlayerActionPanelMode.Supply)
        {
            builder.Append("  PRIORITY ");
            builder.Append(panel.SupplyPriority.ToString());
            builder.Append("  T CYCLE");
        }
        else if (panel.Mode == PlayerActionPanelMode.Tactical)
        {
            builder.Append("  FORMATION ");
            builder.Append(activeFormation.ToString());
            builder.Append("  F3 CYCLE");
        }
        else
        {
            builder.Append("  PRIORITY ");
            builder.Append(panel.Priority.ToString());
        }
        if (panel.Mode == PlayerActionPanelMode.Production)
        {
            builder.Append("  MODE ");
            builder.Append(panel.ProductionMode.ToString());

            if (panel.ProductionMode ==
                ForgeLine.Economy.ProductionRequestMode.DesiredStock)
            {
                builder.Append("  TARGET ");
                builder.Append(
                    panel.DesiredStockQuantity,
                    "F0");
            }
        }

        builder.NewLine();

        switch (panel.Mode)
        {
            case PlayerActionPanelMode.Construction:
                EmitConstructionActions(
                    ref builder,
                    actions,
                    panel.SelectedIndex);
                break;

            case PlayerActionPanelMode.Production:
                EmitProductionActions(
                    ref builder,
                    actions.Production,
                    panel.SelectedIndex);
                break;

            case PlayerActionPanelMode.UnitProduction:
                EmitUnitProductionActions(
                    ref builder,
                    actions.UnitProduction,
                    panel.SelectedIndex);
                break;

            case PlayerActionPanelMode.Logistics:
                EmitLogisticsActions(
                    ref builder,
                    actions.Logistics,
                    panel);
                break;

            case PlayerActionPanelMode.Supply:
                EmitSupplyActions(
                    ref builder,
                    actions.Supply,
                    panel);
                break;

            case PlayerActionPanelMode.Tactical:
                EmitTacticalActions(
                    ref builder,
                    actions.Tactical,
                    panel.SelectedIndex);
                break;
        }

        EmitText(
            builder.Written,
            panel.OriginX,
            panel.OriginY,
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);
    }

    private static void EmitConstructionActions(
        ref OverlayTextBuilder builder,
        PlayerActionSnapshot actions,
        int selectedIndex)
    {
        if (actions.Construction.Count == 0)
        {
            builder.Append("NO CONSTRUCTIBLE CONTENT");
            builder.NewLine();
            return;
        }

        if (selectedIndex >= 0 &&
            selectedIndex <
                actions.Construction.Count)
        {
            PlayerConstructionActionReadModel selected =
                actions.Construction[selectedIndex];
            builder.Append("COST ");
            AppendAmounts(
                ref builder,
                selected.Costs);
            if (selected.RequiresResourceDeposit)
            {
                builder.Append("  DEPOSIT REQUIRED");
            }

            builder.NewLine();
        }
        else
        {
            builder.NewLine();
        }

        for (int index = 0;
             index < actions.Construction.Count;
             index++)
        {
            PlayerConstructionActionReadModel action =
                actions.Construction[index];
            builder.Append(
                index == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append(action.DisplayName);
            builder.Append(
                action.HasRequiredResources
                    ? "  READY"
                    : "  MATERIALS");
            builder.NewLine();
        }
    }

    private static void EmitProductionActions(
        ref OverlayTextBuilder builder,
        PlayerProductionFacilityActionReadModel? facility,
        int selectedIndex)
    {
        if (facility is null)
        {
            builder.Append("SELECT ONE OWNED PROCESSING FACILITY");
            builder.NewLine();
            return;
        }

        builder.Append("STATUS ");
        builder.Append(facility.Status.ToString());
        builder.Append(" ");
        builder.Append(
            facility.Progress * 100.0,
            "F0");
        builder.Append("%");

        if (facility.BlockReason !=
            ForgeLine.Economy.ProductionBlockReason.None)
        {
            builder.Append(" ");
            builder.Append(
                facility.BlockReason.ToString());
        }

        builder.NewLine();

        int recipeCount =
            facility.Recipes.Count;

        for (int index = 0;
             index < recipeCount;
             index++)
        {
            PlayerProductionRecipeActionReadModel recipe =
                facility.Recipes[index];
            builder.Append(
                index == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append(recipe.DisplayName);
            builder.Append(
                recipe.HasInputs
                    ? "  INPUT OK"
                    : "  INPUT MISSING");
            builder.NewLine();
        }

        for (int index = 0;
             index < facility.Requests.Count;
             index++)
        {
            int row =
                recipeCount +
                index;
            PlayerProductionRequestReadModel request =
                facility.Requests[index];

            builder.Append(
                row == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append("QUEUE ");
            builder.Append(request.DisplayName);
            builder.Append(" ");
            builder.Append(request.Priority.ToString());
            builder.Append(" ");
            builder.Append(request.Mode.ToString());

            if (request.Paused)
            {
                builder.Append(" PAUSED");
            }
            else if (request.IsActive)
            {
                builder.Append(" ACTIVE");
            }

            builder.NewLine();
        }

        if (selectedIndex >= 0 &&
            selectedIndex < recipeCount)
        {
            PlayerProductionRecipeActionReadModel selected =
                facility.Recipes[selectedIndex];
            builder.Append("INPUT ");
            AppendAmounts(
                ref builder,
                selected.Inputs);
            builder.NewLine();
        }
    }

    private static void EmitUnitProductionActions(
        ref OverlayTextBuilder builder,
        PlayerUnitProductionFacilityActionReadModel? facility,
        int selectedIndex)
    {
        if (facility is null)
        {
            builder.Append("SELECT ONE OWNED BARRACKS OR FACTORY");
            builder.NewLine();
            return;
        }

        builder.Append("STATUS ");
        builder.Append(facility.Status.ToString());
        builder.Append(" ");
        builder.Append(
            facility.Progress * 100.0,
            "F0");
        builder.Append("%");

        if (facility.BlockReason !=
            UnitProductionBlockReason.None)
        {
            builder.Append(" ");
            builder.Append(
                facility.BlockReason.ToString());
        }

        builder.NewLine();

        int unitCount =
            facility.Units.Count;

        for (int index = 0;
             index < unitCount;
             index++)
        {
            PlayerUnitProductionActionReadModel unit =
                facility.Units[index];
            builder.Append(
                index == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append(unit.DisplayName);
            builder.Append(
                unit.HasInputs
                    ? "  INPUT OK"
                    : "  INPUT MISSING");
            builder.NewLine();
        }

        for (int index = 0;
             index < facility.Requests.Count;
             index++)
        {
            int row =
                unitCount +
                index;
            PlayerUnitProductionRequestReadModel request =
                facility.Requests[index];

            builder.Append(
                row == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append("QUEUE ");
            builder.Append(request.DisplayName);
            builder.Append(" ");
            builder.Append(request.Priority.ToString());

            if (request.IsActive)
            {
                builder.Append(" ACTIVE");
            }

            builder.NewLine();
        }

        if (selectedIndex >= 0 &&
            selectedIndex < unitCount)
        {
            PlayerUnitProductionActionReadModel selected =
                facility.Units[selectedIndex];
            builder.Append("COST ");
            AppendAmounts(
                ref builder,
                selected.Costs);
            builder.NewLine();
        }
    }

    private static void EmitLogisticsActions(
        ref OverlayTextBuilder builder,
        PlayerLogisticsActionReadModel? logistics,
        in PlayerActionPanelView panel)
    {
        if (logistics is null)
        {
            builder.Append("SELECT ONE OWNED LOGISTICS-CAPABLE ENTITY");
            builder.NewLine();
            return;
        }

        if (logistics.Cargo is PlayerCargoStatusReadModel cargo)
        {
            builder.Append("CARGO ");
            builder.Append(cargo.CargoQuantity, "F0");
            builder.Append("/");
            builder.Append(cargo.Capacity, "F0");
            builder.Append(" ");
            builder.Append(cargo.Lifecycle.ToString());
            if (cargo.WaitReason != CargoTransportWaitReason.None)
            {
                builder.Append(" ");
                builder.Append(cargo.WaitReason.ToString());
            }

            builder.NewLine();
        }

        if (logistics.Policies.Count == 0)
        {
            builder.Append("NO STOCK-POLICY TARGET INVENTORY");
            builder.NewLine();
            return;
        }

        builder.Append("MIN ");
        builder.Append(panel.StockMinimum, "F0");
        builder.Append(" TARGET ");
        builder.Append(panel.StockTarget, "F0");
        builder.Append(" MAX ");
        builder.Append(panel.StockMaximum, "F0");
        builder.Append("  M FIELD  LEFT/RIGHT ADJUST");
        builder.NewLine();

        for (int index = 0;
             index < logistics.Policies.Count;
             index++)
        {
            PlayerStockPolicyActionReadModel policy =
                logistics.Policies[index];

            builder.Append(
                index == panel.SelectedIndex
                    ? "X "
                    : "  ");
            builder.Append(policy.DisplayName);
            builder.Append(" STOCK ");
            builder.Append(policy.CurrentQuantity, "F0");

            if (policy.HasPolicy)
            {
                builder.Append(" POLICY ");
                builder.Append(policy.DesiredMinimum, "F0");
                builder.Append("/");
                builder.Append(policy.DesiredTarget, "F0");
                builder.Append("/");
                builder.Append(policy.DesiredMaximum, "F0");
            }
            else
            {
                builder.Append(" NO POLICY");
            }

            if (policy.DistributionState !=
                PlayerDistributionActionState.None)
            {
                builder.Append(" ");
                builder.Append(policy.DistributionState.ToString());
            }

            if (policy.FailureReason !=
                LogisticsTransportRequestFailureReason.None)
            {
                builder.Append(" ");
                builder.Append(policy.FailureReason.ToString());
            }
            else if (policy.BottleneckReason !=
                     LogisticsBottleneckReason.None)
            {
                builder.Append(" ");
                builder.Append(policy.BottleneckReason.ToString());
            }

            builder.NewLine();
        }
    }

    private static void EmitSupplyActions(
        ref OverlayTextBuilder builder,
        PlayerSupplyActionReadModel? supply,
        in PlayerActionPanelView panel)
    {
        if (!supply.HasValue)
        {
            builder.Append("SELECT ONE OWNED SUPPLY UNIT OR PROVIDER");
            builder.NewLine();
            return;
        }

        PlayerSupplyActionReadModel value =
            supply.Value;

        builder.Append("STATUS ");
        builder.Append(value.Status.ToString());
        builder.Append(" FUEL ");
        builder.Append(value.FuelFraction * 100.0, "F0");
        builder.Append("% AMMO ");
        builder.Append(value.AmmunitionFraction * 100.0, "F0");
        builder.Append("%");
        builder.NewLine();

        builder.Append("AUTO ");
        builder.Append(
            panel.AutomaticResupplyEnabled
                ? "ON"
                : "OFF");
        builder.Append(" PROVIDER ");
        builder.Append(value.ProviderState.ToString());
        if (value.ProviderRejections !=
            ResupplyProviderRejection.None)
        {
            builder.Append(" ");
            builder.Append(
                value.ProviderRejections.ToString());
        }

        builder.NewLine();

        builder.Append(
            panel.SelectedIndex == 0
                ? "X "
                : "  ");
        builder.Append("FUEL THRESHOLD ");
        builder.Append(
            panel.AutomaticFuelThreshold * 100.0,
            "F0");
        builder.Append("%");
        builder.NewLine();

        builder.Append(
            panel.SelectedIndex == 1
                ? "X "
                : "  ");
        builder.Append("AMMO THRESHOLD ");
        builder.Append(
            panel.AutomaticAmmunitionThreshold * 100.0,
            "F0");
        builder.Append("%");
        builder.NewLine();

        builder.Append(
            panel.SelectedIndex == 2
                ? "X "
                : "  ");
        builder.Append("REQUEST RESUPPLY");
        builder.NewLine();
        builder.Append("T CYCLE PRIORITY  M TOGGLE AUTO  LEFT/RIGHT ADJUST");
        builder.NewLine();
    }

    private static void EmitTacticalActions(
        ref OverlayTextBuilder builder,
        PlayerTacticalActionReadModel? tactical,
        int selectedIndex)
    {
        if (tactical is null)
        {
            builder.Append("SELECT OWNED UNITS");
            builder.NewLine();
            return;
        }

        builder.Append("SELECTED ");
        builder.Append(tactical.RequestedSelectionCount);
        builder.Append("  COMBAT ");
        builder.Append(tactical.CombatEligibleCount);
        builder.Append("  REJECTED ");
        builder.Append(tactical.RejectedSelectionCount);
        builder.NewLine();

        builder.Append("INTEL TARGETS ");
        builder.Append(tactical.Targets.Count);
        builder.Append("  ARTILLERY ");
        builder.Append(tactical.Artillery.Count);
        builder.Append("  SUPPLY CRIT ");
        builder.Append(tactical.CriticalSupplyCount);
        builder.Append("  RESUPPLY ");
        builder.Append(tactical.ResupplyingCount);
        builder.Append("  SUPP ");
        builder.Append(tactical.SuppressedCount);
        builder.Append("  PINNED ");
        builder.Append(tactical.PinnedCount);
        builder.Append("  REPAIR ");
        builder.Append(tactical.RepairingCount);
        builder.NewLine();

        if (tactical.RetreatReason !=
            RetreatRecoveryReason.None)
        {
            builder.Append("RETREAT ");
            builder.Append(tactical.RetreatReason.ToString());
            builder.Append("  PROVIDER ");
            builder.Append(tactical.RetreatProvider.ToString());
            builder.NewLine();
        }

        builder.Append("ORDER ");
        if (tactical.MixedOrderState)
        {
            builder.Append("MIXED");
        }
        else if (tactical.HasCommonOrder)
        {
            builder.Append(tactical.CurrentOrder.ToString());
            builder.Append(" ");
            builder.Append(tactical.CurrentStatus.ToString());
        }
        else
        {
            builder.Append("NONE");
        }

        builder.NewLine();

        string[] rows =
        [
            "ATTACK TARGET",
            "ATTACK MOVE",
            "STOP",
            "HOLD POSITION",
            "RETREAT",
            "FIRE MISSION",
            "CANCEL FIRE MISSION",
            "RETREAT TO RECOVERY"
        ];

        for (int index = 0;
             index < rows.Length;
             index++)
        {
            builder.Append(
                index == selectedIndex
                    ? "X "
                    : "  ");
            builder.Append(rows[index]);
            builder.NewLine();
        }

        if (tactical.Artillery.Count > 0)
        {
            PlayerArtilleryActionReadModel artillery =
                tactical.Artillery[0];
            builder.Append("ARTY AMMO ");
            builder.Append(artillery.AmmunitionQuantity, "F0");
            builder.Append("/");
            builder.Append(artillery.AmmunitionCapacity, "F0");
            builder.Append(" RANGE ");
            builder.Append(artillery.MinimumRangeMeters, "F0");
            builder.Append("-");
            builder.Append(artillery.MaximumRangeMeters, "F0");
            builder.Append(" ");
            builder.Append(artillery.MissionStatus.ToString());
            builder.NewLine();
        }
    }

    private void EmitTacticalTargeting(
        in TacticalTargetingView targeting,
        int width,
        int height)
    {
        Span<char> buffer =
            stackalloc char[512];
        var builder =
            new OverlayTextBuilder(buffer);

        builder.Append("TARGET MODE ");
        builder.Append(
            targeting.Mode switch
            {
                TacticalTargetingMode.Attack => "ATTACK",
                TacticalTargetingMode.AttackMove => "ATTACK MOVE",
                TacticalTargetingMode.Retreat => "RETREAT",
                TacticalTargetingMode.FireMission => "FIRE MISSION",
                _ => "NONE"
            });
        builder.NewLine();

        builder.Append(
            targeting.Mode switch
            {
                TacticalTargetingMode.Attack =>
                    "CLICK CURRENT IDENTIFIED ENEMY",
                TacticalTargetingMode.FireMission =>
                    "CLICK KNOWN CONTACT OR VISIBLE GROUND",
                _ =>
                    "CLICK GROUND DESTINATION"
            });
        builder.NewLine();
        builder.Append("ESC CANCEL  SELECTED ");
        builder.Append(targeting.SelectedEntityCount);

        if (targeting.Mode == TacticalTargetingMode.Attack)
        {
            builder.Append("  TARGETS ");
            builder.Append(targeting.AvailableAttackTargets);
        }
        else if (targeting.Mode == TacticalTargetingMode.FireMission)
        {
            builder.Append("  CONTACTS ");
            builder.Append(targeting.KnownContacts);
            builder.Append("  ROUNDS ");
            builder.Append(targeting.RequestedRounds);
        }

        EmitText(
            builder.Written,
            MathF.Max(
                12.0f,
                width * 0.5f - 210.0f),
            MathF.Max(
                12.0f,
                height - 76.0f),
            new Vector4(0.98f, 0.78f, 0.18f, 1.0f),
            width,
            height);
    }

    private static void AppendAmounts(
        ref OverlayTextBuilder builder,
        IReadOnlyList<PlayerActionResourceAmount> amounts)
    {
        for (int index = 0;
             index < amounts.Count;
             index++)
        {
            if (index > 0)
            {
                builder.Append("  ");
            }

            PlayerActionResourceAmount amount =
                amounts[index];
            builder.Append(amount.DisplayName);
            builder.Append(" ");
            builder.Append(
                amount.AvailableQuantity,
                "F0");
            builder.Append("/");
            builder.Append(
                amount.RequiredQuantity,
                "F0");
        }
    }

    private void EmitPreAlphaUx(
        in PreAlphaUxView view,
        int width,
        int height)
    {
        if (view.Mode == PreAlphaUxMode.None &&
            !view.ShowOnboarding)
        {
            return;
        }

        Span<char> buffer =
            stackalloc char[4_096];
        var builder =
            new OverlayTextBuilder(buffer);

        if (view.Mode == PreAlphaUxMode.MatchSetup)
        {
            builder.Append("FORGELINE PRE-ALPHA");
            builder.NewLine();
            builder.Append("MATCH SETUP");
            builder.NewLine();
            builder.NewLine();
            builder.Append("MAP ");
            builder.Append(view.MapName);
            builder.NewLine();
            builder.Append("PLAYER ");
            builder.Append(view.PlayerFaction);
            builder.NewLine();
            builder.Append("OPPONENT ");
            builder.Append(view.OpponentDescription);
            builder.NewLine();
            builder.NewLine();
            builder.Append("ENTER START MATCH");
            builder.NewLine();
            builder.Append("ESC EXIT");
            builder.NewLine();
            builder.Append("F12 CONTROLS AND ONBOARDING");
            builder.NewLine();
            builder.NewLine();
            builder.Append("SETTINGS ");
            builder.Append(view.SettingsPath);

            EmitReadableText(
                builder.Written,
                32.0f,
                160.0f,
                new Vector4(
                    0.98f,
                    0.86f,
                    0.32f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.Mode == PreAlphaUxMode.Paused)
        {
            builder.Append("PAUSED");
            builder.NewLine();
            builder.Append("ESC OR SPACE RESUME");
            builder.NewLine();
            builder.Append("F12 CONTROLS");

            EmitReadableText(
                builder.Written,
                32.0f,
                160.0f,
                new Vector4(
                    0.98f,
                    0.86f,
                    0.32f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.Mode == PreAlphaUxMode.Help)
        {
            builder.Append("CONTROLS");
            builder.NewLine();
            builder.Append("CAMERA ");
            builder.Append(view.PanForwardBinding);
            builder.Append("/");
            builder.Append(view.PanLeftBinding);
            builder.Append("/");
            builder.Append(view.PanBackwardBinding);
            builder.Append("/");
            builder.Append(view.PanRightBinding);
            builder.Append(" PAN  ");
            builder.Append(view.RotateLeftBinding);
            builder.Append("/");
            builder.Append(view.RotateRightBinding);
            builder.Append(" ROTATE  ");
            builder.Append(view.PitchUpBinding);
            builder.Append("/");
            builder.Append(view.PitchDownBinding);
            builder.Append(" PITCH");
            builder.NewLine();
            builder.Append("MOUSE WHEEL ZOOM  ");
            builder.Append(view.DragPanBinding);
            builder.Append(" DRAG PAN");
            builder.NewLine();
            builder.Append("LEFT CLICK SELECT  SHIFT LEFT CLICK MULTI SELECT");
            builder.NewLine();
            builder.Append("RIGHT CLICK MOVE  B BUILD  P PROCESS  U UNITS");
            builder.NewLine();
            builder.Append("L LOGISTICS  Y SUPPLY  K COMBAT");
            builder.NewLine();
            builder.Append("F1 PERFORMANCE METRICS  F2 WORLD DEBUG  F3 FORMATION");
            builder.NewLine();
            builder.Append("F4 COMMAND CORE  F5 POWER PLANT  F6 EXTRACTOR");
            builder.NewLine();
            builder.Append("F7 STORAGE DEPOT  F8 SMELTER  F9 ROTATE BUILDING");
            builder.NewLine();
            builder.Append("F10 STRATEGIC OVERLAY  F11 MINIMAP  F12 CLOSE HELP");
            builder.NewLine();
            builder.Append("ESC OR SPACE PAUSE MENU");
            builder.NewLine();
            builder.NewLine();
            builder.Append("QUICK START");
            builder.NewLine();
            builder.Append("1 MOVE CAMERA AND SELECT YOUR UNITS");
            builder.NewLine();
            builder.Append("2 RIGHT CLICK TO MOVE AND SCOUT");
            builder.NewLine();
            builder.Append("3 BUILD POWER AND INDUSTRY WITH B");
            builder.NewLine();
            builder.Append("4 PROCESS MATERIALS WITH P AND PRODUCE UNITS WITH U");
            builder.NewLine();
            builder.Append("5 USE L AND Y TO KEEP THE FRONT SUPPLIED");
            builder.NewLine();
            builder.Append("6 USE K FOR ATTACK ATTACK-MOVE RETREAT AND ARTILLERY");
            builder.NewLine();
            builder.Append("7 DESTROY THE ENEMY COMMAND CORE TO WIN");
            builder.NewLine();

            EmitReadableText(
                builder.Written,
                24.0f,
                140.0f,
                new Vector4(
                    0.93f,
                    0.95f,
                    0.98f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.ShowOnboarding)
        {
            builder.Append("F12 HELP  ESC PAUSE MENU  GOAL DESTROY ENEMY COMMAND CORE");

            EmitReadableText(
                builder.Written,
                12.0f,
                MathF.Max(
                    12.0f,
                    height - 28.0f * _uiScale),
                new Vector4(
                    0.93f,
                    0.95f,
                    0.98f,
                    1.0f),
                width,
                height);
        }
    }

    private void EmitReadableText(
        ReadOnlySpan<char> text,
        float originX,
        float originY,
        Vector4 color,
        int width,
        int height)
    {
        EmitText(
            text,
            originX + 2.0f,
            originY + 2.0f,
            new Vector4(
                0.02f,
                0.02f,
                0.02f,
                0.95f),
            width,
            height);
        EmitText(
            text,
            originX,
            originY,
            color,
            width,
            height);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IGraphicsBuffer vertexBuffer in _vertexBuffers.Values)
        {
            vertexBuffer.Dispose();
        }

        _vertexBuffers.Clear();
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitText(
        ReadOnlySpan<char> text,
        float originX,
        float originY,
        Vector4 color,
        int width,
        int height)
    {
        float x = originX;
        float y = originY;

        for (int index = 0; index < text.Length; index++)
        {
            char character = char.ToUpperInvariant(text[index]);

            if (character == '\n')
            {
                x = originX;
                y += LineAdvance * _uiScale;
                continue;
            }

            EmitGlyph(
                character,
                x,
                y,
                color,
                width,
                height);
            x += GlyphAdvance * _uiScale;
        }
    }

    private void EmitGlyph(
        char character,
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        string pattern = GlyphPattern(character);
        if (pattern.Length == 0)
        {
            return;
        }

        for (int row = 0; row < 7; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                if (pattern[(row * 5) + column] != '1')
                {
                    continue;
                }

                EmitPixel(
                    x + column * GlyphPixelSize * _uiScale,
                    y + row * GlyphPixelSize * _uiScale,
                    color,
                    width,
                    height);
            }
        }
    }

    private void EmitPixel(
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        if (_vertexCount > MaxVertices - 6 ||
            width <= 0 ||
            height <= 0)
        {
            return;
        }

        float left = (x / width) * 2.0f - 1.0f;
        float right =
            ((x + GlyphPixelSize * _uiScale) / width) * 2.0f - 1.0f;
        float top = 1.0f - (y / height) * 2.0f;
        float bottom =
            1.0f -
            ((y + GlyphPixelSize * _uiScale) / height) * 2.0f;

        Vector2 topLeft = new(left, top);
        Vector2 topRight = new(right, top);
        Vector2 bottomLeft = new(left, bottom);
        Vector2 bottomRight = new(right, bottom);

        _vertices[_vertexCount++] =
            new OverlayVertex(topLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomRight, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(topRight, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(topLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomRight, color);
    }

    private IGraphicsBuffer GetFrameVertexBuffer(int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(frameIndex, out IGraphicsBuffer? buffer))
        {
            return buffer;
        }

        buffer = _graphics.CreateBuffer(
            new GraphicsBufferDescription(
                checked((ulong)_vertices.Length * VertexStride),
                GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(frameIndex, buffer);
        return buffer;
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            struct VertexInput
            {
                float2 Position : POSITION;
                float4 Color : COLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position =
                    float4(input.Position, 0.0f, 1.0f);
                output.Color = input.Color;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                return input.Color;
            }
            """;

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader = compiler.Compile(
            vertexShaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "OverlayVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "OverlayPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(vertexShader, pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float2,
                        0),
                    new GraphicsVertexElement(
                        "COLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        8)
                ],
                DepthEnabled = false
            });
    }

    private static string GlyphPattern(char value) =>
        value switch
        {
            'A' => "01110100011000111111100011000110001",
            'B' => "11110100011000111110100011000111110",
            'C' => "01111100001000010000100001000001111",
            'D' => "11110100011000110001100011000111110",
            'E' => "11111100001000011110100001000011111",
            'F' => "11111100001000011110100001000010000",
            'G' => "01111100001000010111100011000101111",
            'H' => "10001100011000111111100011000110001",
            'I' => "11111001000010000100001000010011111",
            'J' => "00111000100001000010100101001001100",
            'K' => "10001100101010011000101001001010001",
            'L' => "10000100001000010000100001000011111",
            'M' => "10001110111010110101100011000110001",
            'N' => "10001110011010110011100011000110001",
            'O' => "01110100011000110001100011000101110",
            'P' => "11110100011000111110100001000010000",
            'Q' => "01110100011000110001101011001001101",
            'R' => "11110100011000111110101001001010001",
            'S' => "01111100001000001110000010000111110",
            'T' => "11111001000010000100001000010000100",
            'U' => "10001100011000110001100011000101110",
            'V' => "10001100011000110001100010101000100",
            'W' => "10001100011000110101101011101110001",
            'X' => "10001100010101000100010101000110001",
            'Y' => "10001100010101000100001000010000100",
            'Z' => "11111000010001000100010001000011111",
            '0' => "01110100011001110101110011000101110",
            '1' => "00100011000010000100001000010001110",
            '2' => "01110100010000100010001000100011111",
            '3' => "11110000010000101110000010000111110",
            '4' => "00010001100101010010111110001000010",
            '5' => "11111100001000011110000010000111110",
            '6' => "01110100001000011110100011000101110",
            '7' => "11111000010001000100010000100001000",
            '8' => "01110100011000101110100011000101110",
            '9' => "01110100011000101111000010000101110",
            '.' => "00000000000000000000000000011000110",
            ':' => "00000001100011000000001100011000000",
            '/' => "00001000100010001000100001000000000",
            '-' => "00000000000000011111000000000000000",
            '%' => "11001000100010001000100001001100000",
            '_' => "00000000000000000000000000000011111",
            ' ' => "",
            _ => "11111000010001000100000000010000100"
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(
        Vector2 Position,
        Vector4 Color);

    private ref struct OverlayTextBuilder
    {
        private Span<char> _buffer;
        private int _length;

        public OverlayTextBuilder(Span<char> buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        public readonly ReadOnlySpan<char> Written =>
            _buffer[.._length];

        public void NewLine() => Append("\n");

        public void Append(string value)
        {
            if (value.AsSpan().TryCopyTo(_buffer[_length..]))
            {
                _length += value.Length;
            }
        }

        public void Append(int value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider: CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void Append(ulong value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider: CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void Append(double value, string format)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    format,
                    CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void AppendBytes(long bytes)
        {
            double megabytes = Math.Max(bytes, 0) / (1024.0 * 1024.0);
            Append(megabytes, "F1");
            Append("MB");
        }
    }
}
