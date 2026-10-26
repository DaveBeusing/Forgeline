using System.Globalization;
using ForgeLine.Game;
using ForgeLine.Presentation;

namespace ForgeLine.UI;

public sealed record DeveloperMenuRow(FrontendDetailLineView Line, Action? Activate = null, Action<int>? Adjust = null);

/// <summary>Uses the existing bounded frontend renderer and geometry, including its scrolling and focus visuals.</summary>
public static class DeveloperMenuView
{
    public static IReadOnlyList<DeveloperMenuRow> Build(DeveloperMenuModel model)
    {
        var rows = new List<DeveloperMenuRow>();
        void Info(string label, string value, bool warning = false) => rows.Add(new(new(label, value, warning)));
        void Control(string label, string value, Action action, bool enabled = true, Action<int>? adjust = null) =>
            rows.Add(new(new(label, value, !enabled, IsSelectable: enabled, CanDecrease: enabled && adjust is not null,
                CanIncrease: enabled && adjust is not null, ValueKind: adjust is null ? FrontendValueKind.Text : FrontendValueKind.Choice), enabled ? action : null, enabled ? adjust : null));
        bool writable = !model.WriteBlocked;
        Control("SECTION", model.Section.ToString(), () => model.ChangeSection(1), true, model.ChangeSection);
        var p = model.Providers;
        foreach (var (name, status) in new[] { ("Menu", p.Menu.Status), ("Session", p.Sessions.Status), ("Actions", p.Actions.Status),
            ("Parameters", p.Parameters.Status), ("Presets", p.Presets.Status) })
            Info(name + " provider", status.Availability == DeveloperProviderAvailability.Available ? "Available" : "Unavailable: " + status.Reason,
                status.Availability != DeveloperProviderAvailability.Available);
        switch (model.Section)
        {
            case DeveloperMenuSection.Session:
                Info("MATCH", model.MatchStatus);
                Info("HOST", model.HostPaused ? "Confirmed paused" : "Confirmed running");
                Info("HOST SPEED", model.Observation?.Speed is { } speed ? speed + "x" : "Unavailable");
                Info("REMAINING STEPS", model.Observation?.RemainingSteps?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable");
                Info("CONTROL OBSERVATION", model.Observation is { } observation ? "Tick " + observation.Tick.Value : "Adapter observation unavailable", model.Observation is null);
                bool sessions = !model.SessionWriteBlocked && p.Sessions.Status.Availability == DeveloperProviderAvailability.Available && model.Observation is not null;
                Control("PAUSE", "Request pause", () => model.SessionCommand(DeveloperOperation.Pause), sessions);
                Control("RESUME", "Transfer menu pause ownership; request resume", () => model.SessionCommand(DeveloperOperation.Resume), sessions);
                foreach (uint count in new uint[] { 1, 10, 100 })
                    Control("STEP " + count, "Request fixed ticks", () => model.SessionCommand(DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(count)), sessions);
                foreach (double rate in new[] { .25, .5, 1, 2, 4 })
                    Control("SPEED " + rate + "x", "Request host speed", () => model.SessionCommand(DeveloperOperation.SetHostSpeed, DeveloperValue.FromDouble(rate)), sessions);
                break;
            case DeveloperMenuSection.Parameters:
                foreach (var descriptor in p.Parameters.Descriptors.Take(128))
                {
                    var targets = descriptor.Targets == DeveloperTargetKinds.Global ? new[] { DeveloperTarget.Global } :
                        model.Participants.Where(participant => participant.IsComputerControlled).Select(participant => DeveloperTarget.ForPlayer(participant.Player)).ToArray();
                    foreach (var target in targets)
                    {
                        var draft = model.Find(descriptor.Id, target);
                        string scope = target.Kind == DeveloperTargetKinds.Global ? "Global" : "Player " + target.Player.Value;
                        string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(descriptor.Key.Replace('.', ' ').Replace('_', ' '));
                        bool editable = writable && draft is not null && p.Parameters.Status.Availability == DeveloperProviderAvailability.Available;
                        Control(name + " / " + scope, draft is null ? "Confirmed value unavailable" : "Draft " + DeveloperMenuModel.Format(draft.Value) +
                            (draft.NeedsReview ? " / UNVERIFIED" : draft.Dirty ? " / edited" : " / unchanged"),
                            () => { }, editable, direction => model.Adjust(descriptor.Id, target, direction));
                        Info("Default / confirmed", DeveloperMenuModel.Format(descriptor.Default) + " / " + (draft is null ? "Unavailable" : DeveloperMenuModel.Format(draft.Observed.Value)));
                        Info("Range / unit / apply", DeveloperMenuModel.Format(descriptor.Minimum) + ".." + DeveloperMenuModel.Format(descriptor.Maximum) +
                            " / " + descriptor.Unit + " / " + descriptor.ApplyMode);
                        if (model.Observation?.Parameters.FirstOrDefault(v => v.Id == descriptor.Id && v.Target == target) is { Id: not DeveloperParameterId.None } value)
                            Info("Basis / initial / pending", DeveloperMenuModel.Format(value.Basis) + " / " + DeveloperMenuModel.Format(value.Initial) +
                                " / " + (value.Pending.HasValue ? DeveloperMenuModel.Format(value.Pending.Value) : "None"));
                        Control("APPLY " + name, "Submit this draft once", () => model.ApplyParameter(descriptor.Id, target), editable);
                        Control("RESET " + name, "Restore captured initial value", () => model.ApplyParameter(descriptor.Id, target, true), editable);
                    }
                }
                break;
            case DeveloperMenuSection.Actions:
                var inspector = model.Inspector;
                Info("SELECTION", inspector.Status ?? "Unavailable");
                if (inspector.Entity.IsValid)
                {
                    Info("TYPE / OWNER", inspector.Type + " / " + inspector.Owner.Value);
                    Info("ENTITY / GENERATION", inspector.Entity.Index + " / " + inspector.Entity.Generation);
                    Info("SNAPSHOT", "Tick " + inspector.Tick.Value);
                    Info("HEALTH", inspector.Details.HasHealth ? inspector.Details.HealthFraction.ToString("P0", CultureInfo.InvariantCulture) : "Unavailable");
                    Info("SUPPLY", inspector.Details.HasSupply ? inspector.Details.SupplyStatus + " / Fuel " + inspector.Details.FuelFraction.ToString("P0", CultureInfo.InvariantCulture) +
                        " / Ammo " + inspector.Details.AmmunitionFraction.ToString("P0", CultureInfo.InvariantCulture) : "Unavailable");
                }
                Control("TARGET", model.Target.Kind + (model.Target.Kind == DeveloperTargetKinds.Player ? " " + model.Target.Player.Value :
                    model.Target.Kind == DeveloperTargetKinds.Entity ? " " + model.Target.Entity : ""), () => model.CycleTarget(1), writable, model.CycleTarget);
                Control("ACTION", model.Action?.Label ?? "Explicit selection required", () => model.CycleAction(1), writable &&
                    p.Menu.Status.Availability == DeveloperProviderAvailability.Available, model.CycleAction);
                if (model.ActionForm is { } form)
                    for (int i = 0; i < form.Fields.Count; i++)
                    {
                        int index = i; var field = form.Fields[index];
                        Control(field.Label, model.ActionValueText(index), () => { }, writable,
                            direction => model.AdjustAction(index, direction));
                        Info("LIMITS", DeveloperMenuModel.Format(field.Minimum) + ".." + DeveloperMenuModel.Format(field.Maximum));
                    }
                else Info("ACTION PAYLOAD", "Typed payload adapter unavailable", true);
                Control("EXECUTE SELECTED ACTION", "Explicit one-shot submission", model.ApplyAction, writable && model.Action.HasValue && model.ActionForm is not null &&
                    p.Actions.Status.Availability == DeveloperProviderAvailability.Available);
                break;
            case DeveloperMenuSection.Visualization:
                Control("DEBUG MASTER", model.Debug.Enabled ? "ON" : "OFF", model.Debug.ToggleMaster);
                foreach (var category in Enum.GetValues<DebugOverlayCategory>().Where(c => c is not (DebugOverlayCategory.None or DebugOverlayCategory.All)))
                    Control(category.ToString(), (model.Debug.Categories & category) != 0 ? "ON" : "OFF", () => model.Debug.ToggleCategory(category));
                Info("DATA", "Only chosen simulation categories request extraction. Rendering needs no simulation debug snapshot.");
                Info("SIMULATION DEBUG DATA", model.DebugDataTick is { } captured ? "Captured at tick " + captured.Value +
                    (captured != model.Inspector.Tick ? " / older than current snapshot" : "") : "Unavailable; waiting for selected category capture", model.DebugDataTick is null);
                break;
            case DeveloperMenuSection.Presets:
                if (model.Observation?.PresetAvailability is { } availability) Info("PRESET STORE", availability, true);
                Control("PRESET", model.Preset?.Label ?? "Unavailable", () => model.CyclePreset(1), model.Observation?.Presets.Count > 0, model.CyclePreset);
                if (model.Preset is { } preset)
                {
                    Info("SCHEMA / COMPATIBILITY", preset.Metadata.Schema + " / " + model.PresetCompatibility);
                    Info("COMPOSITION / SEED", preset.Metadata.CompositionKey + " / " + preset.Metadata.Seed);
                }
                foreach (var intent in Enum.GetValues<DeveloperPresetIntent>())
                    Control(intent.ToString(), "Via registered preset/session adapter", () => model.PresetCommand(intent), writable &&
                        p.Presets.Status.Availability == DeveloperProviderAvailability.Available && model.Observation is not null);
                Info("SESSION LOAD", model.Observation?.Loading == true ? "Loading; writes blocked" : "No load observed");
                break;
        }
        foreach (string entry in model.History) Info("RESULT", entry);
        return rows.AsReadOnly();
    }

    public static FrontendSurfaceView Surface(IReadOnlyList<DeveloperMenuRow> rows, int focus, int scroll, int capacity, string feedback)
    {
        var lines = rows.Skip(scroll).Take(capacity).Select((row, i) => row.Line with { IsFocused = i + scroll == focus }).ToArray();
        return FrontendSurfaceView.Detail("DEVELOPER TOOLS", lines,
            "UP/DOWN / WHEEL SCROLL   LEFT/RIGHT ADJUST   ENTER APPLY / SELECT   ESC BACK TO PAUSE", secondaryAction: "BACK TO PAUSE")
            with { Feedback = feedback, TotalDetailRows = rows.Count, DetailRowOffset = scroll, PauseContext = true };
    }
}
