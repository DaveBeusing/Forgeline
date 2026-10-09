using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Camera-only focus from currently authorized display identities.</summary>
public sealed class RtsCameraFocusController(PlayerId player)
{
    private ulong _homeSequence;
    private SimulationSessionId _session;
    private TimeSpan _feedbackRemaining;
    public string Feedback { get; private set; } = string.Empty;

    public void ResetFeedback() { Feedback = string.Empty; _feedbackRemaining = default; }

    public bool Update(InputState input, PresentationSnapshot? snapshot, RtsCamera camera,
        SelectionSet groupSelection, bool focusGroup, TimeSpan elapsed, bool blocked = false,
        RtsCameraBindings? bindings = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        ulong sequence = input.KeyPressSequence(PlatformKey.Home);
        bool home = input.WasKeyPressed(PlatformKey.Home) && sequence != _homeSequence;
        _homeSequence = sequence;
        if (_session != snapshot?.SessionId || blocked || input.FocusLostThisFrame)
        { Feedback = string.Empty; _feedbackRemaining = default; }
        _session = snapshot?.SessionId ?? default;
        _feedbackRemaining -= elapsed;
        if (_feedbackRemaining <= TimeSpan.Zero) Feedback = string.Empty;
        if (blocked || input.FocusLostThisFrame || snapshot is null || !snapshot.SessionId.IsSpecified ||
            snapshot.PlayerExperience?.IsMatchComplete == true) return false;
        bool modified = input.IsKeyDown(PlatformKey.LeftControl) || input.IsKeyDown(PlatformKey.RightControl) ||
            input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift);
        if (home && !modified && !IsHomeCameraBinding(bindings) && TryHome(snapshot, player, out Vector3 target))
        {
            camera.CenterOn(target);
            Feedback = "CAMERA FOCUSED ON HOME BASE";
        }
        else if (focusGroup && TryGroup(snapshot, player, groupSelection, out target))
        {
            camera.CenterOn(target);
            Feedback = "CAMERA FOCUSED ON CONTROL GROUP";
        }
        else return false;
        _feedbackRemaining = TimeSpan.FromSeconds(1.2);
        return true;
    }

    public static bool TryHome(PresentationSnapshot snapshot, PlayerId player, out Vector3 target)
    {
        RenderInstance? fallback = null;
        foreach (ref readonly var instance in snapshot.Instances)
        {
            if (!OwnedLive(instance, player) || !instance.BuildingFeature.IsSpecified ||
                instance.BuildingFeature.IsConstruction || instance.BuildingFeature.IsDestroyed) continue;
            if (instance.BuildingFeature.Building == BuildingIds.CommandCore)
            { target = instance.Transform.Position; return true; }
            if (fallback is null || instance.Entity.CompareTo(fallback.Value.Entity) < 0) fallback = instance;
        }
        target = fallback?.Transform.Position ?? default;
        return fallback is not null;
    }

    public static bool TryGroup(PresentationSnapshot snapshot, PlayerId player, SelectionSet selection, out Vector3 target)
    {
        Vector3 sum = default;
        int count = 0;
        foreach (ref readonly var instance in snapshot.Instances)
            if (OwnedLive(instance, player) && selection.Contains(instance.Entity))
            { sum += instance.Transform.Position; count++; }
        target = count > 0 ? sum / count : default;
        return count > 0 && float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z);
    }

    private static bool OwnedLive(in RenderInstance instance, PlayerId player) =>
        instance.Selectable.IsSelectable && instance.Selectable.Owner == player &&
        (instance.Visibility & RenderVisibilityMask.World) != 0 && !instance.UnitFeature.IsWreck &&
        !instance.BuildingFeature.IsDestroyed && float.IsFinite(instance.Transform.Position.X) &&
        float.IsFinite(instance.Transform.Position.Y) && float.IsFinite(instance.Transform.Position.Z);

    public static bool IsHomeCameraBinding(RtsCameraBindings? b) => b is not null &&
        (b.PanForward == PlatformKey.Home || b.PanForwardAlternate == PlatformKey.Home ||
         b.PanBackward == PlatformKey.Home || b.PanBackwardAlternate == PlatformKey.Home ||
         b.PanLeft == PlatformKey.Home || b.PanLeftAlternate == PlatformKey.Home ||
         b.PanRight == PlatformKey.Home || b.PanRightAlternate == PlatformKey.Home ||
         b.RotateLeft == PlatformKey.Home || b.RotateRight == PlatformKey.Home ||
         b.PitchUp == PlatformKey.Home || b.PitchDown == PlatformKey.Home);
}
