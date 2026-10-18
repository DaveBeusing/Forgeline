using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum InfantryAction { Idle, Walk, Run, Aim, Fire, Reload, Hit, Suppression, Death }
public readonly record struct InfantryAnimationPose(bool IsSpecified, InfantryAction Action, double Phase, bool Loop)
{
    public static InfantryAnimationPose Interpolate(InfantryAnimationPose a, InfantryAnimationPose b, float alpha) =>
        a.IsSpecified && b.IsSpecified && a.Action == b.Action && a.Loop == b.Loop && b.Phase >= a.Phase
            ? b with { Phase = a.Phase + (b.Phase - a.Phase) * Math.Clamp(float.IsFinite(alpha) ? alpha : 1, 0, 1) } : b;
}
internal readonly record struct InfantryActionInput(bool Enabled, bool Wreck, bool FreshDeath, bool Aiming, bool Reloading,
    double ReloadRemainingSeconds, bool Hit, bool Suppressed);
internal static class InfantryAnimationState
{
    internal static InfantryAnimationPose Advance(InfantryAnimationPose previous, InfantryActionInput input,
        double step, double speed, double shotAge)
    {
        if (!input.Enabled) return default;
        if (input.Wreck)
        {
            if (input.FreshDeath) return new(true, InfantryAction.Death, 0, false);
            if (previous.IsSpecified && previous.Action == InfantryAction.Death && previous.Phase + step < 1)
                return previous with { Phase = previous.Phase + step };
            return default;
        }
        InfantryAction action; bool loop = false; double phase;
        if (input.Hit) { action = InfantryAction.Hit; phase = 0; }
        else if (previous.IsSpecified && previous.Action == InfantryAction.Hit && previous.Phase + step / .3 < 1)
        { action = InfantryAction.Hit; phase = previous.Phase + step / .3; }
        else if (shotAge is >= 0 and < .18) { action = InfantryAction.Fire; phase = shotAge / .18; }
        else if (input.Reloading)
        {
            action = InfantryAction.Reload;
            phase = previous.IsSpecified && previous.Action == action
                ? previous.Phase + (1 - previous.Phase) * step / Math.Max(step, input.ReloadRemainingSeconds + step) : 0;
        }
        else
        {
            loop = true;
            action = input.Suppressed ? InfantryAction.Suppression : speed > 3 ? InfantryAction.Run : speed > .05 ? InfantryAction.Walk : input.Aiming ? InfantryAction.Aim : InfantryAction.Idle;
            double rate = action == InfantryAction.Walk ? speed / 1.8 : action == InfantryAction.Run ? speed / 3.2 : 1;
            phase = previous.IsSpecified && previous.Action == action ? previous.Phase + step * rate : 0;
        }
        return new(true, action, Math.Clamp(phase, 0, loop ? double.MaxValue : 1), loop);
    }
}
internal static class SkinPresentationBounds
{
    internal static int SelectLod(in RenderInstance instance, float diameter, int previous, float scale)
    {
        var pose = instance.MechanicalPose.Infantry;
        if (instance.UnitFeature.IsWreck && !(pose.IsSpecified && pose.Action == InfantryAction.Death)) return 2;
        return pose.IsSpecified ? ScreenSpaceLod.Select(diameter, previous, 18, 6, scale)
            : ScreenSpaceLod.Select(diameter, previous, diameterScale: scale);
    }
    internal static AxisAlignedBounds Resolve(in RenderInstance instance)
    {
        if (!instance.MechanicalPose.Infantry.IsSpecified) return PresentationBounds.ResolveAxisAlignedBounds(instance);
        float radius = Vector3.Abs(instance.Transform.Scale).Length() * RuntimeSkinData.CullingRadius;
        return new(instance.Transform.Position - new Vector3(radius), instance.Transform.Position + new Vector3(radius));
    }
    internal static float Radius(in RenderInstance instance) => instance.MechanicalPose.Infantry.IsSpecified
        ? Vector3.Abs(instance.Transform.Scale).Length() * RuntimeSkinData.CullingRadius : PresentationBounds.ResolveLocalHalfExtents(instance).Length();
}
