using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed partial class PresentationExtractor
{
    private NavigationDiagnosticSnapshot? CaptureNavigationDiagnostics(SimulationContext context, RenderInstance[] instances)
    {
        if (_extraction is null) return null;
        var visible = new List<EntityId>();
        foreach (ref readonly var instance in instances.AsSpan())
        {
            if (!instance.Selectable.IsSelectable || instance.Selectable.Owner != _extraction.Player ||
                (instance.Visibility & RenderVisibilityMask.World) == 0 || instance.UnitFeature.IsWreck) continue;
            if (visible.Count == 4096) return null;
            visible.Add(instance.Entity);
        }
        return _extraction.Scenario.Services.GroundMovement.CaptureNavigationDiagnostics(_extraction.Scenario, context,
            _extraction.Player, visible.ToArray());
    }
}
